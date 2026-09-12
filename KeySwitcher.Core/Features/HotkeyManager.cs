using KeySwitcher.Core.Native;
using KeySwitcher.Core.Settings;
using Serilog;
using System.Collections.Concurrent;

namespace KeySwitcher.Core.Features;

public sealed class HotkeyManager : IDisposable
{
    // WM_USER (0x0400) used as internal wake-up signal for the message thread
    private const uint WmWakeup = 0x0400;

    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action> _pendingActions = new();
    private readonly ManualResetEventSlim _started = new(false);
    private uint _nativeThreadId;
    private readonly List<int> _registeredIds = new();
    private bool _disposed;

    public event Action<HotkeyId>? HotkeyFired;

    public HotkeyManager()
    {
        _thread = new Thread(RunMessageLoop) { IsBackground = true, Name = "HotkeyManager" };
        _thread.Start();
        _started.Wait();
    }

    /// <summary>
    /// Registers all hotkeys from settings. Throws InvalidOperationException if any key is taken.
    /// Call Unregister() before re-registering with new settings.
    /// </summary>
    public void Register(HotkeySettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RunOnMessageThread(() => DoRegister(settings));
    }

    /// <summary>
    /// Unregisters all currently registered hotkeys.
    /// </summary>
    public void Unregister()
    {
        if (_disposed) return;
        RunOnMessageThread(DoUnregister);
    }

    private void RunOnMessageThread(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingActions.Enqueue(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        NativeMethods.PostThreadMessage(_nativeThreadId, WmWakeup, 0, 0);
        tcs.Task.GetAwaiter().GetResult();
    }

    private void DoRegister(HotkeySettings settings)
    {
        DoUnregister();
        RegisterSingle((int)HotkeyId.ManualSwitch, settings.ManualSwitch);
        RegisterSingle((int)HotkeyId.ChangeCase, settings.ChangeCase);
        RegisterSingle((int)HotkeyId.SelectionLayout, settings.SelectionLayout);
        RegisterSingle((int)HotkeyId.Transliterate, settings.Transliterate);
    }

    private void RegisterSingle(int id, string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey)) return;
        var (modifiers, vk) = ParseHotkey(hotkey);
        if (vk == 0)
        {
            // Unparsable name — log it, otherwise a typo in settings.json looks exactly like
            // "the hotkey does nothing" (the key simply never fires).
            Log.Warning("hotkeys: cannot parse '{Hotkey}' (id={Id}) — skipped", hotkey, id);
            return;
        }

        // MOD_NOREPEAT: holding the key must not machine-gun the action. Essential for single-key
        // hotkeys (Insert/Apps), where auto-repeat would otherwise flip the word back and forth.
        if (!NativeMethods.RegisterHotKey(0, id, modifiers | NativeMethods.MOD_NOREPEAT, vk))
            throw new InvalidOperationException($"Hotkey '{hotkey}' is already registered by another application.");

        Log.Debug("hotkeys: registered '{Hotkey}' (id={Id})", hotkey, id);
        _registeredIds.Add(id);
    }

    private void DoUnregister()
    {
        foreach (var id in _registeredIds)
            NativeMethods.UnregisterHotKey(0, id);

        Log.Verbose("hotkeys: unregistered {Count}", _registeredIds.Count);
        _registeredIds.Clear();
    }

    private void RunMessageLoop()
    {
        _nativeThreadId = NativeMethods.GetCurrentThreadId();
        _started.Set();

        while (true)
        {
            int ret = NativeMethods.GetMessage(out NativeMethods.MSG msg, 0, 0, 0);
            if (ret == 0 || ret == -1) break;

            if (msg.message == NativeMethods.WM_HOTKEY)
            {
                try { HotkeyFired?.Invoke((HotkeyId)(int)msg.wParam); }
                catch { /* prevent crashing the message loop */ }
            }
            else if (msg.message == WmWakeup)
            {
                DrainActions();
            }
        }

        DrainActions(); // process any remaining actions queued before WM_QUIT
    }

    private void DrainActions()
    {
        while (_pendingActions.TryDequeue(out var action))
            action();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pendingActions.Enqueue(DoUnregister);
        NativeMethods.PostThreadMessage(_nativeThreadId, WmWakeup, 0, 0);
        NativeMethods.PostThreadMessage(_nativeThreadId, NativeMethods.WM_QUIT, 0, 0);
        _thread.Join(2000);
        _started.Dispose();
    }

    private static (uint modifiers, uint vk) ParseHotkey(string hotkey)
    {
        uint modifiers = 0;
        string[] parts = hotkey.Split('+');

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].Trim().ToLowerInvariant())
            {
                case "alt": modifiers |= NativeMethods.MOD_ALT; break;
                case "ctrl":
                case "control": modifiers |= NativeMethods.MOD_CONTROL; break;
                case "shift": modifiers |= NativeMethods.MOD_SHIFT; break;
                case "win":
                case "windows": modifiers |= NativeMethods.MOD_WIN; break;
            }
        }

        uint vk = GetVirtualKey(parts[^1].Trim());
        return (modifiers, vk);
    }

    private static uint GetVirtualKey(string key) => key.ToUpperInvariant() switch
    {
        "PAUSE" => 0x13,
        "SCROLL" or "SCROLLLOCK" => 0x91,
        "CAPSLOCK" => 0x14,
        "INSERT" => 0x2D,
        "DELETE" or "DEL" => 0x2E,
        "HOME" => 0x24,
        "END" => 0x23,
        "PAGEUP" or "PGUP" => 0x21,
        "PAGEDOWN" or "PGDN" => 0x22,
        "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
        "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
        "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
        "SPACE" => 0x20,
        "TAB" => 0x09,
        "ENTER" or "RETURN" => 0x0D,
        "BACK" or "BACKSPACE" => 0x08,
        "ESC" or "ESCAPE" => 0x1B,
        // Menu/context-menu key: the only key that is neither a character, nor a navigation key,
        // nor a modifier — so it works as a single-key manual switch without clearing WordBuffer.
        "APPS" or "MENU" or "CONTEXTMENU" => 0x5D,
        _ when key.Length == 1 && char.IsLetterOrDigit(key[0]) => (uint)char.ToUpper(key[0]),
        _ => 0
    };
}
