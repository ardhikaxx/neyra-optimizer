using System.Windows;

namespace NeyraOptimizer.App.Views.Dialogs;

/// <summary>Asks the user how to resolve an interrupted optimization batch (rollback vs dismiss).</summary>
public partial class RecoveryDialog : Window
{
    public RecoveryDialog(string body)
    {
        InitializeComponent();
        Owner = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        BodyText.Text = body;
        RollbackBtn.Click += (_, _) => { DialogResult = true; Close(); };
        DismissBtn.Click += (_, _) => { DialogResult = false; Close(); };
    }
}
