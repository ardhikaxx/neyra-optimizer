using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Snapshots;
using NeyraOptimizer.Infrastructure.Persistence;

namespace NeyraOptimizer.App.ViewModels;

public partial class HistoryRow : ObservableObject
{
    public required HistoryRecord Model { get; init; }
    public string StartedText => Model.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string Profile => Model.ProfileUsed?.ToString() ?? "Custom";
    public string Summary => Model.ResultSummary;
    public string RestartText => Model.RestartRequired ? "⚠" : "";
}

public partial class HistoryViewModel : ViewModelBase
{
    private readonly IHistoryRepository _history;

    public ObservableCollection<HistoryRow> Rows { get; } = new();

    public HistoryViewModel(SessionState session, IServiceProvider sp) : base(session)
    {
        _history = sp.GetRequiredService<IHistoryRepository>();
        RefreshCommand.Execute(null);
    }

    [RelayCommand]
    private void Refresh()
    {
        Rows.Clear();
        foreach (var r in _history.LoadAll())
            Rows.Add(new HistoryRow { Model = r });
    }
}
