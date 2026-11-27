using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using KeySwitcher.Core.Layout;
using MouseButtons = System.Windows.Forms.MouseButtons;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;
using WpfFontFamily = System.Windows.Media.FontFamily;
using Serilog;

namespace KeySwitcher.UI.Tray;

/// <summary>
/// Tray icon showing the flag of the active layout, plus a context menu.
/// <para>
/// The icon itself is WinForms (WPF has no tray icon), but the menu is a WPF <see cref="ContextMenu"/>:
/// a WinForms <c>ContextMenuStrip</c> is drawn in the Windows 7 style and ignores the app theme, while the
/// WPF menu picks up the Fluent theme (App.ThemeMode) — light/dark and accent, like the settings window.
/// </para>
/// Owns the <see cref="NotifyIcon"/> WinAPI handle, therefore <see cref="IDisposable"/>;
/// disposed last in the App dispose chain.
/// </summary>
internal sealed class TrayIconController : IDisposable
{
    private const string TooltipEnglish = "KeySwitcher — EN";
    private const string TooltipUkrainian = "KeySwitcher — УК";
    private const int IconSize = 16;

    private readonly Icon _englishIcon = CreateEnglishFlag();
    private readonly Icon _ukrainianIcon = CreateUkrainianFlag();
    private readonly MenuItem _enabledItem;
    private readonly ContextMenu _menu;
    private readonly Window _menuHost;
    private readonly NotifyIcon _notifyIcon;

    private KeyboardLanguage _language = KeyboardLanguage.Unknown;
    private bool _disposed;

    /// <summary>Raised when the user asks for the settings window (menu item or double click).</summary>
    public event Action? SettingsRequested;

    /// <summary>Raised when the user picks "Вихід" in the menu.</summary>
    public event Action? ExitRequested;

    /// <summary>Raised with the new check state after the user toggles "Увімкнено".</summary>
    public event Action<bool>? EnabledChanged;

