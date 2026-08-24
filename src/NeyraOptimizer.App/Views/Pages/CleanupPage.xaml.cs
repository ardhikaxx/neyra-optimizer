using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.App.ViewModels;

namespace NeyraOptimizer.App.Views.Pages;

public partial class CleanupPage : UserControl
{
    public CleanupPage(IServiceProvider sp)
    {
        InitializeComponent();
        DataContext = new CleanupViewModel(sp.GetRequiredService<SessionState>(), sp);
    }
}
