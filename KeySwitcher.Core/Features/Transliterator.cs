using System.Collections.Frozen;
using System.Text;
using KeySwitcher.Core.Input;
using KeySwitcher.Core.Layout;

namespace KeySwitcher.Core.Features;

/// <summary>
/// The "transliterate" hotkey (Ctrl+Shift+F10): writes Ukrainian text in Latin letters following the
/// official rules (КМУ, постанова №55 від 27.01.2010) — "Київ" → "Kyiv", "Запоріжжя" → "Zaporizhzhia".
/// Applies to the selection, or to the last typed word.
/// </summary>
public sealed class Transliterator(WordBuffer wordBuffer, InputSimulator inputSimulator)
    : TextTransformHotkey(wordBuffer, inputSimulator)
{
    private static readonly FrozenDictionary<char, string> s_letters = new Dictionary<char, string>
    {
        ['а'] = "a",  ['б'] = "b",  ['в'] = "v",  ['г'] = "h",  ['ґ'] = "g",
        ['д'] = "d",  ['е'] = "e",  ['ж'] = "zh", ['з'] = "z",  ['и'] = "y",
        ['і'] = "i",  ['к'] = "k",  ['л'] = "l",  ['м'] = "m",  ['н'] = "n",
        ['о'] = "o",  ['п'] = "p",  ['р'] = "r",  ['с'] = "s",  ['т'] = "t",
        ['у'] = "u",  ['ф'] = "f",  ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch",
        ['ш'] = "sh", ['щ'] = "shch",
    }.ToFrozenDictionary();

    // These five letters are spelled differently at the start of a word and inside it: "Євген" →
    // "Yevhen" but "Знам'янка" → "Znamianka". The apostrophe and the soft sign are not transliterated.
    private static readonly FrozenDictionary<char, (string Start, string Inside)> s_contextual =
        new Dictionary<char, (string, string)>
        {
            ['є'] = ("ye", "ie"),
            ['ї'] = ("yi", "i"),
            ['й'] = ("y", "i"),
            ['ю'] = ("yu", "iu"),
            ['я'] = ("ya", "ia"),
        }.ToFrozenDictionary();

    public Task TransliterateAsync() => ApplyAsync(ToLatin, "TRANSLIT");

    public static string ToLatin(string text)
    {
        var result = new StringBuilder(text.Length + 8);
        bool atWordStart = true;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            char lower = char.ToLowerInvariant(c);
            bool upper = char.IsUpper(c);

            // "зг" → "zgh": otherwise it would read as "ж" ("Згурський" → "Zghurskyi").
            if (lower == 'з' && i + 1 < text.Length && char.ToLowerInvariant(text[i + 1]) == 'г')
            {
                result.Append(Capitalize("zgh", upper));
                i++;
                atWordStart = false;
                continue;
            }

            if (s_contextual.TryGetValue(lower, out var pair))
            {
                result.Append(Capitalize(atWordStart ? pair.Start : pair.Inside, upper));
                atWordStart = false;
                continue;
            }

            if (s_letters.TryGetValue(lower, out string? latin))
            {
                result.Append(Capitalize(latin, upper));
                atWordStart = false;
                continue;
            }

            if (lower is 'ь' or '\'' or '’')
                continue; // not transliterated, and does not open a new word

            result.Append(c);
            if (!char.IsLetter(c))
                atWordStart = true; // "Київ, Україна" — a new word after punctuation
        }

        return result.ToString();
    }

    private static string Capitalize(string latin, bool upper) =>
        upper ? char.ToUpperInvariant(latin[0]) + latin[1..] : latin;
}
