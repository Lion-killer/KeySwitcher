using KeySwitcher.Core.Analysis;
using KeySwitcher.Core.Layout;

namespace KeySwitcher.Tests;

/// <summary>
/// Temporary harness used to tune <see cref="WordJudge.DefaultMargin"/>: it measures how plausibility
/// separates text typed in the wrong layout from correctly typed text, and writes the numbers to
/// %TEMP%\keyswitcher-margin-report.txt. Kept as a skipped test so the numbers can be re-measured.
/// </summary>
public class ThresholdTuningTests
{
    [Fact(Skip = "Diagnostic harness — remove Skip to re-measure the statistics")]
    public async Task Report_MarginDistribution()
    {
        var converter = new LayoutConverter();
        var uaDict = await DictionaryAnalyzer.LoadUkrainianAsync();
        var enDict = await DictionaryAnalyzer.LoadEnglishAsync();

        var uaModel = LanguageModel.Build(KeyboardLanguage.Ukrainian, uaDict.Words);
        var enModel = LanguageModel.Build(KeyboardLanguage.English, enDict.Words);
        var judge = new WordJudge(uaModel, enModel, converter, uaDict, enDict);

        var report = new List<string>
        {
            $"ua words={uaModel.WordCount} letters={uaModel.LetterCount}",
            $"en words={enModel.WordCount} letters={enModel.LetterCount}",
        };

        List<double> MistypedMargins(IReadOnlyList<string> words, KeyboardLanguage intendedLayout, int stride)
        {
            var margins = new List<double>();
            for (int i = 0; i < words.Count; i += stride)
            {
                string word = words[i];
                if (word.Length is < 4 or > 12) continue;

                // What the user sees: the word typed with the other layout active.
                Direction wrong = intendedLayout == KeyboardLanguage.Ukrainian ? Direction.UaToEn : Direction.EnToUa;
                string typed = converter.Convert(word, wrong);
                KeyboardLanguage typedLayout = intendedLayout == KeyboardLanguage.Ukrainian
                    ? KeyboardLanguage.English
                    : KeyboardLanguage.Ukrainian;

                margins.Add(judge.Judge(typed, typedLayout).Margin);
            }
            return margins;
        }

        List<double> CorrectMargins(IReadOnlyList<string> words, KeyboardLanguage layout, int stride)
        {
            var margins = new List<double>();
            for (int i = 0; i < words.Count; i += stride)
            {
                string word = words[i];
                if (word.Length is < 4 or > 12) continue;
                margins.Add(judge.Judge(word, layout).Margin);
            }
            return margins;
        }

        var cases = new (string Name, List<double> Margins)[]
        {
            ("ua typed in en layout (should switch)", MistypedMargins(uaDict.Words, KeyboardLanguage.Ukrainian, 379)),
            ("en typed in ua layout (should switch)", MistypedMargins(enDict.Words, KeyboardLanguage.English, 401)),
            ("ua correct (must stay)", CorrectMargins(uaDict.Words, KeyboardLanguage.Ukrainian, 379)),
            ("en correct (must stay)", CorrectMargins(enDict.Words, KeyboardLanguage.English, 401)),
        };

        foreach (var (name, margins) in cases)
        {
            var sorted = margins.Order().ToList();
            report.Add($"\n{name}: n={margins.Count}");
            report.Add($"  min={sorted[0]:F2} p1={sorted[(int)(sorted.Count * 0.01)]:F2} " +
                       $"p5={sorted[(int)(sorted.Count * 0.05)]:F2} median={sorted[sorted.Count / 2]:F2} " +
                       $"p95={sorted[(int)(sorted.Count * 0.95)]:F2} max={sorted[^1]:F2}");
            foreach (double threshold in new[] { 0.5, 1.0, 1.5, 2.0, 2.5, 3.0 })
            {
                double share = margins.Count(m => m >= threshold) / (double)margins.Count;
                report.Add($"  >= {threshold:F1}: {share:P2}");
            }
        }

        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "keyswitcher-margin-report.txt"), report);
    }
}
