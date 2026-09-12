using System.Collections.Frozen;

namespace KeySwitcher.Core.Layout;

public enum Direction { EnToUa, UaToEn }

public sealed class LayoutConverter
{
    private static readonly FrozenDictionary<char, char> s_enToUa = new Dictionary<char, char>
    {
        ['q'] = 'й', ['w'] = 'ц', ['e'] = 'у', ['r'] = 'к', ['t'] = 'е',
        ['y'] = 'н', ['u'] = 'г', ['i'] = 'ш', ['o'] = 'щ', ['p'] = 'з',
        ['a'] = 'ф', ['s'] = 'і', ['d'] = 'в', ['f'] = 'а', ['g'] = 'п',
        ['h'] = 'р', ['j'] = 'о', ['k'] = 'л', ['l'] = 'д',
        [';'] = 'ж', ['\''] = 'є', [':'] = 'Ж', ['"'] = 'Є',
        ['z'] = 'я', ['x'] = 'ч', ['c'] = 'с', ['v'] = 'м', ['b'] = 'и',
        ['n'] = 'т', ['m'] = 'ь',
        [','] = 'б', ['.'] = 'ю', ['<'] = 'Б', ['>'] = 'Ю',
        ['['] = 'х', [']'] = 'ї', ['{'] = 'Х', ['}'] = 'Ї',
    }.ToFrozenDictionary();

    /// <summary>
    /// Shifted counterpart of a punctuation key. Uppercase 'Б', 'Ю', 'Ж', 'Х', 'Ї', 'Є' have no own
    /// physical key: on the Ukrainian layout they sit on Shift+',' etc., and in the English layout
    /// they arrive as '&lt;', '&gt;', ':', '{', '}', '"'. Without this the case was lost
    /// ("Будь ласка" mistyped as "&lt;elm kfcrf" came back as "будь ласка").
    /// </summary>
    private static readonly FrozenDictionary<char, char> s_shiftedEn = new Dictionary<char, char>
    {
        [','] = '<', ['.'] = '>', [';'] = ':', ['\''] = '"', ['['] = '{', [']'] = '}',
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<char, char> s_uaToEn = BuildReverse(s_enToUa);

    private static FrozenDictionary<char, char> BuildReverse(FrozenDictionary<char, char> table)
    {
        var reverse = new Dictionary<char, char>(table.Count);
        foreach (var (en, ua) in table)
        {
            reverse.TryAdd(ua, en);
            reverse.TryAdd(
                char.ToUpperInvariant(ua),
                s_shiftedEn.TryGetValue(en, out char shifted) ? shifted : char.ToUpperInvariant(en));
        }

        return reverse.ToFrozenDictionary();
    }

    /// <summary>
    /// True when the English-layout character <paramref name="c"/> types a Ukrainian letter on the
    /// same key: '.' → 'ю', ',' → 'б', ';' → 'ж', '[' → 'х', ']' → 'ї' (Latin letters do too). Such
    /// a char inside a word is ambiguous — punctuation here, a letter in the other layout.
    /// </summary>
    public static bool TypesUaLetter(char c) => s_enToUa.ContainsKey(char.ToLowerInvariant(c));

    /// <summary>
    /// Which way to convert a piece of text: text with Cyrillic in it becomes Latin, everything else
    /// becomes Cyrillic. Needed where the direction cannot come from the active layout — a selection
    /// of arbitrary length may be in either script.
    /// </summary>
    public static Direction DirectionForText(string text)
    {
        foreach (char c in text)
        {
            if (c is >= 'Ѐ' and <= 'ӿ') return Direction.UaToEn;
        }

        return Direction.EnToUa;
    }

    public string Convert(string word, Direction direction)
    {
        var table = direction == Direction.EnToUa ? s_enToUa : s_uaToEn;
        var chars = new char[word.Length];
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];

            // Exact match first: uppercase UA letters have no own key ('Б' is Shift+',' — that is '*'
            // in the English layout), so the table carries both cases and uppercasing the lowercase
            // mapping would turn 'Б' into ',' instead of '<'.
            if (table.TryGetValue(c, out char exact))
            {
                chars[i] = exact;
                continue;
            }

            char lower = char.ToLowerInvariant(c);
            chars[i] = table.TryGetValue(lower, out char mapped)
                ? (char.IsUpper(c) ? char.ToUpperInvariant(mapped) : mapped)
                : c;
        }
        return new string(chars);
    }
}
