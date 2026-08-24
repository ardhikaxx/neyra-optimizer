using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.App.ViewModels;

namespace NeyraOptimizer.App.Views.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage(IServiceProvider sp)
    {
        InitializeComponent();
        DataContext = new SettingsViewModel(sp.GetRequiredService<SessionState>(), sp);
    }
}
