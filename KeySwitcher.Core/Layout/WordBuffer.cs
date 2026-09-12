using System.Text;

namespace KeySwitcher.Core.Layout;

public sealed class WordBuffer
{
    private readonly StringBuilder _buffer = new();
    private string _recentWord = "";
    private char? _recentDelimiter;

    // Space, Enter, Tab, and punctuation — but NOT apostrophe (Ukrainian: м'яч, п'ять)
    private static readonly HashSet<char> s_delimiters = [' ', '\r', '\n', '\t', '.', ',', '!', '?', ';', ':'];

    public string CurrentWord => _buffer.ToString();

    public int Length => _buffer.Length;

    public bool IsDelimiter(char c) => s_delimiters.Contains(c);

    /// <summary>
    /// Last word dropped by <see cref="Reset"/>, or an empty string. Kept until something proves the
    /// caret moved (<see cref="Invalidate"/>): a manual-hotkey chord clears the buffer through the
    /// normal key path *before* WM_HOTKEY arrives, and the user may well pause before pressing it.
    /// </summary>
    public string RecentWord() => _recentWord;

    /// <summary>
    /// Delimiter that stands between the caret and <see cref="RecentWord"/> on screen, or <c>null</c>
    /// when the caret is right after the word. The manual switch must delete and re-insert it too,
    /// otherwise the space typed after the word eats the first letter of the replacement.
    /// </summary>
    public char? RecentDelimiter => _recentDelimiter;

    public void Add(char c)
    {
        if (s_delimiters.Contains(c))
            Reset(c); // a delimiter ends the word — remember which one, the manual switch needs it
        else
            _buffer.Append(c);
    }

    /// <summary>
    /// Appends a character that <see cref="IsDelimiter"/> calls a delimiter, but which is a letter in
    /// the other layout ('.' → 'ю'). The word stays in one piece: "ghfw.'" is the mistyped "працює",
    /// and cutting it at the dot left the leftover "'" to be switched into "є" on its own.
    /// </summary>
    public void AddAsLetter(char c) => _buffer.Append(c);

    public void Backspace()
    {
        if (_buffer.Length > 0)
        {
            _buffer.Remove(_buffer.Length - 1, 1);
            return;
        }

        // Backspace with an empty buffer deletes text we no longer track. What it hit is most likely
        // the delimiter after the finished word — remember that it is gone.
        _recentDelimiter = null;
    }

    /// <summary>
    /// Drops the current word, keeping it (and the delimiter that followed it on screen) available to
    /// <see cref="ManualSwitcher"/>. <paramref name="delimiter"/> is the character that closed the word,
    /// or <c>null</c> when nothing stands between the caret and the word (shortcut chord, navigation).
    /// </summary>
    public void Reset(char? delimiter = null)
    {
        if (_buffer.Length > 0)
        {
            _recentWord = _buffer.ToString();
            _recentDelimiter = delimiter;
        }

        _buffer.Clear();
    }

    /// <summary>
    /// Declares which word was just written to the document and what follows it. The manual switch
    /// re-arms the buffer this way, so pressing the hotkey again toggles the same word back instead of
    /// converting a stale one.
    /// </summary>
    public void Adopt(string word, char? delimiter)
    {
        _recentWord = word;
        _recentDelimiter = delimiter;
        _buffer.Clear();
    }

    /// <summary>
    /// Drops everything. Used when the caret moved (navigation key, focus change): neither the
    /// current nor the recent word is at the caret anymore, so manual switching must not use them.
    /// </summary>
    public void Invalidate()
    {
        _buffer.Clear();
        _recentWord = "";
        _recentDelimiter = null;
    }
}
