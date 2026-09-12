using System.Runtime.InteropServices;
using KeySwitcher.Core.Native;

namespace KeySwitcher.Core.Features;

/// <summary>
/// Answers whether a process runs with an elevated (administrator) token. KeySwitcher needs this for one
/// thing only: Windows does not hand low-level keyboard input from an elevated window to a non-elevated
/// hook, and refuses to inject input into it (UIPI) — so in such a window the auto-switcher is blind, and
/// the user deserves to know why it looks broken there.
/// </summary>
public static class Elevation
{
    /// <summary>True when KeySwitcher itself runs as administrator.</summary>
    public static bool IsElevated() => HasElevatedToken(NativeMethods.GetCurrentProcess(), closeHandle: false);

    /// <summary>
    /// True when the window belongs to an elevated process. A handle of zero, a dead process or a process
    /// protected so tightly that even its token cannot be opened all answer <c>false</c>: a wrong warning
    /// is worse than a missing one.
    /// </summary>
    public static bool IsWindowElevated(nint hwnd)
    {
        if (hwnd == nint.Zero) return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return false;

        nint process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == nint.Zero) return false;

        try
        {
            return HasElevatedToken(process, closeHandle: true);
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    /// <summary>
    /// True when the window in focus is beyond KeySwitcher's reach: it is elevated while we are not, so the
    /// hook receives no keystrokes from it and injected input would be blocked.
    /// </summary>
    public static bool IsFocusedWindowOutOfReach() =>
        !IsElevated() && IsWindowElevated(NativeMethods.GetForegroundWindow());

    private static bool HasElevatedToken(nint process, bool closeHandle)
    {
        if (!NativeMethods.OpenProcessToken(process, NativeMethods.TOKEN_QUERY, out nint token)) return false;

        try
        {
            int size = Marshal.SizeOf<NativeMethods.TOKEN_ELEVATION>();
            if (!NativeMethods.GetTokenInformation(token, NativeMethods.TokenElevation, out NativeMethods.TOKEN_ELEVATION info, (uint)size, out _))
                return false;

            return info.TokenIsElevated != 0;
        }
        finally
        {
            if (closeHandle) NativeMethods.CloseHandle(token);
        }
    }
}
