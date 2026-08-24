using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.ViewModels;
using NeyraOptimizer.Application.Session;

namespace NeyraOptimizer.App.Views.Pages;

public partial class AnalyzePage : UserControl
{
    public AnalyzePage(IServiceProvider sp)
    {
        InitializeComponent();
        DataContext = new AnalyzeViewModel(sp.GetRequiredService<SessionState>(), sp);
    }
}
