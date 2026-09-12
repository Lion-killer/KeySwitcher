using KeySwitcher.Core.Native;
using Serilog;

namespace KeySwitcher.Core.Hooks;

/// <summary>
/// Subscribes to the shell's notifications about input-language and active-window changes.
/// </summary>
/// <remarks>
/// Uses <c>RegisterShellHookWindow</c> + the registered "SHELLHOOK" window message. This is an
/// event subscription, not polling, and it is the only mechanism that reports a language change to a
/// process that is not in the foreground (<c>WM_INPUTLANGCHANGE</c> goes to the focused window only):
/// <list type="bullet">
///   <item><c>HSHELL_LANGUAGE</c> — the user switched the input language (Ctrl+Shift, Win+Space…).</item>
///   <item><c>HSHELL_WINDOWACTIVATED</c> — another window took focus. With Windows' per-window input
///     language this alone changes the effective layout.</item>
/// </list>
/// The window belongs to the UI layer (the shell posts those messages to a window of ours); it feeds
/// every message it receives to <see cref="HandleMessage"/>.
/// </remarks>
public sealed class ShellHook : IDisposable
{
    private readonly nint _hwnd;
    private readonly uint _shellHookMessage;
    private bool _disposed;

    /// <summary>Raised on <c>HSHELL_LANGUAGE</c> and <c>HSHELL_WINDOWACTIVATED</c>.</summary>
    public event Action? InputLanguageChanged;

    public ShellHook(nint hwnd)
    {
        _hwnd = hwnd;
        _shellHookMessage = NativeMethods.RegisterWindowMessage(NativeMethods.ShellHookMessage);

        if (!NativeMethods.RegisterShellHookWindow(hwnd))
            Log.Warning("shell hook: RegisterShellHookWindow FAILED (err={Error})", System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        else
            Log.Debug("shell hook: registered");
    }

    /// <summary>
    /// Feeds a window message. Returns true when it was one of ours.
    /// </summary>
    public bool HandleMessage(int message, nint wParam)
    {
        if (_disposed || (uint)message != _shellHookMessage) return false;

        // HSHELL_RUDEAPPACTIVATED is HSHELL_WINDOWACTIVATED with the high bit set — mask it off.
        int code = (int)wParam & ~NativeMethods.HSHELL_HIGHBIT;

        // HSHELL_LANGUAGE is listed for completeness, but the shell does not send it for a Ctrl+Shift
        // switch (verified in debug.log) — the auto-switcher watches the layout on each keystroke instead.
        if (code == NativeMethods.HSHELL_LANGUAGE || code == NativeMethods.HSHELL_WINDOWACTIVATED)
        {
            Log.Debug("shell hook: {Event}", code == NativeMethods.HSHELL_LANGUAGE ? "language" : "window activated");
            InputLanguageChanged?.Invoke();
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        NativeMethods.DeregisterShellHookWindow(_hwnd);
    }
}
