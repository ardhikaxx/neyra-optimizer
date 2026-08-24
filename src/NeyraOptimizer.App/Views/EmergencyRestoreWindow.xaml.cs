using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.Application.Restore;
using NeyraOptimizer.Infrastructure.Persistence;

namespace NeyraOptimizer.App.Views;

/// <summary>
/// Emergency Restore: a deliberately minimal window that reads snapshot files directly from
/// ProgramData with integrity checks. Reachable via `NeyraOptimizer.exe --emergency` even when
/// the main interface is broken.
/// </summary>
public partial class EmergencyRestoreWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly IRestoreCenterService _restore;

    public EmergencyRestoreWindow(IServiceProvider services)
    {
        InitializeComponent();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _services = services;
        _restore = services.GetRequiredService<IRestoreCenterService>();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        ListPanel.ItemsSource = await System.Threading.Tasks.Task.Run(() => _restore.ListSnapshots());
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SnapshotSummaryEntry entry) return;
        if (!System.Guid.TryParse(entry.Id, out var id)) return;

        var snap = _restore.LoadSnapshot(id);
        if (snap is null)
        {
            MessageBox.Show(this, Translator.Instance["Restore.IntegrityBad"],
                "Emergency Restore", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = Dialogs.ConfirmDialog.Ask("Restore.Title",
            string.Format(Translator.Instance["Restore.RestoreConfirm"], snap.Changes.Count),
            danger: false, confirmText: Translator.Instance["Common.Restore"]);
        if (!confirm) return;

        try
        {
            var result = await _restore.RestoreAsync(snap, null, CancellationToken.None);
            MessageBox.Show(this,
                string.Format(Translator.Instance["Restore.RestoredOk"], result.RestoredCount, result.FailedCount),
                "Emergency Restore", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Emergency Restore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
