using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.ViewModels;
using NeyraOptimizer.Application.Session;

namespace NeyraOptimizer.App.Views.Pages;

/// <summary>Hosts one usage mode (Gaming / Office / Battery Saver / Low-End / Safe Windows).</summary>
public partial class ModePage : UserControl
{
    public ModePage(IServiceProvider sp, string modeKey)
    {
        InitializeComponent();
        DataContext = new ModeViewModel(modeKey, sp.GetRequiredService<SessionState>(), sp);
    }
}
