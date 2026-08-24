using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.Diagnostics.Measurement;

public interface IBaselineMeasurementService
{
    Task<PerformanceSnapshot> CaptureSnapshotAsync(int sampleSeconds, CancellationToken ct = default);
    IReadOnlyList<MetricComparison> Compare(PerformanceSnapshot before, PerformanceSnapshot after);
}

public sealed class BaselineMeasurementService : IBaselineMeasurementService
{
    private readonly ISystemInformationProvider _systemInfo;
    private readonly IPerformanceMonitor _perfMonitor;
    private readonly IStartupManager _startupManager;
    private readonly IWindowsServiceManager _serviceManager;
    private readonly IPowerManager _powerManager;

    public BaselineMeasurementService(
        ISystemInformationProvider systemInfo,
        IPerformanceMonitor perfMonitor,
        IStartupManager startupManager,
        IWindowsServiceManager serviceManager,
        IPowerManager powerManager)
    {
        _systemInfo = systemInfo;
        _perfMonitor = perfMonitor;
        _startupManager = startupManager;
        _serviceManager = serviceManager;
        _powerManager = powerManager;
    }

    public async Task<PerformanceSnapshot> CaptureSnapshotAsync(int sampleSeconds, CancellationToken ct = default)
    {
        var mem = _perfMonitor.SampleMemory();
        var cpu = await _perfMonitor.SampleCpuLoadAsync(sampleSeconds, ct).ConfigureAwait(false);
        var disk = _perfMonitor.SampleDiskActivePercent();
        var gpu = _perfMonitor.SampleGpuUsagePercent();

        var procSummary = _systemInfo.GetProcessSummary();
        var startupEntries = _startupManager.GetStartupEntries();
        var services = _serviceManager.GetServices();

        var systemVol = _systemInfo.GetStorageVolumes().FirstOrDefault(v => v.IsSystemVolume);
        var freeGb = systemVol?.FreeGb ?? 0;

        var activePlanName = _powerManager.GetActivePlan()?.Name ?? string.Empty;

        return new PerformanceSnapshot
        {
            SampleSeconds = sampleSeconds,
            TotalRamMb = mem.TotalMb,
            AvailableRamMb = mem.AvailableMb,
            CommitLimitMb = mem.CommitLimitMb,
            CommitUsedMb = mem.CommitUsedMb,
            CpuLoadPercent = cpu,
            DiskActivePercent = disk,
            GpuUsagePercent = gpu,
            ProcessCount = procSummary.ProcessCount,
            StartupEntriesEnabled = startupEntries.Count(e => e.IsEnabled),
            AutoStartServicesRunning = services.Count(s => s.Status == ServiceStatus.Running && (s.StartMode == ServiceStartMode.Automatic || s.StartMode == ServiceStartMode.AutomaticDelayed)),
            SystemDriveFreeGb = freeGb,
            PowerPlanName = activePlanName
        };
    }

    public IReadOnlyList<MetricComparison> Compare(PerformanceSnapshot before, PerformanceSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var list = new List<MetricComparison>();

        // RAM Used
        list.Add(new MetricComparison
        {
            MetricName = "Penggunaan RAM",
            Unit = "MB",
            Before = before.UsedRamMb,
            After = after.UsedRamMb,
            LowerIsBetter = true
        });

        // RAM Available
        list.Add(new MetricComparison
        {
            MetricName = "RAM Tersedia",
            Unit = "MB",
            Before = before.AvailableRamMb,
            After = after.AvailableRamMb,
            LowerIsBetter = false
        });

        // CPU Load (if measured)
        if (before.CpuLoadPercent.HasValue && after.CpuLoadPercent.HasValue)
        {
            list.Add(new MetricComparison
            {
                MetricName = "Beban CPU Rata-rata",
                Unit = "%",
                Before = Math.Round(before.CpuLoadPercent.Value, 1),
                After = Math.Round(after.CpuLoadPercent.Value, 1),
                LowerIsBetter = true
            });
        }

        // Active Processes
        list.Add(new MetricComparison
        {
            MetricName = "Jumlah Proses Aktif",
            Unit = "proses",
            Before = before.ProcessCount,
            After = after.ProcessCount,
            LowerIsBetter = true
        });

        // Startup Entries Enabled
        list.Add(new MetricComparison
        {
            MetricName = "Aplikasi Startup Aktif",
            Unit = "item",
            Before = before.StartupEntriesEnabled,
            After = after.StartupEntriesEnabled,
            LowerIsBetter = true
        });

        // Auto Services Running
        list.Add(new MetricComparison
        {
            MetricName = "Service Otomatis Berjalan",
            Unit = "service",
            Before = before.AutoStartServicesRunning,
            After = after.AutoStartServicesRunning,
            LowerIsBetter = true
        });

        // Free Storage
        if (before.SystemDriveFreeGb > 0 && after.SystemDriveFreeGb > 0)
        {
            list.Add(new MetricComparison
            {
                MetricName = "Penyimpanan Sistem Bebas",
                Unit = "GB",
                Before = Math.Round(before.SystemDriveFreeGb, 1),
                After = Math.Round(after.SystemDriveFreeGb, 1),
                LowerIsBetter = false
            });
        }

        return list;
    }
}