using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.App.Views.Dialogs;
using NeyraOptimizer.Application.Modules;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.App.ViewModels;

public partial class StartupEntryRow : ObservableObject
{
    public required StartupEntry Model { get; init; }
    public string Name => Model.Name;
    public string Publisher => string.IsNullOrWhiteSpace(Model.Publisher) ? "—" : Model.Publisher;
    public string Source => Model.SourceDisplay;
    public string Impact => Model.Impact.ToString();
    public bool Protected => Model.IsProtected;
    public string StatusText => Model.IsProtected
        ? Translator.Instance["Common.Status.Protected"]
        : Model.IsEnabled ? Translator.Instance["Common.Status.Enabled"] : Translator.Instance["Common.Status.Disabled"];

    [ObservableProperty] private bool _isEnabled;
}

public partial class StartupViewModel : ViewModelBase
{
    private readonly IModuleDataService _modules;
    private readonly ISingleItemActionService _actions;

    public ObservableCollection<StartupEntryRow> Rows { get; } = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private StartupEntryRow? _selectedRow;

    public IEnumerable<StartupEntryRow> VisibleRows =>
        string.IsNullOrEmpty(SearchText) ? Rows :
        Rows.Where(r => r.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(VisibleRows));

    public StartupViewModel(SessionState session, IServiceProvider sp) : base(session)
    {
        _modules = sp.GetRequiredService<IModuleDataService>();
        _actions = sp.GetRequiredService<ISingleItemActionService>();
        RefreshCommand.Execute(null);
    }

    [RelayCommand]
    private void Refresh()
    {
        Rows.Clear();
        foreach (var e in _modules.GetStartupEntries())
            Rows.Add(new StartupEntryRow { Model = e, IsEnabled = e.IsEnabled });
    }

    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (SelectedRow is not { } row || row.Protected || !CanModify) return;

        var confirm = ConfirmDialog.Ask(row.IsEnabled ? "Common.Disable" : "Common.Enable",
            $"{row.Name} — {row.Source}", danger: false);
        if (!confirm) return;

        var result = await _actions.ToggleStartupAsync(row.Model, enable: !row.IsEnabled, CancellationToken.None);
        NotifyResult(result);
        Refresh();
    }

    protected void NotifyResult(Optimization.Pipeline.OptimizationExecutionResult result)
    {
        if (result.FailedCount > 0)
            ConfirmDialog.Ask("Notify.Warning", string.Join("\n", result.Errors.Take(5)),
                confirmText: Translator.Instance["Common.OK"]);
    }
}
