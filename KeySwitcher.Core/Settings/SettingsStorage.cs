using System.Text.Json;
using Serilog;

namespace KeySwitcher.Core.Settings;

/// <summary>
/// Reads/writes <see cref="AppSettings"/> as JSON. Defaults to
/// <c>%AppData%\KeySwitcher\settings.json</c>; a custom path can be injected for tests.
/// </summary>
public sealed class SettingsStorage
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;

    public SettingsStorage(string? path = null) => _path = path ?? DefaultPath;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KeySwitcher", "settings.json");

    /// <summary>Loads settings, or returns defaults if the file is missing or unreadable.</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                Log.Debug("settings: no file at {Path} — using defaults", _path);
                return new AppSettings();
            }

            string json = File.ReadAllText(_path);
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, s_options) ?? new AppSettings();
            Log.Debug("settings: loaded from {Path}", _path);
            return settings;
        }
        catch (Exception ex)
        {
            // Fall back to defaults rather than crash the app — but never silently: an unreadable file
            // resets every setting the user had, and without a line in the log that looks like the app
            // forgetting them at random.
            Log.Warning(ex, "settings: {Path} is unreadable — using defaults", _path);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!); // no-op if it already exists
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, s_options));
        Log.Debug("settings: saved to {Path}", _path);
    }

    /// <summary>
    /// The settings as JSON — the same text <see cref="Save"/> would write, so an exported file can be
    /// dropped into place by hand.
    /// </summary>
    public static string ToJson(AppSettings settings) => JsonSerializer.Serialize(settings, s_options);

    /// <summary>
    /// Parses an exported file. Unlike <see cref="Load"/> it throws on garbage: an import the user asked for
    /// must not quietly fall back to defaults and look like it worked.
    /// </summary>
    public static AppSettings FromJson(string json) =>
        JsonSerializer.Deserialize<AppSettings>(json, s_options)
        ?? throw new JsonException("the file contains no settings");
}
