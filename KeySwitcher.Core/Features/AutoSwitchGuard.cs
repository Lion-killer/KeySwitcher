using KeySwitcher.Core.Layout;
using Serilog;

namespace KeySwitcher.Core.Features;

/// <summary>
/// One-shot pause for the auto-switcher: after an action that leaves the text state unclear — the user
/// moved the caret with a cursor key, switched the layout by hand, or pressed the manual-switch hotkey —
/// the next automatic replacement is skipped, so the app does not fight the user's own action.
/// </summary>
/// <remarks>
/// Also keeps the layout bookkeeping that tells the user's own switch apart from KeySwitcher's. The shell
/// does not report a Ctrl+Shift switch — <c>HSHELL_LANGUAGE</c> never arrives for it (checked against
/// debug.log: 249 window activations logged, not a single language event) — so the layout read on each
/// keystroke is the only reliable source. Our own switches are recognised because
/// <see cref="MarkOwnSwitch"/> says which layout we just put in place.
/// </remarks>
public sealed class AutoSwitchGuard
{
    private volatile bool _suppressed;

    // Layout attribution — touched only from the hook consumption thread.
    private KeyboardLanguage? _seenLanguage;
    private nint _seenHwnd;
    private KeyboardLanguage? _ownSwitchLanguage;

    public bool IsSuppressed => _suppressed;

    /// <summary>Arms the pause — the next automatic switch is skipped.</summary>
    public void Suppress() => _suppressed = true;

    /// <summary>Disarms the pause without skipping anything (e.g. another instance of the action arrived).</summary>
    public void Clear() => _suppressed = false;

    /// <summary>
    /// True when the caller must skip the switch it is about to perform. The pause is one-shot: it is
    /// consumed here either way, so only the very next attempt is affected.
    /// </summary>
    public bool ConsumeIfSuppressed()
    {
        bool suppressed = _suppressed;
        _suppressed = false;
        return suppressed;
    }

    /// <summary>
    /// Remembers the layout KeySwitcher itself just put in place. The next keystroke reads it back, and
    /// without the mark the change would look like the user's own switch — every automatic fix would then
    /// pause the following word.
    /// </summary>
    public void MarkOwnSwitch(KeyboardLanguage language) => _ownSwitchLanguage = language;

    /// <summary>
    /// Reports the layout read while handling a keystroke in <paramref name="hwnd"/> and arms the pause
    /// when the change is the user's own doing (<paramref name="pauseEnabled"/> mirrors the setting).
    /// Returns true when the pause was armed.
    /// </summary>
    public bool ObserveLayout(KeyboardLanguage language, nint hwnd, bool pauseEnabled)
    {
        bool sameWindow = hwnd == _seenHwnd;
        KeyboardLanguage? previous = _seenLanguage;
        KeyboardLanguage? ownSwitch = _ownSwitchLanguage;

        _seenHwnd = hwnd;
        _seenLanguage = language;
        _ownSwitchLanguage = null; // the mark is good for exactly one keystroke

        // Nothing to compare with yet, or the layout of another window (Windows keeps it per window):
        // a cross-window difference is not the user switching the language.
        if (previous is null || !sameWindow || previous.Value == language) return false;

        if (ownSwitch is not null && ownSwitch.Value == language)
        {
            // Logged here because this is the only place that knows: the shell reports our own switch
            // exactly like the user's, and "the layout changed but nothing was paused" is otherwise a
            // mystery to whoever reads the log (Session 40).
            Log.Verbose("  layout change: recognised as our own switch to {Language}", language.ToString());
            return false;
        }

        if (!pauseEnabled) return false;

        Suppress();
        return true;
    }
}
