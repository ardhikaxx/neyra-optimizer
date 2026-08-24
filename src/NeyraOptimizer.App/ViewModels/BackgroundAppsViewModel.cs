using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.App.Views.Dialogs;
using NeyraOptimizer.Application.Modules;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.App.ViewModels;

public partial class ProcessRow : ObservableObject
{
    public required BackgroundProcessInfo Model { get; init; }
    public string Name => Model.Name;
    public string KindText => Model.Kind.ToString();
    public string MemoryText => $"{Model.MemoryMb:0.#} MB";
    public int Pid => Model.ProcessId;
    public bool CanTerminate => Model.CanTerminate;
}

public partial class BgAppToggleRow : ObservableObject
{
    public required string FamilyName { get; init; }
    public required string DisplayName { get; init; }

    [ObservableProperty] private bool _enabled;
}

public partial class BackgroundAppsViewModel : ViewModelBase
{
    private readonly IModuleDataService _modules;
    private readonly ISingleItemActionService _actions;
    private readonly IProcessAnalyzer _processes;

    public ObservableCollection<ProcessRow> ProcessRows { get; } = new();
    public ObservableCollection<BgAppToggleRow> ToggleRows { get; } = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ProcessRow? _selectedProcess;
    [ObservableProperty] private string _statusText = string.Empty;

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(VisibleProcesses));

    public IEnumerable<ProcessRow> VisibleProcesses =>
        string.IsNullOrEmpty(SearchText) ? ProcessRows :
        ProcessRows.Where(r => r.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    public BackgroundAppsViewModel(SessionState session, IServiceProvider sp) : base(session)
    {
        _modules = sp.GetRequiredService<IModuleDataService>();
        _actions = sp.GetRequiredService<ISingleItemActionService>();
        _processes = sp.GetRequiredService<IProcessAnalyzer>();
        RefreshCommand.Execute(null);
    }

    [RelayCommand]
    private void Refresh()
    {
        ProcessRows.Clear();
        foreach (var p in _modules.GetBackgroundProcesses().Where(p => p.Kind is
                     BackgroundProcessKind.UserApplication or BackgroundProcessKind.UserBackgroundApp))
            ProcessRows.Add(new ProcessRow { Model = p });

        ToggleRows.Clear();
        foreach (var (family, name, enabled) in _modules.GetConfigurableBackgroundApps())
            ToggleRows.Add(new BgAppToggleRow { FamilyName = family, DisplayName = name, Enabled = enabled });
    }

    [RelayCommand]
    private async Task EndTaskAsync()
    {
        if (SelectedProcess is not { } row || !CanModify) return;
        if (!row.CanTerminate) return;

        if (!ConfirmDialog.Ask("Background.EndTask", $"{row.Name} (PID {row.Pid})")) return;
        var ok = _processes.TryTerminate(row.Pid, out var error);
        if (!ok)
            ConfirmDialog.Ask("Notify.Warning", error, confirmText: Translator.Instance["Common.OK"]);
        await Task.Delay(300);
        Refresh();
    }

    /// <summary>Called from XAML checkbox via event binding.</summary>
    public async Task ToggleBackgroundAsync(BgAppToggleRow row)
    {
        if (!CanModify) return;
        try
        {
            await _actions.SetBackgroundAppEnabledAsync(row.FamilyName, row.DisplayName, row.Enabled, CancellationToken.None);
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            row.Enabled = !row.Enabled; // revert UI on failure
        }
    }
}
