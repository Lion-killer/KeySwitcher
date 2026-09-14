using System.Diagnostics;
using KeySwitcher.Core.Native;
using Serilog;

namespace KeySwitcher.Core.Features;

/// <summary>An open top-level window: the program that owns it, as "app.exe", and its title.</summary>
public readonly record struct OpenWindow(string ExeName, string Title)
{
    /// <summary>What the exclusions picker shows, e.g. "chrome.exe — Google Chrome".</summary>
    public string Display => $"{ExeName} — {Title}";
}

/// <summary>
/// Lists the windows that are open right now, so the exclusions tab can offer them by name instead of
/// making the user hunt for an .exe on disk or point at the window with a crosshair.
/// </summary>
public static class OpenWindows
{
    /// <summary>
    /// Visible top-level windows that have a title, one entry per program (the exclusion list compares
    /// .exe names, so ten Chrome windows would only repeat the same entry), sorted by program name.
    /// KeySwitcher itself is skipped — excluding it makes no sense.
    /// </summary>
    public static IReadOnlyList<OpenWindow> Enumerate()
    {
        string ownExe = Process.GetCurrentProcess().ProcessName + ".exe";
        var found = new List<OpenWindow>();

        // GetTopWindow/GetWindow(GW_HWNDNEXT) walk the top-level windows in z-order. A plain loop needs
        // no callback delegate, which keeps LibraryImport happy (see SetWindowsHookEx in NativeMethods).
        for (nint hwnd = NativeMethods.GetTopWindow(nint.Zero);
             hwnd != nint.Zero;
             hwnd = NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDNEXT))
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) continue;

            string title = TitleOf(hwnd);
            if (title.Length == 0) continue; // message-only and helper windows have no title

            string? exeName = ForegroundProcess.ExeNameOfWindow(hwnd);
            if (exeName is null || string.Equals(exeName, ownExe, StringComparison.OrdinalIgnoreCase)) continue;

            if (found.Any(w => string.Equals(w.ExeName, exeName, StringComparison.OrdinalIgnoreCase))) continue;

            found.Add(new OpenWindow(exeName, title));
        }

        found.Sort((a, b) => string.Compare(a.ExeName, b.ExeName, StringComparison.OrdinalIgnoreCase));
        Log.Debug("open windows: {Programs} programs offered", found.Count);
        return found;
    }

    private static string TitleOf(nint hwnd)
    {
        int length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0) return "";

        // Titles are short; anything longer than a stack buffer goes to the heap.
        Span<char> buffer = length < 256 ? stackalloc char[length + 1] : new char[length + 1];
        int copied = NativeMethods.GetWindowText(hwnd, buffer, buffer.Length);
        return copied <= 0 ? "" : new string(buffer[..copied]);
    }
}
