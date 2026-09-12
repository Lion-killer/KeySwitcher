namespace KeySwitcher.Core.Hooks;

public sealed record KeyEvent(int Message, int VkCode, bool IsInjected)
{
    public bool IsKeyDown => Message is Native.NativeMethods.WM_KEYDOWN or Native.NativeMethods.WM_SYSKEYDOWN;
}
