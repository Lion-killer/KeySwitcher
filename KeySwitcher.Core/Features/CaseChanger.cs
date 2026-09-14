using KeySwitcher.Core.Input;
using KeySwitcher.Core.Layout;

namespace KeySwitcher.Core.Features;

/// <summary>
/// The "change case" hotkey (Ctrl+Shift+F11): cycles lower → UPPER → Title Case on the selected text,
/// or on the last typed word when nothing is selected.
/// </summary>
public sealed class CaseChanger(WordBuffer wordBuffer, InputSimulator inputSimulator)
    : TextTransformHotkey(wordBuffer, inputSimulator)
{
    /// <summary>Next case in the cycle: lower → UPPER → Title → lower.</summary>
    /// <remarks>
    /// The current case is read from the text itself instead of counting presses: the user edits text
    /// between presses, and a counter would then hand out the wrong case. Text without letters is
    /// returned unchanged, so the hotkey does nothing on "42".
    /// </remarks>
    public static string Next(string text)
    {
        if (!HasLetters(text)) return text;
        if (IsAllUpper(text)) return ToTitle(text);
        if (IsAllLower(text)) return ToUpper(text);
        return ToLower(text);
    }

    public static bool IsAllLower(string text) => !text.Any(char.IsUpper) && HasLetters(text);

    public static bool IsAllUpper(string text) => !text.Any(char.IsLower) && HasLetters(text);

    public static string ToLower(string text) => text.ToLowerInvariant();

    public static string ToUpper(string text) => text.ToUpperInvariant();

    /// <summary>
    /// First letter of every word uppercase, the rest lowercase: "hello world" → "Hello World".
    /// The apostrophe and the hyphen stay inside the word — "м'яч" → "М'яч", "будь-який" → "Будь-який",
    /// not "М'Яч" or "Будь-Який".
    /// </summary>
    public static string ToTitle(string text)
    {
        var chars = text.ToLowerInvariant().ToCharArray();
        bool startOfWord = true;

        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];

            if (c is '\'' or '’' or '-') continue; // part of the word: does not start a new one

            if (char.IsLetter(c))
            {
                if (startOfWord) chars[i] = char.ToUpperInvariant(c);
                startOfWord = false;
            }
            else
            {
                startOfWord = true; // space or punctuation — the next letter opens a word
            }
        }

        return new string(chars);
    }

    /// <summary>Applies the next case to the selection, or to the last typed word.</summary>
    public Task ChangeCaseAsync() => ApplyAsync(Next, "CASECHANGE");

    private static bool HasLetters(string text)
    {
        foreach (char c in text)
        {
            if (char.IsLetter(c)) return true;
        }
        return false;
    }
}
