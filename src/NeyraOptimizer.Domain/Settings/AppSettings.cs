using NeyraOptimizer.Domain.Enums;

namespace NeyraOptimizer.Domain.Settings;

public sealed class AppSettings
{
    /// <summary>Bumped whenever persisted settings need migration.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public LanguagePreference Language { get; set; } = LanguagePreference.English;

    public bool OnboardingCompleted { get; set; }
    public bool UserConsentedToChanges { get; set; }
    public bool AdvancedModeEnabled { get; set; }

    public bool ConfirmBeforeApply { get; set; } = true;
    public bool CreateRestorePointBeforeChanges { get; set; } = true;
    public bool AutomaticScanOnStart { get; set; } = true;

    public LogSeverity LoggingLevel { get; set; } = LogSeverity.Info;
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Dashboard refresh interval in seconds. Clamped to >= 2 to avoid aggressive polling.</summary>
    public int DashboardRefreshSeconds { get; set; } = 3;

    public bool DashboardMonitoringEnabled { get; set; } = true;

    /// <summary>
    /// Update checking is disabled by default: version 1.0 has no update server. The setting exists
    /// so the preference can be honored when a signed update channel becomes available.
    /// </summary>
    public bool UpdateCheckEnabled { get; set; }
    public string UpdateServerUrl { get; set; } = string.Empty;

    public UsageProfileKind PreferredUsageProfile { get; set; } = UsageProfileKind.Balanced;
}
