using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace KeySwitcher.Core.Diagnostics;

/// <summary>
/// Serilog setup for the whole app: one logger, two files.
/// <list type="bullet">
///   <item><c>debug.log</c> — everything from Verbose up. This is the temporary per-keystroke trace that
///     exists only while the auto-switcher is being polished; the keystroke lines are Verbose, so raising
///     <c>minimumLevel</c> to Debug silences them without touching a single call site.</item>
///   <item><c>error.log</c> — Error and above, the permanent journal that stays: it is what the user is
///     shown as "details are in the file", and the only trace left behind by a fatal crash.</item>
/// </list>
/// </summary>
public static class Logging
{
    /// <summary>Size at which a log file is rolled aside; the sink keeps one previous file per sink.</summary>
    public const long FileSizeLimitBytes = 256 * 1024;

    /// <summary>
    /// The level of the running app, changeable while it runs: the "detailed logging" setting flips it
    /// between Verbose (keystroke trace) and Debug (mechanics only) without a restart.
    /// </summary>
    private static readonly LoggingLevelSwitch s_level = new(LogEventLevel.Verbose);

    /// <summary>
    /// How long an event may sit in the sink's buffer. Small on purpose: the trace is read from the file
    /// while the app is still running, and a log that only appears after shutdown diagnoses nothing.
    /// </summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Same shape as the hand-rolled logger had ("18:10:12.211  message"), with the level added: the file
    /// holds a diagnostic trace, and the level is what makes it readable.
    /// </summary>
    private const string OutputTemplate = "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeySwitcher");

    public static string DebugFilePath => Path.Combine(Folder, "debug.log");

    public static string ErrorFilePath => Path.Combine(Folder, "error.log");

    /// <summary>
    /// Builds the logger. Paths and the minimum level are parameters so tests can point the sinks at a temp
    /// directory; the app itself calls <see cref="Initialize"/> once, before anything else runs.
    /// </summary>
    public static ILogger Create(
        string? debugFilePath = null,
        string? errorFilePath = null,
        LoggingLevelSwitch? level = null) =>
        new LoggerConfiguration()
            .MinimumLevel.ControlledBy(level ?? s_level)
            .WriteTo.File(
                debugFilePath ?? DebugFilePath,
                restrictedToMinimumLevel: LogEventLevel.Verbose,
                outputTemplate: OutputTemplate,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 2,
                flushToDiskInterval: FlushInterval)
            .WriteTo.File(
                errorFilePath ?? ErrorFilePath,
                restrictedToMinimumLevel: LogEventLevel.Error,
                outputTemplate: OutputTemplate,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 2,
                flushToDiskInterval: FlushInterval)
            .CreateLogger();

    /// <summary>Installs the app-wide logger into <see cref="Log.Logger"/>.</summary>
    public static void Initialize() => Log.Logger = Create();

    /// <summary>
    /// Sets the level of <see cref="Log.Logger"/> — what the "detailed logging" checkbox does. Takes
    /// effect immediately: the switch is consulted per event, nothing has to be rebuilt or restarted.
    /// </summary>
    public static void SetMinimumLevel(LogEventLevel level) => s_level.MinimumLevel = level;

    /// <summary>
    /// Records what produced the log — build, OS, runtime — once per run. An error report without it is
    /// guesswork: the same message means different things on a different build or a different Windows.
    /// </summary>
    public static void WriteEnvironmentInfo()
    {
        var assembly = Assembly.GetEntryAssembly()?.GetName() ?? typeof(Logging).Assembly.GetName();
        Log.Information("environment: {Product} {Version} | {Os} | .NET {Runtime}",
            assembly.Name, assembly.Version, RuntimeInformation.OSDescription, Environment.Version);
    }
}
