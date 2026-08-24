using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Optimization.Modes;

namespace NeyraOptimizer.Application.Modules;

/// <summary>
/// Read-model loaders backing the individual manager pages (Startup, Services, Tasks, Debloat,
/// Background, Visual Effects, Power, Cleanup). All reads are non-privileged where possible.
/// </summary>
public interface IModuleDataService
{
    IReadOnlyList<StartupEntry> GetStartupEntries();
    IReadOnlyList<ServiceInfo> GetServices();
    IReadOnlyList<ScheduledTaskInfo> GetScheduledTasks(CancellationToken ct = default);
    IReadOnlyList<InstalledAppInfo> GetInstalledApps(CancellationToken ct = default);
    IReadOnlyList<BackgroundProcessInfo> GetBackgroundProcesses(CancellationToken ct = default);
    IReadOnlyList<(string PackageFamilyName, string DisplayName, bool Enabled)> GetConfigurableBackgroundApps();
    IReadOnlyDictionary<string, bool> GetVisualEffectStates();
    Domain.Models.Power.PowerPlanInfo? GetActivePowerPlan();
    IReadOnlyList<Domain.Models.Power.PowerPlanInfo> GetPowerPlans();
}

public sealed class ModuleDataService : IModuleDataService
{
    private readonly IStartupManager _startup;
    private readonly IWindowsServiceManager _services;
    private readonly ITaskSchedulerManager _tasks;
    private readonly IAppPackageManager _packages;
    private readonly IProcessAnalyzer _processes;
    private readonly IBackgroundActivityManager _background;
    private readonly IVisualEffectsManager _visuals;
    private readonly IPowerManager _power;

    public ModuleDataService(
        IStartupManager startup,
        IWindowsServiceManager services,
        ITaskSchedulerManager tasks,
        IAppPackageManager packages,
        IProcessAnalyzer processes,
        IBackgroundActivityManager background,
        IVisualEffectsManager visuals,
        IPowerManager power)
    {
        _startup = startup;
        _services = services;
        _tasks = tasks;
        _packages = packages;
        _processes = processes;
        _background = background;
        _visuals = visuals;
        _power = power;
    }

    public IReadOnlyList<StartupEntry> GetStartupEntries() => Safe(() => _startup.GetStartupEntries(), Array.Empty<StartupEntry>());
    public IReadOnlyList<ServiceInfo> GetServices() => Safe(() => _services.GetServices(), Array.Empty<ServiceInfo>());
    public IReadOnlyList<ScheduledTaskInfo> GetScheduledTasks(CancellationToken ct = default) => Safe(() => _tasks.GetTasks(ct), Array.Empty<ScheduledTaskInfo>());
    public IReadOnlyList<InstalledAppInfo> GetInstalledApps(CancellationToken ct = default) => Safe(() => _packages.GetInstalledApps(ct), Array.Empty<InstalledAppInfo>());
    public IReadOnlyList<BackgroundProcessInfo> GetBackgroundProcesses(CancellationToken ct = default) => Safe(() => _processes.GetProcessesWithClassification(ct), Array.Empty<BackgroundProcessInfo>());
    public IReadOnlyList<(string PackageFamilyName, string DisplayName, bool Enabled)> GetConfigurableBackgroundApps() =>
        Safe(() => _background.GetConfigurableApps(), Array.Empty<(string, string, bool)>());
    public IReadOnlyDictionary<string, bool> GetVisualEffectStates() =>
        Safe(() => (IReadOnlyDictionary<string, bool>)new Dictionary<string, bool>(_visuals.GetCurrentEffectStates()), new Dictionary<string, bool>());
    public Domain.Models.Power.PowerPlanInfo? GetActivePowerPlan() => Safe(_power.GetActivePlan, null);
    public IReadOnlyList<Domain.Models.Power.PowerPlanInfo> GetPowerPlans() => Safe(() => _power.GetPowerPlans(), Array.Empty<Domain.Models.Power.PowerPlanInfo>());

    private static T Safe<T>(Func<T> action, T fallback)
    {
        try { return action(); }
        catch { return fallback; }
    }
}
