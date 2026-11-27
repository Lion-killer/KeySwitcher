using System.Windows;
using System.Windows.Controls;

namespace KeySwitcher.UI.Windows;

/// <summary>
/// <see cref="MessageBox"/> in the app's Fluent theme. Same call shape (message, caption, buttons, image →
/// result), so a call site changes by the class name only.
/// </summary>
public partial class MessageDialog : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;

    private MessageDialog(string message, string caption, MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();

        Title = caption;
        MessageText.Text = message;
        SetIcon(image);

        switch (buttons)
        {
            case MessageBoxButton.YesNo:
                AddButton("Так", MessageBoxResult.Yes, isDefault: true);
                AddButton("Ні", MessageBoxResult.No, isCancel: true);
                break;
            default:
                AddButton("OK", MessageBoxResult.OK, isDefault: true, isCancel: true);
                break;
        }
    }

    public static MessageBoxResult Show(string message, string caption, MessageBoxButton buttons, MessageBoxImage image) =>
        Show(null, message, caption, buttons, image);

    /// <param name="owner">
    /// The dialog stays on top of it and centred over it; <c>null</c> — centred on the screen, which is what a
    /// background tray app with no window of its own needs.
    /// </param>
    public static MessageBoxResult Show(Window? owner, string message, string caption,
        MessageBoxButton buttons, MessageBoxImage image)
    {
        var dialog = new MessageDialog(message, caption, buttons, image);
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Topmost = true; // nothing of ours is in front, so it must not open behind other apps
        }

        dialog.ShowDialog();
        return dialog._result;
    }

    // Glyphs and colours of the Windows 11 status icons. Question has no glyph of its own there — the
    // accent "info" glyph would mislead, so the help glyph is used.
    private void SetIcon(MessageBoxImage image)
    {
        (string glyph, string brush) = image switch
        {
            MessageBoxImage.Error => ("", "SystemFillColorCriticalBrush"),
            MessageBoxImage.Warning => ("", "SystemFillColorCautionBrush"),
            MessageBoxImage.Question => ("", "AccentFillColorDefaultBrush"),
            MessageBoxImage.Information => ("", "AccentFillColorDefaultBrush"),
            _ => ("", "TextFillColorPrimaryBrush"),
        };

        IconGlyph.Text = glyph;
        IconGlyph.SetResourceReference(TextBlock.ForegroundProperty, brush);
        if (glyph.Length == 0) IconGlyph.Visibility = Visibility.Collapsed;
    }

    private void AddButton(string text, MessageBoxResult result, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 110,
            IsDefault = isDefault,
            IsCancel = isCancel,
            Margin = new Thickness(Buttons.Children.Count == 0 ? 0 : 8, 0, 0, 0),
        };

        // The default action is the accent one, as in Windows 11 dialogs.
        if (isDefault) button.SetResourceReference(StyleProperty, "AccentButtonStyle");

        button.Click += (_, _) =>
        {
            _result = result;
            Close();
        };
        Buttons.Children.Add(button);
    }

    protected override void OnClosed(EventArgs e)
    {
        // Closing with the title-bar ✕ (or Alt+F4) means "no" for a question and "OK" for a notice —
        // the same answers MessageBox gives.
        if (_result == MessageBoxResult.None)
            _result = Buttons.Children.Count > 1 ? MessageBoxResult.No : MessageBoxResult.OK;

        base.OnClosed(e);
    }
}
