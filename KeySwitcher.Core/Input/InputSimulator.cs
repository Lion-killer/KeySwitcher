using System.Runtime.InteropServices;
using KeySwitcher.Core.Features;
using KeySwitcher.Core.Native;
using Serilog;

namespace KeySwitcher.Core.Input;

public sealed class InputSimulator
{
    private volatile bool _isSendingInput;

    public bool IsSendingInput => _isSendingInput;

    public void DeleteChars(int count)
    {
        if (count <= 0) return;
        var list = new List<NativeMethods.INPUT>(count * 2);
        AddBackspaces(list, count);
        SendInputs(list);
    }

    // Replace the last typed word with newWord. Caller switches the system layout AFTER this returns.
    public Task ReplaceLastWordAsync(string oldWord, string newWord) =>
        ReplaceCoreAsync(oldWord.Length, newWord);

    // Auto-switch on a delimiter: the delimiter char is already in the document after the word, so we
    // delete word + delimiter and paste the converted word followed by that same delimiter.
    public Task ReplaceWordKeepingDelimiterAsync(string oldWord, string newWord, char delimiter) =>
        ReplaceCoreAsync(oldWord.Length + 1, newWord + delimiter);

    /// <summary>Text currently on the clipboard, or <c>null</c> when there is none.</summary>
    public string? GetClipboardText() => TryGetClipboardText();

    /// <summary>
    /// Sends Ctrl+C and returns the text that ended up on the clipboard.
    /// </summary>
    /// <remarks>
    /// There is no API to ask whether something is selected, so the caller compares the result with
    /// the clipboard read beforehand: an unchanged clipboard means nothing was copied, i.e. no
    /// selection. (In a focused console Ctrl+C without a selection sends ^C to the running process —
    /// the price of the trick; put the terminal into the exclusions list if that ever bites.)
    /// </remarks>
    public async Task<string?> CopySelectionAsync()
    {
        Log.Debug("    CopySelection fg={Foreground}", ForegroundProcess.DescribeForeground());
        await SendChordAsync(NativeMethods.VK_C).ConfigureAwait(false);
        await Task.Delay(120).ConfigureAwait(false); // let the target app fill the clipboard
        return TryGetClipboardText();
    }

    /// <summary>Pastes <paramref name="text"/> over the current selection and restores the clipboard.</summary>
    public async Task ReplaceSelectionAsync(string text)
    {
        Log.Debug("    ReplaceSelection paste='{Text}' fg={Foreground}", text, ForegroundProcess.DescribeForeground());
        string? saved = TryGetClipboardText();
        SetClipboardText(text);

        await PasteAsync().ConfigureAwait(false);

        await Task.Delay(120).ConfigureAwait(false);
        TryRestoreClipboard(saved);
    }

    /// <summary>
    /// Selects <paramref name="length"/> characters back from the caret (Shift+←), so the text just
    /// written stays selected and the next hotkey press works on it again.
    /// </summary>
    public void SelectBackwards(int length)
    {
        if (length <= 0) return;

        var list = new List<NativeMethods.INPUT>(length * 4);
        for (int i = 0; i < length; i++)
        {
            list.Add(CreateKeyInput(NativeMethods.VK_SHIFT, 0));
            list.Add(CreateKeyInput(NativeMethods.VK_LEFT, 0));
            list.Add(CreateKeyInput(NativeMethods.VK_LEFT, NativeMethods.KEYEVENTF_KEYUP));
            list.Add(CreateKeyInput(NativeMethods.VK_SHIFT, NativeMethods.KEYEVENTF_KEYUP));
        }

        _isSendingInput = true;
        try
        {
            SendInputs(list);
        }
        finally
        {
            _isSendingInput = false;
        }
    }

    // Insert via clipboard + Ctrl+V, not Unicode SendInput. Win11 Notepad ignores
    // KEYEVENTF_UNICODE injection (pastes blanks for Cyrillic); Ctrl+V paste works everywhere.
    // Two subtleties learned the hard way:
    //  - Ctrl and V must NOT go in one batch: the target sometimes processes V before it registers
    //    Ctrl as held, typing 'v'/'м' instead of pasting. Send Ctrl down, pause, then V, then Ctrl up.
    //  - Paste runs while the layout is still the original; the caller switches layout AFTER this
    //    returns, otherwise a Ukrainian layout turns the injected V into 'м'.
    private async Task ReplaceCoreAsync(int deleteCount, string insertText)
    {
        Log.Debug("    ReplaceCore del={DeleteCount} paste='{Text}' fg={Foreground}",
            deleteCount, insertText, ForegroundProcess.DescribeForeground());
        string? saved = TryGetClipboardText();
        SetClipboardText(insertText);

        _isSendingInput = true;
        try
        {
            DeleteChars(deleteCount);
            await Task.Delay(15).ConfigureAwait(false);
        }
        finally
        {
            _isSendingInput = false;
        }

        await PasteAsync().ConfigureAwait(false);

        await Task.Delay(120).ConfigureAwait(false); // let the target app process the paste
        TryRestoreClipboard(saved);
    }

