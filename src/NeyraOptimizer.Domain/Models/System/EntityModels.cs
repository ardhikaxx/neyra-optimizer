using NeyraOptimizer.Domain.Enums;

namespace NeyraOptimizer.Domain.Models.System;

/// <summary>Complete device profile produced by the System Analyzer.</summary>
public sealed class SystemProfile
{
    public Guid AnalysisId { get; init; } = Guid.NewGuid();
    public DateTime AnalyzedAtUtc { get; init; } = DateTime.UtcNow;

    public required WindowsIdentityInfo Windows { get; init; }
    public required CpuInfo Cpu { get; init; }
    public required MemoryInfo Memory { get; init; }
    public IReadOnlyList<GpuInfo> Gpus { get; init; } = Array.Empty<GpuInfo>();
    public IReadOnlyList<StorageVolumeInfo> Volumes { get; init; } = Array.Empty<StorageVolumeInfo>();
    public BatteryInfo? Battery { get; init; }
    public SecurityStatusInfo Security { get; init; } = new();
    public ChassisKind Chassis { get; init; }

    public TimeSpan Uptime => DateTime.UtcNow - BootTimeUtc;
    public DateTime BootTimeUtc { get; init; }

    public string ActivePowerPlanName { get; init; } = string.Empty;
    public string ActivePowerPlanGuid { get; init; } = string.Empty;
    public bool IsRunningAsAdministrator { get; init; }

    /// <summary>True when the machine has at least one SSD on the system volume.</summary>
    public bool HasSystemSsd => Volumes.Any(v => v.IsSystemVolume && v.MediaType == StorageMediaType.Ssd);

    public bool HasDedicatedGpu => Gpus.Any(g => g.IsDedicated);

    public DeviceClass DeviceClass { get; set; } = DeviceClass.Unknown;
    public int HardwareScore { get; set; }
    public IReadOnlyList<string> ClassificationReasons { get; set; } = Array.Empty<string>();
}

public static class DeviceClassExtensions
{
    public static string ToKey(this DeviceClass c) => c switch
    {
        DeviceClass.LowEnd => "lowend",
        DeviceClass.EntryLevel => "entry",
        DeviceClass.Balanced => "balanced",
        DeviceClass.MidRange => "midrange",
        DeviceClass.HighPerformance => "highperf",
        DeviceClass.Gaming => "gaming",
        DeviceClass.Custom => "custom",
        _ => "unknown",
    };
}

public enum StartupSource
{
    RunKeyCurrentUser = 0,
    RunKeyLocalMachine = 1,
    RunKeyCurrentUserWow64 = 2,
    RunKeyLocalMachineWow64 = 3,
    StartupFolderUser = 4,
    StartupFolderCommon = 5,
}

public sealed class StartupEntry
{
    /// <summary>Stable identity: source + registry path/value name or folder file path.</summary>
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Publisher { get; init; }
    public required string Command { get; init; }
    public string? Location { get; init; }
    public StartupSource Source { get; init; }
    public bool IsEnabled { get; init; }
    public StartupImpact Impact { get; init; }
    public RiskLevel RiskLevel { get; init; }
    public bool IsProtected { get; init; }
    public string ProtectionReason { get; init; } = string.Empty;

    public string SourceDisplay => Source switch
    {
        StartupSource.RunKeyCurrentUser => "HKCU\\...\\Run",
        StartupSource.RunKeyLocalMachine => "HKLM\\...\\Run",
        StartupSource.RunKeyCurrentUserWow64 => "HKCU\\...\\Run (WOW64)",
        StartupSource.RunKeyLocalMachineWow64 => "HKLM\\...\\Run (WOW64)",
        StartupSource.StartupFolderUser => "Startup Folder (User)",
        StartupSource.StartupFolderCommon => "Startup Folder (All Users)",
        _ => "Unknown",
    };
}

public enum ServiceStartMode
{
    Unknown = 0,
    Boot = 1,
    System = 2,
    Automatic = 3,
    AutomaticDelayed = 4,
    Manual = 5,
    Disabled = 6,
}

public enum ServiceStatus
{
    Unknown = 0,
    Stopped = 1,
    StartPending = 2,
    StopPending = 3,
    Running = 4,
    PausePending = 5,
    Paused = 6,
}

public sealed class ServiceInfo
{
    /// <summary>Service key name (not the localized display name). Used for all operations.</summary>
    public required string ServiceName { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; init; } = string.Empty;
    public ServiceStartMode StartMode { get; init; }
    public ServiceStatus Status { get; init; }
    public string AccountName { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public IReadOnlyList<string> ServicesDependedOn { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DependentServices { get; init; } = Array.Empty<string>();
    public bool CanPauseAndContinue { get; init; }
    public ServiceClassification Classification { get; init; }
    public RiskLevel RiskLevel { get; init; }
    public bool IsProtected { get; init; }
    public string ProtectionReason { get; init; } = string.Empty;
}

public sealed record ScheduledTaskInfo
{
    /// <summary>Full task path including folders, e.g. \Microsoft\Windows\...\TaskName. Unique identifier.</summary>
    public required string TaskPath { get; init; }
    public required string Name { get; init; }
    public string Author { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public bool IsRunning { get; init; }
    public DateTime? LastRunTimeUtc { get; init; }
    public DateTime? NextRunTimeUtc { get; init; }
    public string TriggersSummary { get; init; } = string.Empty;
    public string ActionsSummary { get; init; } = string.Empty;
    public RiskLevel RiskLevel { get; init; }
    public bool IsProtected { get; init; }
    public string ProtectionReason { get; init; } = string.Empty;
    public RecommendationCategory Category { get; init; } = RecommendationCategory.Optional;
}

public enum InstalledAppKind
{
    Win32 = 0,
    Appx = 1,
}

public sealed class InstalledAppInfo
{
    /// <summary>For AppX: PackageFullName. For Win32: uninstall registry key name.</summary>
    public required string Id { get; init; }
    public InstalledAppKind Kind { get; init; }
    public required string DisplayName { get; init; }
    public string Publisher { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string? InstallLocation { get; init; }
    public long? SizeBytes { get; init; }
    /// <summary>PackageFamilyName for AppX packages (useful for reinstall links).</summary>
    public string? PackageFamilyName { get; init; }
    public bool IsProtected { get; init; }
    public string ProtectionReason { get; init; } = string.Empty;
    public RiskLevel RiskLevel { get; init; }
    public RecommendationCategory Category { get; init; } = RecommendationCategory.Optional;
    /// <summary>Honest reinstall note. Empty when reinstall cannot be verified.</summary>
    public string ReinstallNote { get; init; } = string.Empty;
    public bool IsProvisioned { get; init; }
}

public enum BackgroundProcessKind
{
    UserApplication = 0,
    UserBackgroundApp = 1,
    SystemProcess = 2,
    ServiceHost = 3,
    SecurityProcess = 4,
    DriverHost = 5,
    ProtectedSystem = 6,
}

public sealed class BackgroundProcessInfo
{
    public int ProcessId { get; init; }
    public required string Name { get; init; }
    public string? WindowTitle { get; init; }
    public BackgroundProcessKind Kind { get; init; }
    public double MemoryMb { get; init; }
    public double CpuTimeSeconds { get; init; }
    public DateTime? StartTimeUtc { get; init; }
    public bool CanTerminate { get; init; }
    public string TerminationNote { get; init; } = string.Empty;
}

