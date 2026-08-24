using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.App.ViewModels;

namespace NeyraOptimizer.App.Views.Pages;

public partial class PowerPage : UserControl
{
    public PowerPage(IServiceProvider sp)
    {
        InitializeComponent();
        DataContext = new PowerViewModel(sp.GetRequiredService<SessionState>(), sp);
    }
}
