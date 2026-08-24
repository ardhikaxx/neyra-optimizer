using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.ViewModels;
using NeyraOptimizer.Application.Session;

namespace NeyraOptimizer.App.Views.Pages;

public partial class PrivacyPage : UserControl
{
    private readonly PrivacyViewModel _vm;

    public PrivacyPage(IServiceProvider sp)
    {
        InitializeComponent();
        _vm = new PrivacyViewModel(sp.GetRequiredService<SessionState>(), sp);
        DataContext = _vm;
    }

    private async void Privacy_Changed(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ViewModels.PrivacyRow row)
            await _vm.ToggleAsync(row);
    }
}
