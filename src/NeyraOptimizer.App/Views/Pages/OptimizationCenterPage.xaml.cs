using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.ViewModels;
using NeyraOptimizer.Application.Session;

namespace NeyraOptimizer.App.Views.Pages;

public partial class OptimizationCenterPage : UserControl
{
    public OptimizationCenterPage(IServiceProvider sp)
    {
        InitializeComponent();
        DataContext = new OptimizationCenterViewModel(sp.GetRequiredService<SessionState>(), sp);
    }
}
