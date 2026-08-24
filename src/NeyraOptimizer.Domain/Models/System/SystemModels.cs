using NeyraOptimizer.Domain.Enums;

namespace NeyraOptimizer.Domain.Models.System;

public sealed class CpuInfo
{
    public string Name { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public int PhysicalCores { get; init; }
    public int LogicalProcessors { get; init; }
    public double BaseClockGhz { get; init; }
    public double MaxClockGhz { get; init; }
    /// <summary>Relative compute score (0..100) used only for internal classification heuristics.</summary>
    public int HeuristicScore { get; set; }
}

public sealed class GpuInfo
{
    public string Name { get; init; } = string.Empty;
    public string Vendor { get; init; } = string.Empty;
    public long VramMb { get; init; }
    public bool IsDedicated { get; init; }
    public string DriverVersion { get; init; } = string.Empty;
}

public sealed class MemoryInfo
{
    public long TotalPhysicalMb { get; init; }
    public int? SpeedMHz { get; init; }
    public int SlotsUsed { get; init; }
    public int SlotsTotal { get; init; }
}

public sealed class StorageVolumeInfo
{
    public char DriveLetter { get; init; }
    public string Label { get; init; } = string.Empty;
    public long TotalGb { get; init; }
    public long FreeGb { get; init; }
    public StorageMediaType MediaType { get; init; }
    public bool IsSystemVolume { get; init; }
}

public sealed class BatteryInfo
{
    public bool IsPresent { get; init; }
    public int ChargePercent { get; init; }
    public bool IsCharging { get; init; }
    public PowerSource PowerSource { get; init; }
    public int? EstimatedRuntimeMinutes { get; init; }
    public uint? DesignCapacityMilliWattHours { get; init; }
    public uint? FullChargeCapacityMilliWattHours { get; init; }
    public int? BatteryHealthPercent => DesignCapacityMilliWattHours is > 0 && FullChargeCapacityMilliWattHours is > 0
        ? (int)Math.Round(FullChargeCapacityMilliWattHours.Value * 100.0 / DesignCapacityMilliWattHours.Value)
        : null;
}

public sealed class WindowsIdentityInfo
{
    public string MachineName { get; init; } = Environment.MachineName;
    public string Edition { get; init; } = string.Empty;
    /// <summary>Marketing display version such as 22H2 / 23H2 / 24H2 when available.</summary>
    public string DisplayVersion { get; init; } = string.Empty;
    public string VersionString { get; init; } = string.Empty;
    public int BuildNumber { get; init; }
    public int UpdateBuildRevision { get; init; }
    public string BuildLabel => $"build {BuildNumber}.{UpdateBuildRevision}";
    public string Architecture { get; init; } = string.Empty;
    public bool Is64BitOperatingSystem { get; init; }
    public string LocaleName { get; init; } = string.Empty;
    public bool IsVirtualMachine { get; init; }
    public bool IsWindows10 => BuildNumber >= 10240 && BuildNumber < 22000;
    public bool IsWindows11 => BuildNumber >= 22000;

    public static readonly int MinimumSupportedBuild = 17763; // Windows 10 1809 and later are evaluated; see CompatibilityChecker.
    public static readonly int MaximumTestedBuildKnownAtRelease = 26100;
}

public sealed class SecurityStatusInfo
{
    public bool DefenderEnabled { get; init; }
    public bool RealTimeProtectionEnabled { get; init; }
    public bool FirewallEnabled { get; init; }
    public bool UacEnabled { get; init; }
    public bool TamperProtectionEnabled { get; init; }
    public bool AntivirusRegistered { get; init; }
    public string AntivirusProductName { get; init; } = string.Empty;
}

public sealed class ProcessSnapshotSummary
{
    public int ProcessCount { get; init; }
    public int ThreadCount { get; init; }
    public int HandleCount { get; init; }
}

public sealed class PerformanceSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime MeasuredAtUtc { get; init; } = DateTime.UtcNow;
    public int SampleSeconds { get; init; }

    public long TotalRamMb { get; init; }
    public long AvailableRamMb { get; init; }
    public long UsedRamMb => Math.Max(0, TotalRamMb - AvailableRamMb);
    public double RamUsagePercent => TotalRamMb <= 0 ? 0 : Math.Round(UsedRamMb * 100.0 / TotalRamMb, 1);

    public long CommitLimitMb { get; init; }
    public long CommitUsedMb { get; init; }
    public double CommitUsagePercent => CommitLimitMb <= 0 ? 0 : Math.Round(CommitUsedMb * 100.0 / CommitLimitMb, 1);

    /// <summary>CPU load averaged over the sample window. Null when not measurable.</summary>
    public double? CpuLoadPercent { get; init; }

    /// <summary>Disk active time percent of the system volume, null when the counter is unavailable.</summary>
    public double? DiskActivePercent { get; init; }

    /// <summary>GPU usage percent, null when no GPU performance counters exist on this machine.</summary>
    public double? GpuUsagePercent { get; init; }

    public int ProcessCount { get; init; }
    public int StartupEntriesEnabled { get; init; }
    public int AutoStartServicesRunning { get; init; }

    /// <summary>Free space in GB on the system drive.</summary>
    public double SystemDriveFreeGb { get; init; }

    public string PowerPlanName { get; init; } = string.Empty;
}

public sealed class MetricComparison
{
    public required string MetricName { get; init; }
    public required string Unit { get; init; }
    public required double Before { get; init; }
    public required double After { get; init; }
    /// <summary>True when lower values are better (RAM used, CPU idle load...). False when higher is better.</summary>
    public bool LowerIsBetter { get; init; } = true;
    public double Delta => After - Before;
    public bool Improved => LowerIsBetter ? After < Before - Epsilon : After > Before + Epsilon;
    public bool Degraded => LowerIsBetter ? After > Before + Epsilon : After < Before - Epsilon;
    private const double Epsilon = 1e-9;

    public string DeltaText
    {
        get
        {
            var d = Delta;
            var sign = d > 0 ? "+" : d < 0 ? "-" : string.Empty;
            return $"{sign}{Math.Abs(d):0.##} {Unit}".Trim();
        }
    }
}
