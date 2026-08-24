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

public partial class ServiceRow : ObservableObject
{
    public required ServiceInfo Model { get; init; }
    public string ServiceName => Model.ServiceName;
    public string DisplayName => string.IsNullOrWhiteSpace(Model.DisplayName) ? Model.ServiceName : Model.DisplayName;
    public string StartModeText => Model.StartMode switch
    {
        ServiceStartMode.AutomaticDelayed => "Automatic (Delayed)",
        _ => Model.StartMode.ToString(),
    };
    public string StatusText => Model.Status.ToString();
    public bool Protected => Model.IsProtected;
    public string ProtectionReason => Model.ProtectionReason;

    [ObservableProperty] private bool _isExpanded;
}

public partial class ServicesViewModel : ViewModelBase
{
    private readonly IModuleDataService _modules;
    private readonly ISingleItemActionService _actions;

    public ObservableCollection<ServiceRow> Rows { get; } = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ServiceRow? _selectedRow;
    [ObservableProperty] private string _filterMode = "All";

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnFilterModeChanged(string value) => ApplyFilter();

    public ServicesViewModel(SessionState session, IServiceProvider sp) : base(session)
    {
        _modules = sp.GetRequiredService<IModuleDataService>();
        _actions = sp.GetRequiredService<ISingleItemActionService>();
        RefreshCommand.Execute(null);
    }

    public IEnumerable<ServiceRow> VisibleRows { get; private set; } = Enumerable.Empty<ServiceRow>();

    private void ApplyFilter()
    {
        IEnumerable<ServiceRow> q = Rows;
        if (FilterMode == "Running") q = q.Where(r => r.Model.Status == ServiceStatus.Running);
        else if (FilterMode == "Automatic") q = q.Where(r => r.Model.StartMode is ServiceStartMode.Automatic or ServiceStartMode.AutomaticDelayed);
        else if (FilterMode == "Optional") q = q.Where(r => !r.Protected && (r.Model.Classification == ServiceClassification.Optional || r.Model.Classification == ServiceClassification.Advanced));

        if (!string.IsNullOrEmpty(SearchText))
            q = q.Where(r => r.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                             r.ServiceName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        VisibleRows = q.ToList();
        OnPropertyChanged(nameof(VisibleRows));
    }

    [RelayCommand]
    private void Refresh()
    {
        Rows.Clear();
        foreach (var s in _modules.GetServices().OrderBy(s => s.DisplayName))
            Rows.Add(new ServiceRow { Model = s });
        ApplyFilter();
    }

    private async Task SetModeAsync(ServiceStartMode mode)
    {
        if (SelectedRow is not { } row || row.Protected || !CanModify) return;
        var confirm = ConfirmDialog.Ask("Services.Title",
            $"{row.ServiceName}: {row.StartModeText} → {mode}", danger: mode == ServiceStartMode.Disabled);
        if (!confirm) return;

        var result = await _actions.SetServiceStartModeAsync(row.Model, mode, CancellationToken.None);
        NotifyResult(result);
        Refresh();
    }

    [RelayCommand] private Task SetManual() => SetModeAsync(ServiceStartMode.Manual);
    [RelayCommand] private Task SetAuto() => SetModeAsync(ServiceStartMode.Automatic);
    [RelayCommand] private Task SetDisabled() => SetModeAsync(ServiceStartMode.Disabled);

    protected void NotifyResult(Optimization.Pipeline.OptimizationExecutionResult result)
    {
        if (result.FailedCount > 0)
            ConfirmDialog.Ask("Notify.Warning", string.Join("\n", result.Errors.Take(5)),
                confirmText: Translator.Instance["Common.OK"]);
    }
}
