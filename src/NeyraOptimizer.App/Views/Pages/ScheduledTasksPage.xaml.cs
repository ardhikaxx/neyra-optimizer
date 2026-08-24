using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.App.ViewModels;

namespace NeyraOptimizer.App.Views.Pages;

public partial class ScheduledTasksPage : UserControl
{
    public ScheduledTasksPage(IServiceProvider sp)
    {
        InitializeComponent();
        DataContext = new ScheduledTasksViewModel(sp.GetRequiredService<SessionState>(), sp);
    }
}
