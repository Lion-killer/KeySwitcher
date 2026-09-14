using Microsoft.Win32;
using Serilog;

namespace KeySwitcher.Core.Features;

/// <summary>
/// Keeps the "run at Windows startup" entry in sync with <c>AppSettings.AutoStart</c>.
/// Writes to <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, so no admin rights are needed.
/// </summary>
public static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KeySwitcher";

    /// <summary>Adds the Run entry when <paramref name="enabled"/>, removes it otherwise.</summary>
    public static void SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (enabled)
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    Log.Warning("auto-start: cannot resolve the executable path — Run entry not written");
                    return;
                }

                // Quoted: the path may contain spaces (e.g. Program Files).
                key.SetValue(ValueName, $"\"{exePath}\"");
                Log.Debug("auto-start: Run entry set to {Exe}", exePath);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Debug("auto-start: Run entry removed");
            }
        }
        catch (Exception ex)
        {
            // The registry can be locked down by policy: the setting is still persisted, only the
            // auto-start entry is lost — and then the checkbox quietly lies, so say it out loud.
            Log.Warning(ex, "auto-start: Run entry not updated (enabled={Enabled})", enabled);
        }
    }
}
