using KeySwitcher.Core.Features;

namespace KeySwitcher.Tests;

/// <summary>
/// Transliteration per КМУ №55 (2010). Reference spellings come from the official table and its
/// examples ("Київ" → "Kyiv", "Запоріжжя" → "Zaporizhzhia").
/// </summary>
public class TransliteratorTests
{
    [Theory]
    [InlineData("Київ", "Kyiv")]
    [InlineData("Харків", "Kharkiv")]
    [InlineData("Львів", "Lviv")]
    [InlineData("Дніпро", "Dnipro")]
    [InlineData("Одеса", "Odesa")]
    [InlineData("Чернівці", "Chernivtsi")]
    [InlineData("Запоріжжя", "Zaporizhzhia")]
    [InlineData("Євген", "Yevhen")]
    [InlineData("Ґанок", "Ganok")]
    [InlineData("Щербак", "Shcherbak")]
    [InlineData("Гончар", "Honchar")]
    public void ToLatin_CityAndNameExamples_MatchOfficialSpelling(string ukrainian, string latin) =>
        Assert.Equal(latin, Transliterator.ToLatin(ukrainian));

    // є ї й ю я are spelled differently inside a word: "Юлія" → "Yuliia", not "Yuliya".
    [Theory]
    [InlineData("Юлія", "Yuliia")]
    [InlineData("Ялта", "Yalta")]
    [InlineData("Кам'янець", "Kamianets")]
    [InlineData("Знаменка", "Znamenka")]
    public void ToLatin_ContextDependentLetters_UseInsideWordSpelling(string ukrainian, string latin) =>
        Assert.Equal(latin, Transliterator.ToLatin(ukrainian));

    [Fact]
    public void ToLatin_ZghDigraph_AvoidsReadingAsZh() =>
        Assert.Equal("Zghurskyi", Transliterator.ToLatin("Згурський"));

    [Theory]
    [InlineData("льон", "lon")]
    [InlineData("об'єкт", "obiekt")]
    [InlineData("п'ять", "piat")]
    public void ToLatin_SoftSignAndApostrophe_AreDropped(string ukrainian, string latin) =>
        Assert.Equal(latin, Transliterator.ToLatin(ukrainian));

    [Fact]
    public void ToLatin_CapitalizedWord_KeepsCase() =>
        Assert.Equal("Kyiv", Transliterator.ToLatin("Київ"));

    [Fact]
    public void ToLatin_AllUpperCaseWord_IsAllUpperCase() =>
        Assert.Equal("KYIV", Transliterator.ToLatin("КИЇВ"));

    [Fact]
    public void ToLatin_Phrase_StartsEachWordFresh() =>
        Assert.Equal("Kyiv, Ukraina", Transliterator.ToLatin("Київ, Україна"));

    [Theory]
    [InlineData("42", "42")]
    [InlineData("hello", "hello")]
    [InlineData("", "")]
    [InlineData("...", "...")]
    public void ToLatin_TextWithoutUkrainian_IsUnchanged(string text, string expected) =>
        Assert.Equal(expected, Transliterator.ToLatin(text));

    [Fact]
    public void ToLatin_MixedText_LeavesLatinAndDigitsAlone() =>
        Assert.Equal("Kyiv 2024", Transliterator.ToLatin("Київ 2024"));

    [Fact]
    public void ToLatin_IsIdempotentForLatinText() =>
        Assert.Equal("Kyiv", Transliterator.ToLatin(Transliterator.ToLatin("Київ")));
}