    // Ctrl+V with the recursion guard. Ctrl and V must NOT go in one batch: the target sometimes
    // processes V before it registers Ctrl as held, typing 'v'/'м' instead of pasting.
    private Task PasteAsync() => SendChordAsync(NativeMethods.VK_V);

    // Ctrl+key as separate steps (Ctrl down, pause, key, pause, Ctrl up) so that the target app sees
    // the modifier as held. The guard makes the keyboard hook ignore our own injected events.
    private async Task SendChordAsync(int vk)
    {
        _isSendingInput = true;
        try
        {
            SendKey(NativeMethods.VK_CONTROL, down: true);
            await Task.Delay(10).ConfigureAwait(false);
            SendKey(vk, down: true);
            SendKey(vk, down: false);
            await Task.Delay(10).ConfigureAwait(false);
            SendKey(NativeMethods.VK_CONTROL, down: false);
        }
        finally
        {
            _isSendingInput = false;
        }
    }

    private static void SendKey(int vk, bool down) =>
        SendInputs([CreateKeyInput(vk, down ? 0 : NativeMethods.KEYEVENTF_KEYUP)]);

    private static void AddBackspaces(List<NativeMethods.INPUT> list, int count)
    {
        for (int i = 0; i < count; i++)
        {
            list.Add(CreateKeyInput(NativeMethods.VK_BACK, 0));
            list.Add(CreateKeyInput(NativeMethods.VK_BACK, NativeMethods.KEYEVENTF_KEYUP));
        }
    }

    private static void SendInputs(List<NativeMethods.INPUT> inputs)
    {
        if (inputs.Count == 0) return;
        var arr = inputs.ToArray();
        uint sent = NativeMethods.SendInput((uint)arr.Length, arr, Marshal.SizeOf<NativeMethods.INPUT>());
        Log.Debug("      SendInput requested={Requested} sent={Sent} err={Error}",
            arr.Length, sent, Marshal.GetLastWin32Error());
    }

    private static string? TryGetClipboardText()
    {
        if (!NativeMethods.OpenClipboard(0))
        {
            Log.Debug("clipboard: busy — cannot read it");
            return null;
        }
        try
        {
            nint hData = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
            if (hData == 0) return null;

            nint ptr = NativeMethods.GlobalLock(hData);
            if (ptr == 0) return null;
            try { return Marshal.PtrToStringUni(ptr); }
            finally { NativeMethods.GlobalUnlock(hData); }
        }
        finally { NativeMethods.CloseClipboard(); }
    }

    private static void SetClipboardText(string text)
    {
        nuint size = (nuint)((text.Length + 1) * sizeof(char));
        nint hMem = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, size);
        if (hMem == 0) return;

        nint ptr = NativeMethods.GlobalLock(hMem);
        if (ptr == 0) { NativeMethods.GlobalFree(hMem); return; }

        char[] chars = text.ToCharArray();
        Marshal.Copy(chars, 0, ptr, chars.Length);
        Marshal.WriteInt16(ptr, text.Length * sizeof(char), 0); // null terminator
        NativeMethods.GlobalUnlock(hMem);

        if (!NativeMethods.OpenClipboard(0))
        {
            // Another process holds the clipboard: the paste below will find the old content, so the
            // replacement silently does not happen. The classic "I pressed it and nothing changed".
            Log.Warning("clipboard: cannot open it — the replacement is skipped");
            NativeMethods.GlobalFree(hMem);
            return;
        }

        NativeMethods.EmptyClipboard();
        if (NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, hMem) == 0)
        {
            Log.Warning("clipboard: SetClipboardData failed (err={Error})", Marshal.GetLastWin32Error());
            NativeMethods.GlobalFree(hMem); // system did not take ownership
        }
        NativeMethods.CloseClipboard();
    }

    private static void TryRestoreClipboard(string? text)
    {
        if (text is null) return;
        try
        {
            SetClipboardText(text);
        }
        catch (Exception ex)
        {
            // The user's own clipboard content is lost — quiet, but not silent.
            Log.Warning(ex, "clipboard: previous content not restored");
        }
    }

    private static NativeMethods.INPUT CreateKeyInput(int vk, uint flags) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.INPUT_UNION
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = (ushort)vk,
                wScan = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = 0
            }
        }
    };
}
