using KeySwitcher.Core.Features;

namespace KeySwitcher.Tests;

public class CapsLockFixerTests
{
    private readonly CapsLockFixer _fixer = new();

    // ---- ShouldFix ----

    [Fact]
    public void ShouldFix_EmptyString_ReturnsFalse()
    {
        Assert.False(_fixer.ShouldFix(""));
    }

    [Theory]
    [InlineData("h")]
    [InlineData("hE")]
    public void ShouldFix_TooShort_ReturnsFalse(string word)
    {
        Assert.False(_fixer.ShouldFix(word));
    }

    [Theory]
    [InlineData("hELLO")]
    [InlineData("hEL")]
    [InlineData("hE3L")]
    [InlineData("eFG")]
    public void ShouldFix_FirstLowerRestTwoOrMoreUpper_ReturnsTrue(string word)
    {
        Assert.True(_fixer.ShouldFix(word));
    }

    [Fact]
    public void ShouldFix_UkrainianCyrillicPattern_ReturnsTrue()
    {
        // п is lowercase Cyrillic, Р, И, В, І, Т are uppercase
        Assert.True(_fixer.ShouldFix("пРИВІТ"));
    }

    [Fact]
    public void ShouldFix_MinimalThreeChars_ReturnsTrue()
    {
        // hEL: first lowercase, E and L uppercase
        Assert.True(_fixer.ShouldFix("hEL"));
    }

    [Fact]
    public void ShouldFix_AllUppercase_ReturnsFalse()
    {
        Assert.False(_fixer.ShouldFix("HELLO"));
    }

    [Fact]
    public void ShouldFix_AllLowercase_ReturnsFalse()
    {
        Assert.False(_fixer.ShouldFix("hello"));
    }

    [Fact]
    public void ShouldFix_OnlyOneUppercaseInRest_ReturnsFalse()
    {
        // hEllo: E is uppercase (1 total) — not enough
        Assert.False(_fixer.ShouldFix("hEllo"));
    }

    [Fact]
    public void ShouldFix_FirstCharDigit_ReturnsFalse()
    {
        Assert.False(_fixer.ShouldFix("3EFG"));
    }

    [Fact]
    public void ShouldFix_FirstCharUppercase_ReturnsFalse()
    {
        Assert.False(_fixer.ShouldFix("HELLO"));
        Assert.False(_fixer.ShouldFix("Hello"));
    }

    [Fact]
    public void ShouldFix_WordWithApostropheAndUppercase_ReturnsTrue()
    {
        // п'ЯЧ: п lowercase, apostrophe ignored, Я and Ч uppercase (2)
        Assert.True(_fixer.ShouldFix("п'ЯЧ"));
    }

    [Fact]
    public void ShouldFix_MixedUpperLower_TwoUpper_ReturnsTrue()
    {
        // hELlo: h lower, E(1) L(2) l l o → 2 uppercase → true
        Assert.True(_fixer.ShouldFix("hELlo"));
    }

    // ---- InvertCase ----

    [Fact]
    public void InvertCase_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", _fixer.InvertCase(""));
    }

    [Fact]
    public void InvertCase_LowercaseWord_ReturnsUppercase()
    {
        Assert.Equal("HELLO", _fixer.InvertCase("hello"));
    }

    [Fact]
    public void InvertCase_UppercaseWord_ReturnsLowercase()
    {
        Assert.Equal("hello", _fixer.InvertCase("HELLO"));
    }

    [Fact]
    public void InvertCase_CapsLockPattern_FixesToNormal()
    {
        // hELLO → Hello (typical CapsLock mistake)
        Assert.Equal("Hello", _fixer.InvertCase("hELLO"));
    }

    [Fact]
    public void InvertCase_MixedCase_InvertsEachChar()
    {
        Assert.Equal("HeLlO", _fixer.InvertCase("hElLo"));
    }

    [Fact]
    public void InvertCase_UkrainianLowercase_ReturnsUppercase()
    {
        Assert.Equal("ПРИВІТ", _fixer.InvertCase("привіт"));
    }

    [Fact]
    public void InvertCase_UkrainianCapsLockPattern_FixesToNormal()
    {
        // пРИВІТ → Привіт
        Assert.Equal("Привіт", _fixer.InvertCase("пРИВІТ"));
    }

    [Fact]
    public void InvertCase_DigitsUnchanged()
    {
        Assert.Equal("ABC123def", _fixer.InvertCase("abc123DEF"));
    }

    [Fact]
    public void InvertCase_ApostropheUnchanged()
    {
        // п'ЯЧ → П'яч
        Assert.Equal("П'яч", _fixer.InvertCase("п'ЯЧ"));
    }

    [Fact]
    public void InvertCase_SingleLowerChar_ReturnsUpper()
    {
        Assert.Equal("A", _fixer.InvertCase("a"));
    }

    [Fact]
    public void InvertCase_SingleUpperChar_ReturnsLower()
    {
        Assert.Equal("a", _fixer.InvertCase("A"));
    }

    [Fact]
    public void InvertCase_RoundTrip_GivesOriginal()
    {
        const string original = "hELLO wORLD";
        Assert.Equal(original, _fixer.InvertCase(_fixer.InvertCase(original)));
    }
}
