using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using KeySwitcher.Core.Analysis;
using KeySwitcher.Core.Diagnostics;
using KeySwitcher.Core.Features;
using KeySwitcher.Core.Hooks;
using KeySwitcher.Core.Input;
using KeySwitcher.Core.Layout;
using KeySwitcher.Core.Settings;
using KeySwitcher.UI.Tray;
using KeySwitcher.UI.Windows;
using Serilog;
using Serilog.Events;

namespace KeySwitcher.UI;

public partial class App : Application
{
    private const string MutexName = @"Local\KeySwitcher.SingleInstance.9F2A";

    // Initialized in this order and disposed in reverse: every WinAPI handle is released before
    // the component that created it disappears (see OnExit).
    private Mutex? _instanceMutex;
    private TrayIconController? _trayIcon;
    private KeyboardHook? _keyboardHook;
    private HotkeyManager? _hotkeyManager;
    private AutoSwitcher? _autoSwitcher;
    private ManualSwitcher? _manualSwitcher;
    private CaseChanger? _caseChanger;
    private Transliterator? _transliterator;
    private LayoutDetector? _layoutDetector;
    private DispatcherTimer? _languageTimer;
    private ShellHook? _shellHook;
    private HwndSource? _messageWindow;
    private SettingsWindow? _settingsWindow;

    private readonly SettingsStorage _settingsStorage = new();
    private WordJudge? _wordJudge;
    private DictionaryAnalyzer? _builtInUkrainian;
    private DictionaryAnalyzer? _builtInEnglish;
    private IReadOnlyList<string> _personalWords = [];
    private AppSettings _settings = new();
    private bool _uiErrorShown;
    private bool _outOfReachWarned;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The logger comes first: everything below is worth a line in debug.log, and a failure during
        // bootstrap must leave a trace in error.log.
        Logging.Initialize();
        Logging.WriteEnvironmentInfo();

