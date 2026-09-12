using KeySwitcher.Core.Analysis;
using KeySwitcher.Core.Input;
using KeySwitcher.Core.Layout;
using KeySwitcher.Core.Native;
using Serilog;

namespace KeySwitcher.Core.Features;

/// <summary>
/// Manual UA↔EN switch of the last typed word, triggered by a hotkey.
/// Reads the current word from the shared <see cref="WordBuffer"/>, converts it to the other layout,
/// replaces it in the active app and then switches the system layout to match.
/// Unlike the auto-switcher it never consults dictionaries or statistics: the hotkey means
/// "convert this", whatever the word looks like.
/// </summary>
public sealed class ManualSwitcher
{
    private readonly WordBuffer _wordBuffer;
    private readonly LayoutDetector _layoutDetector;
    private readonly LayoutConverter _layoutConverter;
    private readonly WordJudge _wordJudge;
    private readonly InputSimulator _inputSimulator;

    public ManualSwitcher(
        WordBuffer wordBuffer,
        LayoutDetector layoutDetector,
        LayoutConverter layoutConverter,
        WordJudge wordJudge,
        InputSimulator inputSimulator)
    {
        _wordBuffer = wordBuffer;
        _layoutDetector = layoutDetector;
        _layoutConverter = layoutConverter;
        _wordJudge = wordJudge;
        _inputSimulator = inputSimulator;
    }

    /// <summary>
    /// Switches the layout of the selected text (Ctrl+Shift+F10); with nothing selected the last typed
    /// word is switched, exactly like the manual hotkey.
    /// </summary>
    /// <remarks>
    /// The direction comes from the text itself (Cyrillic → Latin, otherwise → Cyrillic), not from the
    /// active layout: a selection can be arbitrarily long and in either script.
    /// The converted text is left selected, so a second press converts it back — the natural toggle.
    /// Long multi-line selections are only re-selected within the last line (Shift+← does not cross
    /// line breaks).
    /// </remarks>
    public async Task SwitchSelectionAsync()
    {
        string? before = _inputSimulator.GetClipboardText();
        string? copied = await _inputSimulator.CopySelectionAsync();

        // No API reports whether something is selected — an unchanged clipboard means nothing was copied.
        if (string.IsNullOrEmpty(copied) || string.Equals(copied, before, StringComparison.Ordinal))
        {
            await SwitchLastWordAsync();
            return;
        }

        Direction direction = LayoutConverter.DirectionForText(copied);
        string converted = _layoutConverter.Convert(copied, direction);
        Log.Debug("  SELECTION-LAYOUT '{Text}' -> '{Converted}' dir={Direction}", copied, converted, direction.ToString());
        if (string.Equals(converted, copied, StringComparison.Ordinal)) return;

        await _inputSimulator.ReplaceSelectionAsync(converted);
        _inputSimulator.SelectBackwards(converted.Length);

        // Follow with the system layout: the user is writing in the other language from now on.
        KeyboardLanguage target = direction == Direction.UaToEn
            ? KeyboardLanguage.English
            : KeyboardLanguage.Ukrainian;
        await _layoutDetector.SwitchToAsync(NativeMethods.GetForegroundWindow(), target);
    }

    /// <summary>
    /// Converts the last typed word to the other layout and replaces it in place.
    /// Call from the manual-switch hotkey handler. No-op when the buffer is empty.
    /// </summary>
    public async Task SwitchLastWordAsync()
    {
        // A single-key switch (Insert) finds the word still in the buffer. A chord hotkey
        // (Ctrl/Alt + key) clears the buffer through the normal key path *before* WM_HOTKEY arrives,
        // so fall back to the word that reset dropped.
        string word = _wordBuffer.CurrentWord;
        bool fromRecent = false;
        char? delimiter = null;
        if (word.Length == 0)
        {
            word = _wordBuffer.RecentWord();
            delimiter = _wordBuffer.RecentDelimiter;
            fromRecent = true;
        }

        if (word.Length == 0)
        {
            Log.Debug("  MANUAL: nothing to switch (no word in buffer)");
            return;
        }

        KeyboardLanguage lang = _layoutDetector.GetCurrentLanguage();

        // Unknown layout falls through to UaToEn — manual switch is an explicit toggle.
        string converted = _wordJudge.ConvertToOther(word, lang);
        Log.Debug("  MANUAL '{Word}' -> '{Converted}' lang={Lang} recent={Recent} delim='{Delimiter}'",
            word, converted, lang.ToString(), fromRecent, delimiter?.ToString() ?? "null");

        // Re-arm with what ends up on screen (converted + its delimiter) so the next hotkey press
        // toggles this same word back.
        _wordBuffer.Adopt(converted, delimiter);

        // The delimiter typed after the word sits between the caret and the word: it must go with the
        // word, otherwise the backspaces eat it and the first letter of the word stays behind.
        if (delimiter is null)
            await _inputSimulator.ReplaceLastWordAsync(word, converted);
        else
            await _inputSimulator.ReplaceWordKeepingDelimiterAsync(word, converted, delimiter.Value);

        // After the replacement, per the InputSimulator contract — see AutoSwitcher.
        nint hwnd = NativeMethods.GetForegroundWindow();
        await _layoutDetector.SwitchLayoutAsync(hwnd);
    }
}
