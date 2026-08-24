using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.App.Views.Dialogs;
using NeyraOptimizer.Application.Modules;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.App.ViewModels;

public partial class AppRow : ObservableObject
{
    public required InstalledAppInfo Model { get; init; }

    public string DisplayName => Model.DisplayName;
    public string Publisher => string.IsNullOrWhiteSpace(Model.Publisher) ? "—" : Model.Publisher;
    public string Version => string.IsNullOrWhiteSpace(Model.Version) ? "—" : Model.Version;
    public string SizeText => Model.SizeBytes is long s && s > 0
        ? $"{s / (1024.0 * 1024):0.#} MB" : "—";
    public string KindText => Model.Kind == InstalledAppKind.Appx ? "AppX/MSIX" : "Win32";
    public bool Protected => Model.IsProtected;
    public string ReinstallNote => string.IsNullOrEmpty(Model.ReinstallNote)
        ? Translator.Instance["Debloat.NotReinstallable"]
        : Translator.Instance["Debloat.Reinstallable"];
    public string CategoryBadge => Protected ? Translator.Instance["Common.Status.Protected"] : KindText;

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) { if (Protected) _isSelected = false; }
}

public partial class DebloatViewModel : ViewModelBase
{
    private readonly IModuleDataService _modules;
    private readonly ISingleItemActionService _actions;

    public ObservableCollection<AppRow> Rows { get; } = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private AppRow? _selectedRow;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = string.Empty;

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(VisibleRows));

    public IEnumerable<AppRow> VisibleRows =>
        string.IsNullOrEmpty(SearchText) ? Rows :
        Rows.Where(r => r.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    public DebloatViewModel(SessionState session, IServiceProvider sp) : base(session)
    {
        _modules = sp.GetRequiredService<IModuleDataService>();
        _actions = sp.GetRequiredService<ISingleItemActionService>();
        RefreshCommand.Execute(null);
    }

    [RelayCommand]
    private void Refresh()
    {
        Rows.Clear();
        foreach (var a in _modules.GetInstalledApps().OrderBy(a => a.DisplayName))
            Rows.Add(new AppRow { Model = a });
    }

    [RelayCommand]
    private void SelectAllOptional()
    {
        foreach (var r in Rows)
            r.IsSelected = !r.Protected && r.Model.Category is RecommendationCategory.Safe or RecommendationCategory.Optional;
    }

    [RelayCommand]
    private async Task ApplySelectedAsync()
    {
        var selected = Rows.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0 || !CanModify || IsBusy) return;

        var message = string.Format(Translator.Instance["Debloat.UninstallWarning"], selected.Count);
        if (!ConfirmDialog.Ask("Debloat.Title", message,
            detail: string.Join("\n", selected.Take(12).Select(r => "• " + r.DisplayName)) +
                    (selected.Count > 12 ? $"\n… (+{selected.Count - 12})" : string.Empty),
            confirmLocKey: "Common.Apply", danger: true)) return;

        IsBusy = true;
        int ok = 0, failed = 0;
        var errors = new List<string>();
        try
        {
            for (int i = 0; i < selected.Count; i++)
            {
                StatusText = $"{selected[i].DisplayName} ({i + 1}/{selected.Count})";
                try
                {
                    var result = await _actions.UninstallAppAsync(selected[i].Model, CancellationToken.None);
                    if (result.AppliedCount > 0 && result.FailedCount == 0) ok++;
                    else { failed++; errors.AddRange(result.Errors.Take(2)); }
                }
                catch (Exception ex) { failed++; errors.Add(ex.Message); }
            }
        }
        finally { IsBusy = false; StatusText = string.Empty; }

        var summary = $"OK: {ok}, Failed: {failed}" + (errors.Count > 0 ? "\n" + string.Join("\n", errors.Take(6)) : string.Empty);
        ConfirmDialog.Ask("Notify.Info", summary, confirmText: Translator.Instance["Common.OK"]);
        Refresh();
    }
}
