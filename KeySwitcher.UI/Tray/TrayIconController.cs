using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using KeySwitcher.Core.Layout;
using Serilog;

namespace KeySwitcher.UI.Tray;

/// <summary>
/// Tray icon showing the flag of the active layout, plus a context menu.
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
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ContextMenuStrip _menu;
    private readonly NotifyIcon _notifyIcon;

    private KeyboardLanguage _language = KeyboardLanguage.Unknown;
    private Action? _balloonClick;
    private bool _disposed;

    /// <summary>Raised when the user asks for the settings window (menu item or double click).</summary>
    public event Action? SettingsRequested;

    /// <summary>Raised when the user picks "Вихід" in the menu.</summary>
    public event Action? ExitRequested;

    /// <summary>Raised with the new check state after the user toggles "Увімкнено".</summary>
    public event Action<bool>? EnabledChanged;

    public TrayIconController()
    {
        _enabledItem = new ToolStripMenuItem("Увімкнено") { CheckOnClick = true, Checked = true };
        _enabledItem.Click += (_, _) => EnabledChanged?.Invoke(_enabledItem.Checked);

        var settingsItem = new ToolStripMenuItem("Налаштування...");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();

        var exitItem = new ToolStripMenuItem("Вихід");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(
        [
            _enabledItem,
            new ToolStripSeparator(),
            settingsItem,
            new ToolStripSeparator(),
            exitItem
        ]);

        _notifyIcon = new NotifyIcon
        {
            Icon = _englishIcon,
            Text = TooltipEnglish,
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => SettingsRequested?.Invoke();
        _notifyIcon.BalloonTipClicked += (_, _) =>
        {
            // One click, one action: a balloon that was dismissed and clicked later must not fire twice.
            Action? click = _balloonClick;
            _balloonClick = null;
            click?.Invoke();
        };
    }

    /// <summary>Check state of the "Увімкнено" item. Setting it does not raise <see cref="EnabledChanged"/>.</summary>
    public bool IsEnabled
    {
        get => _enabledItem.Checked;
        set => _enabledItem.Checked = value;
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
    /// <param name="onClick">
    /// Runs on the UI thread when the user clicks the balloon; a balloon has no buttons, so a click is the
    /// only way to answer one. The action belongs to the balloon shown last — a later balloon (an unrelated
    /// warning, say) replaces it, and clicking that warning will not fire something the user never saw.
    /// </param>
    public void ShowBalloon(string title, string text, Action? onClick = null)
    {
        if (_disposed) return;

        _balloonClick = onClick;
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(10000);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifyIcon.Visible = false; // must be hidden before disposing, otherwise the icon lingers
        _notifyIcon.Dispose();
        _menu.Dispose();
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
