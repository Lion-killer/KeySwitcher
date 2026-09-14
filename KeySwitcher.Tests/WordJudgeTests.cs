using KeySwitcher.Core.Analysis;
using KeySwitcher.Core.Layout;

namespace KeySwitcher.Tests;

/// <summary>
/// The wrong-layout decision: letter combinations first, dictionaries as a fallback. Numbers used in
/// the assertions come from the measurement harness (see <see cref="ThresholdTuningTests"/>).
/// </summary>
public class WordJudgeTests
{
    // Building the statistics from ~700k words takes ~0.5 s — once per test run.
    private static readonly Lazy<Task<JudgeFixture>> s_fixture = new(BuildFixtureAsync);

    private sealed record JudgeFixture(
        WordJudge Judge,
        LayoutConverter Converter,
        DictionaryAnalyzer UaDictionary,
        DictionaryAnalyzer EnDictionary,
        IReadOnlyList<string> UaWords,
        IReadOnlyList<string> EnWords);

    private static async Task<JudgeFixture> BuildFixtureAsync()
    {
        var converter = new LayoutConverter();
        var ua = await DictionaryAnalyzer.LoadUkrainianAsync();
        var en = await DictionaryAnalyzer.LoadEnglishAsync();

        var judge = new WordJudge(
            LanguageModel.Build(KeyboardLanguage.Ukrainian, ua.Words),
            LanguageModel.Build(KeyboardLanguage.English, en.Words),
            converter, ua, en);

        return new JudgeFixture(judge, converter, ua, en, ua.Words, en.Words);
    }

    private static Task<JudgeFixture> Fixture() => s_fixture.Value;

    /// <summary>
    /// A personal word is the user saying "this is a word, leave it alone". It only has to reach the
    /// lookup of the current language — the statistics are not consulted for a known word at all.
    /// </summary>
    [Fact]
    public void PersonalWord_KnownToTheCurrentLanguage_IsNeverSwitched()
    {
        // Its own judge with tiny dictionaries: this test replaces them, and the fixture is shared.
        var uaWords = new List<string> { "привіт", "світ" };
        var enWords = new List<string> { "hello", "world" };
        var judge = new WordJudge(
            LanguageModel.Build(KeyboardLanguage.Ukrainian, uaWords),
            LanguageModel.Build(KeyboardLanguage.English, enWords),
            new LayoutConverter(),
            DictionaryAnalyzer.Create(uaWords),
            DictionaryAnalyzer.Create(enWords));

        // "ghbdtn" typed in the English layout is a mistyped "привіт" — the converted form is a listed word.
        Assert.True(judge.Judge("ghbdtn", KeyboardLanguage.English).ShouldSwitch);

        // Until the user adds it to their own list: then it is a known English word and stays as typed.
        judge.SetDictionaries(
            DictionaryAnalyzer.Create(uaWords),
            DictionaryAnalyzer.Create(enWords).WithExtraWords(["ghbdtn"]));

        Assert.False(judge.Judge("ghbdtn", KeyboardLanguage.English).ShouldSwitch);
    }

    /// <summary>
    /// "Is this word already known?" is what keeps the dictionary offer honest: a word the dictionaries
    /// already hold is never suggested again. Same lists, same answer as the lookup itself.
    /// </summary>
    [Fact]
    public void IsKnown_AnswersPerLanguage_AndCaseInsensitively()
    {
        var judge = new WordJudge(
            LanguageModel.Build(KeyboardLanguage.Ukrainian, ["привіт"]),
            LanguageModel.Build(KeyboardLanguage.English, ["hello"]),
            new LayoutConverter(),
            DictionaryAnalyzer.Create(["привіт"]),
            DictionaryAnalyzer.Create(["hello"]));

        Assert.True(judge.IsKnown("привіт", KeyboardLanguage.Ukrainian));
        Assert.True(judge.IsKnown("HELLO", KeyboardLanguage.English));
        Assert.False(judge.IsKnown("привіт", KeyboardLanguage.English));
        Assert.False(judge.IsKnown("працює", KeyboardLanguage.Ukrainian));
    }

    // "ghfw.'" is українське "працює" typed with the English layout active: 'ю' lives on '.',
    // 'є' on the apostrophe. Neither the full word nor its parts are in any dictionary.
    [Fact]
    public async Task MistypedUkrainianWord_SwitchesByStatistics()
    {
        var f = await Fixture();
        Assert.False(f.UaDictionary.Contains("працює")); // the dictionary really cannot help here

        var judgement = f.Judge.Judge("ghfw.'", KeyboardLanguage.English);

        Assert.Equal("працює", judgement.Converted);
        Assert.True(judgement.ByStatistics);
        Assert.True(judgement.ShouldSwitch);
    }

    [Fact]
    public async Task MistypedEnglishWord_Switches()
    {
        var f = await Fixture();
        var judgement = f.Judge.Judge("ghbdsn", KeyboardLanguage.English);

        Assert.Equal("привіт", judgement.Converted);
        Assert.True(judgement.ShouldSwitch);
    }

