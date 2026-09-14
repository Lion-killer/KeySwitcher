using System.Diagnostics;
using System.Threading.Channels;
using KeySwitcher.Core.Analysis;
using KeySwitcher.Core.Hooks;
using KeySwitcher.Core.Input;
using KeySwitcher.Core.Layout;
using KeySwitcher.Core.Native;
using KeySwitcher.Core.Settings;
using Serilog;

namespace KeySwitcher.Core.Features;

public sealed class AutoSwitcher : IDisposable
{
    private const int VkBack      = 0x08;
    private const int VkTab       = 0x09;
    private const int VkReturn    = 0x0D;
    private const int VkSpace     = 0x20;
    private const int VkOem1      = 0xBA; // ; :
    private const int VkOemComma  = 0xBC; // , <
    private const int VkOemPeriod = 0xBE; // . >
    private const int VkOem4      = 0xDB; // [ {
    private const int VkOem6      = 0xDD; // ] }
    private const int VkOem7      = 0xDE; // ' "

    private readonly LayoutDetector _layoutDetector;
    private readonly LayoutConverter _layoutConverter;
    private readonly CharsetAnalyzer _charsetAnalyzer;
    private readonly WordJudge _wordJudge;
    private readonly InputSimulator _inputSimulator;
    private readonly WordBuffer _wordBuffer;
    private readonly AutoReplacer _autoReplacer;
    private readonly CapsLockFixer _capsLockFixer = new();
    private readonly AutoSwitchGuard _guard = new();
    private readonly CancellationTokenSource _cts = new();
    private volatile AppSettings _settings;

    // Cache foreground window exclusion result — recomputed only on window change
    private nint _cachedHwnd;
    private bool _cachedIsExcluded;

    // Physical modifier state, tracked from the hook event stream (injected events are filtered out,
    // so our own Ctrl+V injection doesn't disturb it). Needed to ignore shortcut chords (Ctrl+S)
    // and to preserve letter case (Shift/CapsLock).
    private bool _shiftDown;
    private bool _ctrlDown;
    private bool _altDown;
    private nint _lastInputHwnd;

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Raised after KeySwitcher switched the layout itself, so the tray can update its flag at once.
    /// The user's own switches are reported by <see cref="Hooks.ShellHook"/> (HSHELL_LANGUAGE) — there
    /// is no need to inspect every keystroke for them.
    /// </summary>
    public event Action? LanguageChanged;

    public AppSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;

