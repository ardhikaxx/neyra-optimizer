using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.ViewModels;
using NeyraOptimizer.Application.Session;

namespace NeyraOptimizer.App.Views.Pages;

public partial class VisualEffectsPage : UserControl
{
    private readonly VisualEffectsViewModel _vm;

    public VisualEffectsPage(IServiceProvider sp)
    {
        InitializeComponent();
        _vm = new VisualEffectsViewModel(sp.GetRequiredService<SessionState>(), sp);
        DataContext = _vm;
    }

    private async void Effect_Changed(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Domain.Models.Power.VisualEffectItem effect
            && effect.ProposedEnabled != effect.CurrentEnabled)
            await _vm.ToggleEffectAsync(effect);
    }
}
