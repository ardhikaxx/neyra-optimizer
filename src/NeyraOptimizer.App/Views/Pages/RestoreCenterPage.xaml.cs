using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.App.ViewModels;

namespace NeyraOptimizer.App.Views.Pages;

public partial class RestoreCenterPage : UserControl
{
    public RestoreCenterPage(IServiceProvider sp)
    {
        InitializeComponent();
        DataContext = new RestoreCenterViewModel(sp.GetRequiredService<SessionState>(), sp);
    }
}
