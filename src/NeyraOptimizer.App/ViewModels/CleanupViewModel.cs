using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.App.Views.Dialogs;
using NeyraOptimizer.Application.Modules;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Models.Power;

namespace NeyraOptimizer.App.ViewModels;

public partial class CleanupRow : ObservableObject
{
    public required CleanupCandidate Model { get; init; }
    public string DisplayName => Model.DisplayName;
    public string Description => Model.Description;
    public string SizeText => FormatSize(Model.EstimatedSizeBytes);
    public bool AdminRequired => Model.RequiresAdministrator;

    [ObservableProperty] private bool _isSelected;

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (1024.0 * 1024 * 1024):0.0#} GB",
        >= 1L << 20 => $"{bytes / (1024.0 * 1024):0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0} KB",
        > 0 => $"{bytes} B",
        _ => "â€”",
    };
}

public partial class CleanupViewModel : ViewModelBase
{
    private readonly ICleanupCoordinator _cleanup;

    public ObservableCollection<CleanupRow> Rows { get; } = new();
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isCleaning;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private long _totalBytes;
    [ObservableProperty] private bool _hasResults;

    public string TotalText => CleanupRow.FormatSize(TotalBytes);

    partial void OnTotalBytesChanged(long value) => OnPropertyChanged(nameof(TotalText));

    public CleanupViewModel(SessionState session, IServiceProvider sp) : base(session)
    {
        _cleanup = sp.GetRequiredService<ICleanupCoordinator>();
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        StatusText = Translator.Instance["Cleanup.Scan"];
        try
        {
            var candidates = await Task.Run(() => _cleanup.Scan(CancellationToken.None));
            Rows.Clear();
            long total = 0;
            foreach (var c in candidates.Where(c => c.IsAvailableOnThisMachine))
            {
                Rows.Add(new CleanupRow
                {
                    Model = c,
                    IsSelected = c.SafeByDefault,
                });
                total += c.EstimatedSizeBytes;
            }
            TotalBytes = total;
            HasResults = Rows.Count > 0;
            StatusText = string.Empty;
        }
        catch (Exception ex) { StatusText = ex.Message; }
        finally { IsScanning = false; }
    }

    [RelayCommand]
    private async Task CleanSelectedAsync()
    {
        var selected = Rows.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0 || !CanModify || IsCleaning) return;

        var estimate = CleanupRow.FormatSize(selected.Sum(s => s.Model.EstimatedSizeBytes));
        if (!ConfirmDialog.Ask("Cleanup.Title",
                string.Format(Translator.Instance["Cleanup.Confirm"], estimate), danger: true,
                confirmLocKey: "Cleanup.DeleteSelected")) return;

        IsCleaning = true;
        long freed = 0;
        try
        {
            for (int i = 0; i < selected.Count; i++)
            {
                StatusText = $"{selected[i].DisplayName} ({i + 1}/{selected.Count})";
                try { freed += await _cleanup.DeleteAsync(selected[i].Model, null, CancellationToken.None); }
                catch (Exception ex) { StatusText = ex.Message; }
            }
            StatusText = string.Format(Translator.Instance["Cleanup.FreedReport"], CleanupRow.FormatSize(freed));
            await ScanAsync();
            StatusText = string.Format(Translator.Instance["Cleanup.FreedReport"], CleanupRow.FormatSize(freed));
        }
        finally { IsCleaning = false; }
    }
}
