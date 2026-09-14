using KeySwitcher.Core.Layout;

namespace KeySwitcher.Tests;

public class LayoutConverterTests
{
    private readonly LayoutConverter _converter = new();

    [Theory]
    [InlineData("q", 'й')]
    [InlineData("w", 'ц')]
    [InlineData("e", 'у')]
    [InlineData("a", 'ф')]
    [InlineData("s", 'і')]
    [InlineData("z", 'я')]
    [InlineData("p", 'з')]
    [InlineData("h", 'р')]
    [InlineData("m", 'ь')]
    public void EnToUa_SingleChar_MapsCorrectly(string input, char expected)
    {
        var result = _converter.Convert(input, Direction.EnToUa);
        Assert.Equal(expected.ToString(), result);
    }

    [Theory]
    [InlineData("й", 'q')]
    [InlineData("ц", 'w')]
    [InlineData("у", 'e')]
    [InlineData("ф", 'a')]
    [InlineData("і", 's')]
    [InlineData("я", 'z')]
    [InlineData("з", 'p')]
    [InlineData("р", 'h')]
    [InlineData("ь", 'm')]
    public void UaToEn_SingleChar_MapsCorrectly(string input, char expected)
    {
        var result = _converter.Convert(input, Direction.UaToEn);
        Assert.Equal(expected.ToString(), result);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("world")]
    [InlineData("keyboard")]
    [InlineData("qwerty")]
    [InlineData("test")]
    public void EnToUa_ThenUaToEn_ReturnsOriginal(string original)
    {
        var ua = _converter.Convert(original, Direction.EnToUa);
        var backToEn = _converter.Convert(ua, Direction.UaToEn);
        Assert.Equal(original, backToEn);
    }

    [Theory]
    [InlineData("привіт")]
    [InlineData("світ")]
    [InlineData("клавіатура")]
    [InlineData("йцукен")]
    public void UaToEn_ThenEnToUa_ReturnsOriginal(string original)
    {
        var en = _converter.Convert(original, Direction.UaToEn);
        var backToUa = _converter.Convert(en, Direction.EnToUa);
        Assert.Equal(original, backToUa);
    }

    [Theory]
    [InlineData("hello", "HELLO")]
    [InlineData("world", "WORLD")]
    [InlineData("qwerty", "QWERTY")]
    public void EnToUa_UpperCaseInput_ResultIsUpperCase(string lower, string upper)
    {
        var lowerResult = _converter.Convert(lower, Direction.EnToUa);
        var upperResult = _converter.Convert(upper, Direction.EnToUa);
        Assert.Equal(lowerResult.ToUpperInvariant(), upperResult);
    }

    [Theory]
    [InlineData("привіт", "ПРИВІТ")]
    [InlineData("світ", "СВІТ")]
    public void UaToEn_UpperCaseInput_ResultIsUpperCase(string lower, string upper)
    {
        var lowerResult = _converter.Convert(lower, Direction.UaToEn);
        var upperResult = _converter.Convert(upper, Direction.UaToEn);
        Assert.Equal(lowerResult.ToUpperInvariant(), upperResult);
    }

    [Fact]
    public void Convert_MixedCase_PreservesFirstLetterUpperCase()
    {
        var result = _converter.Convert("Hello", Direction.EnToUa);
        Assert.True(char.IsUpper(result[0]));
        Assert.True(result[1..].All(char.IsLower));
    }

    [Fact]
    public void Convert_Digits_PassThrough()
    {
        var result = _converter.Convert("hello123", Direction.EnToUa);
        Assert.Equal("123", result[^3..]);
    }

    [Fact]
    public void Convert_Space_PassesThrough()
    {
        var result = _converter.Convert("hi there", Direction.EnToUa);
        Assert.Equal(' ', result[2]);
    }

