namespace KeySwitcher.Core.Settings;

public sealed record AppSettings
{
    public bool AutoSwitch { get; init; } = true;
    public bool AutoSwitchLayout { get; init; } = true;
    public bool FixCapsLock { get; init; } = true;
    public bool AutoStart { get; init; } = false;

    /// <summary>Skip the next automatic switch after a cursor key (arrows, Home/End, PgUp/PgDn) moved the caret.</summary>
    public bool SkipSwitchAfterCaretMove { get; init; } = true;

    /// <summary>Skip the next automatic switch after the user switched the layout by hand (the manual hotkey).</summary>
    public bool SkipSwitchAfterManualSwitch { get; init; } = true;

    /// <summary>
    /// Trace every keystroke into debug.log (the Verbose level). On by default while the auto-switcher is
    /// still being tuned: it is what turns "the word was not switched" into a readable cause. Raise it to
    /// Debug for silence — see <c>Logging</c>.
    /// </summary>
    public bool VerboseLog { get; init; } = true;

    public HotkeySettings Hotkeys { get; init; } = new();
    public IReadOnlyList<string> Exclusions { get; init; } = [];
    public IReadOnlyList<AutoReplaceEntry> AutoReplace { get; init; } = [];
}
