using KeySwitcher.Core.Analysis;
using KeySwitcher.Core.Layout;

namespace KeySwitcher.Tests;

public class LanguageModelTests
{
    private static LanguageModel Build(string[] words) =>
        LanguageModel.Build(KeyboardLanguage.Ukrainian, words);

    [Fact]
    public void Score_UkrainianWord_BeatsLatinSequence()
    {
        var model = Build(["праця", "працювати", "працює", "слово", "вікно", "перемикач", "налаштування"]);

        Assert.True(model.Score("працює") > model.Score("ghfw"),
            "a Ukrainian-looking sequence must score above a Latin one in the Ukrainian model");
    }

    [Fact]
    public void Score_ImpossibleSequence_ScoresBelowTypicalOne()
    {
        var model = Build(["слово", "сонце", "стіл", "синій", "село"]);

        double typical = model.Score("соло");
        double impossible = model.Score("ъыэ");

        Assert.True(typical > impossible, $"typical={typical:F2} impossible={impossible:F2}");
    }

    [Fact]
    public void Score_WordWithoutLetters_IsZero() => Assert.Equal(0, Build(["слово"]).Score("..!!"));

    [Fact]
    public void Score_EmptyWord_IsZero() => Assert.Equal(0, Build(["слово"]).Score(""));

    [Fact]
    public void Score_PunctuationIsSkipped_NotSplittingBigrams()
    {
        var model = Build(["праця", "працює", "працю"]);

        // "ghfw.'" is what the user types for "працює"; punctuation must not hide the bigrams.
        Assert.Equal(model.Score("ghfw'"), model.Score("ghfw'."));
    }

    [Fact]
    public void Build_CountsWordsAndLetters()
    {
        var model = Build(["аб", "вгд"]);

        Assert.Equal(2, model.WordCount);
        Assert.Equal(5, model.LetterCount);
    }

    [Fact]
    public void Build_StrideSamplesTheList()
    {
        var words = Enumerable.Range(0, 100).Select(i => $"слово{i}").ToArray();

        Assert.Equal(100, LanguageModel.Build(KeyboardLanguage.Ukrainian, words).WordCount);
        Assert.Equal(20, LanguageModel.Build(KeyboardLanguage.Ukrainian, words, stride: 5).WordCount);
    }

    [Fact]
    public void Score_IsCaseInsensitive()
    {
        var model = Build(["праця", "слово"]);

        Assert.Equal(model.Score("слово"), model.Score("СЛОВО"));
    }
}
