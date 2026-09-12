using System.Runtime.InteropServices;
using System.Threading.Channels;
using KeySwitcher.Core.Native;
using Serilog;

namespace KeySwitcher.Core.Hooks;

// Must be installed from the UI thread — SetWindowsHookEx(WH_KEYBOARD_LL) requires
// a thread with an active message loop (GetMessage/DispatchMessage). The WPF dispatcher
// thread has one; a Task.Run worker thread does not, so callbacks never arrive.
public sealed class KeyboardHook : IDisposable
{
    private readonly Channel<KeyEvent> _channel;
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc; // keeps delegate alive for GC
    private nint _hookId;

    public ChannelReader<KeyEvent> Reader => _channel.Reader;

    public KeyboardHook()
    {
        _channel = Channel.CreateUnbounded<KeyEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
        _hookProc = HookCallback;
    }

    // Call from the UI thread only.
    public void Install()
    {
        if (_hookId != 0)
            return;

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule!;

        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            Marshal.GetFunctionPointerForDelegate(_hookProc),
            NativeMethods.GetModuleHandle(module.ModuleName!),
            0);

        if (_hookId == 0)
            throw new InvalidOperationException("Failed to install low-level keyboard hook.");
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var ks = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            bool isInjected = (ks.flags & NativeMethods.LLKHF_INJECTED) != 0;
            _channel.Writer.TryWrite(new KeyEvent((int)wParam, (int)ks.vkCode, isInjected));
        }
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = 0;
            // Safe to log here (unlike inside HookCallback): the hook is already gone.
            Log.Verbose("keyboard hook: removed");
        }
        _channel.Writer.TryComplete();
    }
}
