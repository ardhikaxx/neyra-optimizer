using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Infrastructure.IO;
using NeyraOptimizer.Infrastructure.Logging;

namespace NeyraOptimizer.App.ViewModels;

public partial class LogRow
{
    public required LogEntry Entry { get; init; }
    public string TimeText => Entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff");
    public string Level => Entry.Severity.ToString();
    public string Message => Entry.Message;
}

public partial class LogsViewModel : ViewModelBase
{
    private readonly INeyraLogger _logger;

    public ObservableCollection<LogRow> Rows { get; } = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private LogSeverity _minSeverity = LogSeverity.Debug;

    public LogsViewModel(SessionState session, IServiceProvider sp) : base(session)
    {
        _logger = sp.GetRequiredService<INeyraLogger>();
        RefreshCommand.Execute(null);
    }

    public IEnumerable<LogSeverity> SeverityOptions =>
        new[] { LogSeverity.Debug, LogSeverity.Info, LogSeverity.Warning, LogSeverity.Error, LogSeverity.Critical };

    public IEnumerable<LogRow> VisibleRows
    {
        get
        {
            IEnumerable<LogRow> q = Rows.Where(r => r.Entry.Severity >= MinSeverity);
            if (!string.IsNullOrEmpty(SearchText))
                q = q.Where(r => r.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                                 r.Entry.Component.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            return q;
        }
    }

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(VisibleRows));
    partial void OnMinSeverityChanged(LogSeverity value) => OnPropertyChanged(nameof(VisibleRows));

    [RelayCommand]
    private void Refresh()
    {
        Rows.Clear();
        foreach (var e in _logger.SnapshotRecent(1000))
            Rows.Add(new LogRow { Entry = e });
        OnPropertyChanged(nameof(VisibleRows));
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try { System.Diagnostics.Process.Start("explorer.exe", AppPaths.LogsDir); }
        catch { /* explorer may fail on odd configs; non-critical */ }
    }
}
