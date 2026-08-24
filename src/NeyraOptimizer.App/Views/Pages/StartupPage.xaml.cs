using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.App.ViewModels;

namespace NeyraOptimizer.App.Views.Pages;

public partial class StartupPage : UserControl
{
    public StartupPage(IServiceProvider sp)
    {
        InitializeComponent();
        DataContext = new StartupViewModel(sp.GetRequiredService<SessionState>(), sp);
    }
}
