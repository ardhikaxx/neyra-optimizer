using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.ViewModels;
using NeyraOptimizer.Application.Session;

namespace NeyraOptimizer.App.Views.Pages;

public partial class DashboardPage : UserControl
{
    private readonly DashboardViewModel _vm;
    public DashboardPage(IServiceProvider sp)
    {
        InitializeComponent();
        _vm = new DashboardViewModel(sp.GetRequiredService<SessionState>(), sp);
        DataContext = _vm;
        Unloaded += (_, _) => _vm.StopMonitoring();
    }

    public Task TriggerStartupScanAsync() => _vm.AnalyzeCommand.ExecuteAsync(null);
}
