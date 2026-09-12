using System.Runtime.InteropServices;
using KeySwitcher.Core.Native;
using Microsoft.Win32;
using Serilog;

namespace KeySwitcher.Core.Layout;

public enum KeyboardLanguage { Ukrainian, English, Unknown }

public sealed class LayoutDetector
{
    private const int LangUkrainian = 0x0422;
    private const int LangEnglish   = 0x0409;

    // Virtual keys of the system layout-toggle chords.
    private const byte VkShift   = 0x10;
    private const byte VkControl = 0x11;
    private const byte VkMenu    = 0x12; // Alt
    private const byte VkLWin    = 0x5B;
    private const byte VkSpace   = 0x20;
    private const byte VkOem3    = 0xC0; // `~

    private const int PollStepMs = 25;
    private const int PollTimeoutMs = 300;

    public KeyboardLanguage GetCurrentLanguage()
    {
        uint threadId = InputThreadOf(NativeMethods.GetForegroundWindow());
        return HklToLanguage(NativeMethods.GetKeyboardLayout(threadId));
    }

    /// <summary>
    /// Window that actually receives typed characters: the focused control of the foreground thread.
    /// </summary>
    /// <remarks>
    /// The top-level window is the wrong source: on Windows 11 Notepad's text control (RichEditD2DPT)
    /// lives in its own thread, whose input language is the one the user types with, while the
    /// top-level window's thread keeps its old language forever. VS Code has both in one thread, which
    /// is why the bug showed up only in Notepad.
    /// </remarks>
    public static nint FocusedWindow(nint fallbackHwnd)
    {
        var info = new NativeMethods.GUITHREADINFO { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };

        if (NativeMethods.GetGUIThreadInfo(0, ref info) && info.hwndFocus != nint.Zero)
            return info.hwndFocus;

        return fallbackHwnd != nint.Zero ? fallbackHwnd : info.hwndActive;
    }

    /// <summary>Thread whose input language applies to the focused window of <paramref name="hwnd"/>.</summary>
    /// <remarks>
    /// The thread id is the *return value* of GetWindowThreadProcessId; its out parameter is the
    /// process id. Passing the process id to GetKeyboardLayout yields 0, which maps to Unknown and
    /// silently breaks both switching decisions and the tray flag.
    /// </remarks>
    public static uint InputThreadOf(nint hwnd) =>
        NativeMethods.GetWindowThreadProcessId(FocusedWindow(hwnd), out _);

    /// <summary>
    /// Switches the foreground window's input language to the opposite of the current one.
    /// Returns true when the change was observed.
    /// </summary>
    public Task<bool> SwitchLayoutAsync(nint hwnd, CancellationToken ct = default)
    {
        int current = LangIdOf(InputThreadOf(hwnd));
        KeyboardLanguage target = current == LangUkrainian ? KeyboardLanguage.English
            : current == LangEnglish ? KeyboardLanguage.Ukrainian
            : KeyboardLanguage.English; // unknown layout: an explicit toggle lands on English
        return SwitchToAsync(hwnd, target, ct);
    }

    /// <summary>
    /// Switches the foreground window's input language to <paramref name="target"/>.
    /// Returns true when that language is active afterwards (also when it already was).
    /// </summary>
    /// <remarks>
    /// Mechanism, in order of increasing intrusiveness (verified on Windows 11, per-window input language):
    /// 1. Post WM_INPUTLANGCHANGEREQUEST. No key injection at all, so it cannot disturb what the user is
    ///    typing — but only classic Win32 apps honour it (Chromium/Electron ignore it).
    /// 2. Inject the system's own toggle chord from HKCU\Keyboard Layout\Toggle. The shell performs the
    ///    switch, so it works in every app. Skipped while the user physically holds a modifier: our
    ///    injected key-up would release their held Ctrl/Shift mid-chord.
    /// <c>AttachThreadInput</c> + <c>ActivateKeyboardLayout</c> does NOT work — it changes only the
    /// calling thread's layout.
    /// Both paths are asynchronous, so the language is re-read (polled) before success is claimed.
    /// </remarks>
    public async Task<bool> SwitchToAsync(nint hwnd, KeyboardLanguage target, CancellationToken ct = default)
    {
        if (target == KeyboardLanguage.Unknown) return false;

        uint fgThread = InputThreadOf(hwnd);
        int current = LangIdOf(fgThread);
        int targetLangId = target == KeyboardLanguage.Ukrainian ? LangUkrainian : LangEnglish;
        if (current == targetLangId) return true;

        // Never call LoadKeyboardLayout here: it registers an extra layout in the system, which then
        // shows up as a duplicate entry in the Win+Space switcher (seen in practice: a second
        // "українська" with HKL 0xF0A80422). Use the handle of a layout the user already has.
        nint targetHkl = FindLoadedLayout(targetLangId);
        if (targetHkl != nint.Zero)
            NativeMethods.PostMessage(FocusedWindow(hwnd), NativeMethods.WM_INPUTLANGCHANGEREQUEST, 0, targetHkl);
        else
            Log.Debug("    SwitchLayout: no loaded layout for lang {Language:X4} — message path skipped", targetLangId);

        if (await WaitForLanguageAsync(fgThread, targetLangId, "WM_INPUTLANGCHANGEREQUEST", ct)) return true;

        if (IsModifierHeld())
        {
            // The user is typing a chord of their own right now — injecting ours would break it.
            Log.Debug("    SwitchLayout skipped: a modifier is held down");
            return false;
        }

        var (chord, chordName) = ToggleChord();
        InjectChord(chord);
        if (await WaitForLanguageAsync(fgThread, targetLangId, chordName, ct)) return true;

        Log.Debug("    SwitchLayout FAILED {Current:X4}->{Target:X4} (thread {Thread})", current, targetLangId, fgThread);
        return false;
    }

