using System.Collections.Frozen;
using System.Reflection;
using Serilog;

namespace KeySwitcher.Core.Analysis;

public sealed class DictionaryAnalyzer
{
    // Resource names = RootNamespace + folder path (dots). See KeySwitcher.Core.csproj EmbeddedResource.
    private const string UkResource = "KeySwitcher.Core.Resources.Dictionaries.uk.txt";
    private const string EnResource = "KeySwitcher.Core.Resources.Dictionaries.en.txt";

    private readonly IReadOnlyList<string> _words;
    private readonly FrozenSet<string> _lookup;

    private DictionaryAnalyzer(IReadOnlyList<string> words)
    {
        _words = words;
        _lookup = words.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The loaded word list. Handed to <see cref="LanguageModel"/> so the letter statistics are built
    /// from the very same data as the lookups; the strings are shared, so it costs one array.
    /// </summary>
    public IReadOnlyList<string> Words => _words;

    public static Task<DictionaryAnalyzer> LoadUkrainianAsync(CancellationToken ct = default) =>
        LoadFromResourceAsync(UkResource, ct);

    public static Task<DictionaryAnalyzer> LoadEnglishAsync(CancellationToken ct = default) =>
        LoadFromResourceAsync(EnResource, ct);

    public static async Task<DictionaryAnalyzer> LoadFromResourceAsync(string resourceName, CancellationToken ct = default)
    {
        Assembly assembly = typeof(DictionaryAnalyzer).Assembly;
        await using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded dictionary '{resourceName}' not found.");

        using var reader = new StreamReader(stream);
        var words = new List<string>();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                words.Add(line);
        }

        // The count is the only visible sign of a broken or emptied embedded resource, and everything
        // downstream — statistics included — is built from this data. "uk.txt" reads better than the
        // full resource id, and the resource path lives in the csproj anyway.
        int marker = resourceName.LastIndexOf("Dictionaries.", StringComparison.Ordinal);
        string label = marker < 0 ? resourceName : resourceName[(marker + "Dictionaries.".Length)..];
        Log.Information("dictionary {Dictionary}: {Words} words", label, words.Count);
        return Create(words);
    }

    public static DictionaryAnalyzer Create(IEnumerable<string> words) =>
        new(words as IReadOnlyList<string> ?? words.ToList());

    /// <summary>
    /// The same words plus extras — the user's personal list. Returns a new instance: analyzers are
    /// immutable, and the letter statistics are built once, from the built-in lists.
    /// </summary>
    public DictionaryAnalyzer WithExtraWords(IEnumerable<string> words)
    {
        var extra = words.Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
        if (extra.Count == 0) return this;

        var combined = new List<string>(_words.Count + extra.Count);
        combined.AddRange(_words);
        combined.AddRange(extra);
        return new DictionaryAnalyzer(combined);
    }

    public bool Contains(string word) => _lookup.Contains(word);
}