    [Fact]
    public void Convert_EmptyString_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _converter.Convert(string.Empty, Direction.EnToUa));
        Assert.Equal(string.Empty, _converter.Convert(string.Empty, Direction.UaToEn));
    }

    [Fact]
    public void EnToUa_RoundTrip_WithUpperCase()
    {
        const string original = "Hello World";
        var ua = _converter.Convert(original, Direction.EnToUa);
        var backToEn = _converter.Convert(ua, Direction.UaToEn);
        Assert.Equal(original, backToEn);
    }

    // 'ю' and 'є' live on the '.' and '\'' keys — a word with them looks like "ghfw.'" when typed
    // in the English layout. Cutting the word at that dot was the "працює" bug.
    [Fact]
    public void EnToUa_DotAndApostrophe_BecomeYuAndYe() =>
        Assert.Equal("ює", _converter.Convert(".'", Direction.EnToUa));

    [Fact]
    public void EnToUa_WordWithPunctuationKeys_ConvertsWholeWord() =>
        Assert.Equal("працює", _converter.Convert("ghfw.'", Direction.EnToUa));

    [Fact]
    public void UaToEn_WordWithPunctuationKeys_ConvertsBack() =>
        Assert.Equal("ghfw.'", _converter.Convert("працює", Direction.UaToEn));

    [Theory]
    [InlineData('.')]
    [InlineData(',')]
    [InlineData(';')]
    [InlineData(':')]
    [InlineData('[')]
    [InlineData(']')]
    [InlineData('\'')]
    [InlineData('g')]
    [InlineData('A')]
    public void TypesUaLetter_KeysThatTypeUkrainianLetters_AreTrue(char c) =>
        Assert.True(LayoutConverter.TypesUaLetter(c));

    [Theory]
    [InlineData('!')]
    [InlineData('?')]
    [InlineData('/')]
    [InlineData('-')]
    [InlineData('1')]
    public void TypesUaLetter_OtherKeys_AreFalse(char c) =>
        Assert.False(LayoutConverter.TypesUaLetter(c));

    // Uppercase Ю Б Ж Х Ї Є sit on Shift+'.' ',' ';' '[' ']' '\'', which type '>' '<' ':' '{' '}' '"'
    // in the English layout. Dropping that case turned "Будь ласка" into "будь ласка".
    [Theory]
    [InlineData('>', "Ю")]
    [InlineData('<', "Б")]
    [InlineData(':', "Ж")]
    [InlineData('{', "Х")]
    [InlineData('}', "Ї")]
    [InlineData('"', "Є")]
    public void EnToUa_ShiftedKeys_BecomeUpperCaseLetters(char en, string ua) =>
        Assert.Equal(ua, _converter.Convert(en.ToString(), Direction.EnToUa));

    [Theory]
    [InlineData("Ю", ">")]
    [InlineData("Б", "<")]
    [InlineData("Ж", ":")]
    [InlineData("Х", "{")]
    [InlineData("Ї", "}")]
    [InlineData("Є", "\"")]
    public void UaToEn_UpperCaseLetters_BecomeShiftedKeys(string ua, string en) =>
        Assert.Equal(en, _converter.Convert(ua, Direction.UaToEn));

    [Theory]
    [InlineData("Будь ласка")]
    [InlineData("Юлія")]
    [InlineData("Їжак")]
    [InlineData("Хтось")]
    [InlineData("Життя")]
    [InlineData("Європа")]
    public void UaToEn_ThenEnToUa_LowerCaseRoundTrip(string original) =>
        Assert.Equal(original, _converter.Convert(_converter.Convert(original, Direction.UaToEn), Direction.EnToUa));

    [Fact]
    public void UaToEn_WordStartingWithUppercaseLetter_KeepsCase() =>
        Assert.Equal("<elm kfcrf", _converter.Convert("Будь ласка", Direction.UaToEn));

    // The direction for text of unknown origin (a selection): the script in it decides.
    [Theory]
    [InlineData("привіт", Direction.UaToEn)]
    [InlineData("Привіт!", Direction.UaToEn)]
    [InlineData("ghbdsn", Direction.EnToUa)]
    [InlineData("hello world", Direction.EnToUa)]
    [InlineData("42", Direction.EnToUa)]
    [InlineData("ghbdtn привіт", Direction.UaToEn)]
    public void DirectionForText_DetectsScript(string text, Direction expected) =>
        Assert.Equal(expected, LayoutConverter.DirectionForText(text));

    [Fact]
    public void DirectionForText_WordConversionRoundTrip_RestoresText()
    {
        const string original = "привіт світ";

        string latin = _converter.Convert(original, LayoutConverter.DirectionForText(original));
        string back = _converter.Convert(latin, LayoutConverter.DirectionForText(latin));

        Assert.Equal("ghbdsn cdsn", latin);
        Assert.Equal(original, back); // the second press of the hotkey toggles the text back
    }

    // Layout-faithful, not involutive: on the Ukrainian layout the '.' key types 'ю', so a dot in text
    // typed with the English layout means 'ю' — while a real full stop pressed in the Ukrainian layout
    // sits on another key and has no representation here. Round trips are therefore exact for words but
    // not for punctuation.
    [Fact]
    public void DirectionForText_DotKeyMeansYuOnlyTowardsUkrainian()
    {
        Assert.Equal("руддщю цщкдв", _converter.Convert("hello. world", Direction.EnToUa));
        Assert.Equal("ghbdsn. cdsn", _converter.Convert("привіт. світ", Direction.UaToEn));
    }
}
