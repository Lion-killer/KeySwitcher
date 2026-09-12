using System.Runtime.InteropServices;

namespace KeySwitcher.Core.Native;

internal static partial class NativeMethods
{
    internal const int VK_CAPITAL = 0x14;
    internal const uint KEYEVENTF_KEYUP = 0x0002;

    // Keyboard hook
    internal const int WH_KEYBOARD_LL = 13;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const uint LLKHF_INJECTED = 0x00000010;

    internal delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    // Shell hook (RegisterShellHookWindow): the shell posts these as the "SHELLHOOK" window message.
    // Unlike WM_INPUTLANGCHANGE they reach a window that is not focused, so they are the only way for a
    // background app to be told "the input language changed".
    internal const int HSHELL_WINDOWACTIVATED = 4;
    internal const int HSHELL_LANGUAGE = 8;
    internal const int HSHELL_HIGHBIT = 0x8000;
    internal const string ShellHookMessage = "SHELLHOOK";

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterShellHookWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeregisterShellHookWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessage(string lpString);

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        internal uint vkCode;
        internal uint scanCode;
        internal uint flags;
        internal uint time;
        internal nuint dwExtraInfo;
    }

    [LibraryImport("user32.dll")]
    internal static partial short GetKeyState(int nVirtKey);

    // Physical key state, independent of the calling thread's message queue (unlike GetKeyState).
    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll")]
    internal static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    // lpfn passed as nint via Marshal.GetFunctionPointerForDelegate to avoid LibraryImport delegate limitations
    // EntryPoint *W required: LibraryImport uses ExactSpelling (no automatic A/W probing like DllImport)
    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
    internal static partial nint SetWindowsHookEx(int idHook, nint lpfn, nint hMod, uint dwThreadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    internal static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string lpModuleName);

    // Token elevation ("does this process run as administrator") — see Elevation. Needed because Windows
    // blocks keyboard input between different integrity levels (UIPI).
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint TOKEN_QUERY = 0x0008;
    internal const int TokenElevation = 20;

    [StructLayout(LayoutKind.Sequential)]
    internal struct TOKEN_ELEVATION
    {
        internal int TokenIsElevated;
    }

    [LibraryImport("kernel32.dll")]
    internal static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    internal static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint hObject);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenProcessToken(nint ProcessHandle, uint DesiredAccess, out nint TokenHandle);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetTokenInformation(
        nint TokenHandle, int TokenInformationClass, out TOKEN_ELEVATION TokenInformation,
        uint TokenInformationLength, out uint ReturnLength);

    // Layout detection
    internal const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    internal const uint WM_INPUTLANGCHANGE = 0x0051;

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int GetWindowText(nint hWnd, Span<char> lpString, int nMaxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    internal static partial int GetWindowTextLength(nint hWnd);

    // Walking the top-level windows for the exclusions picker. A loop over GetTopWindow/GetWindow keeps
    // a callback delegate out of the P/Invoke, which is what EnumWindows would need.
    internal const uint GW_HWNDNEXT = 2;

    [LibraryImport("user32.dll")]
    internal static partial nint GetTopWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial nint GetWindow(nint hWnd, uint uCmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    internal static partial nint GetKeyboardLayout(uint idThread);

    // DO NOT use LoadKeyboardLayout to get an HKL for a switch: it registers an extra layout in the
    // system, which appears as a duplicate entry in the Win+Space switcher. Use this instead.
    [LibraryImport("user32.dll")]
    internal static partial int GetKeyboardLayoutList(int nBuff, [Out] nint[]? lpList);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        internal int left;
        internal int top;
        internal int right;
        internal int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GUITHREADINFO
    {
        internal int cbSize;
        internal int flags;
        internal nint hwndActive;
        internal nint hwndFocus;
        internal nint hwndCapture;
        internal nint hwndMenuOwner;
        internal nint hwndMoveSize;
        internal nint hwndCaret;
        internal RECT rcCaret;
    }

    // idThread = 0 asks about the foreground thread. hwndFocus is the window that really receives
    // typed characters — on Windows 11 that is often NOT the top-level window (Notepad's text control
    // lives in its own thread), and the input language belongs to the focused window's thread.
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO pgui);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    // SendInput
    internal const uint INPUT_KEYBOARD = 1;
    internal const int VK_BACK = 0x08;
    internal const int VK_CONTROL = 0x11;
    internal const int VK_SHIFT = 0x10;
    internal const int VK_LEFT = 0x25;
    internal const int VK_C = 0x43;
    internal const int VK_V = 0x56;

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        internal ushort wVk;
        internal ushort wScan;
        internal uint dwFlags;
        internal uint time;
        internal nuint dwExtraInfo;
    }

    // Size = 32 matches the Windows INPUT union (max is MOUSEINPUT = 32 bytes on 64-bit)
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    internal struct INPUT_UNION
    {
        [FieldOffset(0)]
        internal KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        internal uint type;
        internal INPUT_UNION u;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(uint nInputs, ReadOnlySpan<INPUT> pInputs, int cbSize);

    // Clipboard
    internal const uint CF_UNICODETEXT = 13;
    internal const uint GMEM_MOVEABLE = 0x0002;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    internal static partial nint GetClipboardData(uint uFormat);

    [LibraryImport("user32.dll")]
    internal static partial nint SetClipboardData(uint uFormat, nint hMem);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalFree(nint hMem);

    // Hotkey manager
    internal const uint WM_HOTKEY = 0x0312;
    internal const uint WM_QUIT = 0x0012;
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        internal nint hwnd;
        internal uint message;
        internal nint wParam;
        internal nint lParam;
        internal uint time;
        internal int ptX;
        internal int ptY;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint hWnd, int id);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    internal static partial int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();
}
