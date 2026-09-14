using System.Diagnostics;
using KeySwitcher.Core.Features;

namespace KeySwitcher.Tests;

/// <summary>
/// The exclusions picker offers the programs whose windows are open right now, so the enumeration must
/// survive whatever is on the desktop (helper windows, dead processes, empty titles) without throwing.
/// </summary>
public class OpenWindowsTests
{
    [Fact]
    public void Enumerate_NeverListsKeySwitcherItself()
    {
        string ownExe = Process.GetCurrentProcess().ProcessName + ".exe";

        IReadOnlyList<OpenWindow> windows = OpenWindows.Enumerate();

        Assert.DoesNotContain(windows, w => string.Equals(w.ExeName, ownExe, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Enumerate_ReturnsUsableEntries()
    {
        foreach (OpenWindow window in OpenWindows.Enumerate())
        {
            Assert.EndsWith(".exe", window.ExeName, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(window.Title));
            Assert.Contains(window.ExeName, window.Display, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Enumerate_ListsEachProgramOnce()
    {
        IReadOnlyList<OpenWindow> windows = OpenWindows.Enumerate();

        Assert.Equal(
            windows.Count,
            windows.Select(w => w.ExeName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
