using NeyraOptimizer.Diagnostics.Measurement;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Engines;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Domain.Rules;

namespace NeyraOptimizer.Diagnostics.Analyzer;

public interface ISystemAnalyzer
{
    Task<AnalysisBundle> AnalyzeFullSystemAsync(int baselineSampleSeconds = 2, CancellationToken ct = default);
}

public sealed class SystemAnalyzer : ISystemAnalyzer
{
    private readonly ISystemInformationProvider _systemInfo;
    private readonly IStartupManager _startupManager;
    private readonly IWindowsServiceManager _serviceManager;
    private readonly ITaskSchedulerManager _taskManager;
    private readonly IAppPackageManager _packageManager;
    private readonly IProcessAnalyzer _processAnalyzer;
    private readonly IBaselineMeasurementService _measurement;
    private readonly IPowerManager _powerManager;

    public SystemAnalyzer(
        ISystemInformationProvider systemInfo,
        IStartupManager startupManager,
        IWindowsServiceManager serviceManager,
        ITaskSchedulerManager taskManager,
        IAppPackageManager packageManager,
        IProcessAnalyzer processAnalyzer,
        IBaselineMeasurementService measurement,
        IPowerManager powerManager)
    {
        _systemInfo = systemInfo;
        _startupManager = startupManager;
        _serviceManager = serviceManager;
        _taskManager = taskManager;
        _packageManager = packageManager;
        _processAnalyzer = processAnalyzer;
        _measurement = measurement;
        _powerManager = powerManager;
    }

    public async Task<AnalysisBundle> AnalyzeFullSystemAsync(int baselineSampleSeconds = 2, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // 1. Gather hardware and OS info
        var windows = _systemInfo.GetWindowsIdentity();
        var cpu = _systemInfo.GetCpu();
        var memory = _systemInfo.GetMemory();
        var gpus = _systemInfo.GetGpus();
        var volumes = _systemInfo.GetStorageVolumes();
        var battery = _systemInfo.GetBattery();
        var security = _systemInfo.GetSecurityStatus();
        var chassis = _systemInfo.GetChassisKind();
        var bootTime = _systemInfo.GetBootTimeUtc();
        var isElevated = _systemInfo.IsCurrentProcessElevated();

        var activePlan = _powerManager.GetActivePlan();

        var profile = new SystemProfile
        {
            Windows = windows,
            Cpu = cpu,
            Memory = memory,
            Gpus = gpus,
            Volumes = volumes,
            Battery = battery,
            Security = security,
            Chassis = chassis,
            BootTimeUtc = bootTime,
            ActivePowerPlanName = activePlan?.Name ?? "Balanced",
            ActivePowerPlanGuid = activePlan?.PlanGuid ?? string.Empty,
            IsRunningAsAdministrator = isElevated
        };

        // Classify device (result is stored back onto the profile)
        var (deviceClass, hardwareScore, reasons) = DeviceClassifier.Classify(profile);
        profile.DeviceClass = deviceClass;
        profile.HardwareScore = hardwareScore;
        profile.ClassificationReasons = reasons;

        ct.ThrowIfCancellationRequested();

        // 2. Gather services, startup, tasks, apps, and processes
        var startupEntries = _startupManager.GetStartupEntries();
        var services = _serviceManager.GetServices();
        var tasks = _taskManager.GetTasks(ct);
        var apps = _packageManager.GetInstalledApps(ct);
        var processes = _processAnalyzer.GetProcessesWithClassification(ct);

        // 3. Capture baseline measurement
        PerformanceSnapshot? baseline = null;
        try
        {
            baseline = await _measurement.CaptureSnapshotAsync(baselineSampleSeconds, ct).ConfigureAwait(false);
        }
        catch
        {
            // Non-critical; baseline fallback
        }

        return new AnalysisBundle
        {
            Profile = profile,
            StartupEntries = startupEntries,
            Services = services,
            Tasks = tasks,
            InstalledApps = apps,
            BackgroundProcesses = processes,
            Baseline = baseline
        };
    }
}