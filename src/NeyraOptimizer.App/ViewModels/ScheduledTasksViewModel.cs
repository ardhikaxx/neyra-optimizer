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

public partial class TaskRow : ObservableObject
{
    public required ScheduledTaskInfo Model { get; init; }
    public string Name => Model.Name;
    public string Path => Model.TaskPath;
    public string LastRunText => Model.LastRunTimeUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? Translator.Instance["Tasks.NeverRun"];
    public string NextRunText => Model.NextRunTimeUtc is null || Model.NextRunTimeUtc.Value == DateTime.MinValue
        ? "—" : Model.NextRunTimeUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public bool Protected => Model.IsProtected;
    public string StatusText => Model.IsProtected
        ? Translator.Instance["Common.Status.Protected"]
        : Model.IsEnabled ? Translator.Instance["Common.Status.Enabled"] : Translator.Instance["Common.Status.Disabled"];
}

public partial class ScheduledTasksViewModel : ViewModelBase
{
    private readonly IModuleDataService _modules;
    private readonly ISingleItemActionService _actions;

    public ObservableCollection<TaskRow> Rows { get; } = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private TaskRow? _selectedRow;

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(VisibleRows));

    public IEnumerable<TaskRow> VisibleRows =>
        string.IsNullOrEmpty(SearchText) ? Rows :
        Rows.Where(r => r.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                        r.Path.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    public ScheduledTasksViewModel(SessionState session, IServiceProvider sp) : base(session)
    {
        _modules = sp.GetRequiredService<IModuleDataService>();
        _actions = sp.GetRequiredService<ISingleItemActionService>();
        RefreshCommand.Execute(null);
    }

    [RelayCommand]
    private void Refresh()
    {
        Rows.Clear();
        foreach (var t in _modules.GetScheduledTasks())
            Rows.Add(new TaskRow { Model = t });
    }

    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (SelectedRow is not { } row || row.Protected || !CanModify) return;
        var confirm = ConfirmDialog.Ask(row.Model.IsEnabled ? "Common.Disable" : "Common.Enable", row.Path);
        if (!confirm) return;

        var result = await _actions.SetTaskEnabledAsync(row.Model, enable: !row.Model.IsEnabled, CancellationToken.None);
        if (result.FailedCount > 0)
            ConfirmDialog.Ask("Notify.Warning", string.Join("\n", result.Errors.Take(5)),
                confirmText: Translator.Instance["Common.OK"]);
        Refresh();
    }
}
