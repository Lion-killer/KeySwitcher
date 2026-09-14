using KeySwitcher.Core.Features;
using KeySwitcher.Core.Layout;
using KeySwitcher.Core.Settings;

namespace KeySwitcher.Tests;

public class AutoReplacerTests
{
    private static readonly AutoReplaceEntry[] s_entries =
    [
        new("нп", "наприклад"),
        new("тб", "тобто"),
        new("zs", "зі скорочення"),
    ];

    private static AutoReplacer Build(string typed)
    {
        var buffer = new WordBuffer();
        foreach (var c in typed)
            buffer.Add(c);
        return new AutoReplacer(buffer);
    }

    [Theory]
    [InlineData(' ')]
    [InlineData('\t')]
    [InlineData('\r')]
    public void IsTrigger_WordTerminators_AreTrue(char delimiter) =>
        Assert.True(AutoReplacer.IsTrigger(delimiter));

    [Theory]
    [InlineData('.')]
    [InlineData(',')]
    [InlineData('!')]
    [InlineData('?')]
    [InlineData(';')]
    [InlineData(':')]
    public void IsTrigger_Punctuation_IsFalse(char delimiter) =>
        Assert.False(AutoReplacer.IsTrigger(delimiter));

    [Fact]
    public void FindReplacement_KnownShortcut_ReturnsReplacement() =>
        Assert.Equal("наприклад", AutoReplacer.FindReplacement("нп", s_entries));

    [Fact]
    public void FindReplacement_UnknownWord_ReturnsNull() =>
        Assert.Null(AutoReplacer.FindReplacement("привіт", s_entries));

    [Fact]
    public void FindReplacement_DifferentCase_StillMatches() =>
        Assert.Equal("тобто", AutoReplacer.FindReplacement("Тб", s_entries));

    [Fact]
    public void FindReplacement_EmptyWord_ReturnsNull() =>
        Assert.Null(AutoReplacer.FindReplacement("", s_entries));

    [Fact]
    public void FindReplacement_NoEntries_ReturnsNull() =>
        Assert.Null(AutoReplacer.FindReplacement("нп", []));

    [Fact]
    public void FindReplacement_EmptyShortcutRow_IsIgnored() =>
        Assert.Null(AutoReplacer.FindReplacement("", [new AutoReplaceEntry("", "")]));

    [Fact]
    public void TryGetReplacement_ShortcutThenSpace_Replaces()
    {
        var replacer = Build("нп");

        Assert.True(replacer.TryGetReplacement(' ', s_entries, out string replacement));
        Assert.Equal("наприклад", replacement);
    }

    [Fact]
    public void TryGetReplacement_ShortcutThenEnter_Replaces()
    {
        var replacer = Build("тб");

        Assert.True(replacer.TryGetReplacement('\r', s_entries, out string replacement));
        Assert.Equal("тобто", replacement);
    }

    [Fact]
    public void TryGetReplacement_ShortcutThenPunctuation_DoesNotReplace()
    {
        var replacer = Build("нп");

        Assert.False(replacer.TryGetReplacement(',', s_entries, out string replacement));
        Assert.Equal("", replacement);
    }

    [Fact]
    public void TryGetReplacement_OrdinaryWord_DoesNotReplace()
    {
        var replacer = Build("слово");

        Assert.False(replacer.TryGetReplacement(' ', s_entries, out string replacement));
        Assert.Equal("", replacement);
    }

    [Fact]
    public void TryGetReplacement_AfterDelimiter_BufferIsEmptyAndNothingMatches()
    {
        var replacer = Build("нп ");
        Assert.False(replacer.TryGetReplacement(' ', s_entries, out _)); // already consumed by the space
    }

    [Fact]
    public void FindReplacement_ShortcutIsPrefixOfWord_DoesNotMatch() =>
        Assert.Null(AutoReplacer.FindReplacement("нпп", s_entries));
}
