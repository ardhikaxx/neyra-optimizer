using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.App.ViewModels;

namespace NeyraOptimizer.App.Views.Pages;

public partial class ServicesPage : UserControl
{
    public ServicesPage(IServiceProvider sp)
    {
        InitializeComponent();
        DataContext = new ServicesViewModel(sp.GetRequiredService<SessionState>(), sp);
    }
}
