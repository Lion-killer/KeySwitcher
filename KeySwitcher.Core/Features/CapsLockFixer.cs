using KeySwitcher.Core.Native;
using Serilog;

namespace KeySwitcher.Core.Features;

public sealed class CapsLockFixer
{
    // Condition: first char is lowercase letter, ≥2 uppercase letters in the rest
    public bool ShouldFix(string word)
    {
        if (word.Length < 3)
            return false;
        if (!char.IsLetter(word[0]) || !char.IsLower(word[0]))
            return false;

        int upperCount = 0;
        for (int i = 1; i < word.Length; i++)
        {
            if (char.IsUpper(word[i]))
                upperCount++;
        }
        return upperCount >= 2;
    }

    public string InvertCase(string word)
    {
        var chars = word.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsUpper(chars[i]))
                chars[i] = char.ToLowerInvariant(chars[i]);
            else if (char.IsLower(chars[i]))
                chars[i] = char.ToUpperInvariant(chars[i]);
        }
        return new string(chars);
    }

    public void TurnOffCapsLock()
    {
        if ((NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 0x0001) != 0)
        {
            NativeMethods.keybd_event(NativeMethods.VK_CAPITAL, 0x45, 0, 0);
            NativeMethods.keybd_event(NativeMethods.VK_CAPITAL, 0x45, NativeMethods.KEYEVENTF_KEYUP, 0);
            Log.Verbose("  capslock: off key sent");
        }
    }
}
