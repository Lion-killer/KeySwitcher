using KeySwitcher.Core.Features;
using KeySwitcher.Core.Layout;
using KeySwitcher.Core.Settings;

namespace KeySwitcher.Tests;

/// <summary>
/// The one-shot auto-switch pause requested by the user: after a cursor key (arrows, Home/End, PgUp/PgDn),
/// after the manual-switch hotkey, or after the user switched the layout themselves (Ctrl+Shift,
/// Win+Space), the next automatic replacement is skipped — once. The same guard tells the user's own
/// layout switch apart from KeySwitcher's (the shell reports neither of them).
/// </summary>
public class AutoSwitchGuardTests
{
    private const nint HWND = 0x1234;

    [Fact]
    public void NewGuard_IsNotSuppressed() =>
        Assert.False(new AutoSwitchGuard().IsSuppressed);

    [Fact]
    public void Suppress_ThenConsume_SkipsOnceOnly()
    {
        var guard = new AutoSwitchGuard();

        guard.Suppress();

        Assert.True(guard.IsSuppressed);
        Assert.True(guard.ConsumeIfSuppressed());  // the switch right after the cursor key is skipped
        Assert.False(guard.IsSuppressed);
        Assert.False(guard.ConsumeIfSuppressed()); // and the following one happens as usual
    }

    [Fact]
    public void Consume_WithoutSuppress_DoesNotSkip()
    {
        var guard = new AutoSwitchGuard();

        Assert.False(guard.ConsumeIfSuppressed());
    }

    [Fact]
    public void Suppress_Twice_StillSkipsOnlyOnce()
    {
        var guard = new AutoSwitchGuard();

        guard.Suppress();
        guard.Suppress();

        Assert.True(guard.ConsumeIfSuppressed());
        Assert.False(guard.ConsumeIfSuppressed());
    }

    [Fact]
    public void Clear_DisarmsThePause()
    {
        var guard = new AutoSwitchGuard();

        guard.Suppress();
        guard.Clear();

        Assert.False(guard.IsSuppressed);
        Assert.False(guard.ConsumeIfSuppressed());
    }

    // The shell reports nothing when the user presses Ctrl+Shift (verified in debug.log), so the layout
    // read on each keystroke is the only source: a change in the same window is the user's switch —
    // unless it is exactly the layout KeySwitcher itself put in place.
    [Fact]
    public void LayoutChange_InTheSameWindow_IsTheUsers()
    {
        var guard = new AutoSwitchGuard();

        Assert.False(guard.ObserveLayout(KeyboardLanguage.English, HWND, pauseEnabled: true)); // first read
        Assert.True(guard.ObserveLayout(KeyboardLanguage.Ukrainian, HWND, pauseEnabled: true));
        Assert.True(guard.IsSuppressed);
    }

    [Fact]
    public void SameLayoutAgain_IsNotAChange()
    {
        var guard = new AutoSwitchGuard();

        guard.ObserveLayout(KeyboardLanguage.English, HWND, pauseEnabled: true);

        Assert.False(guard.ObserveLayout(KeyboardLanguage.English, HWND, pauseEnabled: true));
        Assert.False(guard.IsSuppressed);
    }

    [Fact]
    public void LayoutChange_InAnotherWindow_IsNotAUserSwitch()
    {
        var guard = new AutoSwitchGuard();

        guard.ObserveLayout(KeyboardLanguage.English, HWND, pauseEnabled: true);

        // Windows keeps the layout per window: this is a different window's layout, not a switch.
        Assert.False(guard.ObserveLayout(KeyboardLanguage.Ukrainian, HWND + 1, pauseEnabled: true));
        Assert.False(guard.IsSuppressed);
    }

    [Fact]
    public void OwnSwitch_IsNotTakenForTheUsers()
    {
        var guard = new AutoSwitchGuard();
        guard.ObserveLayout(KeyboardLanguage.English, HWND, pauseEnabled: true);

        guard.MarkOwnSwitch(KeyboardLanguage.Ukrainian);

        // The next keystroke reads back the layout we just switched to: our fix, not the user's choice.
        Assert.False(guard.ObserveLayout(KeyboardLanguage.Ukrainian, HWND, pauseEnabled: true));
        Assert.False(guard.IsSuppressed);

        // The mark is good for that one keystroke only: the user's own switch back counts again.
        Assert.True(guard.ObserveLayout(KeyboardLanguage.English, HWND, pauseEnabled: true));
        Assert.True(guard.IsSuppressed);
    }

    // Our switch may be ignored by the app (Chrome ignores WM_INPUTLANGCHANGEREQUEST): the mark then
    // names the layout we did not get, and the user's own switch must still be recognised.
    [Fact]
    public void OwnSwitch_ThatDidNotLand_DoesNotSwallowTheUsersSwitch()
    {
        var guard = new AutoSwitchGuard();
        guard.ObserveLayout(KeyboardLanguage.English, HWND, pauseEnabled: true);

        guard.MarkOwnSwitch(KeyboardLanguage.English);

        Assert.True(guard.ObserveLayout(KeyboardLanguage.Ukrainian, HWND, pauseEnabled: true));
        Assert.True(guard.IsSuppressed);
    }

    [Fact]
    public void LayoutChange_WithThePauseDisabled_DoesNotSuppress()
    {
        var guard = new AutoSwitchGuard();
        guard.ObserveLayout(KeyboardLanguage.English, HWND, pauseEnabled: false);

        Assert.False(guard.ObserveLayout(KeyboardLanguage.Ukrainian, HWND, pauseEnabled: false));
        Assert.False(guard.IsSuppressed);
    }

    [Fact]
    public void Settings_BothPausesAreOnByDefault()
    {
        var settings = new AppSettings();

        Assert.True(settings.SkipSwitchAfterCaretMove);
        Assert.True(settings.SkipSwitchAfterManualSwitch);
    }

    [Fact]
    public void Settings_PausesSurviveJsonRoundTrip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"keyswitcher-guard-{Guid.NewGuid():N}.json");
        try
        {
            var storage = new SettingsStorage(path);
            storage.Save(new AppSettings { SkipSwitchAfterCaretMove = false });

            AppSettings loaded = storage.Load();

            Assert.False(loaded.SkipSwitchAfterCaretMove);
            Assert.True(loaded.SkipSwitchAfterManualSwitch);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