    public TrayIconController()
    {
        // MenuItem toggles IsChecked itself before raising Click, so the handler sees the new state.
        _enabledItem = new MenuItem { Header = "Увімкнено", IsCheckable = true, IsChecked = true };
        _enabledItem.Click += (_, _) => EnabledChanged?.Invoke(_enabledItem.IsChecked);

        var settingsItem = new MenuItem { Header = "Налаштування...", Icon = Glyph(""), FontWeight = FontWeights.SemiBold };
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();

        var exitItem = new MenuItem { Header = "Вихід", Icon = Glyph("") };
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        _menu = new ContextMenu();
        _menu.Items.Add(_enabledItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(exitItem);

        // A popup menu of a background app closes on an outside click only if that app is in the
        // foreground; otherwise it hangs on screen until an item is picked. An invisible window that is
        // activated before the menu opens gives it that foreground — and when the user clicks elsewhere,
        // the window deactivates and takes the menu with it. (ContextMenuStrip did the same internally.)
        _menuHost = new Window
        {
            Width = 0, Height = 0,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
        };
        _menu.PlacementTarget = _menuHost;
        _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _menu.Closed += (_, _) => _menuHost.Hide();
        _menuHost.Deactivated += (_, _) => _menu.IsOpen = false;

        _notifyIcon = new NotifyIcon
        {
            Icon = _englishIcon,
            Text = TooltipEnglish,
            Visible = true
        };
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) ShowMenu();
        };
        _notifyIcon.DoubleClick += (_, _) => SettingsRequested?.Invoke();
    }

    /// <summary>Check state of the "Увімкнено" item. Setting it does not raise <see cref="EnabledChanged"/>.</summary>
    public bool IsEnabled
    {
        get => _enabledItem.IsChecked;
        set => _enabledItem.IsChecked = value;
    }

    /// <summary>Shows the flag matching the given layout; no-op when it is already displayed.</summary>
    public void SetLanguage(KeyboardLanguage language)
    {
        if (_disposed || language == _language) return;

        _language = language;
        bool ukrainian = language == KeyboardLanguage.Ukrainian;
        _notifyIcon.Icon = ukrainian ? _ukrainianIcon : _englishIcon;

        // The tooltip repeats the language: if the shell fails to repaint the icon, hovering still
        // shows what KeySwitcher thinks the layout is.
        _notifyIcon.Text = ukrainian ? TooltipUkrainian : TooltipEnglish;

        Log.Debug("tray: language -> {Language}", language.ToString());
    }

    /// <summary>
    /// Shows a balloon next to the tray icon. For things the user must know but that should not steal
    /// focus with a dialog — the tray app is a background tool, not a chat partner.
    /// </summary>
    /// <remarks>
    /// Notices only: Windows 11 reports the balloon's ✕ as a click and drops the balloon entirely under
    /// "Do not disturb", so anything the user may answer goes through a toast (App.LearnPersonalWord).
    /// </remarks>
    public void ShowBalloon(string title, string text)
    {
        if (_disposed) return;

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(10000);
    }

    private void ShowMenu()
    {
        if (_disposed) return;

        _menuHost.Show();
        _menuHost.Activate();
        _menu.IsOpen = true;
    }

    // Menu icons come from the system icon font, the same one the Windows 11 menus use.
    private static TextBlock Glyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = new WpfFontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontSize = 14,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifyIcon.Visible = false; // must be hidden before disposing, otherwise the icon lingers
        _notifyIcon.Dispose();
        _menu.IsOpen = false;
        _menuHost.Close();
        _englishIcon.Dispose();
        _ukrainianIcon.Dispose();
    }

    // Flags are drawn as 16x16 pixel art: integer rectangles only, no anti-aliasing, so the shapes
    // stay crisp in the tray. A grey border keeps the white parts visible on a light taskbar.
    //
    // Each icon is packed into a real ICO container and loaded with Icon(Stream) instead of
    // Icon.FromHandle(Bitmap.GetHicon()): icons that do not own their image data are a known source of
    // "the tray icon does not repaint until the mouse hovers it" on Windows 11.

    /// <summary>Ukrainian flag: two horizontal bands, blue over yellow.</summary>
    private static Icon CreateUkrainianFlag()
    {
        using var bitmap = new Bitmap(IconSize, IconSize);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);

            using var blue = new SolidBrush(Color.FromArgb(0x00, 0x57, 0xB7));
            using var yellow = new SolidBrush(Color.FromArgb(0xFF, 0xD7, 0x00));
            graphics.FillRectangle(blue, 0, 0, IconSize, IconSize / 2);
            graphics.FillRectangle(yellow, 0, IconSize / 2, IconSize, IconSize / 2);

            DrawFlagBorder(graphics);
        }

        return LoadAsIco(bitmap);
    }

    /// <summary>
    /// US flag (the layout is en-US, LANGID 0x0409), simplified: the real 13 stripes would alias
    /// into mush at 16px, so 8 stripes of 2px under an 8x8 canton read correctly instead.
    /// </summary>
    private static Icon CreateEnglishFlag()
    {
        const int stripeHeight = 2;

        using var bitmap = new Bitmap(IconSize, IconSize);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);

            using var red = new SolidBrush(Color.FromArgb(0xB2, 0x22, 0x34));
            using var white = new SolidBrush(Color.White);
            using var canton = new SolidBrush(Color.FromArgb(0x3C, 0x3B, 0x6E));

            for (int y = 0; y < IconSize; y += stripeHeight)
                graphics.FillRectangle(y / stripeHeight % 2 == 0 ? red : white, 0, y, IconSize, stripeHeight);

            graphics.FillRectangle(canton, 0, 0, IconSize / 2, IconSize / 2);

            DrawFlagBorder(graphics);
        }

        return LoadAsIco(bitmap);
    }

    /// <summary>Packs the drawn bitmap into a single-image ICO container and loads it as an Icon.</summary>
    private static Icon LoadAsIco(Bitmap bitmap)
    {
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        byte[] pixels = png.ToArray();

        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico))
        {
            // ICONDIR
            writer.Write((ushort)0);        // reserved
            writer.Write((ushort)1);        // type: icon
            writer.Write((ushort)1);        // image count
            // ICONDIRENTRY
            writer.Write((byte)IconSize);   // width
            writer.Write((byte)IconSize);   // height
            writer.Write((byte)0);          // palette colours
            writer.Write((byte)0);          // reserved
            writer.Write((ushort)1);        // colour planes
            writer.Write((ushort)32);       // bits per pixel
            writer.Write(pixels.Length);    // image size
            writer.Write(6 + 16);           // image offset
            writer.Write(pixels);
            writer.Flush();

            ico.Position = 0;
            return new Icon(ico, IconSize, IconSize);
        }
    }

    private static void DrawFlagBorder(Graphics graphics)
    {
        using var pen = new Pen(Color.FromArgb(0x70, 0x70, 0x70));
        graphics.DrawRectangle(pen, 0, 0, IconSize - 1, IconSize - 1);
    }
}
