using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.App.ViewModels;

namespace NeyraOptimizer.App.Views.Pages;

public partial class DebloatPage : UserControl
{
    public DebloatPage(IServiceProvider sp)
    {
        InitializeComponent();
        DataContext = new DebloatViewModel(sp.GetRequiredService<SessionState>(), sp);
    }
}
