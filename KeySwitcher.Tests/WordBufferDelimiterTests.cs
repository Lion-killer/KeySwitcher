using KeySwitcher.Core.Layout;

namespace KeySwitcher.Tests;

/// <summary>
/// The delimiter typed after a word stays on screen between the caret and that word. The manual
/// switch has to delete and re-insert it, otherwise it eats the space and leaves the first letter
/// of the replacement behind ("привіт " → "пghbdsn").
/// </summary>
public class WordBufferDelimiterTests
{
    private static WordBuffer Build(string input)
    {
        var buf = new WordBuffer();
        foreach (var c in input)
            buf.Add(c);
        return buf;
    }

    [Fact]
    public void RecentDelimiter_BeforeAnythingTyped_IsNull()
    {
        var buf = new WordBuffer();
        Assert.Null(buf.RecentDelimiter);
    }

    [Fact]
    public void RecentDelimiter_WordInProgress_IsNull()
    {
        var buf = Build("ghbd");
        Assert.Null(buf.RecentDelimiter);
    }

    [Fact]
    public void RecentDelimiter_AfterSpaceTyped_IsSpace()
    {
        var buf = Build("ghbdsn ");
        Assert.Equal(' ', buf.RecentDelimiter);
    }

    [Fact]
    public void RecentDelimiter_EnterAndTab_AreRemembered()
    {
        Assert.Equal('\r', Build("ghbdsn\r").RecentDelimiter);
        Assert.Equal('\t', Build("ghbdsn\t").RecentDelimiter);
    }

    [Fact]
    public void Reset_WithDelimiter_RemembersBoth()
    {
        var buf = Build("ghbdsn");

        buf.Reset(' ');

        Assert.Equal("ghbdsn", buf.RecentWord());
        Assert.Equal(' ', buf.RecentDelimiter);
    }

    [Fact]
    public void Reset_WithoutDelimiter_LeavesNoDelimiter()
    {
        var buf = Build("ghbdsn ");
        buf.Reset(' ');
        buf.Add('a'); // new word in progress — the old delimiter belongs to the old word

        buf.Reset(); // shortcut chord with a word in the buffer

        Assert.Equal("a", buf.RecentWord());
        Assert.Null(buf.RecentDelimiter);
    }

    [Fact]
    public void Reset_EmptyBuffer_KeepsPreviousWordAndDelimiter()
    {
        var buf = Build("ghbdsn ");
        buf.Reset(' ');

        buf.Reset(); // nothing dropped — the state on screen is unchanged

        Assert.Equal("ghbdsn", buf.RecentWord());
        Assert.Equal(' ', buf.RecentDelimiter);
    }

    [Fact]
    public void Backspace_EmptyBuffer_ForgetsDelimiter()
    {
        var buf = Build("ghbdsn ");
        buf.Reset(' ');

        buf.Backspace(); // user deleted the space we recorded

        Assert.Equal("ghbdsn", buf.RecentWord());
        Assert.Null(buf.RecentDelimiter);
    }

    [Fact]
    public void Backspace_InsideWord_KeepsDelimiterState()
    {
        var buf = Build("ghbd");

        buf.Backspace();

        Assert.Equal("ghb", buf.CurrentWord);
        Assert.Null(buf.RecentDelimiter);
    }

    [Fact]
    public void Invalidate_DropsDelimiter()
    {
        var buf = Build("ghbdsn ");
        buf.Reset(' ');

        buf.Invalidate(); // caret moved: even the delimiter is not where we left it

        Assert.Equal("", buf.RecentWord());
        Assert.Null(buf.RecentDelimiter);
    }

    [Fact]
    public void Adopt_ReplacesWordAndDelimiter()
    {
        var buf = Build("ghbdsn ");
        buf.Reset(' ');

        buf.Adopt("привіт", ' '); // what ManualSwitcher writes into the document

        Assert.Equal("", buf.CurrentWord);
        Assert.Equal("привіт", buf.RecentWord());
        Assert.Equal(' ', buf.RecentDelimiter);
    }

    [Fact]
    public void Adopt_WithoutDelimiter_ClearsPreviousDelimiter()
    {
        var buf = Build("ghbdsn ");
        buf.Reset(' ');

        buf.Adopt("привіт", null); // switched mid-word — no delimiter in the document

        Assert.Equal("привіт", buf.RecentWord());
        Assert.Null(buf.RecentDelimiter);
    }

    [Fact]
    public void Adopt_ThenType_AccumulatesNewWord()
    {
        var buf = Build("ghbdsn ");
        buf.Reset(' ');
        buf.Adopt("привіт", ' ');

        buf.Add('х');

        Assert.Equal("х", buf.CurrentWord); // the adopted word is only used while the buffer is empty
    }

    // '.' and ',' are delimiters in the English layout but 'ю' and 'б' in the Ukrainian one, so a
    // mistyped "працює" arrives as "ghfw.'" — the buffer has to survive that dot.
    [Theory]
    [InlineData('.')]
    [InlineData(',')]
    [InlineData(';')]
    public void AddAsLetter_DelimiterChar_StaysInWord(char c)
    {
        var buf = Build("ghfw");

        buf.AddAsLetter(c);

        Assert.Equal("ghfw" + c, buf.CurrentWord);
        Assert.Null(buf.RecentDelimiter);
    }

    [Fact]
    public void AddAsLetter_WholeMistypedWord_StaysTogether()
    {
        var buf = Build("ghfw");
        buf.AddAsLetter('.');
        buf.Add('\''); // apostrophe is a letter here too

        Assert.Equal("ghfw.'", buf.CurrentWord);
    }

    [Fact]
    public void Add_RealDelimiter_StillResetsWord()
    {
        var buf = Build("ghfw");

        buf.Add(' '); // the caller chose punctuation, so the word really ended

        Assert.Equal("", buf.CurrentWord);
        Assert.Equal("ghfw", buf.RecentWord());
        Assert.Equal(' ', buf.RecentDelimiter);
    }
}
