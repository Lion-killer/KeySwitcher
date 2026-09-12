using KeySwitcher.Core.Input;
using KeySwitcher.Core.Layout;
using Serilog;

namespace KeySwitcher.Core.Features;

/// <summary>
/// Shared mechanics of the hotkeys that transform the text under the caret (change case,
/// transliterate): the operation applies to the selection when there is one, otherwise to the last
/// typed word.
/// </summary>
public abstract class TextTransformHotkey(WordBuffer wordBuffer, InputSimulator inputSimulator)
{
    protected InputSimulator InputSimulator { get; } = inputSimulator;

    /// <summary>Applies <paramref name="transform"/> to the selection, or to the last typed word.</summary>
    /// <param name="transform">Returns the new text; returning the input unchanged means "nothing to do".</param>
    /// <param name="name">Tag for the debug log.</param>
    protected async Task ApplyAsync(Func<string, string> transform, string name)
    {
        string? before = InputSimulator.GetClipboardText();
        string? copied = await InputSimulator.CopySelectionAsync();

        // No API reports whether something is selected — an unchanged clipboard means nothing was copied.
        if (!string.IsNullOrEmpty(copied) && !string.Equals(copied, before, StringComparison.Ordinal))
        {
            string changed = transform(copied);
            if (string.Equals(changed, copied, StringComparison.Ordinal)) return;

            Log.Debug("  {Operation}(selection) '{Text}' -> '{Changed}'", name, copied, changed);
            await InputSimulator.ReplaceSelectionAsync(changed);
            return;
        }

        // Nothing selected — the last typed word, exactly like the manual layout switch.
        string word = wordBuffer.CurrentWord;
        char? delimiter = null;
        if (word.Length == 0)
        {
            word = wordBuffer.RecentWord();
            delimiter = wordBuffer.RecentDelimiter;
        }

        if (word.Length == 0)
        {
            Log.Debug("  {Operation}: nothing to transform (no selection, no word in buffer)", name);
            return;
        }

        string transformed = transform(word);
        Log.Debug("  {Operation}(word) '{Word}' -> '{Transformed}' delim='{Delimiter}'",
            name, word, transformed, delimiter?.ToString() ?? "null");
        if (string.Equals(transformed, word, StringComparison.Ordinal)) return;

        // Re-arm with what ends up on screen, so the next press starts from there.
        wordBuffer.Adopt(transformed, delimiter);

        if (delimiter is null)
            await InputSimulator.ReplaceLastWordAsync(word, transformed);
        else
            await InputSimulator.ReplaceWordKeepingDelimiterAsync(word, transformed, delimiter.Value);
    }
}
