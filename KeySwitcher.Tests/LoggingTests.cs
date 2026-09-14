using KeySwitcher.Core.Diagnostics;
using KeySwitcher.Core.Settings;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace KeySwitcher.Tests;

/// <summary>
/// The Serilog wiring: debug.log takes the whole trace, error.log only the errors, and the keystroke
/// trace (Verbose) must be silenceable from the level alone — that is the lever left for turning the
/// temporary tracing off without touching a single call site.
/// </summary>
public class LoggingTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), $"keyswitcher-log-{Guid.NewGuid():N}");

    private string DebugPath => Path.Combine(_folder, "debug.log");

    private string ErrorPath => Path.Combine(_folder, "error.log");

    public void Dispose()
    {
        Log.CloseAndFlush();
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A sink may still hold the file (a failing test leaves its logger alive); the temp folder is
            // not worth masking the real failure for.
        }
    }

    // ILogger itself has no Dispose: the concrete logger does, and flushing is what puts the events in
    // the files — the sinks buffer.
    private static void Flush(ILogger logger) => (logger as IDisposable)?.Dispose();

    [Fact]
    public void Create_WritesTheTraceToDebugLog_AndErrorsToBoth()
    {
        ILogger logger = Logging.Create(DebugPath, ErrorPath);

        logger.Verbose("keystroke {Vk}", "0x41");
        logger.Information("startup: ready");
        logger.Error(new InvalidOperationException("boom"), "startup failed");
        Flush(logger);

        string debug = File.ReadAllText(DebugPath);
        Assert.Contains("keystroke", debug, StringComparison.Ordinal);
        Assert.Contains("startup: ready", debug, StringComparison.Ordinal);
        Assert.Contains("boom", debug, StringComparison.Ordinal);

        string error = File.ReadAllText(ErrorPath);
        Assert.Contains("startup failed", error, StringComparison.Ordinal);
        Assert.Contains("boom", error, StringComparison.Ordinal);
        Assert.DoesNotContain("keystroke", error, StringComparison.Ordinal);
        Assert.DoesNotContain("startup: ready", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MinimumLevel_CanBeRaisedAtRuntime()
    {
        // Exactly what the "detailed logging" checkbox does: the switch is read per event, so a running app
        // stops writing the keystroke trace the moment the box is unticked.
        var level = new LoggingLevelSwitch(LogEventLevel.Verbose);
        ILogger logger = Logging.Create(DebugPath, ErrorPath, level);

        logger.Verbose("keystroke {Vk}", "0x41");
        level.MinimumLevel = LogEventLevel.Debug;
        logger.Verbose("keystroke {Vk}", "0x42");
        logger.Debug("SWITCH done");
        Flush(logger);

        string debug = File.ReadAllText(DebugPath);
        Assert.Contains("0x41", debug, StringComparison.Ordinal);
        Assert.DoesNotContain("0x42", debug, StringComparison.Ordinal);
        Assert.Contains("SWITCH done", debug, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailedLogging_IsOnByDefault_AndSurvivesJsonRoundTrip()
    {
        Assert.True(new AppSettings().VerboseLog);

        string path = Path.Combine(_folder, "settings.json");
        Directory.CreateDirectory(_folder);
        var storage = new SettingsStorage(path);

        storage.Save(new AppSettings { VerboseLog = false });

        Assert.False(storage.Load().VerboseLog);
    }

    [Fact]
    public void Create_ReportsEachEventWithLevelAndTime()
    {
        ILogger logger = Logging.Create(DebugPath, ErrorPath);

        logger.Information("startup: ready (elevated={Elevated})", false);
        Flush(logger);

        // Serilog renders a bool property in lowercase — worth pinning down, since everything else in the
        // log is read by eye.
        string debug = File.ReadAllText(DebugPath);
        Assert.Contains("[INF] startup: ready (elevated=false)", debug, StringComparison.Ordinal);
        Assert.Matches(@"\d{2}:\d{2}:\d{2}\.\d{3} \[INF\]", debug);
    }

    [Fact]
    public void Messages_WithBracesInTheValues_AreRenderedAsText()
    {
        // Values come from whatever the user types, so a stray "{" must not be read as a property.
        ILogger logger = Logging.Create(DebugPath, ErrorPath);

        logger.Debug("  handle buffer='{Buffer}'", "ghbdtn{something}");
        Flush(logger);

        Assert.Contains("buffer='ghbdtn{something}'", File.ReadAllText(DebugPath), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryEntry_ReachesTheFile_WithoutDisposingTheLogger()
    {
        // The log is read while the app is still running — a trace that only appears after shutdown would
        // diagnose nothing. The sink buffers, so it flushes on its own interval.
        ILogger logger = Logging.Create(DebugPath, ErrorPath);

        logger.Information("startup: ready");

        Assert.True(WaitForFile("startup: ready"), $"debug.log never received the event:{Environment.NewLine}{Read()}");
        Flush(logger);
    }

    /// <summary>Polls the trace file for <paramref name="text"/> until the sink's flush interval has passed.</summary>
    private bool WaitForFile(string text)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (File.Exists(DebugPath) && Read().Contains(text, StringComparison.Ordinal)) return true;
            Thread.Sleep(100);
        }

        return false;
    }

    private string Read()
    {
        try
        {
            // The sink keeps the file open: read it the way a running app's log is read in practice —
            // Get-Content, an editor — i.e. sharing the handle.
            using var stream = new FileStream(DebugPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException) { return ""; }
    }
}
