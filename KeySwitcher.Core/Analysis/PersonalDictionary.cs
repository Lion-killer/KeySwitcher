using System.IO;

namespace KeySwitcher.Core.Analysis;

/// <summary>
/// The user's own word list — <c>%AppData%\KeySwitcher\custom-words.txt</c>, one word per line. Those words
/// are added to the built-in dictionaries, so the switcher stops "fixing" names, brands and terms it does
/// not know — the usual complaint about a dictionary that only holds common vocabulary.
/// </summary>
/// <remarks>
/// A separate file, not a field in <c>settings.json</c>: settings are replaced wholesale by <c>with</c>,
/// while the word list is edited by hand as often as from the dialog, and a long list has no business
/// being re-serialised on every tray toggle.
/// </remarks>
public static class PersonalDictionary
{
    private const string Header =
        "# KeySwitcher: власні слова, по одному в рядку. Рядки, що починаються з #, ігноруються.";

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KeySwitcher", "custom-words.txt");

    /// <summary>Reads the list; a missing file is an empty list, not an error.</summary>
    public static IReadOnlyList<string> Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return [];

        var words = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string line in File.ReadAllLines(path))
        {
            string word = line.Trim();
            if (word.Length == 0 || word.StartsWith('#')) continue;
            if (seen.Add(word)) words.Add(word);
        }

        return words;
    }

    /// <summary>Writes the list, one word per line. Replaces the file, so the dialog's list is the truth.</summary>
    public static void Save(IEnumerable<string> words, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var lines = new List<string> { Header };
        lines.AddRange(words.Select(w => w.Trim()).Where(w => w.Length > 0));

        File.WriteAllLines(path, lines);
    }

    /// <summary>
    /// Splits the words by script: Cyrillic joins the Ukrainian dictionary, Latin the English one.
    /// The file deliberately has no language column — a personal word's language follows from its letters,
    /// and the user should not have to answer a question the app can answer itself. Words in neither
    /// script (digits, punctuation) are dropped: they would never reach a lookup.
    /// </summary>
    public static (IReadOnlyList<string> Ukrainian, IReadOnlyList<string> English) SplitByScript(
        IEnumerable<string> words)
    {
        var ukrainian = new List<string>();
        var english = new List<string>();

        foreach (string word in words)
        {
            // The first letter decides: "Згурський" is Ukrainian even though it has no other markers.
            char first = word.FirstOrDefault(char.IsLetter);
            if (first == '\0') continue;

            if (first is >= 'Ѐ' and <= 'ӿ') ukrainian.Add(word);
            else if (first is >= 'A' and <= 'z') english.Add(word);
        }

        return (ukrainian, english);
    }
}
