using KeySwitcher.Core.Features;

namespace KeySwitcher.Tests;

/// <summary>
/// The elevation check exists for one reason: Windows (UIPI) gives a non-elevated hook no keystrokes from
/// an elevated window. Garbage handles and dead processes must answer "not elevated" rather than crash —
/// a wrong warning is worse than a missing one.
/// </summary>
public class ElevationTests
{
    [Fact]
    public void IsWindowElevated_WithoutAWindow_IsFalse()
    {
        Assert.False(Elevation.IsWindowElevated(nint.Zero));
    }

    [Fact]
    public void IsWindowElevated_ForAHandleThatIsNotAWindow_IsFalse()
    {
        Assert.False(Elevation.IsWindowElevated(0xBADF00D));
    }

    [Fact]
    public void IsElevated_DoesNotThrow_AndAgreesWithTheFocusedWindowCheck()
    {
        // With KeySwitcher itself elevated the hook reaches everything, so nothing can be out of reach.
        if (Elevation.IsElevated())
            Assert.False(Elevation.IsFocusedWindowOutOfReach());
    }
}
