using KeySwitcher.Core.Analysis;
using KeySwitcher.Core.Layout;

namespace KeySwitcher.Tests;

public class CharsetAnalyzerTests
{
    private readonly CharsetAnalyzer _analyzer = new();

    // --- HasMixedUnicodeBlocks ---

    [Fact]
    public void HasMixedUnicodeBlocks_PureLatin_ReturnsFalse()
        => Assert.False(_analyzer.HasMixedUnicodeBlocks("hello"));

    [Fact]
    public void HasMixedUnicodeBlocks_PureCyrillic_ReturnsFalse()
        => Assert.False(_analyzer.HasMixedUnicodeBlocks("привіт"));

    [Fact]
    public void HasMixedUnicodeBlocks_MixedLatinAndCyrillic_ReturnsTrue()
        => Assert.True(_analyzer.HasMixedUnicodeBlocks("приvet"));

    [Fact]
    public void HasMixedUnicodeBlocks_EmptyString_ReturnsFalse()
        => Assert.False(_analyzer.HasMixedUnicodeBlocks(""));

    [Fact]
    public void HasMixedUnicodeBlocks_DigitsOnly_ReturnsFalse()
        => Assert.False(_analyzer.HasMixedUnicodeBlocks("12345"));

    [Fact]
    public void HasMixedUnicodeBlocks_PunctuationOnly_ReturnsFalse()
        => Assert.False(_analyzer.HasMixedUnicodeBlocks("!?.,:"));

    // Boundary chars: visually identical letters with different Unicode codepoints
    [Fact]
    public void HasMixedUnicodeBlocks_AllCyrillicCOM_ReturnsFalse()
        => Assert.False(_analyzer.HasMixedUnicodeBlocks("СОМ")); // СОМ — all Cyrillic

    [Fact]
    public void HasMixedUnicodeBlocks_AllLatinCOM_ReturnsFalse()
        => Assert.False(_analyzer.HasMixedUnicodeBlocks("COM")); // all Latin

    [Fact]
    public void HasMixedUnicodeBlocks_CyrillicCWithLatinOM_ReturnsTrue()
        => Assert.True(_analyzer.HasMixedUnicodeBlocks("СOM")); // Cyrillic С + Latin OM

    [Fact]
    public void HasMixedUnicodeBlocks_LatinCWithCyrillicOM_ReturnsTrue()
        => Assert.True(_analyzer.HasMixedUnicodeBlocks("CОМ")); // Latin C + Cyrillic ОМ

    // --- IsDefinitelyWrongLanguage ---

    [Theory]
    [InlineData('й', KeyboardLanguage.English)]
    [InlineData('а', KeyboardLanguage.English)]
    [InlineData('Щ', KeyboardLanguage.English)]
    [InlineData('С', KeyboardLanguage.English)] // Cyrillic С
    [InlineData('О', KeyboardLanguage.English)] // Cyrillic О
    [InlineData('Р', KeyboardLanguage.English)] // Cyrillic Р
    public void IsDefinitelyWrongLanguage_CyrillicInEnglish_ReturnsTrue(char c, KeyboardLanguage lang)
        => Assert.True(_analyzer.IsDefinitelyWrongLanguage(c, lang));

    [Theory]
    [InlineData('a', KeyboardLanguage.Ukrainian)]
    [InlineData('q', KeyboardLanguage.Ukrainian)]
    [InlineData('Z', KeyboardLanguage.Ukrainian)]
    [InlineData('C', KeyboardLanguage.Ukrainian)] // Latin C (looks like Cyrillic С)
    [InlineData('O', KeyboardLanguage.Ukrainian)] // Latin O (looks like Cyrillic О)
    [InlineData('P', KeyboardLanguage.Ukrainian)] // Latin P (looks like Cyrillic Р)
    public void IsDefinitelyWrongLanguage_LatinInUkrainian_ReturnsTrue(char c, KeyboardLanguage lang)
        => Assert.True(_analyzer.IsDefinitelyWrongLanguage(c, lang));

    [Theory]
    [InlineData('а', KeyboardLanguage.Ukrainian)]
    [InlineData('й', KeyboardLanguage.Ukrainian)]
    [InlineData('С', KeyboardLanguage.Ukrainian)] // Cyrillic С
    [InlineData('О', KeyboardLanguage.Ukrainian)] // Cyrillic О
    public void IsDefinitelyWrongLanguage_CyrillicInUkrainian_ReturnsFalse(char c, KeyboardLanguage lang)
        => Assert.False(_analyzer.IsDefinitelyWrongLanguage(c, lang));

    [Theory]
    [InlineData('a', KeyboardLanguage.English)]
    [InlineData('Z', KeyboardLanguage.English)]
    [InlineData('C', KeyboardLanguage.English)] // Latin C
    [InlineData('O', KeyboardLanguage.English)] // Latin O
    public void IsDefinitelyWrongLanguage_LatinInEnglish_ReturnsFalse(char c, KeyboardLanguage lang)
        => Assert.False(_analyzer.IsDefinitelyWrongLanguage(c, lang));

    [Theory]
    [InlineData('5', KeyboardLanguage.English)]
    [InlineData('5', KeyboardLanguage.Ukrainian)]
    [InlineData('!', KeyboardLanguage.English)]
    [InlineData('!', KeyboardLanguage.Ukrainian)]
    public void IsDefinitelyWrongLanguage_NeutralChars_ReturnsFalse(char c, KeyboardLanguage lang)
        => Assert.False(_analyzer.IsDefinitelyWrongLanguage(c, lang));

    [Theory]
    [InlineData('й', KeyboardLanguage.Unknown)]
    [InlineData('a', KeyboardLanguage.Unknown)]
    public void IsDefinitelyWrongLanguage_UnknownLanguage_ReturnsFalse(char c, KeyboardLanguage lang)
        => Assert.False(_analyzer.IsDefinitelyWrongLanguage(c, lang));

    // Boundary chars: C/С, O/О, P/Р — visually identical, different Unicode blocks
    [Fact]
    public void BoundaryChar_CyrillicC_IsWrongInEnglish()
        => Assert.True(_analyzer.IsDefinitelyWrongLanguage('С', KeyboardLanguage.English));

    [Fact]
    public void BoundaryChar_LatinC_IsWrongInUkrainian()
        => Assert.True(_analyzer.IsDefinitelyWrongLanguage('C', KeyboardLanguage.Ukrainian));

    [Fact]
    public void BoundaryChar_CyrillicO_IsWrongInEnglish()
        => Assert.True(_analyzer.IsDefinitelyWrongLanguage('О', KeyboardLanguage.English));

    [Fact]
    public void BoundaryChar_LatinO_IsWrongInUkrainian()
        => Assert.True(_analyzer.IsDefinitelyWrongLanguage('O', KeyboardLanguage.Ukrainian));

    [Fact]
    public void BoundaryChar_CyrillicP_IsWrongInEnglish()
        => Assert.True(_analyzer.IsDefinitelyWrongLanguage('Р', KeyboardLanguage.English));

    [Fact]
    public void BoundaryChar_LatinP_IsWrongInUkrainian()
        => Assert.True(_analyzer.IsDefinitelyWrongLanguage('P', KeyboardLanguage.Ukrainian));
}
