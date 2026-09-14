using KeySwitcher.Core.Analysis;

namespace KeySwitcher.Tests;

public class DictionaryAnalyzerTests
{
    private static DictionaryAnalyzer CreateAnalyzer(IEnumerable<string> words) =>
        DictionaryAnalyzer.Create(words);

    [Fact]
    public void Contains_KnownWord_ReturnsTrue()
    {
        var analyzer = CreateAnalyzer(["hello", "world", "test"]);
        Assert.True(analyzer.Contains("hello"));
    }

    [Fact]
    public void Contains_UnknownWord_ReturnsFalse()
    {
        var analyzer = CreateAnalyzer(["hello", "world"]);
        Assert.False(analyzer.Contains("unknown"));
    }

    [Fact]
    public void Contains_CaseInsensitive_ReturnsTrue()
    {
        var analyzer = CreateAnalyzer(["Hello"]);
        Assert.True(analyzer.Contains("hello"));
        Assert.True(analyzer.Contains("HELLO"));
        Assert.True(analyzer.Contains("Hello"));
    }

    [Fact]
    public void Contains_WordInDifferentCase_ReturnsTrue()
    {
        var analyzer = CreateAnalyzer(["world"]);
        Assert.True(analyzer.Contains("World"));
        Assert.True(analyzer.Contains("WORLD"));
    }

    [Fact]
    public void Contains_EmptyWord_ReturnsFalse()
    {
        var analyzer = CreateAnalyzer(["hello"]);
        Assert.False(analyzer.Contains(""));
    }

    [Fact]
    public void Contains_EmptyDictionary_ReturnsFalse()
    {
        var analyzer = CreateAnalyzer([]);
        Assert.False(analyzer.Contains("hello"));
    }

    [Fact]
    public void Contains_UkrainianWord_ReturnsTrue()
    {
        var analyzer = CreateAnalyzer(["привіт", "світ", "м'яч"]);
        Assert.True(analyzer.Contains("привіт"));
        Assert.True(analyzer.Contains("м'яч"));
    }

    [Fact]
    public void Contains_UkrainianWord_CaseInsensitive()
    {
        var analyzer = CreateAnalyzer(["Привіт"]);
        Assert.True(analyzer.Contains("привіт"));
        Assert.True(analyzer.Contains("ПРИВІТ"));
    }

    [Fact]
    public void Contains_UkrainianWord_NotInDictionary_ReturnsFalse()
    {
        var analyzer = CreateAnalyzer(["привіт", "світ"]);
        Assert.False(analyzer.Contains("хмара"));
    }

    [Fact]
    public void Create_WithDuplicates_DeduplicatesWords()
    {
        var analyzer = CreateAnalyzer(["hello", "hello", "HELLO"]);
        Assert.True(analyzer.Contains("hello"));
    }

    [Fact]
    public void WithExtraWords_MakesThemKnown_AndKeepsTheOriginalOnes()
    {
        var analyzer = CreateAnalyzer(["hello", "world"]);

        DictionaryAnalyzer extended = analyzer.WithExtraWords(["Згурський", "KeySwitcher"]);

        Assert.True(extended.Contains("Згурський"));
        Assert.True(extended.Contains("keyswitcher")); // the lookup is case-insensitive, as before
        Assert.True(extended.Contains("hello"));
        Assert.Equal(["hello", "world", "Згурський", "KeySwitcher"], extended.Words);

        // The original instance is untouched: the statistics were built from its word list.
        Assert.False(analyzer.Contains("Згурський"));
    }

    [Fact]
    public void WithExtraWords_WithoutWords_ReturnsTheSameInstance()
    {
        var analyzer = CreateAnalyzer(["hello"]);

        Assert.Same(analyzer, analyzer.WithExtraWords([]));
        Assert.Same(analyzer, analyzer.WithExtraWords(["  ", ""]));
    }

    [Fact]
    public void Contains_WordWithApostrophe_ReturnsTrue()
    {
        var analyzer = CreateAnalyzer(["м'яч", "п'ять", "об'єкт"]);
        Assert.True(analyzer.Contains("м'яч"));
        Assert.True(analyzer.Contains("п'ять"));
        Assert.True(analyzer.Contains("об'єкт"));
    }

    [Fact]
    public void Contains_PartialWord_ReturnsFalse()
    {
        var analyzer = CreateAnalyzer(["hello"]);
        Assert.False(analyzer.Contains("hell"));
        Assert.False(analyzer.Contains("ello"));
    }

    [Fact]
    public void Contains_WordWithSpaces_ReturnsFalse()
    {
        var analyzer = CreateAnalyzer(["hello"]);
        Assert.False(analyzer.Contains("hello "));
        Assert.False(analyzer.Contains(" hello"));
    }

    [Fact]
    public async Task LoadEnglishAsync_EmbeddedResource_ContainsCommonWords()
    {
        var analyzer = await DictionaryAnalyzer.LoadEnglishAsync();
        Assert.True(analyzer.Contains("hello"));
        Assert.True(analyzer.Contains("World")); // case-insensitive
        Assert.False(analyzer.Contains("zzqqxx"));
    }

    [Fact]
    public async Task LoadUkrainianAsync_EmbeddedResource_ContainsCommonWords()
    {
        var analyzer = await DictionaryAnalyzer.LoadUkrainianAsync();
        Assert.True(analyzer.Contains("слово"));
        Assert.True(analyzer.Contains("об'єкт"));   // apostrophe preserved during cleaning
        Assert.True(analyzer.Contains("ПРИВІТ"));    // case-insensitive
        Assert.False(analyzer.Contains("350658"));   // hunspell count header was stripped
    }
}
