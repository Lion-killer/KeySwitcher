using KeySwitcher.Core.Layout;
using KeySwitcher.Core.Settings;
using Serilog;

namespace KeySwitcher.Core.Features;

/// <summary>
/// Replaces user-defined shortcuts with full text ("нп" → "наприклад"). The word itself lives in
/// <see cref="WordBuffer"/>; the entry list arrives with every call because settings are replaced
/// wholesale by <c>with</c> while the switcher keeps running.
/// </summary>
public sealed class AutoReplacer(WordBuffer wordBuffer)
{
    private readonly WordBuffer _wordBuffer = wordBuffer;

    /// <summary>
    /// Only the keys that end a word on purpose trigger a replacement. Punctuation is
    /// excluded: "тб," is most likely a typo in the middle of a sentence, not a request for "тобто,".
    /// </summary>
    public static bool IsTrigger(char delimiter) => delimiter is ' ' or '\t' or '\r' or '\n';

    /// <summary>Replacement configured for the finished word, or <c>null</c> if there is none.</summary>
    public static string? FindReplacement(string word, IReadOnlyList<AutoReplaceEntry> entries)
    {
        if (word.Length == 0) return null;

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Shortcut))
            {
                // "Додати" leaves empty rows until they are edited — worth naming in the log, because a
                // half-filled row in settings.json is otherwise indistinguishable from a typo.
                Log.Debug("auto-replace: entry with an empty shortcut skipped (replacement='{Replacement}')", entry.Replacement);
                continue;
            }

            if (string.Equals(entry.Shortcut, word, StringComparison.OrdinalIgnoreCase))
                return entry.Replacement;
        }

        return null;
    }

    /// <summary>
    /// True when <paramref name="delimiter"/> ends the word and the buffered word is a shortcut.
    /// Must be called BEFORE <see cref="WordBuffer.Reset"/> — the word is read from the buffer.
    /// </summary>
    public bool TryGetReplacement(
        char delimiter, IReadOnlyList<AutoReplaceEntry> entries, out string replacement)
    {
        replacement = "";
        if (!IsTrigger(delimiter)) return false;

        string? found = FindReplacement(_wordBuffer.CurrentWord, entries);
        if (found is null) return false;

        replacement = found;
        return true;
    }
}

