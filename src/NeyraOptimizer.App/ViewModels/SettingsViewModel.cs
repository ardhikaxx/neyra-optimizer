using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.App.Views.Dialogs;
using NeyraOptimizer.Application.Restore;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Settings;
using NeyraOptimizer.Infrastructure.IO;
using NeyraOptimizer.Infrastructure.Persistence;

namespace NeyraOptimizer.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IServiceProvider _sp;

    [ObservableProperty] private ThemePreference _theme;
    [ObservableProperty] private LanguagePreference _language;
    [ObservableProperty] private int _refreshSeconds;
    [ObservableProperty] private bool _monitoring;
    [ObservableProperty] private bool _autoScan;
    [ObservableProperty] private bool _confirmBeforeApply;
    [ObservableProperty] private bool _restorePointDefault;
    [ObservableProperty] private LogSeverity _loggingLevel;
    [ObservableProperty] private bool _notifications;
    [ObservableProperty] private bool _advancedMode;
    [ObservableProperty] private string _statusText = string.Empty;

    public IEnumerable<ThemePreference> ThemeOptions => Enum.GetValues<ThemePreference>();
    public IEnumerable<LanguagePreference> LanguageOptions => Enum.GetValues<LanguagePreference>();
    public IEnumerable<LogSeverity> LogLevels =>
        new[] { LogSeverity.Debug, LogSeverity.Info, LogSeverity.Warning, LogSeverity.Error, LogSeverity.Critical };

    public string DataFolder => AppPaths.ProgramDataRoot;
    public string UpdateNote => Translator.Instance["Settings.UpdateNone"];
    public string UninstallKeepNote => Translator.Instance["Settings.UninstallKeepOption"];

    public SettingsViewModel(SessionState session, IServiceProvider sp) : base(session)
    {
        _sp = sp;
        var s = session.Settings;
        _theme = s.Theme; _language = s.Language; _refreshSeconds = s.DashboardRefreshSeconds;
        _monitoring = s.DashboardMonitoringEnabled; _autoScan = s.AutomaticScanOnStart;
        _confirmBeforeApply = s.ConfirmBeforeApply; _restorePointDefault = s.CreateRestorePointBeforeChanges;
        _loggingLevel = s.LoggingLevel; _notifications = s.NotificationsEnabled; _advancedMode = s.AdvancedModeEnabled;
    }

    [RelayCommand]
    private void Save()
    {
        var s = Session.Settings;
        bool languageChanged = s.Language != Language;
        bool themeChanged = s.Theme != Theme;

        s.Theme = Theme;
        s.Language = Language;
        s.DashboardRefreshSeconds = Math.Clamp(RefreshSeconds, 2, 60);
        s.DashboardMonitoringEnabled = Monitoring;
        s.AutomaticScanOnStart = AutoScan;
        s.ConfirmBeforeApply = ConfirmBeforeApply;
        s.CreateRestorePointBeforeChanges = RestorePointDefault;
        s.LoggingLevel = LoggingLevel;
        s.NotificationsEnabled = Notifications;
        s.AdvancedModeEnabled = AdvancedMode;
        new SettingsRepository().Save(s);

        if (themeChanged)
            NeyraApplication.ApplyTheme(s.Theme);

        StatusText = Translator.Instance["Settings.Saved"] +
                     (languageChanged ? " (" + Translator.Instance["App.Title"] + " restart applies all labels)" : string.Empty);
    }

    /// <summary>Uninstall assistant: restore every recorded change BEFORE the user uninstalls.</summary>
    [RelayCommand]
    private async Task UninstallRestoreAllAsync()
    {
        if (!CanModify) return;
        if (!ConfirmDialog.Ask("Settings.UninstallSection",
                Translator.Instance["Settings.UninstallRestoreAll"], danger: true,
                confirmLocKey: "Common.Restore")) return;

        var restore = _sp.GetRequiredService<IRestoreCenterService>();
        try
        {
            var result = await restore.RestoreEverythingAsync(null, CancellationToken.None);
            ConfirmDialog.Ask("Notify.Info",
                string.Format(Translator.Instance["Restore.RestoredOk"], result.RestoredCount, result.FailedCount),
                confirmText: Translator.Instance["Common.OK"]);
        }
        catch (Exception ex)
        {
            ConfirmDialog.Ask("Notify.Error", ex.Message, confirmText: Translator.Instance["Common.OK"]);
        }
    }
}