            // The exclusion verdict is cached per window. Without dropping it here, adding the app the
            // user is typing in right now to the list would only take effect once they switch to
            // another window and back.
            _cachedHwnd = nint.Zero;
        }
    }

    public AutoSwitcher(
        KeyboardHook hook,
        LayoutDetector layoutDetector,
        LayoutConverter layoutConverter,
        CharsetAnalyzer charsetAnalyzer,
        WordJudge wordJudge,
        InputSimulator inputSimulator,
        WordBuffer wordBuffer,
        AutoReplacer autoReplacer,
        AppSettings settings)
    {
        _layoutDetector = layoutDetector;
        _layoutConverter = layoutConverter;
        _charsetAnalyzer = charsetAnalyzer;
        _wordJudge = wordJudge;
        _inputSimulator = inputSimulator;
        _wordBuffer = wordBuffer;
        _autoReplacer = autoReplacer;
        _settings = settings;

        _ = ProcessEventsAsync(hook.Reader, _cts.Token);
    }

    private async Task ProcessEventsAsync(ChannelReader<KeyEvent> reader, CancellationToken ct)
    {
        try
        {
            await foreach (var evt in reader.ReadAllAsync(ct))
            {
                Log.Verbose("evt vk=0x{Vk:X2} msg=0x{Msg:X} down={Down} injected={Injected} sending={Sending} enabled={Enabled}",
                    evt.VkCode, evt.Message, evt.IsKeyDown, evt.IsInjected, _inputSimulator.IsSendingInput, IsEnabled);
                if (evt.IsInjected || _inputSimulator.IsSendingInput) continue;

                TrackModifiers(evt); // needs key-ups too — must run before the IsKeyDown filter

                if (!evt.IsKeyDown) continue;
                if (!IsEnabled) continue;

                nint hwnd = NativeMethods.GetForegroundWindow();
                if (hwnd != _lastInputHwnd)
                {
                    // Focus moved — whatever was buffered no longer matches the text at the caret.
                    _lastInputHwnd = hwnd;
                    _wordBuffer.Invalidate();
                    ResyncModifiers();
                }
                if (IsExcluded(hwnd))
                {
                    Log.Debug("  excluded hwnd={Hwnd}", hwnd);
                    continue;
                }

                try
                {
                    await HandleKeyAsync(evt.VkCode, hwnd);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One failed replacement must not fault the loop and silently kill auto-switching.
                    // Warning, not Error: a single keystroke going wrong tends to repeat, and error.log is
                    // the permanent journal — see Logging.
                    Log.Warning(ex, "  handle FAILED vk=0x{Vk:X2}", evt.VkCode);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// The user switched the layout by hand (hotkey). Their choice stands: the nearest automatic
    /// replacement is skipped — see <see cref="AutoSwitchGuard"/>.
    /// </summary>
    public void NotifyManualSwitch()
    {
        if (_settings.SkipSwitchAfterManualSwitch) _guard.Suppress();
    }

    /// <summary>True when this automatic switch must be skipped (the pause is consumed either way).</summary>
    private bool ConsumeSuppression()
    {
        if (!_guard.ConsumeIfSuppressed()) return false;

        Log.Debug("  switch skipped: pause after a cursor key / manual switch");
        return true;
    }

    // The hook never sees key-ups from an elevated window or the secure desktop (UAC prompt,
    // Ctrl+Alt+Del): Alt+Tab into an elevated app and release Alt there, and _altDown stays true
    // forever — HandleKeyAsync then bails out on every keystroke and auto-switching is silently dead
    // until restart. Focus change is the only moment the tracked state can have drifted, so re-read the
    // physical keys here. Reading them per keystroke instead would be wrong: the consumer runs behind
    // the hook, so while typing fast GetAsyncKeyState already reports the *next* key's modifiers.
    private void ResyncModifiers()
    {
        _shiftDown = IsPhysicallyDown(0x10); // VK_SHIFT / CONTROL / MENU answer for either side
        _ctrlDown  = IsPhysicallyDown(0x11);
        _altDown   = IsPhysicallyDown(0x12);
    }

    private static bool IsPhysicallyDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private void TrackModifiers(KeyEvent evt)
    {
        bool down = evt.IsKeyDown;
        switch (evt.VkCode)
        {
            case 0xA0 or 0xA1 or 0x10: _shiftDown = down; break; // LSHIFT / RSHIFT / SHIFT
            case 0xA2 or 0xA3 or 0x11: _ctrlDown = down; break;  // LCTRL / RCTRL / CTRL
            case 0xA4 or 0xA5 or 0x12: _altDown = down; break;   // LALT / RALT / ALT
        }
    }

    // Keys that move the caret or edit outside the buffered word — the buffer no longer
    // matches what's at the caret, so it must be discarded (Invalidate, not Reset: the word must
    // not be reused by a manual switch either).
    // Insert is deliberately NOT here: it only toggles overwrite mode, leaves the caret and the text
    // alone, and is the most practical single-key manual switch on keyboards without Pause/ScrollLock.
    private static bool IsNavigationKey(int vk) =>
        vk is (>= 0x21 and <= 0x28) // PgUp, PgDn, End, Home, arrows
            or 0x1B                 // Escape
            or 0x2E;                // Delete

    private async Task HandleKeyAsync(int vk, nint hwnd)
    {
        if (_ctrlDown || _altDown)
        {
            // Shortcut chord (Ctrl+S, Ctrl+V, Alt+Tab…) — text state is unpredictable afterwards.
            _wordBuffer.Reset();
            return;
        }

        if (IsNavigationKey(vk))
        {
            // Caret moved (or text was edited outside the word) — the buffered word is stale.
            _wordBuffer.Invalidate();

            // …and the user is editing inside existing text right now, so the nearest automatic
            // replacement is skipped as well (settings, on by default).
            if (_settings.SkipSwitchAfterCaretMove) _guard.Suppress();
            return;
        }

        if (vk == VkBack)
        {
            _wordBuffer.Backspace();
            return;
        }

        KeyboardLanguage lang = _layoutDetector.GetCurrentLanguage();

        // The user's own layout switch (Ctrl+Shift, Win+Space) shows up here, on the first keystroke in
        // the new layout: the shell never reports it, so the layout read per keystroke is the only
        // reliable source. Our own switches are marked and therefore not mistaken for the user's.
        if (_guard.ObserveLayout(lang, hwnd, _settings.SkipSwitchAfterManualSwitch))
            Log.Debug("  user switched the layout: next automatic switch is paused");

        char? ch = VkToChar(vk, lang);
        Log.Verbose("  handle vk=0x{Vk:X2} lang={Lang} ch='{Char}' buffer='{Buffer}'",
            vk, lang.ToString(), ch?.ToString() ?? "null", _wordBuffer.CurrentWord);
        if (ch is null) return;

        if (_wordBuffer.IsDelimiter(ch.Value))
        {
            // Read the buffer before Reset — the auto-replacer matches on the word it holds.
            string word = _wordBuffer.CurrentWord;
            bool isShortcut = _autoReplacer.TryGetReplacement(ch.Value, _settings.AutoReplace, out string replacement);

            // Auto-replace wins over the dictionary: the user configured this shortcut explicitly,
            // and a shortcut is hardly ever a real word in either language.
            if (isShortcut && word.Length > 0)
            {
                _wordBuffer.Reset(ch.Value);
                await PerformAutoReplaceAsync(word, replacement, ch.Value);
                return;
            }

            // The char may also belong to the word (see CheckWordOnDelimiterAsync): it then stays in
            // the buffer and the buffer must not be reset.
            if (await CheckWordOnDelimiterAsync(word, ch.Value, lang, hwnd)) return;

            _wordBuffer.Reset(ch.Value); // the delimiter is on screen after the word — remember it
        }
        else
        {
            _wordBuffer.Add(ch.Value);
            string word = _wordBuffer.CurrentWord;

            // Immediate switch: mixed Unicode blocks (100% wrong layout) or char impossible for language
            if ((_charsetAnalyzer.HasMixedUnicodeBlocks(word) ||
                 _charsetAnalyzer.IsDefinitelyWrongLanguage(ch.Value, lang)) &&
                !ConsumeSuppression())
            {
                _wordBuffer.Reset();
                string converted = ConvertToOther(word, lang);
                await PerformSwitchAsync(word, converted, hwnd);
            }
        }
    }

    /// <summary>
    /// Handles a delimiter right after a word: fixes an accidental CapsLock and switches the word when
    /// it was typed in the wrong layout. Returns <c>true</c> when the char turned out to be a letter of
    /// the other layout — the caller must then keep it in the buffer (no reset, the word continues).
    /// </summary>
    private async Task<bool> CheckWordOnDelimiterAsync(string word, char delimiter, KeyboardLanguage lang, nint hwnd)
    {
        if (word.Length == 0) return false;

        WordJudgement judgement = _wordJudge.Judge(word, lang);
        Log.Debug(
            "  delimiter word='{Word}' lang={Lang} converted='{Converted}' " +
            "scores={Current:F2}/{Other:F2} margin={Margin:F2} letters={Letters} inDict={KnownInCurrent} " +
            "convertedInDict={KnownConverted} byStatistics={ByStatistics}",
            word, lang.ToString(), judgement.Converted, judgement.CurrentScore, judgement.OtherScore, judgement.Margin,
            judgement.Letters, judgement.KnownInCurrent, judgement.KnownConverted, judgement.ByStatistics);

        // '.', ',' and ';' also type 'ю', 'б' and 'ж'. Only a word the dictionary knows may be cut
        // here; otherwise the char goes back into the buffer as a letter (mistyped "працює" arrives as
        // "ghfw.'"). The statistics are deliberately not used at this step: mid-word they cannot tell
        // "ghfw." (the 'ю' of "працює") from "ghbdtn." (a real full stop after "привіт.").
        if (LayoutConverter.TypesUaLetter(delimiter) && !judgement.KnownInCurrent && !judgement.KnownConverted)
        {
            Log.Debug("  defer delimiter '{Delimiter}' — '{Word}' is unknown to both dictionaries", delimiter.ToString(), word);
            _wordBuffer.AddAsLetter(delimiter);
            return true;
        }

        // Accidental CapsLock ("hELLO" → "Hello"): the case is corrected first and the corrected word
        // is judged again — the two mistakes are independent and can meet in one word ("hELLO" typed
        // in the wrong layout needs both fixes).
        if (_settings.FixCapsLock && _capsLockFixer.ShouldFix(word))
        {
            string corrected = _capsLockFixer.InvertCase(word);
            Log.Debug("  CAPSLOCK '{Word}'->'{Corrected}' fg={Foreground}", word, corrected, ForegroundProcess.DescribeForeground());
            await _inputSimulator.ReplaceWordKeepingDelimiterAsync(word, corrected, delimiter);
            _capsLockFixer.TurnOffCapsLock();

            word = corrected;
            judgement = _wordJudge.Judge(word, lang);
        }

        // The pause shields THIS word — the one the user is typing after moving the caret or switching
        // the layout by hand — so it is consumed here, where the word is really finished. Consuming it
        // further down (only when a switch was in order) let it leak onto a later word instead.
        bool paused = ConsumeSuppression();

        if (!judgement.ShouldSwitch || paused) return false;

        Log.Debug("  SWITCH(delim) '{Word}'->'{Converted}' byStatistics={ByStatistics} fg={Foreground}",
            word, judgement.Converted, judgement.ByStatistics, ForegroundProcess.DescribeForeground());
        await _inputSimulator.ReplaceWordKeepingDelimiterAsync(word, judgement.Converted, delimiter);

        // Layout switch strictly AFTER the replacement (InputSimulator contract): Ctrl+V is injected
        // as VK codes, so switching first makes a Ukrainian layout type 'м' instead of pasting.
        if (_settings.AutoSwitchLayout)
        {
            await _layoutDetector.SwitchLayoutAsync(hwnd);

            // Remember the layout we just put in place: the next keystroke reads it back, and without
            // the mark that change would look like the user's own switch (AutoSwitchGuard).
            _guard.MarkOwnSwitch(_layoutDetector.GetCurrentLanguage());
        }
        LanguageChanged?.Invoke();
        Log.Debug("  SWITCH done lang={Lang} fg={Foreground}",
            _layoutDetector.GetCurrentLanguage().ToString(), ForegroundProcess.DescribeForeground());

        return false;
    }

    // No layout switch here: the shortcut was typed in the current layout and the replacement is
    // literal text pasted through the clipboard, so the layout must stay where the user left it.
    private async Task PerformAutoReplaceAsync(string word, string replacement, char delimiter)
    {
        Log.Debug("  AUTOREPLACE '{Word}'->'{Replacement}' fg={Foreground}",
            word, replacement, ForegroundProcess.DescribeForeground());
        await _inputSimulator.ReplaceWordKeepingDelimiterAsync(word, replacement, delimiter);
    }

    private string ConvertToOther(string word, KeyboardLanguage lang) =>
        _wordJudge.ConvertToOther(word, lang);

    private async Task PerformSwitchAsync(string word, string replacement, nint hwnd)
    {
        Log.Debug("  SWITCH(immediate) '{Word}'->'{Replacement}' fg={Foreground}",
            word, replacement, ForegroundProcess.DescribeForeground());
        await _inputSimulator.ReplaceLastWordAsync(word, replacement);

        // After, not before — see CheckWordOnDelimiterAsync.
        if (_settings.AutoSwitchLayout)
        {
            await _layoutDetector.SwitchLayoutAsync(hwnd);
            _guard.MarkOwnSwitch(_layoutDetector.GetCurrentLanguage());
        }
        LanguageChanged?.Invoke();
        Log.Debug("  SWITCH done lang={Lang} fg={Foreground}",
            _layoutDetector.GetCurrentLanguage().ToString(), ForegroundProcess.DescribeForeground());
    }

    // Maps a VK code to the character it produces in the given keyboard layout.
    // Letter VKs (0x41–0x5A) are converted via LayoutConverter for UA layout.
    // OEM keys are mapped using their US unshifted base char, then converted for UA.
    // Returns null for VK codes that don't contribute to word accumulation.
    private char? VkToChar(int vk, KeyboardLanguage lang)
    {
        if (vk == VkSpace)  return ' ';
        if (vk == VkReturn) return '\r';
        if (vk == VkTab)    return '\t';

        // Letters A–Z (VK equals ASCII uppercase). Preserve case: Shift XOR CapsLock → uppercase.
        // LayoutConverter preserves case on conversion, so 'Ghbdsn' correctly becomes 'Привіт'.
        if (vk is >= 0x41 and <= 0x5A)
        {
            bool capsOn = (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 1) != 0;
            char enChar = _shiftDown ^ capsOn ? (char)vk : (char)(vk | 0x20);
            if (lang == KeyboardLanguage.Ukrainian)
                return _layoutConverter.Convert(enChar.ToString(), Direction.EnToUa)[0];
            return enChar;
        }

        // OEM keys: the physical key types punctuation in the English layout and a letter in the
        // Ukrainian one. Shift matters: '<' is 'Б', not ',' — otherwise uppercase Ю Б Ж Х Ї Є lose
        // their case ("Будь ласка" came back as "будь ласка").
        char? enOem = (vk, _shiftDown) switch
        {
            (VkOem1, false)      => ';',
            (VkOem1, true)       => ':',
            (VkOemComma, false)  => ',',
            (VkOemComma, true)   => '<',
            (VkOemPeriod, false) => '.',
            (VkOemPeriod, true)  => '>',
            (VkOem7, false)      => '\'',
            (VkOem7, true)       => '"',
            (VkOem4, false)      => '[',
            (VkOem4, true)       => '{',
            (VkOem6, false)      => ']',
            (VkOem6, true)       => '}',
            _                    => null
        };

        if (enOem is null) return null;

        if (lang == KeyboardLanguage.Ukrainian)
            return _layoutConverter.Convert(enOem.Value.ToString(), Direction.EnToUa)[0];

        return enOem.Value;
    }

    private bool IsExcluded(nint hwnd)
    {
        if (hwnd == _cachedHwnd) return _cachedIsExcluded;
        _cachedHwnd = hwnd;

        var settings = _settings;
        if (settings.Exclusions.Count == 0)
        {
            _cachedIsExcluded = false;
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            string exeName = proc.ProcessName + ".exe";
            _cachedIsExcluded = settings.Exclusions.Any(e =>
                string.Equals(e, exeName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            _cachedIsExcluded = false;
        }
        return _cachedIsExcluded;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
