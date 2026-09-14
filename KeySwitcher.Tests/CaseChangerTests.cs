using KeySwitcher.Core.Features;

namespace KeySwitcher.Tests;

/// <summary>
/// The change-case cycle (PLAN 3.8): lower → UPPER → Title → lower. The current case is read from the
/// text, so a counter cannot desync when the user edits between presses.
/// </summary>
public class CaseChangerTests
{
    [Theory]
    [InlineData("привіт", "ПРИВІТ")]
    [InlineData("hello", "HELLO")]
    [InlineData("hello world", "HELLO WORLD")]
    public void Next_LowerCase_GoesUpperCase(string text, string expected) =>
        Assert.Equal(expected, CaseChanger.Next(text));

    [Theory]
    [InlineData("ПРИВІТ", "Привіт")]
    [InlineData("HELLO", "Hello")]
    [InlineData("HELLO WORLD", "Hello World")]
    public void Next_UpperCase_GoesTitleCase(string text, string expected) =>
        Assert.Equal(expected, CaseChanger.Next(text));

    [Theory]
    [InlineData("Привіт", "привіт")]
    [InlineData("Hello World", "hello world")]
    [InlineData("hELLO", "hello")]
    public void Next_TitleOrMixed_GoesLowerCase(string text, string expected) =>
        Assert.Equal(expected, CaseChanger.Next(text));

    [Fact]
    public void Next_FullCycle_ReturnsToStart()
    {
        string start = "привіт";

        string second = CaseChanger.Next(start);
        string third = CaseChanger.Next(second);
        string fourth = CaseChanger.Next(third);

        Assert.Equal("ПРИВІТ", second);
        Assert.Equal("Привіт", third);
        Assert.Equal(start, fourth);
    }

    [Theory]
    [InlineData("")]
    [InlineData("42")]
    [InlineData("...")]
    [InlineData("!? ")]
    public void Next_TextWithoutLetters_IsUnchanged(string text) =>
        Assert.Equal(text, CaseChanger.Next(text));

    [Theory]
    [InlineData("м'яч", "М'яч")]
    [InlineData("будь-який", "Будь-який")]
    [InlineData("об'єкт", "Об'єкт")]
    public void ToTitle_ApostropheAndHyphenStayInsideWord(string text, string expected) =>
        Assert.Equal(expected, CaseChanger.ToTitle(text));

    [Fact]
    public void ToTitle_UpperTailIsLowered() =>
        Assert.Equal("Привіт", CaseChanger.ToTitle("пРИВІТ"));

    [Fact]
    public void ToTitle_Sentence_SeparatesWordsOnSpacesAndPunctuation() =>
        Assert.Equal("Привіт, Світ!", CaseChanger.ToTitle("привіт, світ!"));

    [Theory]
    [InlineData("привіт", true)]
    [InlineData("привіт 42", true)]
    [InlineData("Привіт", false)]
    [InlineData("42", false)]
    public void IsAllLower_MatchesOnlyLowerCaseText(string text, bool expected) =>
        Assert.Equal(expected, CaseChanger.IsAllLower(text));

    [Theory]
    [InlineData("ПРИВІТ", true)]
    [InlineData("ПРИВІТ 42", true)]
    [InlineData("Привіт", false)]
    [InlineData("42", false)]
    public void IsAllUpper_MatchesOnlyUpperCaseText(string text, bool expected) =>
        Assert.Equal(expected, CaseChanger.IsAllUpper(text));

    [Fact]
    public void ToLower_UkrainianUpperCase_IsLowered() =>
        Assert.Equal("їжак", CaseChanger.ToLower("ЇЖАК"));

    [Fact]
    public void ToUpper_UkrainianLowerCase_IsUpperCased() =>
        Assert.Equal("ЇЖАК", CaseChanger.ToUpper("їжак"));
}
