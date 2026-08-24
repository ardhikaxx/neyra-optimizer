using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.App.ViewModels;
using NeyraOptimizer.Application.Recovery;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Settings;

namespace NeyraOptimizer.App.Views;

public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly MainViewModel _vm;

    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        _vm = new MainViewModel(services);
        DataContext = _vm;
        Activated += (_, _) => _vm.RefreshBanners();
    }

    /// <summary>Runs the read-only initial scan after the shell is visible.</summary>
    public async Task RunStartupScanAsync()
    {
        var dashboard = _vm.CurrentPage as Pages.DashboardPage;
        if (dashboard is not null)
            await dashboard.TriggerStartupScanAsync();
    }

    public async Task ShowCrashRecoveryAsync(PendingRecoveryInfo pending)
    {
        var body = string.Format(Translator.Instance["Recovery.Body"],
            pending.Description, Math.Min(pending.Completed, pending.Total), Math.Max(pending.Total, 1));

        var dlg = new Views.Dialogs.RecoveryDialog(body) { Owner = this };
        var choice = dlg.ShowDialog() == true;
        var recovery = _services.GetRequiredService<ICrashRecoveryService>();
        if (choice)
        {
            var snap = pending.SnapshotId is Guid id ? recovery.LoadSnapshotFor(
                new PendingRecoveryInfo(id, pending.Description, pending.Phase, id, pending.Completed, pending.Total)) : null;
            if (snap is null)
            {
                MessageBox.Show(this, Translator.Instance["Restore.IntegrityBad"],
                    "Neyra Optimizer", MessageBoxButton.OK, MessageBoxImage.Warning);
                recovery.Dismiss(pending);
                return;
            }

            _vm.IsBusy = true;
            _vm.BusyText = Translator.Instance["Progress.PhaseRestorePoint"];
            try
            {
                var result = await recovery.RollbackAsync(snap, null, CancellationToken.None);
                MessageBox.Show(this,
                    string.Format(Translator.Instance["Restore.RestoredOk"], result.RestoredCount, result.FailedCount),
                    "Neyra Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
                recovery.Dismiss(pending);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Neyra Optimizer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { _vm.IsBusy = false; }
        }
        else
        {
            recovery.Dismiss(pending);
        }
    }
}