    [Theory]
    [InlineData("привіт")]
    [InlineData("працює")] // not even in the dictionary — statistics alone must keep it
    [InlineData("налаштування")]
    public async Task CorrectUkrainianWord_IsKept(string word)
    {
        var f = await Fixture();
        var judgement = f.Judge.Judge(word, KeyboardLanguage.Ukrainian);

        Assert.False(judgement.ShouldSwitch);
        Assert.True(judgement.Margin < WordJudge.DefaultMargin);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("keyboard")]
    [InlineData("switching")]
    public async Task CorrectEnglishWord_IsKept(string word)
    {
        var f = await Fixture();
        Assert.False(f.Judge.Judge(word, KeyboardLanguage.English).ShouldSwitch);
    }

    [Fact]
    public async Task KnownWord_IsNeverTouchedEvenWhenStatisticsDisagree()
    {
        var f = await Fixture();
        var judgement = f.Judge.Judge("слово", KeyboardLanguage.Ukrainian);

        Assert.True(judgement.KnownInCurrent);
        Assert.False(judgement.ShouldSwitch);
    }

    // Two letters carry too little signal — the guard must keep the statistics out of it.
    [Fact]
    public async Task ShortWord_StatisticsAreNotUsed()
    {
        var f = await Fixture();
        var judgement = f.Judge.Judge("lj", KeyboardLanguage.English);

        Assert.Equal(2, judgement.Letters);
        Assert.False(judgement.ByStatistics);

        // Fallback path: "lj" → "до" is a listed word, so the switch happens through the dictionary.
        Assert.Equal("до", judgement.Converted);
        Assert.True(judgement.KnownConverted);
        Assert.True(judgement.ShouldSwitch);
    }

    // Known trade-off: the English list contains two-letter junk ("yt", "nb", "wt"), and a word
    // listed in the current language is never touched. "yt" therefore stays — a short word with the
    // wrong layout is left for the manual hotkey.
    [Fact]
    public async Task ShortWord_KnownInCurrentLanguage_IsLeftAlone()
    {
        var f = await Fixture();
        Assert.True(f.EnDictionary.Contains("yt"));
        Assert.Equal("не", f.Judge.Judge("yt", KeyboardLanguage.English).Converted);
        Assert.False(f.Judge.Judge("yt", KeyboardLanguage.English).ShouldSwitch);
    }

    [Fact]
    public async Task ShortWord_NothingRecognisable_IsKept()
    {
        var f = await Fixture();
        Assert.False(f.Judge.Judge("zz", KeyboardLanguage.English).ShouldSwitch);
    }

    [Fact]
    public async Task ConvertToOther_ForManualSwitch_AlwaysConverts()
    {
        var f = await Fixture();
        Assert.Equal("працює", f.Judge.ConvertToOther("ghfw.'", KeyboardLanguage.English));
        Assert.Equal("ghfw.'", f.Judge.ConvertToOther("працює", KeyboardLanguage.Ukrainian));
    }

    [Theory]
    [InlineData(KeyboardLanguage.Ukrainian, 0.95)]
    [InlineData(KeyboardLanguage.English, 0.95)]
    public async Task Accuracy_WrongLayoutText_IsDetected(KeyboardLanguage intended, double expected)
    {
        var f = await Fixture();
        var words = intended == KeyboardLanguage.Ukrainian ? f.UaWords : f.EnWords;
        Direction wrong = intended == KeyboardLanguage.Ukrainian ? Direction.UaToEn : Direction.EnToUa;
        KeyboardLanguage typedLayout = intended == KeyboardLanguage.Ukrainian
            ? KeyboardLanguage.English
            : KeyboardLanguage.Ukrainian;

        int checkedWords = 0, detected = 0;
        for (int i = 0; i < words.Count; i += 379)
        {
            string word = words[i];
            if (word.Length is < 4 or > 12) continue;

            checkedWords++;
            if (f.Judge.Judge(f.Converter.Convert(word, wrong), typedLayout).ShouldSwitch)
                detected++;
        }

        Assert.True(checkedWords > 300, $"sample too small: {checkedWords}");
        Assert.True(detected / (double)checkedWords >= expected,
            $"detected {detected}/{checkedWords} = {detected / (double)checkedWords:P2}, expected >= {expected:P0}");
    }

    [Theory]
    [InlineData(KeyboardLanguage.Ukrainian)]
    [InlineData(KeyboardLanguage.English)]
    public async Task Accuracy_CorrectlyTypedText_IsLeftAlone(KeyboardLanguage layout)
    {
        var f = await Fixture();
        var words = layout == KeyboardLanguage.Ukrainian ? f.UaWords : f.EnWords;

        int checkedWords = 0, wronglySwitched = 0;
        for (int i = 0; i < words.Count; i += 379)
        {
            string word = words[i];
            if (word.Length is < 4 or > 12) continue;

            checkedWords++;
            if (f.Judge.Judge(word, layout).ShouldSwitch)
                wronglySwitched++;
        }

        Assert.True(checkedWords > 300, $"sample too small: {checkedWords}");
        // Correct words are protected twice: most are listed in the dictionary, and the statistics
        // alone (measured) stay under the margin for 99.7% of the rest.
        Assert.True(wronglySwitched <= checkedWords * 0.02,
            $"wrongly switched {wronglySwitched}/{checkedWords}");
    }
}
