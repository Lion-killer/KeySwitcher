using KeySwitcher.Core.Settings;

namespace KeySwitcher.Tests;

public class SettingsStorageTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public SettingsStorageTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "KeySwitcherTests", Guid.NewGuid().ToString("N"));
        _path = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_FileNotFound_ReturnsDefaults()
    {
        var storage = new SettingsStorage(_path);
        var settings = storage.Load();

        Assert.True(settings.AutoSwitch);
        Assert.True(settings.FixCapsLock);
        Assert.False(settings.AutoStart);
        Assert.Equal("Insert", settings.Hotkeys.ManualSwitch);
        Assert.Empty(settings.Exclusions);
        Assert.Empty(settings.AutoReplace);
    }

    [Fact]
    public void Save_CreatesDirectoryAndFile()
    {
        var storage = new SettingsStorage(_path);
        storage.Save(new AppSettings());

        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void SaveThenLoad_DefaultSettings_RoundTrips()
    {
        var storage = new SettingsStorage(_path);
        var original = new AppSettings();
        storage.Save(original);

        var loaded = storage.Load();

        Assert.Equal(original.AutoSwitch, loaded.AutoSwitch);
        Assert.Equal(original.FixCapsLock, loaded.FixCapsLock);
        Assert.Equal(original.AutoStart, loaded.AutoStart);
        Assert.Equal(original.Hotkeys, loaded.Hotkeys); // HotkeySettings is a record → value equality
    }

    [Fact]
    public void SaveThenLoad_CustomSettings_RoundTrips()
    {
        var storage = new SettingsStorage(_path);
        var original = new AppSettings
        {
            AutoSwitch = false,
            AutoStart = true,
            Hotkeys = new HotkeySettings { ManualSwitch = "Ctrl+Shift+F5", ChangeCase = "Alt+C" },
            Exclusions = ["devenv.exe", "WindowsTerminal.exe"],
            AutoReplace =
            [
                new AutoReplaceEntry("нп", "наприклад"),
                new AutoReplaceEntry("тб", "тобто"),
            ],
        };
        storage.Save(original);

        var loaded = storage.Load();

        Assert.False(loaded.AutoSwitch);
        Assert.True(loaded.AutoStart);
        Assert.Equal("Ctrl+Shift+F5", loaded.Hotkeys.ManualSwitch);
        Assert.Equal(original.Exclusions, loaded.Exclusions);     // sequence equality
        Assert.Equal(original.AutoReplace, loaded.AutoReplace);   // AutoReplaceEntry is a record
    }

    [Fact]
    public void Save_WritesCamelCaseJson()
    {
        var storage = new SettingsStorage(_path);
        storage.Save(new AppSettings { Exclusions = ["a.exe"] });

        string json = File.ReadAllText(_path);

        Assert.Contains("\"autoSwitch\"", json);
        Assert.Contains("\"fixCapsLock\"", json);
        Assert.Contains("\"manualSwitch\"", json);
        Assert.DoesNotContain("\"AutoSwitch\"", json);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, "{ this is not valid json ]");

        var settings = new SettingsStorage(_path).Load();

        Assert.True(settings.AutoSwitch); // fell back to defaults
    }

    [Fact]
    public void Load_FromKnownJsonFormat_Deserializes()
    {
        // Matches the settings.json format documented in the README
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, """
        {
          "autoSwitch": false,
          "fixCapsLock": true,
          "autoStart": true,
          "hotkeys": { "manualSwitch": "Insert", "changeCase": "Ctrl+Shift+F11", "selectionLayout": "Ctrl+Shift+F10", "transliterate": "Ctrl+Shift+F9" },
          "exclusions": ["devenv.exe"],
          "autoReplace": [ { "shortcut": "нп", "replacement": "наприклад" } ]
        }
        """);

        var s = new SettingsStorage(_path).Load();

        Assert.False(s.AutoSwitch);
        Assert.True(s.AutoStart);
        Assert.Single(s.Exclusions);
        Assert.Equal("devenv.exe", s.Exclusions[0]);
        Assert.Single(s.AutoReplace);
        Assert.Equal("нп", s.AutoReplace[0].Shortcut);
        Assert.Equal("наприклад", s.AutoReplace[0].Replacement);
    }

    // ----- Export / import (the "Інше" tab) -----

    [Fact]
    public void ToJson_ThenFromJson_RoundTrips()
    {
        var original = new AppSettings
        {
            AutoSwitch = false,
            VerboseLog = false,
            AutoStart = true,
            Hotkeys = new HotkeySettings { ManualSwitch = "Pause", ChangeCase = "Ctrl+Alt+C" },
            Exclusions = ["devenv.exe"],
            AutoReplace = [new AutoReplaceEntry("нп", "наприклад")],
        };

        AppSettings imported = SettingsStorage.FromJson(SettingsStorage.ToJson(original));

        Assert.False(imported.AutoSwitch);
        Assert.False(imported.VerboseLog);
        Assert.True(imported.AutoStart);
        Assert.Equal("Pause", imported.Hotkeys.ManualSwitch);
        Assert.Equal("Ctrl+Alt+C", imported.Hotkeys.ChangeCase);
        Assert.Equal(original.Exclusions, imported.Exclusions);
        Assert.Equal(original.AutoReplace, imported.AutoReplace);
    }

    [Fact]
    public void ToJson_WritesTheSameShapeAsSettingsJson()
    {
        string json = SettingsStorage.ToJson(new AppSettings());

        Assert.Contains("\"autoSwitch\": true", json, StringComparison.Ordinal);  // camelCase, like the file
        Assert.Contains("\"hotkeys\"", json, StringComparison.Ordinal);
        Assert.Contains(Environment.NewLine, json);                              // indented, readable by hand
    }

    [Fact]
    public void FromJson_Garbage_Throws()
    {
        // Unlike Load (which falls back to defaults), an import the user asked for must fail loudly.
        Assert.ThrowsAny<Exception>(() => SettingsStorage.FromJson("{ not json ]"));
    }

    [Fact]
    public void FromJson_Null_Throws()
    {
        Assert.ThrowsAny<Exception>(() => SettingsStorage.FromJson("null"));
    }

    [Fact]
    public void ExportedJson_CanBeLoadedAsSettingsFile()
    {
        // The point of export: the file can be dropped next to the app's own settings.json.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, SettingsStorage.ToJson(new AppSettings { AutoSwitch = false }));

        Assert.False(new SettingsStorage(_path).Load().AutoSwitch);
    }
}
