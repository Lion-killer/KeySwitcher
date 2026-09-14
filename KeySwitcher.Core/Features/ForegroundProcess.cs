using System.Diagnostics;
using KeySwitcher.Core.Native;
using Serilog;
using Serilog.Events;

namespace KeySwitcher.Core.Features;

/// <summary>
/// Identifies the executable that owns a window, as "app.exe" — the form the exclusions list uses.
/// </summary>
public static class ForegroundProcess
{
    public static string? ExeNameOfWindow(nint hwnd)
    {
        if (hwnd == nint.Zero) return null;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName + ".exe";
        }
        catch
        {
            return null; // the process may already be gone
        }
    }

    /// <summary>
    /// Describes the current foreground window as "hwnd:'title'" for trace lines: a replacement landing in
    /// the wrong window is the classic symptom, and the title is what makes it recognisable in the log.
    /// </summary>
    public static string DescribeForeground()
    {
        // Every caller passes this straight into Log.Debug, and arguments are evaluated before the
        // call — Serilog only checks the level inside. Without the guard the foreground window is
        // queried on every replacement even with "detailed logging" off, which is the normal setting.
        if (!Log.IsEnabled(LogEventLevel.Debug)) return "";

        nint hwnd = NativeMethods.GetForegroundWindow();
        Span<char> buffer = stackalloc char[256];
        int length = NativeMethods.GetWindowText(hwnd, buffer, buffer.Length);
        return $"0x{hwnd:X}:'{new string(buffer[..Math.Max(0, length)])}'";
    }
}
