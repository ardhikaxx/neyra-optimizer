using System.Windows;

namespace NeyraOptimizer.App.Views.Dialogs;

/// <summary>Non-cancelable progress dialog bound to IProgress&lt;string&gt; step text.</summary>
public partial class ProgressWindow : Window
{
    private readonly Progress<string> _progress;
    private readonly Progress<double>? _percent;

    public IProgress<string> StepProgress => _progress;

    public ProgressWindow(string title)
    {
        InitializeComponent();
        Title = title;
        Owner = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        _progress = new Progress<string>(s => StepText.Text = s);
        _percent = new Progress<double>(v => Bar.Value = v);
    }

    public void ReportPercent(double value) => ((IProgress<double>)_percent!).Report(value);

    public static ProgressWindow Show(string title) => new(title);
}