        // Registered before anything else, for the same reason.
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        if (!TryAcquireSingleInstance())
        {
            Log.Information("startup: another instance is already running - exiting");
            MessageBox.Show("KeySwitcher вже запущено.", "KeySwitcher",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _settings = _settingsStorage.Load();

        // The installer's autostart checkbox (installer/KeySwitcher.iss) asks the app instead of editing
        // settings.json itself: the file format and the Run entry are the app's business, and a Run key
        // written behind the app's back would be deleted on the very next start (AutoStart = false).
        bool autoStartRequested = e.Args.Any(arg =>
            string.Equals(arg, "--enable-autostart", StringComparison.OrdinalIgnoreCase));
        if (autoStartRequested)
        {
            _settings = _settings with { AutoStart = true };
            Log.Information("startup: --enable-autostart - auto-start switched on by the installer");
        }

        // The "detailed logging" setting is applied right after the file is read: before that the logger
        // runs on its default Verbose, and the startup lines above are the only ones written with it.
        ApplyLogLevel(_settings);

        // The Run entry is the source of truth for Windows, so re-apply the persisted flag on each start.
        AutoStartManager.SetEnabled(_settings.AutoStart);

        if (autoStartRequested)
        {
            try
            {
                _settingsStorage.Save(_settings);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "settings save failed");
            }
        }

        try
        {
            await InitializeAsync();
            Log.Information("startup: ready (elevated={Elevated})", Elevation.IsElevated());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "startup failed");
            MessageBox.Show($"Не вдалося запустити KeySwitcher:\n{ex.Message}\n\nПодробиці у файлі:\n{Logging.ErrorFilePath}",
                "KeySwitcher", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // "KeySwitcher.UI.exe --settings" opens the window straight away: handy when the tray is
        // unavailable, and the only way to reach the window without a mouse for manual checks.
        if (e.Args.Any(arg => string.Equals(arg, "--settings", StringComparison.OrdinalIgnoreCase)))
        {
            Log.Debug("startup: --settings requested");
            OnSettingsRequested();
        }
    }

    private async Task InitializeAsync()
    {
        // One buffer shared by both switchers - the manual hotkey reads the word the auto-switcher
        // accumulated; the word survives in the buffer for a moment after the chord clears it.
        var wordBuffer = new WordBuffer();
        var layoutConverter = new LayoutConverter();
        var charsetAnalyzer = new CharsetAnalyzer();
        var inputSimulator = new InputSimulator();
        _layoutDetector = new LayoutDetector();

        // Tray first: it is the only visible signal that the app came up. No splash screen.
        _trayIcon = new TrayIconController { IsEnabled = _settings.AutoSwitch };
        _trayIcon.SettingsRequested += OnSettingsRequested;
        _trayIcon.ExitRequested += OnExitRequested;
        _trayIcon.EnabledChanged += OnEnabledChanged;
        _trayIcon.SetLanguage(_layoutDetector.GetCurrentLanguage());

        Log.Debug("startup: loading dictionaries");
        Task<DictionaryAnalyzer> uaTask = DictionaryAnalyzer.LoadUkrainianAsync();
        Task<DictionaryAnalyzer> enTask = DictionaryAnalyzer.LoadEnglishAsync();
        await Task.WhenAll(uaTask, enTask);

        _builtInUkrainian = uaTask.Result;
        _builtInEnglish = enTask.Result;

        // Letter-combination statistics are built from the same word lists: they are the primary
        // wrong-layout signal, the dictionaries only back them up. CPU-bound — off the UI thread.
        Log.Debug("startup: building language models");
        Task<LanguageModel> uaModelTask = Task.Run(() => LanguageModel.Build(KeyboardLanguage.Ukrainian, _builtInUkrainian.Words));
        Task<LanguageModel> enModelTask = Task.Run(() => LanguageModel.Build(KeyboardLanguage.English, _builtInEnglish.Words));
        await Task.WhenAll(uaModelTask, enModelTask);

        // The personal words go into the lookups only: the statistics describe the language, not the user.
        IReadOnlyList<string> personalWords = PersonalDictionary.Load();
        (IReadOnlyList<string> personalUkrainian, IReadOnlyList<string> personalEnglish) =
            PersonalDictionary.SplitByScript(personalWords);
        _personalWords = personalWords;
        Log.Information("personal dictionary: {Words} words (uk={Ukrainian}, en={English})",
            personalWords.Count, personalUkrainian.Count, personalEnglish.Count);

        _wordJudge = new WordJudge(
            uaModelTask.Result, enModelTask.Result, layoutConverter,
            _builtInUkrainian.WithExtraWords(personalUkrainian),
            _builtInEnglish.WithExtraWords(personalEnglish));

        _keyboardHook = new KeyboardHook();

        _hotkeyManager = new HotkeyManager();
        _hotkeyManager.HotkeyFired += OnHotkeyFired;
        RegisterHotkeys(_settings.Hotkeys);

        _autoSwitcher = new AutoSwitcher(
            _keyboardHook, _layoutDetector, layoutConverter, charsetAnalyzer,
            _wordJudge, inputSimulator, wordBuffer, new AutoReplacer(wordBuffer), _settings)
        {
            IsEnabled = _settings.AutoSwitch,
        };
        _autoSwitcher.LanguageChanged += UpdateTrayLanguage;

        _manualSwitcher = new ManualSwitcher(wordBuffer, _layoutDetector, layoutConverter, _wordJudge, inputSimulator);
        _caseChanger = new CaseChanger(wordBuffer, inputSimulator);
        _transliterator = new Transliterator(wordBuffer, inputSimulator);

        // The shell posts HSHELL_LANGUAGE / HSHELL_WINDOWACTIVATED to a window of ours, so create one:
        // a hidden HwndSource gives us a real window without showing anything on screen.
        _messageWindow = new HwndSource(new HwndSourceParameters("KeySwitcher.MessageWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        });
        _shellHook = new ShellHook(_messageWindow.Handle);
        _shellHook.InputLanguageChanged += OnInputLanguageChanged;
        _messageWindow.AddHook(OnMessageReceived);

        // Install last, on the UI thread (WH_KEYBOARD_LL needs a message loop).
        _keyboardHook.Install();

        // WinEventHook-based layout tracking lands in Phase 3; polling is enough to keep the icon current
        _languageTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _languageTimer.Tick += OnLanguageTick;
        _languageTimer.Start();

        Log.Debug("startup: hook installed, bootstrap complete");
    }

    private bool TryAcquireSingleInstance()
    {
        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (createdNew) return true;

        // Do not keep the mutex: OnExit must never release one this process does not own.
        _instanceMutex.Dispose();
        _instanceMutex = null;
        return false;
    }

    private void RegisterHotkeys(HotkeySettings hotkeys)
    {
        try
        {
            _hotkeyManager!.Register(hotkeys);
        }
        catch (InvalidOperationException ex)
        {
            // A taken hotkey isn't a fault of ours and shouldn't kill auto-switch — warn and carry on.
            Log.Warning(ex, "hotkeys: registration failed");
            MessageBox.Show(
                $"Гаряча клавіша зайнята іншою програмою:\n{ex.Message}\n\n" +
                "KeySwitcher працює далі, але ця комбінація не діє. Змініть її у налаштуваннях (трей → Налаштування).",
                "KeySwitcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Fatal: the CLR is already tearing the process down — all that is left to do is leave evidence.
    // The exception goes into the message as a property, not as the exception slot: the payload may be
    // anything at all, and a wrapped one would only add a stack trace pointing here.
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        Log.Fatal("unhandled exception (terminating={Terminating}): {Details}", e.IsTerminating, e.ExceptionObject);

    // Not fatal for a tray app: log it, tell the user once, and keep auto-switching alive. A broken
    // settings window must not cost the user the whole tool.
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "UI exception");
        e.Handled = true;

        if (_uiErrorShown) return; // one dialog per run — nobody reads a wall of them
        _uiErrorShown = true;

        MessageBox.Show(
            $"Сталася помилка інтерфейсу:\n{e.Exception.Message}\n\n" +
            $"Подробиці у файлі:\n{Logging.ErrorFilePath}\n\nKeySwitcher продовжує працювати.",
            "KeySwitcher", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // Nobody awaits these tasks, so there is nobody else to observe the exception: log it and mark it
    // observed, otherwise it resurfaces at collection time and takes the process with it.
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "unobserved task exception");
        e.SetObserved();
    }

    // HotkeyFired is raised on the HotkeyManager message thread - marshal to the UI thread.
    private void OnHotkeyFired(HotkeyId id) => Dispatcher.InvokeAsync(() => HandleHotkeyAsync(id));

    private async void HandleHotkeyAsync(HotkeyId id)
    {
        try
        {
            if (id == HotkeyId.ManualSwitch && _manualSwitcher is not null)
            {
                await _manualSwitcher.SwitchLastWordAsync();

                // The user's own switch stands: pause the auto-switcher for the nearest word (settings).
                _autoSwitcher?.NotifyManualSwitch();
            }
            else if (id == HotkeyId.ChangeCase && _caseChanger is not null)
                await _caseChanger.ChangeCaseAsync();
            else if (id == HotkeyId.SelectionLayout && _manualSwitcher is not null)
                await _manualSwitcher.SwitchSelectionAsync();
            else if (id == HotkeyId.Transliterate && _transliterator is not null)
                await _transliterator.TransliterateAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "hotkey {Hotkey} failed", id);
        }
    }

    private void OnLanguageTick(object? sender, EventArgs e)
    {
        UpdateTrayLanguage();
        WarnIfWindowIsOutOfReach();
    }

    // Fires on HSHELL_LANGUAGE and HSHELL_WINDOWACTIVATED, i.e. right when another window takes focus.
    private void OnInputLanguageChanged()
    {
        UpdateTrayLanguage();
        WarnIfWindowIsOutOfReach();
    }

    // Windows keeps input apart by integrity level (UIPI): a non-elevated KeySwitcher receives no
    // keystrokes from an elevated window, so auto-switching is simply blind there. Say it once instead of
    // looking broken. The check runs on activation because an elevated window never sends us a keystroke
    // to notice it from.
    private void WarnIfWindowIsOutOfReach()
    {
        if (_outOfReachWarned || !Elevation.IsFocusedWindowOutOfReach()) return;

        _outOfReachWarned = true;
        Log.Information("elevated window in focus: the hook is out of reach there (UIPI)");
        _trayIcon?.ShowBalloon("KeySwitcher",
            "Це вікно запущено з правами адміністратора — тут KeySwitcher не бачить натискань. " +
            "Запусти KeySwitcher від імені адміністратора, якщо автоперемикання потрібне й там.");
    }

    // Safety net for the shell hook: a missed notification must not leave the flag stale forever.
    private nint OnMessageReceived(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        _shellHook?.HandleMessage(msg, wParam);
        return 0;
    }

    // Reads the active window's layout and mirrors it in the tray flag. Called from the polling timer
    // (safety net) and from AutoSwitcher.LanguageChanged (immediately on a modifier keystroke).
    private void UpdateTrayLanguage()
    {
        if (_trayIcon is null || _layoutDetector is null) return;
        _trayIcon.SetLanguage(_layoutDetector.GetCurrentLanguage());
    }

    private void OnEnabledChanged(bool enabled)
    {
        _settings = _settings with { AutoSwitch = enabled };
        ApplyLogLevel(_settings);

        if (_autoSwitcher is not null)
        {
            _autoSwitcher.Settings = _settings;
            _autoSwitcher.IsEnabled = enabled;
        }

        try
        {
            _settingsStorage.Save(_settings);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "settings save failed");
        }
    }

    private void OnSettingsRequested()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        // Non-modal: auto-switch keeps running and the tray stays reachable while the dialog is open.
        _settingsWindow = new SettingsWindow(_settings, _personalWords);
        _settingsWindow.SettingsSaved += OnSettingsSaved;
        _settingsWindow.PersonalWordsSaved += OnPersonalWordsSaved;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    /// <summary>
    /// The dialog's personal word list is the truth: write it where the next start will read it, then
    /// put it to work straight away — a word added to stop the switcher "fixing" it is expected to work
    /// on the very next word, not after a restart.
    /// </summary>
    private void OnPersonalWordsSaved(IReadOnlyList<string> words)
    {
        try
        {
            PersonalDictionary.Save(words);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "personal dictionary: could not be saved");
            return;
        }

        _personalWords = words;
        (IReadOnlyList<string> ukrainian, IReadOnlyList<string> english) = PersonalDictionary.SplitByScript(words);

        // Rebuilding the lookups copies ~340k words each; keep it off the UI thread. The judge swaps its
        // references atomically, so a keystroke judged meanwhile sees one pair or the other.
        _ = Task.Run(() =>
        {
            _wordJudge!.SetDictionaries(
                _builtInUkrainian!.WithExtraWords(ukrainian),
                _builtInEnglish!.WithExtraWords(english));

            Log.Information("personal dictionary: {Words} words applied (uk={Ukrainian}, en={English})",
                words.Count, ukrainian.Count, english.Count);
        });
    }

    private void OnSettingsSaved(AppSettings settings)
    {
        _settings = settings;
        ApplyLogLevel(settings);

        try
        {
            _settingsStorage.Save(settings);
            Log.Information("settings: applied from the dialog and saved");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "settings save failed");
        }

        if (_autoSwitcher is not null)
        {
            _autoSwitcher.Settings = settings;
            _autoSwitcher.IsEnabled = settings.AutoSwitch;
        }

        if (_trayIcon is not null) _trayIcon.IsEnabled = settings.AutoSwitch;

        _hotkeyManager?.Unregister();
        RegisterHotkeys(settings.Hotkeys);

        AutoStartManager.SetEnabled(settings.AutoStart);
    }

