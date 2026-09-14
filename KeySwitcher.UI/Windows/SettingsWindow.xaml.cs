using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeySwitcher.Core.Features;
using KeySwitcher.Core.Settings;
using Serilog;

namespace KeySwitcher.UI.Windows;

/// <summary>
/// Settings dialog (General / Hotkeys / Exclusions / Auto-replace tabs). Edits a copy of the immutable
/// <see cref="AppSettings"/>: nothing is applied until "Зберегти" is clicked, then the new instance is
/// handed to the owner through <see cref="SettingsSaved"/>.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _original;
    private readonly ObservableCollection<string> _exclusions = [];
    private readonly ObservableCollection<string> _personalWords = [];
    private readonly ObservableCollection<AutoReplaceRow> _autoReplace = [];

    /// <summary>Raised on "Зберегти" with the edited settings; the owner persists and applies them.</summary>
    public event Action<AppSettings>? SettingsSaved;

    /// <summary>
    /// Raised on "Зберегти" with the edited personal word list. It lives in its own file, so the owner
    /// saves it and re-applies it to the running switcher.
    /// </summary>
    public event Action<IReadOnlyList<string>>? PersonalWordsSaved;

    public SettingsWindow(AppSettings settings, IReadOnlyList<string> personalWords)
    {
        _original = settings;
        InitializeComponent();

        ApplyToControls(settings);
        foreach (string word in personalWords) _personalWords.Add(word);

        ExclusionsList.ItemsSource = _exclusions;
        PersonalWordsList.ItemsSource = _personalWords;
        AutoReplaceGrid.ItemsSource = _autoReplace;
        RefreshOpenWindows();
    }

    /// <summary>
    /// Fills the dialog from settings. Used by the constructor and by import — the imported file has to
    /// land in the same controls the user would have edited by hand.
    /// </summary>
    private void ApplyToControls(AppSettings settings)
    {
        AutoSwitchCheck.IsChecked = settings.AutoSwitch;
        AutoSwitchLayoutCheck.IsChecked = settings.AutoSwitchLayout;
        FixCapsLockCheck.IsChecked = settings.FixCapsLock;
        SkipCaretMoveCheck.IsChecked = settings.SkipSwitchAfterCaretMove;
        SkipManualSwitchCheck.IsChecked = settings.SkipSwitchAfterManualSwitch;
        AutoStartCheck.IsChecked = settings.AutoStart;
        VerboseLogCheck.IsChecked = settings.VerboseLog;

        ManualSwitchBox.Text = settings.Hotkeys.ManualSwitch;
        ChangeCaseBox.Text = settings.Hotkeys.ChangeCase;
        SelectionLayoutBox.Text = settings.Hotkeys.SelectionLayout;
        TransliterateBox.Text = settings.Hotkeys.Transliterate;

        _exclusions.Clear();
        foreach (string exe in settings.Exclusions) _exclusions.Add(exe);

        _autoReplace.Clear();
        foreach (AutoReplaceEntry entry in settings.AutoReplace)
            _autoReplace.Add(new AutoReplaceRow { Shortcut = entry.Shortcut, Replacement = entry.Replacement });
    }

    /// <summary>What the dialog shows right now — saved on "Зберегти", written out on export.</summary>
    private AppSettings CollectSettings() => _original with
    {
        AutoSwitch = AutoSwitchCheck.IsChecked == true,
        AutoSwitchLayout = AutoSwitchLayoutCheck.IsChecked == true,
        FixCapsLock = FixCapsLockCheck.IsChecked == true,
        SkipSwitchAfterCaretMove = SkipCaretMoveCheck.IsChecked == true,
        SkipSwitchAfterManualSwitch = SkipManualSwitchCheck.IsChecked == true,
        AutoStart = AutoStartCheck.IsChecked == true,
        VerboseLog = VerboseLogCheck.IsChecked == true,
        Hotkeys = _original.Hotkeys with
        {
            ManualSwitch = ManualSwitchBox.Text,
            ChangeCase = ChangeCaseBox.Text,
            SelectionLayout = SelectionLayoutBox.Text,
            Transliterate = TransliterateBox.Text,
        },
        Exclusions = [.. _exclusions.Where(x => !string.IsNullOrWhiteSpace(x))],
        AutoReplace =
        [
            .. _autoReplace
                .Where(row => !string.IsNullOrWhiteSpace(row.Shortcut))
                .Select(row => new AutoReplaceEntry(row.Shortcut!.Trim(), row.Replacement ?? ""))
        ],
    };

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        AppSettings updated = CollectSettings();

        SettingsSaved?.Invoke(updated);
        PersonalWordsSaved?.Invoke([.. _personalWords]);
        Close();
    }

    // ----- Exclusions tab -----

    private void OnAddExclusionFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Виберіть програму",
            Filter = "Програми (*.exe)|*.exe|Усі файли (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true)
            AddExclusion(Path.GetFileName(dialog.FileName));
    }

    private void OnOpenWindowsDropDownOpened(object sender, EventArgs e) => RefreshOpenWindows();

    /// <summary>
    /// Rebuilds the dropdown: the set of open windows changes while the dialog is up (the user starts the
    /// app they want to exclude and comes back), and rebuilding is cheaper to reason about than keeping a
    /// list in sync in the background. Runs once on open and again on every dropdown.
    /// </summary>
    private void RefreshOpenWindows()
    {
        var windows = OpenWindows.Enumerate()
            .Where(w => !_exclusions.Any(x => string.Equals(x, w.ExeName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        OpenWindowsBox.ItemsSource = windows;
        OpenWindowsBox.SelectedItem = null;
    }

    private void OnOpenWindowSelected(object sender, SelectionChangedEventArgs e)
    {
        if (OpenWindowsBox.SelectedItem is not OpenWindow window) return;

        AddExclusion(window.ExeName);
        Log.Information("exclusions: picked {Exe} from the open windows", window.ExeName);

        // Back to no choice: the entry is in the list above now, and the box stays usable for the next
        // pick (this re-enters the handler — the guard above returns for a null selection).
        OpenWindowsBox.SelectedItem = null;
    }

    private void AddExclusion(string? exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName)) return;

        // The list is compared case-insensitively by the switcher, so keep it unique here as well.
        if (_exclusions.Any(x => string.Equals(x, exeName, StringComparison.OrdinalIgnoreCase))) return;

        _exclusions.Add(exeName);
    }

    private void OnRemoveExclusionClick(object sender, RoutedEventArgs e)
    {
        if (ExclusionsList.SelectedItem is string exe)
            _exclusions.Remove(exe);
    }

    // ----- Personal dictionary tab -----

    private void OnPersonalWordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        e.Handled = true; // Enter adds the word — the dialog has no default button of its own here
        AddPersonalWord();
    }

    private void OnAddPersonalWordClick(object sender, RoutedEventArgs e) => AddPersonalWord();

    /// <summary>
    /// Switches to the dictionary tab — where a word offered from the tray balloon lands, so the question and
    /// the list it changes are on screen together.
    /// </summary>
    public void ShowDictionaryTab() => Tabs.SelectedItem = DictionaryTab;

    /// <summary>
    /// Adds a word that came from outside the dialog — the tray balloon offers words the user keeps
    /// converting by hand. The dialog keeps its own copy of the list, so without this the word would be lost
    /// the moment the user pressed "Зберегти" here.
    /// </summary>
    public void AddPersonalWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return;

        // The lookup is case-insensitive, so keeping the list unique is what the user expects to see.
        if (!_personalWords.Any(w => string.Equals(w, word, StringComparison.OrdinalIgnoreCase)))
            _personalWords.Add(word);
    }

    private void AddPersonalWord()
    {
        string word = PersonalWordBox.Text.Trim();
        if (word.Length == 0) return;

        AddPersonalWord(word);

        PersonalWordBox.Clear();
        PersonalWordBox.Focus();
    }

    private void OnRemovePersonalWordClick(object sender, RoutedEventArgs e)
    {
        if (PersonalWordsList.SelectedItem is string word)
            _personalWords.Remove(word);
    }

    // ----- Auto-replace tab -----

    private void OnAddAutoReplaceClick(object sender, RoutedEventArgs e)
    {
        var row = new AutoReplaceRow();
        _autoReplace.Add(row);
        AutoReplaceGrid.SelectedItem = row;
        AutoReplaceGrid.ScrollIntoView(row);
        AutoReplaceGrid.Focus();
    }

    private void OnRemoveAutoReplaceClick(object sender, RoutedEventArgs e)
    {
        if (AutoReplaceGrid.SelectedItem is AutoReplaceRow row)
            _autoReplace.Remove(row);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    // ----- Other tab: export / import -----

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Експорт налаштувань",
            FileName = "keyswitcher-settings.json",
            DefaultExt = ".json",
            Filter = "Налаштування KeySwitcher (*.json)|*.json|Усі файли (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            // What the dialog shows, not what is on disk: the user may have edited a value and expects the
            // export to contain it.
            File.WriteAllText(dialog.FileName, SettingsStorage.ToJson(CollectSettings()));
            Log.Information("settings: exported to {Path}", dialog.FileName);

            MessageBox.Show(this, $"Налаштування збережено у файл:\n{dialog.FileName}", "KeySwitcher",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "settings export failed");
            MessageBox.Show(this, $"Не вдалося зберегти файл:\n{ex.Message}", "KeySwitcher",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Імпорт налаштувань",
            DefaultExt = ".json",
            Filter = "Налаштування KeySwitcher (*.json)|*.json|Усі файли (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ApplyToControls(SettingsStorage.FromJson(File.ReadAllText(dialog.FileName)));
            Log.Information("settings: imported from {Path}", dialog.FileName);

            // Deliberately not applied yet: the dialog now shows what arrived, and "Зберегти" makes it real.
            // A file that turns out to be the wrong one can simply be cancelled.
            MessageBox.Show(this, "Налаштування завантажено у вікно. Перевірте значення й натисніть «Зберегти».",
                "KeySwitcher", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            // Throws on purpose (SettingsStorage.FromJson) — a silent fall back to defaults would look
            // like a successful import of an empty file.
            Log.Warning(ex, "settings import failed for {Path}", dialog.FileName);
            MessageBox.Show(this, $"Не вдалося прочитати файл налаштувань:\n{ex.Message}", "KeySwitcher",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Esc closes the window. IsCancel is deliberately not used: it sets DialogResult, which throws
    // for a window opened with Show() instead of ShowDialog() — that is what broke "Скасувати".
    // KeyDown (bubbling) is used so the hotkey recorder keeps priority: it marks PreviewKeyDown
    // handled, and WPF then does not raise KeyDown at all.
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        e.Handled = true;
        Close();
    }

    // Key recorder: swallow every keystroke so the box never accumulates text, and write the chord
    // back as a string the HotkeyManager parser understands ("Ctrl+Shift+F5", "Pause").
    private void OnHotkeyBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (sender is not TextBox box) return;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            box.Text = InitialHotkey(box);
            return;
        }

        if (IsModifierKey(key)) return; // a modifier alone is not a chord yet

        string? chord = FormatChord(Keyboard.Modifiers, key);
        if (chord is null) return; // a key the manager cannot register — ignore

        box.Text = chord;
    }

    private string InitialHotkey(TextBox box) =>
        box == ManualSwitchBox ? _original.Hotkeys.ManualSwitch :
        box == ChangeCaseBox ? _original.Hotkeys.ChangeCase :
        box == SelectionLayoutBox ? _original.Hotkeys.SelectionLayout :
        _original.Hotkeys.Transliterate;

    // The ✕ next to a hotkey box: an empty string means "not assigned" — HotkeyManager skips it and
    // the combination stops working after the settings are saved.
    private void OnClearHotkeyClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string boxName } && FindName(boxName) is TextBox box)
            box.Text = string.Empty;
    }

    private static bool IsModifierKey(Key key) => key
        is Key.LeftShift or Key.RightShift
        or Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt
        or Key.LWin or Key.RWin
        or Key.None;

    private static string? FormatChord(ModifierKeys modifiers, Key key)
    {
        string? name = GetKeyName(key);
        if (name is null) return null;

        var parts = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(name);

        return string.Join('+', parts);
    }

    // Mirrors HotkeyManager.GetVirtualKey — a key missing there would silently fail to register.
    private static string? GetKeyName(Key key) => key switch
    {
        Key.Pause => "Pause",
        Key.Scroll => "Scroll",
        Key.CapsLock => "CapsLock",
        Key.Insert => "Insert",
        Key.Delete => "Delete",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        >= Key.F1 and <= Key.F12 => key.ToString(),
        Key.Space => "Space",
        Key.Tab => "Tab",
        Key.Enter => "Enter",
        Key.Back => "Back",
        Key.Escape => "Esc",
        Key.Apps => "Apps", // menu/context-menu key — usable as a bare single-key switch
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + ((int)key - (int)Key.D0))).ToString(),
        _ => null,
    };
}

/// <summary>
/// Mutable row behind the auto-replace grid: <see cref="AutoReplaceEntry"/> is an immutable positional
/// record, and DataGrid inline editing needs settable properties.
/// </summary>
internal sealed class AutoReplaceRow
{
    public string? Shortcut { get; set; } = "";
    public string? Replacement { get; set; }
}
