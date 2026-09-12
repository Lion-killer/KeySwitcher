using KeySwitcher.Core.Layout;
using Serilog;

namespace KeySwitcher.Core.Analysis;

/// <summary>
/// Letter-combination statistics of one language: which sequences of letters are typical for it and
/// which never occur. Built from the bundled word lists, so nothing is hardcoded.
/// <para>
/// This is what tells "ghfw.'" apart from a real English word: the dictionary only knows the exact
/// forms it lists, while the model knows the *shape* of the language — "ghfw" is unpronounceable
/// English (no vowels), and "працює" fits Ukrainian perfectly, even though the dictionary lacks it.
/// </para>
/// </summary>
public sealed class LanguageModel
{
    // Add-k smoothing: a sequence the model has never seen must be unlikely, not impossible —
    // log(0) would destroy the whole score and make every unknown word "infinitely foreign".
    private const double UnigramSmoothing = 0.5;
    private const double BigramSmoothing = 0.1;

    private readonly Dictionary<char, int> _unigrams = [];
    private readonly Dictionary<int, int> _bigrams = [];
    private double _unigramDenominator;
    private double _bigramDenominator;

    private LanguageModel(KeyboardLanguage language) => Language = language;

    public KeyboardLanguage Language { get; }

    public int WordCount { get; private set; }

    public int LetterCount { get; private set; }

    /// <summary>
    /// Builds the model from a word list. <paramref name="stride"/> lets a big list be sampled
    /// (every n-th word) — the lists are alphabetical, so the sample stays spread over the alphabet.
    /// </summary>
    public static LanguageModel Build(KeyboardLanguage language, IReadOnlyList<string> words, int stride = 1)
    {
        var model = new LanguageModel(language);

        for (int i = 0; i < words.Count; i += stride)
            model.AddWord(words[i]);

        model.Finish();

        // The statistics decide every switch, so their size belongs in the log: a model built from a
        // handful of words scores nonsense, and the symptom ("it stopped recognising wrong layouts")
        // says nothing about the cause.
        Log.Debug("language model {Language}: {Words} words, {Letters} letters sampled (stride={Stride})",
            language.ToString(), model.WordCount, model.LetterCount, stride);
        return model;
    }

    private void AddWord(string word)
    {
        char previous = '\0';
        bool counted = false;

        foreach (char raw in word)
        {
            char c = Normalize(raw);
            if (c == '\0') continue;

            _unigrams[c] = _unigrams.GetValueOrDefault(c) + 1;
            LetterCount++;
            counted = true;

            if (previous != '\0')
            {
                int key = Key(previous, c);
                _bigrams[key] = _bigrams.GetValueOrDefault(key) + 1;
            }

            previous = c;
        }

        if (counted) WordCount++;
    }

    private void Finish()
    {
        int alphabet = Math.Max(_unigrams.Count, 1);
        _unigramDenominator = LetterCount + UnigramSmoothing * alphabet;
        _bigramDenominator = BigramSmoothing * alphabet;
    }

    /// <summary>
    /// Average log-probability per letter: the higher (closer to zero), the more the word looks like
    /// this language. Averaging instead of summing keeps scores comparable between words of different
    /// lengths — otherwise long words would always look worse.
    /// </summary>
    public double Score(string word)
    {
        double total = 0;
        int letters = 0;
        char previous = '\0';

        foreach (char raw in word)
        {
            char c = Normalize(raw);
            if (c == '\0') continue;

            total += Math.Log(UnigramProbability(c));
            if (previous != '\0')
                total += Math.Log(BigramProbability(previous, c));

            previous = c;
            letters++;
        }

        // No letters at all (punctuation only) — nothing to judge.
        return letters == 0 ? 0 : total / letters;
    }

    private double UnigramProbability(char c) =>
        (_unigrams.GetValueOrDefault(c) + UnigramSmoothing) / _unigramDenominator;

    private double BigramProbability(char a, char b) =>
        (_bigrams.GetValueOrDefault(Key(a, b)) + BigramSmoothing) /
        (_unigrams.GetValueOrDefault(a) + _bigramDenominator);

    // Letters and the apostrophe (м'яч, об'єкт) carry the statistics; digits and punctuation are
    // skipped without breaking the sequence — a hyphen in "будь-який" must not hide the bigram.
    private static char Normalize(char c)
    {
        if (c == '\'') return c;
        return char.IsLetter(c) ? char.ToLowerInvariant(c) : '\0';
    }

    private static int Key(char a, char b) => (a << 16) | b;
}
