using System.Windows;
using System.Windows.Controls;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Rules;
using NeyraOptimizer.Optimization.Pipeline;

namespace NeyraOptimizer.App.Views.Dialogs;

/// <summary>
/// Dry-run preview: shows exactly what will change before anything is applied, with an explicit
/// restore-point consent checkbox. Uninstalls get a dedicated irreversibility warning.
/// </summary>
public sealed record PreviewResult(bool Confirmed, bool CreateRestorePoint);

public partial class PreviewChangesDialog : Window
{
    public PreviewChangesDialog(OptimizationPreview preview, IReadOnlyList<Recommendation> selected)
    {
        InitializeComponent();
        Owner = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        Title = Translator.Instance["Preview.Title"];

        AddLine("Preview.ServicesToModify", preview.ServicesToModify);
        AddLine("Preview.StartupToDisable", preview.StartupEntriesToDisable);
        AddLine("Preview.TasksToDisable", preview.TasksToDisable);
        AddLine("Preview.VisualEffects", preview.VisualEffectsToTune);
        AddLine("Preview.PrivacySettings", preview.PrivacySettingsToApply);
        AddLine("Preview.PackagesToRemove", preview.PackagesToUninstall);

        if (preview.RequiresAdministrator)
            AdminNote.Visibility = Visibility.Visible;
        if (preview.RequiresRestart)
            RestartNote.Visibility = Visibility.Visible;
        if (selected.Any(s => s.Area == RuleArea.Debloat))
            UninstallWarning.Visibility = Visibility.Visible;

        foreach (var w in preview.Warnings.Take(6))
            WarningsList.Items.Add(new TextBlock { Text = "• " + w, TextWrapping = TextWrapping.Wrap });
        WarningsPanel.Visibility = WarningsList.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        RestorePointCheck.IsChecked = true;
        ApplyBtn.Content = Translator.Instance["Preview.ConfirmApply"];
        CancelBtn.Content = Translator.Instance["Common.Cancel"];

        ApplyBtn.Click += (_, _) => { Result = new PreviewResult(true, RestorePointCheck.IsChecked == true); DialogResult = true; Close(); };
        CancelBtn.Click += (_, _) => { Result = new PreviewResult(false, false); Close(); };
    }

    public PreviewResult? Result { get; private set; }

    private void AddLine(string key, int count) => Lines.Children.Add(new TextBlock
    {
        Text = string.Format(Translator.Instance[key], count),
        FontSize = 13,
        Margin = new Thickness(0, 2, 0, 2),
    });

    public static PreviewResult? Show(OptimizationPreview preview, IReadOnlyList<Recommendation> selected)
    {
        var dlg = new PreviewChangesDialog(preview, selected);
        dlg.ShowDialog();
        return dlg.Result;
    }
}
