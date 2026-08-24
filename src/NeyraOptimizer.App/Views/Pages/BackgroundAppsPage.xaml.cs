using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.ViewModels;
using NeyraOptimizer.Application.Session;

namespace NeyraOptimizer.App.Views.Pages;

public partial class BackgroundAppsPage : UserControl
{
    private readonly BackgroundAppsViewModel _vm;

    public BackgroundAppsPage(IServiceProvider sp)
    {
        InitializeComponent();
        _vm = new BackgroundAppsViewModel(sp.GetRequiredService<SessionState>(), sp);
        DataContext = _vm;
    }

    private async void BgToggle_Changed(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ViewModels.BgAppToggleRow row)
            await _vm.ToggleBackgroundAsync(row);
    }
}
