namespace KeySwitcher.Core.Analysis;

/// <summary>
/// Result of judging one typed word: which text it should become and why. Scores are average
/// log-probabilities per letter (see <see cref="LanguageModel.Score"/>) — the closer to zero, the
/// more the reading looks like that language.
/// </summary>
public readonly record struct WordJudgement(
    string Word,
    string Converted,
    double CurrentScore,
    double OtherScore,
    int Letters,
    bool KnownInCurrent,
    bool KnownConverted,
    bool ByStatistics)
{
    public double Margin => OtherScore - CurrentScore;

    /// <summary>True when the word has to be replaced by <see cref="Converted"/>.</summary>
    public bool ShouldSwitch => !KnownInCurrent && (ByStatistics || KnownConverted);
}