    private void OnExitRequested() => Shutdown();

    /// <summary>
    /// Turns the "detailed logging" checkbox into a log level: Verbose writes the per-keystroke trace,
    /// Debug leaves the decisions and the mechanics. The switch is read per event, so the change applies
    /// to a running app — no restart, which matters because the setting is usually flipped to catch
    /// something that is happening right now.
    /// </summary>
    private static void ApplyLogLevel(AppSettings settings)
    {
        LogEventLevel level = settings.VerboseLog ? LogEventLevel.Verbose : LogEventLevel.Debug;
        Logging.SetMinimumLevel(level);
        Log.Debug("logging: detailed logging {State}", settings.VerboseLog ? "on (Verbose)" : "off (Debug)");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("exit: disposing");

        _languageTimer?.Stop();

        // Reverse init order: AutoSwitcher -> HotkeyManager -> KeyboardHook -> ShellHook -> TrayIconController
        _autoSwitcher?.Dispose();
        _hotkeyManager?.Dispose();
        _keyboardHook?.Dispose();
        _shellHook?.Dispose();
        _messageWindow?.Dispose();
        _trayIcon?.Dispose();

        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
        }

        // Last of all: anything still buffered in the sinks must reach the files.
        Log.CloseAndFlush();

        base.OnExit(e);
    }
}