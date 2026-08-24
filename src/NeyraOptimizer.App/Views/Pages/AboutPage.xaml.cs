using System.Windows.Controls;
using NeyraOptimizer.App.ViewModels;

namespace NeyraOptimizer.App.Views.Pages;

public partial class AboutPage : UserControl
{
    public string VersionText =>
        string.Format(NeyraOptimizer.App.Localization.Translator.Instance["About.Version"],
            System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0");

    public AboutPage(IServiceProvider sp)
    {
        InitializeComponent();
        DataContext = this;
    }
}
