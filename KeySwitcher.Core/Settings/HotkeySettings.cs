namespace KeySwitcher.Core.Settings;

public sealed record HotkeySettings
{
    // The manual switch is a single key on purpose: it is the gesture repeated most often, and it has
    // to work on compact keyboards, where the F-row needs Fn. The price is that Insert is taken
    // globally — the overwrite-mode toggle no longer reaches applications (deliberate trade-off).
    public string ManualSwitch { get; init; } = "Insert";

    // Pause/ScrollLock are absent on compact and laptop keyboards, so the other three live in the
    // F-key row: Ctrl+Shift+F11 / F10 / F9 are free in Windows and most applications.
    public string ChangeCase { get; init; } = "Ctrl+Shift+F11";
    public string SelectionLayout { get; init; } = "Ctrl+Shift+F10";
    public string Transliterate { get; init; } = "Ctrl+Shift+F9";
}
