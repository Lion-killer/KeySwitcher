using KeySwitcher.Core.Layout;

namespace KeySwitcher.Core.Analysis;

public sealed class CharsetAnalyzer
{
    private static bool IsLatin(char c) => c is >= 'A' and <= 'z';
    private static bool IsCyrillic(char c) => c is >= 'Ѐ' and <= 'ӿ';

    public bool HasMixedUnicodeBlocks(string word)
    {
        bool hasLatin = word.Any(IsLatin);
        bool hasCyrillic = word.Any(IsCyrillic);
        return hasLatin && hasCyrillic;
    }

    public bool IsDefinitelyWrongLanguage(char c, KeyboardLanguage lang) => lang switch
    {
        KeyboardLanguage.English => IsCyrillic(c),
        KeyboardLanguage.Ukrainian => IsLatin(c),
        _ => false
    };
}