    // Ctrl, Shift, Alt, Win — held physically, not just logically.
    private static bool IsModifierHeld() =>
        IsDown(0xA0) || IsDown(0xA1) || IsDown(VkShift) ||   // LShift / RShift / Shift
        IsDown(0xA2) || IsDown(0xA3) || IsDown(VkControl) || // LCtrl / RCtrl / Ctrl
        IsDown(0xA4) || IsDown(0xA5) || IsDown(VkMenu) ||    // LAlt / RAlt / Alt
        IsDown(VkLWin) || IsDown(0x5C);                      // LWin / RWin

    private static bool IsDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private static async Task<bool> WaitForLanguageAsync(
        uint threadId, int target, string mechanism, CancellationToken ct)
    {
        for (int waited = 0; waited <= PollTimeoutMs; waited += PollStepMs)
        {
            if (LangIdOf(threadId) == target)
            {
                Log.Debug("    SwitchLayout {Mechanism} -> {Language}", mechanism, HklToLanguage(NativeMethods.GetKeyboardLayout(threadId)));
                return true;
            }

            await Task.Delay(PollStepMs, ct).ConfigureAwait(false);
        }

        return false;
    }

    private static int LangIdOf(uint threadId) => (int)((long)NativeMethods.GetKeyboardLayout(threadId) & 0xFFFF);

    /// <summary>
    /// Handle of the layout the user already has for <paramref name="langId"/>, or <c>nint.Zero</c>.
    /// </summary>
    /// <remarks>
    /// Prefers a "preloaded" layout, whose high word repeats the language id (e.g. 0x4220422), over a
    /// transient one hot-loaded by LoadKeyboardLayout (e.g. 0xF0A80422) — both carry the same language,
    /// but only the former is the layout the user actually chose.
    /// </remarks>
    private static nint FindLoadedLayout(int langId)
    {
        int count = NativeMethods.GetKeyboardLayoutList(0, null);
        if (count <= 0) return nint.Zero;

        var layouts = new nint[count];
        int written = NativeMethods.GetKeyboardLayoutList(count, layouts);
        nint fallback = nint.Zero;

        for (int i = 0; i < written; i++)
        {
            long hkl = (long)layouts[i];
            if ((int)(hkl & 0xFFFF) != langId) continue;
            if ((int)((hkl >> 16) & 0xFFFF) == langId) return layouts[i];
            fallback = layouts[i];
        }

        return fallback;
    }

    // Keys are injected one at a time with a small gap: all-at-once makes the shell miss the chord.
    private static void InjectChord(byte[] keys)
    {
        foreach (byte key in keys)
        {
            NativeMethods.keybd_event(key, 0, 0, 0);
            Thread.Sleep(20);
        }

        for (int i = keys.Length - 1; i >= 0; i--)
        {
            NativeMethods.keybd_event(keys[i], 0, NativeMethods.KEYEVENTF_KEYUP, 0);
            Thread.Sleep(20);
        }
    }

    // HKCU\Keyboard Layout\Toggle — "Language Hotkey": 1 = Alt+Shift, 2 = Ctrl+Shift,
    // 3 = not assigned, 4 = grave accent. Win+Space is the always-on modern shortcut.
    private static (byte[] keys, string name) ToggleChord()
    {
        int setting = ReadToggleSetting();

        return setting switch
        {
            1 => ([VkMenu, VkShift], "Alt+Shift"),
            2 => ([VkControl, VkShift], "Ctrl+Shift"),
            4 => ([VkOem3], "grave"),
            _ => ([VkLWin, VkSpace], "Win+Space"),
        };
    }

    private static int ReadToggleSetting()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Keyboard Layout\Toggle");
            return key?.GetValue("Language Hotkey") as int?
                ?? key?.GetValue("Hotkey") as int?
                ?? 0;
        }
        catch
        {
            return 0; // fall back to Win+Space
        }
    }

    private static KeyboardLanguage HklToLanguage(nint hkl)
    {
        int langId = (int)((long)hkl & 0xFFFF);
        return langId switch
        {
            LangUkrainian => KeyboardLanguage.Ukrainian,
            LangEnglish   => KeyboardLanguage.English,
            _             => KeyboardLanguage.Unknown
        };
    }
}
