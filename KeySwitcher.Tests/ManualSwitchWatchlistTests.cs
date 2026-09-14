using KeySwitcher.Core.Analysis;

namespace KeySwitcher.Tests;

/// <summary>
/// The counters behind "you convert this word by hand all the time — add it to the dictionary?". The
/// expensive part is being wrong: an offer too early is noise, a lost counter means the word is never
/// offered at all, and a declined word must not come back.
/// </summary>
public class ManualSwitchWatchlistTests
{
    private const string Word = "працює";

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"keyswitcher-watch-{Guid.NewGuid():N}.json");

    [Fact]
    public void Record_StaysQuietBeforeTheThreshold()
    {
        string path = TempFile();
        try
        {
            var watchlist = new ManualSwitchWatchlist(path: path);

            Assert.Null(watchlist.Record(Word));
            Assert.Null(watchlist.Record(Word));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Record_ReturnsTheWordWithItsCount_OnTheThreshold()
    {
        string path = TempFile();
        try
        {
            var watchlist = new ManualSwitchWatchlist(path: path);
            watchlist.Record(Word);
            watchlist.Record(Word);

            Assert.Equal((Word, 3), watchlist.Record(Word));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Record_OffersAgainOnEveryFurtherThreshold()
    {
        string path = TempFile();
        try
        {
            var watchlist = new ManualSwitchWatchlist(path: path);
            for (int i = 1; i <= 2; i++) Assert.Null(watchlist.Record(Word));

            Assert.Equal((Word, 3), watchlist.Record(Word)); // first offer

            // A reminder for a word that keeps coming back, not a one-off question.
            Assert.Null(watchlist.Record(Word));
            Assert.Null(watchlist.Record(Word));
            Assert.Equal((Word, 6), watchlist.Record(Word));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Record_CountsEachWordSeparately()
    {
        string path = TempFile();
        try
        {
            var watchlist = new ManualSwitchWatchlist(path: path);

            // Another word's switches must not push this one over its threshold.
            watchlist.Record("ghfw.'");
            watchlist.Record(Word);
            watchlist.Record(Word);

            Assert.Equal((Word, 3), watchlist.Record(Word));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Record_CountsCaseInsensitively()
    {
        string path = TempFile();
        try
        {
            var watchlist = new ManualSwitchWatchlist(path: path);
            watchlist.Record("Працює");
            watchlist.Record("працює");

            Assert.Equal(3, watchlist.Record("ПРАЦЮЄ")!.Value.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Record_IgnoresBlankWords()
    {
        string path = TempFile();
        try
        {
            var watchlist = new ManualSwitchWatchlist(path: path);

            Assert.Null(watchlist.Record(""));
            Assert.Null(watchlist.Record("   "));
            Assert.Equal(0, watchlist.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Record_KeepsCountingAcrossRestarts()
    {
        string path = TempFile();
        try
        {
            var before = new ManualSwitchWatchlist(path: path);
            before.Record(Word);
            before.Record(Word);

            // A new instance is a restart: the counters are read back from the file.
            var after = new ManualSwitchWatchlist(path: path);

            Assert.Equal((Word, 3), after.Record(Word));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Forget_DropsTheCount_SoTheOfferStartsFromScratch()
    {
        string path = TempFile();
        try
        {
            var watchlist = new ManualSwitchWatchlist(path: path);
            watchlist.Record(Word);
            watchlist.Record(Word);

            watchlist.Forget(Word);
            Assert.Equal(0, watchlist.Count);

            // The counting starts over: one switch after the word is in the dictionary is not an offer.
            Assert.Null(watchlist.Record(Word));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Decline_SilencesTheWordForGood()
    {
        string path = TempFile();
        try
        {
            var watchlist = new ManualSwitchWatchlist(path: path);
            watchlist.Decline(Word);

            for (int i = 0; i < 6; i++) Assert.Null(watchlist.Record(Word));

            // …and the answer survives a restart: the user refused once, not once per run.
            var afterRestart = new ManualSwitchWatchlist(path: path);
            Assert.Null(afterRestart.Record(Word));
            Assert.Null(afterRestart.Record(Word));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_UnreadableFile_StartsEmpty_AndStillCounts()
    {
        string path = TempFile();
        try
        {
            File.WriteAllText(path, "це не json");

            var watchlist = new ManualSwitchWatchlist(path: path);

            Assert.Equal(0, watchlist.Count);
            watchlist.Record(Word);
            watchlist.Record(Word);
            Assert.Equal((Word, 3), watchlist.Record(Word));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Threshold_IsNeverZero()
    {
        string path = TempFile();
        try
        {
            // A zero threshold would mean "offer every time" and a modulo by zero — clamp it to one.
            var watchlist = new ManualSwitchWatchlist(threshold: 0, path: path);

            Assert.Equal((Word, 1), watchlist.Record(Word));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DefaultPath_LivesNextToTheSettings()
    {
        Assert.EndsWith(Path.Combine("KeySwitcher", "manual-switches.json"), ManualSwitchWatchlist.DefaultPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ManualSwitchWatchlist.DefaultPath, StringComparison.OrdinalIgnoreCase);
    }
}
