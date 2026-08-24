using System.Windows;
using NeyraOptimizer.App.Localization;

namespace NeyraOptimizer.App.Views.Dialogs;

/// <summary>
/// Unified in-app dialog surface (no native MessageBox for feature flows). Risk-aware confirm
/// dialog with distinct visual treatment for destructive actions.
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string? detail = null,
        string? confirmText = null, bool danger = false)
    {
        InitializeComponent();
        Owner = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        TitleText.Text = title;
        BodyText.Text = message;
        DetailBorder.Visibility = string.IsNullOrWhiteSpace(detail) ? Visibility.Collapsed : Visibility.Visible;
        DetailText.Text = detail ?? string.Empty;
        ConfirmBtn.Content = confirmText ?? Translator.Instance["Common.OK"];

        if (danger)
        {
            ConfirmBtn.Style = (Style)FindResource("DangerButton");
            IconText.Text = "\uEA14"; // warning glyph
            IconText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Danger");
        }

        ConfirmBtn.Click += (_, _) => { DialogResult = true; Close(); };
        CancelBtn.Content = Translator.Instance["Common.Cancel"];
        CancelBtn.Click += (_, _) => { DialogResult = false; Close(); };
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) { DialogResult = false; Close(); }
        };
    }

    public static bool Ask(string titleKey, string message, string? detail = null,
        string? confirmLocKey = null, string? confirmText = null, bool danger = false) =>
        new ConfirmDialog(
            Translator.Instance[titleKey],
            message,
            detail,
            confirmText ?? (confirmLocKey is null ? null : Translator.Instance[confirmLocKey]),
            danger).ShowDialog() == true;
}
