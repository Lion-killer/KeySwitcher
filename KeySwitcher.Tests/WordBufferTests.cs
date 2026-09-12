using KeySwitcher.Core.Layout;

namespace KeySwitcher.Tests;

public class WordBufferTests
{
    private static WordBuffer Build(string input)
    {
        var buf = new WordBuffer();
        foreach (var c in input)
            buf.Add(c);
        return buf;
    }

    [Fact]
    public void InitialState_IsEmpty()
    {
        var buf = new WordBuffer();
        Assert.Equal("", buf.CurrentWord);
        Assert.Equal(0, buf.Length);
    }

    [Fact]
    public void Add_LatinChars_AccumulatesWord()
    {
        var buf = Build("hello");
        Assert.Equal("hello", buf.CurrentWord);
        Assert.Equal(5, buf.Length);
    }

    [Fact]
    public void Add_CyrillicChars_AccumulatesWord()
    {
        var buf = Build("привіт");
        Assert.Equal("привіт", buf.CurrentWord);
    }

    [Theory]
    [InlineData(' ')]
    [InlineData('\r')]
    [InlineData('\n')]
    [InlineData('\t')]
    [InlineData('.')]
    [InlineData(',')]
    [InlineData('!')]
    [InlineData('?')]
    [InlineData(';')]
    [InlineData(':')]
    public void Add_Delimiter_ResetsBuffer(char delimiter)
    {
        var buf = Build("word");
        buf.Add(delimiter);
        Assert.Equal("", buf.CurrentWord);
    }

    [Fact]
    public void Add_Apostrophe_DoesNotResetBuffer()
    {
        // Ukrainian: м'яч — apostrophe is part of the word
        var buf = Build("м'яч");
        Assert.Equal("м'яч", buf.CurrentWord);
    }

    [Fact]
    public void Add_ApostropheInMidWord_PreservesFullWord()
    {
        var buf = Build("об'єкт");
        Assert.Equal("об'єкт", buf.CurrentWord);
    }

    [Fact]
    public void Add_ApostropheAtStart_Accumulates()
    {
        var buf = Build("'test");
        Assert.Equal("'test", buf.CurrentWord);
    }

    [Fact]
    public void Add_AfterDelimiter_StartsNewWord()
    {
        var buf = Build("first second");
        Assert.Equal("second", buf.CurrentWord);
    }

    [Fact]
    public void Add_MultipleDelimiters_BufferRemainsEmpty()
    {
        var buf = Build("word   ");
        Assert.Equal("", buf.CurrentWord);
        Assert.Equal(0, buf.Length);
    }

    [Fact]
    public void Backspace_RemovesLastChar()
    {
        var buf = Build("hello");
        buf.Backspace();
        Assert.Equal("hell", buf.CurrentWord);
        Assert.Equal(4, buf.Length);
    }

    [Fact]
    public void Backspace_EmptyBuffer_DoesNotThrow()
    {
        var buf = new WordBuffer();
        var ex = Record.Exception(() => buf.Backspace());
        Assert.Null(ex);
        Assert.Equal("", buf.CurrentWord);
    }

    [Fact]
    public void Backspace_OnSingleChar_LeavesEmpty()
    {
        var buf = Build("a");
        buf.Backspace();
        Assert.Equal("", buf.CurrentWord);
        Assert.Equal(0, buf.Length);
    }

    [Fact]
    public void Backspace_MultipleTimesUntilEmpty_NeverThrows()
    {
        var buf = Build("ab");
        buf.Backspace();
        buf.Backspace();
        buf.Backspace(); // extra backspace on empty
        Assert.Equal("", buf.CurrentWord);
    }

    [Fact]
    public void Reset_ClearsBuffer()
    {
        var buf = Build("hello");
        buf.Reset();
        Assert.Equal("", buf.CurrentWord);
        Assert.Equal(0, buf.Length);
    }

