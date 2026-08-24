using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.App.Views;
using NeyraOptimizer.App.Views.Dialogs;
using NeyraOptimizer.Application.Restore;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Snapshots;
using NeyraOptimizer.Infrastructure.Persistence;

namespace NeyraOptimizer.App.ViewModels;

public partial class SnapshotRow : ObservableObject
{
    public required SnapshotSummaryEntry Model { get; init; }
    public string Id => Model.Id;
    public string Description => string.IsNullOrEmpty(Model.Description) ? "(tanpa deskripsi)" : Model.Description;
    public string CreatedText => Model.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string ChangesText => $"{Model.AppliedCount}/{Model.ChangeCount}";
    public string IntegrityText => Model.Integrity switch
    {
        SnapshotIntegrity.Verified => "✓ SHA-256",
        SnapshotIntegrity.NoManifest => "⚠ no manifest",
        _ => "✗ corrupt",
    };
    public bool Restorable => Model.Integrity != SnapshotIntegrity.Corrupt && Model.AppliedCount > 0;
}

public partial class RestoreCenterViewModel : ViewModelBase
{
    private readonly IRestoreCenterService _restore;
    private readonly IServiceProvider _sp;

    public ObservableCollection<SnapshotRow> Rows { get; } = new();
    [ObservableProperty] private SnapshotRow? _selectedRow;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public RestoreCenterViewModel(SessionState session, IServiceProvider sp) : base(session)
    {
        _restore = sp.GetRequiredService<IRestoreCenterService>();
        _sp = sp;
        Load();
    }

    [RelayCommand]
    private void Refresh() => Load();

    private void Load()
    {
        Rows.Clear();
        foreach (var s in _restore.ListSnapshots())
            Rows.Add(new SnapshotRow { Model = s });
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (SelectedRow is not { } row || !row.Restorable || IsBusy || !CanModify) return;
        if (!Guid.TryParse(row.Id, out var id)) return;

        var snap = _restore.LoadSnapshot(id);
        if (snap is null)
        {
            ConfirmDialog.Ask("Notify.Warning", Translator.Instance["Restore.IntegrityBad"],
                confirmText: Translator.Instance["Common.OK"]);
            return;
        }

        if (!ConfirmDialog.Ask("Restore.Title",
                string.Format(Translator.Instance["Restore.RestoreConfirm"], snap.Changes.Count),
                danger: false, confirmLocKey: "Common.Restore")) return;

        IsBusy = true;
        try
        {
            var result = await _restore.RestoreAsync(snap, null, CancellationToken.None);
            StatusText = string.Format(Translator.Instance["Restore.RestoredOk"], result.RestoredCount, result.FailedCount);
            ConfirmDialog.Ask("Notify.Info", StatusText, confirmText: Translator.Instance["Common.OK"]);
            Load();
        }
        catch (Exception ex) { StatusText = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedRow is not { } row || !CanModify) return;
        if (!ConfirmDialog.Ask("Common.Delete", row.Description, danger: true)) return;
        if (Guid.TryParse(row.Id, out var id))
            _restore.DeleteSnapshot(id);
        Load();
    }

    [RelayCommand]
    private void OpenEmergency()
    {
        new EmergencyRestoreWindow(_sp).Show();
    }
}
