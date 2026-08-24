using System.Windows;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Diagnostics.Reporting;
using NeyraOptimizer.Infrastructure.IO;

namespace NeyraOptimizer.App.Views.Pages;

public partial class HelpSafetyPage : UserControl
{
    public HelpSafetyPage(IServiceProvider sp)
    {
        InitializeComponent();
        var bundleSvc = sp.GetRequiredService<ISupportBundleService>();
        var session = sp.GetRequiredService<SessionState>();
        DataContext = this;
        CreateBundleCmd = new RelayCommand(() => _ = CreateBundleAsync(bundleSvc, session));
    }

    public RelayCommand CreateBundleCmd { get; }

    private static async Task CreateBundleAsync(ISupportBundleService svc, SessionState session)
    {
        // Show the user what will be included BEFORE creating anything.
        var items = string.Join("\n", svc.GetIncludedItems().Select(i => "• " + i));
        if (!Views.Dialogs.ConfirmDialog.Ask("Help.SupportBundle", items,
                confirmLocKey: "Common.Export")) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Translator.Instance["Help.SupportBundle"],
            FileName = $"NeyraSupport-{DateTime.UtcNow:yyyyMMdd-HHmm}.zip",
            Filter = "ZIP (*.zip)|*.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            AppPaths.EnsureDirectories();
            var profile = session.LastAnalysis?.Profile
                ?? throw new InvalidOperationException("Jalankan analisis terlebih dahulu.");
            await Task.Run(() => svc.CreateSupportBundleAsync(profile, dialog.FileName).GetAwaiter().GetResult());
            Views.Dialogs.ConfirmDialog.Ask("Notify.Success", dialog.FileName,
                confirmText: NeyraOptimizer.App.Localization.Translator.Instance["Common.OK"]);
        }
        catch (Exception ex)
        {
            Views.Dialogs.ConfirmDialog.Ask("Notify.Error", ex.Message,
                confirmText: NeyraOptimizer.App.Localization.Translator.Instance["Common.OK"]);
        }
    }
}
