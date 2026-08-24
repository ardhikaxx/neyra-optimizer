using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.App.ViewModels;

namespace NeyraOptimizer.App.Views.Pages;

public partial class LogsPage : UserControl
{
    public LogsPage(IServiceProvider sp)
    {
        InitializeComponent();
        DataContext = new LogsViewModel(sp.GetRequiredService<SessionState>(), sp);
    }
}
