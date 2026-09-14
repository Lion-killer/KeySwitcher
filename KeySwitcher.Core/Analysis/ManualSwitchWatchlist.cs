using System.Text.Json;
using Serilog;

namespace KeySwitcher.Core.Analysis;

/// <summary>
/// Counts words the user keeps converting by hand with the manual hotkey (<c>Insert</c>), so the app can
/// offer to put such a word into the <see cref="PersonalDictionary"/> — the personal list is what the
/// automatic switcher leaves alone, which is exactly what a word the user fixes by hand needs.
/// </summary>
/// <remarks>
/// The counts live in <c>%AppData%\KeySwitcher\manual-switches.json</c> (one entry per word) and survive
/// restarts: a word is usually converted once in a while, so a per-session counter would rarely reach the
/// threshold. A word the user refused from the offer dialog is marked <see cref="Declined"/> and never
/// offered again — asking a second time about an answered question is worse than not asking at all.
/// Not thread-safe: the hotkey handler and the tray balloon both run on the UI thread.
/// </remarks>
public sealed class ManualSwitchWatchlist
{
    /// <summary>Switches of one word after which the user is offered to add it to the personal dictionary.</summary>
    public const int DefaultThreshold = 3;

    /// <summary>Marker for a word the user refused. The count is dropped with it: there is nothing to watch.</summary>
    private const int Declined = -1;

    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _threshold;
    private readonly string _path;

    /// <param name="threshold">Switches after which the word is offered; the offer repeats every threshold.</param>
    /// <param name="path">Counter file; <c>%AppData%\KeySwitcher\manual-switches.json</c> by default.</param>
    public ManualSwitchWatchlist(int threshold = DefaultThreshold, string? path = null)
    {
        _threshold = Math.Max(1, threshold);
        _path = path ?? DefaultPath;
        Load();
    }

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KeySwitcher", "manual-switches.json");

    /// <summary>Words watched or refused so far.</summary>
    public int Count => _counts.Count;

    /// <summary>
    /// Counts one manual switch of <paramref name="word"/>. Returns the word and its total number of
    /// switches when it is time to offer it for the personal dictionary, otherwise <c>null</c>.
    /// </summary>
    public (string Word, int Count)? Record(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;

        // The user has already answered the question about this word — do not ask it again.
        if (_counts.TryGetValue(word, out int known) && known == Declined) return null;

        // The number of switches doubles as the offer cadence: every threshold-th switch is a reminder for
        // a word that keeps coming back, and the recorded count keeps working after a restart.
        int count = known + 1;
        _counts[word] = count;
        Save();

        Log.Verbose("manual switches: '{Word}' switched by hand {Count} time(s)", word, count);
        return count % _threshold == 0 ? (word, count) : null;
    }

    /// <summary>The word is in the personal dictionary now — there is nothing left to watch.</summary>
    public void Forget(string word)
    {
        if (_counts.Remove(word)) Save();
    }

    /// <summary>
    /// The user refused the offer. The counter goes with it and the word is never offered again: the answer
    /// was given deliberately, so the question must not return.
    /// </summary>
    public void Decline(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return;

        _counts[word] = Declined;
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                Log.Debug("manual switches: no file at {Path} — nothing watched yet", _path);
                return;
            }

            Dictionary<string, int>? counts =
                JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(_path), s_options);
            if (counts is null) return;

            foreach ((string word, int count) in counts)
                if (!string.IsNullOrWhiteSpace(word)) _counts[word] = count;

            Log.Debug("manual switches: {Words} watched word(s) loaded from {Path}", _counts.Count, _path);
        }
        catch (Exception ex)
        {
            // A broken file costs at most a repeated offer, so the counters start empty — but the reason has
            // to reach the log, otherwise losing them would look like a bug in the counting itself.
            Log.Warning(ex, "manual switches: {Path} is unreadable — starting with an empty list", _path);
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_counts, s_options));
        }
        catch (Exception ex)
        {
            // Losing the counters is not worth interrupting the user: they stay in memory for this run, and
            // the next manual switch will try to write again.
            Log.Warning(ex, "manual switches: could not write {Path}", _path);
        }
    }
}
