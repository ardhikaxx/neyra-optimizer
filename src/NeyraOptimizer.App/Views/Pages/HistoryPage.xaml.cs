using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.App.ViewModels;

namespace NeyraOptimizer.App.Views.Pages;

public partial class HistoryPage : UserControl
{
    public HistoryPage(IServiceProvider sp)
    {
        InitializeComponent();
        DataContext = new HistoryViewModel(sp.GetRequiredService<SessionState>(), sp);
    }
}
