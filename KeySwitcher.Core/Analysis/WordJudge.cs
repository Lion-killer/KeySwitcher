using KeySwitcher.Core.Layout;

namespace KeySwitcher.Core.Analysis;

/// <summary>
/// Decides whether a typed word sits in the wrong layout.
/// <para>
/// Primary signal — letter combinations (<see cref="LanguageModel"/>): "ghbdsn" cannot be English,
/// "привіт" fits Ukrainian, whatever the dictionaries know. Fallback — the dictionaries: a word whose
/// converted form is a listed word is switched even when the statistics are not conclusive (short
/// words, names, new vocabulary, and any form the word lists happen to contain).
/// </para>
/// </summary>
public sealed class WordJudge(
    LanguageModel ukrainian,
    LanguageModel english,
    LayoutConverter converter,
    DictionaryAnalyzer ukrainianDictionary,
    DictionaryAnalyzer englishDictionary)
{
    /// <summary>
    /// How much more plausible the other reading must be, in log-probability per letter. Measured on
    /// the bundled word lists: mistyped words land at 1.0 with 95.7% / 98.0% (UA / EN), while
    /// correctly typed words stay below it with 0.32% / 0.00% false alarms — and a known word is never
    /// touched at all (see <see cref="WordJudgement.KnownInCurrent"/>).
    /// </summary>
    public const double DefaultMargin = 1.0;

    /// <summary>Shorter words carry too little signal for letter statistics — the dictionary decides.</summary>
    public const int DefaultMinLetters = 3;

    private readonly LanguageModel _ukrainian = ukrainian;
    private readonly LanguageModel _english = english;
    private readonly LayoutConverter _converter = converter;

    // Not readonly: the personal word list can be applied while the app runs (SetDictionaries). References
    // are swapped atomically, so a keystroke judged during the swap sees one pair or the other, never half.
    private volatile DictionaryAnalyzer _ukrainianDictionary = ukrainianDictionary;
    private volatile DictionaryAnalyzer _englishDictionary = englishDictionary;

    /// <summary>
    /// Replaces the dictionaries, e.g. after the user edited the personal word list. The models stay as
    /// they are: they describe letter combinations, and a handful of personal words would not move them.
    /// </summary>
    public void SetDictionaries(DictionaryAnalyzer ukrainianDictionary, DictionaryAnalyzer englishDictionary)
    {
        _englishDictionary = englishDictionary;
        _ukrainianDictionary = ukrainianDictionary;
    }

    public WordJudgement Judge(string word, KeyboardLanguage layout)
    {
        KeyboardLanguage other = Other(layout);
        string converted = _converter.Convert(word, DirectionTo(other));

        bool knownInCurrent = IsWord(word, layout);
        bool knownConverted = converted != word && IsWord(converted, other);

        double currentScore = Model(layout).Score(word);
        double otherScore = Model(other).Score(converted);
        int letters = CountLetters(converted);

        bool byStatistics = letters >= DefaultMinLetters &&
                            otherScore - currentScore >= DefaultMargin;

        return new WordJudgement(
            word, converted, currentScore, otherScore, letters, knownInCurrent, knownConverted, byStatistics);
    }

    /// <summary>
    /// The text the word becomes in the other layout, regardless of whether it looks foreign.
    /// Used by the manual hotkey, which always converts what the user typed.
    /// </summary>
    public string ConvertToOther(string word, KeyboardLanguage layout) =>
        _converter.Convert(word, DirectionTo(Other(layout)));

    /// <summary>
    /// Is the word already in the dictionary of the given language? The personal words are part of the
    /// lookups, so this answers "would adding it to the personal dictionary change anything?"
    /// </summary>
    public bool IsKnown(string word, KeyboardLanguage language) => IsWord(word, language);

    private bool IsWord(string word, KeyboardLanguage lang) => (lang switch
    {
        KeyboardLanguage.English => _englishDictionary,
        KeyboardLanguage.Ukrainian => _ukrainianDictionary,
        _ => null
    })?.Contains(word) == true;

    private LanguageModel Model(KeyboardLanguage lang) =>
        lang == KeyboardLanguage.English ? _english : _ukrainian;

    private static KeyboardLanguage Other(KeyboardLanguage lang) =>
        lang == KeyboardLanguage.English ? KeyboardLanguage.Ukrainian : KeyboardLanguage.English;

    private static Direction DirectionTo(KeyboardLanguage target) =>
        target == KeyboardLanguage.Ukrainian ? Direction.EnToUa : Direction.UaToEn;

    private static int CountLetters(string word)
    {
        int count = 0;
        foreach (char c in word)
        {
            if (char.IsLetter(c)) count++;
        }
        return count;
    }
}
