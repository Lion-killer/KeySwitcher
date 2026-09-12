using KeySwitcher.Core.Settings;

namespace KeySwitcher.Tests;

/// <summary>
/// The shipped hotkey defaults (user-visible, so a change here is a decision, not an accident).
/// The manual switch is a single key: it is pressed most often and must work on compact keyboards,
/// where the F-row needs Fn. The other three use the F-key row, because Pause/ScrollLock are missing
/// there and Ctrl+Shift+&lt;letter&gt; is taken by every editor and browser.
/// </summary>
public class HotkeyDefaultTests
{
    [Fact]
    public void Defaults_AreTheDocumentedCombinations()
    {
        var hotkeys = new AppSettings().Hotkeys;

        Assert.Equal("Insert", hotkeys.ManualSwitch);
        Assert.Equal("Ctrl+Shift+F11", hotkeys.ChangeCase);
        Assert.Equal("Ctrl+Shift+F10", hotkeys.SelectionLayout);
        Assert.Equal("Ctrl+Shift+F9", hotkeys.Transliterate);
    }

    [Fact]
    public void Defaults_AreAllDistinct()
    {
        var hotkeys = new AppSettings().Hotkeys;
        string[] all = [hotkeys.ManualSwitch, hotkeys.ChangeCase, hotkeys.SelectionLayout, hotkeys.Transliterate];

        Assert.Equal(all.Length, all.Distinct().Count());
    }

    [Fact]
    public void Defaults_WithoutFile_UsesSettingsOrDefaults()
    {
        // No settings.json in a fresh profile: every combination must come from the record initializers.
        var storage = new SettingsStorage(Path.Combine(Path.GetTempPath(), $"keyswitcher-missing-{Guid.NewGuid():N}.json"));

        Assert.Equal(new AppSettings().Hotkeys, storage.Load().Hotkeys);
    }
}
