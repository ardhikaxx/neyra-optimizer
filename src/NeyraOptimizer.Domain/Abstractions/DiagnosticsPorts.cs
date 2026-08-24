using Microsoft.Win32;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.Power;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.Domain.Abstractions;

public sealed class SystemInformationException : Exception
{
    public SystemInformationException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Read-only system information collection. Never mutates the machine.</summary>
public interface ISystemInformationProvider
{
    WindowsIdentityInfo GetWindowsIdentity();
    CpuInfo GetCpu();
    MemoryInfo GetMemory();
    IReadOnlyList<GpuInfo> GetGpus();
    IReadOnlyList<StorageVolumeInfo> GetStorageVolumes();
    BatteryInfo GetBattery();
    SecurityStatusInfo GetSecurityStatus();
    ChassisKind GetChassisKind();
    DateTime GetBootTimeUtc();
    bool IsCurrentProcessElevated();
    ProcessSnapshotSummary GetProcessSummary();
}

/// <summary>Near-real-time performance sampling. Implementations must be cheap and disposable.</summary>
public interface IPerformanceMonitor : IDisposable
{
    /// <summary>CPU load percent averaged over sampleSeconds. Null when unavailable.</summary>
    Task<double?> SampleCpuLoadAsync(int sampleSeconds, CancellationToken ct);
    /// <summary>Instant memory metrics from GlobalMemoryStatusEx.</summary>
    (long TotalMb, long AvailableMb, long CommitLimitMb, long CommitUsedMb) SampleMemory();
    /// <summary>System disk active time percent. Null when counters are unavailable.</summary>
    double? SampleDiskActivePercent();
    /// <summary>GPU engine utilization percent. Null when no GPU counters exist.</summary>
    double? SampleGpuUsagePercent();
}

/// <summary>Scans safe cleanup locations and computes sizes (dry-run). Deletion happens only on explicit apply.</summary>
public interface ICleanupScanner
{
    IReadOnlyList<CleanupCandidate> Scan(CancellationToken ct);
    Task<long> DeleteCandidateAsync(CleanupCandidate candidate, IProgress<(long freedBytes, string currentPath)>? progress, CancellationToken ct);
}