    [Fact]
    public void Reset_EmptyBuffer_DoesNotThrow()
    {
        var buf = new WordBuffer();
        var ex = Record.Exception(() => buf.Reset());
        Assert.Null(ex);
    }

    [Fact]
    public void RecentWord_AfterReset_KeepsDroppedWord()
    {
        var buf = Build("ghbdsn");

        buf.Reset(); // what a Ctrl/Alt chord or Insert does before WM_HOTKEY arrives

        Assert.Equal("", buf.CurrentWord);
        Assert.Equal("ghbdsn", buf.RecentWord());
    }

    [Fact]
    public void RecentWord_AfterDelimiter_KeepsFinishedWord()
    {
        var buf = Build("ghbdsn ");
        Assert.Equal("ghbdsn", buf.RecentWord());
    }

    [Fact]
    public void RecentWord_NoResetYet_IsEmpty()
    {
        var buf = Build("ghbdsn");
        Assert.Equal("", buf.RecentWord());
    }

    [Fact]
    public void RecentWord_EmptyReset_KeepsPreviousWord()
    {
        var buf = Build("ghbdsn");
        buf.Reset();
        buf.Reset(); // nothing to drop — must not wipe the stashed word

        Assert.Equal("ghbdsn", buf.RecentWord());
    }

    [Fact]
    public void Invalidate_AfterReset_DropsRecentWord()
    {
        var buf = Build("ghbdsn");
        buf.Reset();
        buf.Invalidate(); // caret moved: the word is not at the caret anymore

        Assert.Equal("", buf.CurrentWord);
        Assert.Equal("", buf.RecentWord());
    }

    [Fact]
    public void Invalidate_ThenType_DropsPreviousWord()
    {
        var buf = Build("ghbdsn");
        buf.Invalidate();
        buf.Add('a');

        Assert.Equal("a", buf.CurrentWord);
        Assert.Equal("", buf.RecentWord());
    }

    [Fact]
    public void IsDelimiter_SpaceAndPunctuation_ReturnsTrue()
    {
        var buf = new WordBuffer();
        foreach (var c in new[] { ' ', '\r', '\n', '\t', '.', ',', '!', '?', ';', ':' })
            Assert.True(buf.IsDelimiter(c), $"Expected '{c}' to be a delimiter");
    }

    [Fact]
    public void IsDelimiter_Apostrophe_ReturnsFalse()
    {
        var buf = new WordBuffer();
        Assert.False(buf.IsDelimiter('\''));
    }

    [Theory]
    [InlineData('a')]
    [InlineData('Z')]
    [InlineData('й')]
    [InlineData('Ї')]
    [InlineData('0')]
    [InlineData('-')]
    public void IsDelimiter_RegularChars_ReturnsFalse(char c)
    {
        var buf = new WordBuffer();
        Assert.False(buf.IsDelimiter(c));
    }

    [Fact]
    public void Length_MatchesCurrentWordLength()
    {
        var buf = Build("привіт");
        Assert.Equal(buf.CurrentWord.Length, buf.Length);
    }

    [Fact]
    public void ComplexSequence_TypingUkrainianWordWithApostrophe()
    {
        // Simulate typing п'ять with a backspace correction
        var buf = new WordBuffer();
        foreach (var c in "п'ят")
            buf.Add(c);
        buf.Backspace(); // correct 'т' to 'т'... actually remove last
        buf.Add('т');
        buf.Add('ь');
        Assert.Equal("п'ять", buf.CurrentWord);
    }

    [Fact]
    public void AddThenReset_ThenAddAgain_Works()
    {
        var buf = new WordBuffer();
        foreach (var c in "first")
            buf.Add(c);
        buf.Reset();
        foreach (var c in "second")
            buf.Add(c);
        Assert.Equal("second", buf.CurrentWord);
    }

    [Fact]
    public void Sentence_LastWordRetained()
    {
        // "hello world" — after space, 'w','o','r','l','d' accumulate
        var buf = Build("hello world");
        Assert.Equal("world", buf.CurrentWord);
    }
}
