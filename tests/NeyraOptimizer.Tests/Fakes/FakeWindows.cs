using Microsoft.Win32;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.Power;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Security.Elevation;

namespace NeyraOptimizer.Tests.Fakes;

/// <summary>In-memory registry implementing the typed port. Values record previous states.</summary>
public sealed class FakeRegistry : IRegistryManager
{
    public Dictionary<string, RegistryValueDto> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    private static string Key(RegRoot root, string subKey, string name) =>
        $"{root}:{subKey}\\{name}".ToLowerInvariant();

    public bool KeyExists(RegRoot root, string subKey) =>
        Values.Keys.Any(k => k.StartsWith($"{root}:{subKey}\\".ToLowerInvariant(), StringComparison.Ordinal));

    public RegistryValueDto? GetValue(RegRoot root, string subKey, string valueName) =>
        Values.TryGetValue(Key(root, subKey, valueName), out var v) ? v : null;

    public IReadOnlyList<RegistryValueDto> GetValues(RegRoot root, string subKey)
    {
        var prefix = $"{root}:{subKey}\\".ToLowerInvariant();
        return Values.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(kv => kv.Value).ToList();
    }

    public IReadOnlyList<string> GetSubKeyNames(RegRoot root, string subKey) => Array.Empty<string>();

    public void SetValue(RegRoot root, string subKey, string valueName, object data, RegistryValueKind kind) =>
        Values[Key(root, subKey, valueName)] = new RegistryValueDto(valueName, data, kind);

    public bool DeleteValue(RegRoot root, string subKey, string valueName) =>
        Values.Remove(Key(root, subKey, valueName));
}

public sealed class FakeServiceManager : IWindowsServiceManager
{
    public Dictionary<string, ServiceInfo> Services { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int SetStartModeCalls;
    public Exception? ThrowOnSet;

    public void Add(string name, ServiceStartMode mode = ServiceStartMode.Automatic, ServiceStatus status = ServiceStatus.Running) =>
        Services[name] = new ServiceInfo
        {
            ServiceName = name,
            DisplayName = name,
            StartMode = mode,
            Status = status,
        };

    public IReadOnlyList<ServiceInfo> GetServices() => Services.Values.ToList();

    public ServiceInfo? GetService(string serviceName) =>
        Services.TryGetValue(serviceName, out var s) ? s : null;

    public void SetStartMode(string serviceName, ServiceStartMode mode)
    {
        Interlocked.Increment(ref SetStartModeCalls);
        if (ThrowOnSet is not null) throw ThrowOnSet;
        if (!Services.TryGetValue(serviceName, out var svc))
            throw new InvalidOperationException($"Service '{serviceName}' does not exist.");
        Services[serviceName] = new ServiceInfo
        {
            ServiceName = svc.ServiceName,
            DisplayName = svc.DisplayName,
            Description = svc.Description,
            StartMode = mode,
            Status = svc.Status,
            AccountName = svc.AccountName,
            ExecutablePath = svc.ExecutablePath,
            ServicesDependedOn = svc.ServicesDependedOn,
            DependentServices = svc.DependentServices,
            CanPauseAndContinue = svc.CanPauseAndContinue,
            Classification = svc.Classification,
            RiskLevel = svc.RiskLevel,
            IsProtected = svc.IsProtected,
            ProtectionReason = svc.ProtectionReason,
        };
    }

    public Task StopAsync(string serviceName, int timeoutSeconds, CancellationToken ct) =>
        Task.CompletedTask;
}

public sealed class FakeStartupManager : IStartupManager
{
    public Dictionary<string, StartupEntry> Entries { get; } = new();

    public void Add(string id, bool enabled = true, StartupSource source = StartupSource.RunKeyCurrentUser) =>
        Entries[id] = new StartupEntry
        {
            Id = id,
            Name = id,
            Command = $"cmd /c {id}",
            Source = source,
            IsEnabled = enabled,
        };

    public IReadOnlyList<StartupEntry> GetStartupEntries() => Entries.Values.ToList();

    public StartupToggleResult Disable(string entryId)
    {
        if (!Entries.TryGetValue(entryId, out var e)) return new StartupToggleResult(false, "missing");
        Entries[entryId] = new StartupEntry
        {
            Id = e.Id, Name = e.Name, Publisher = e.Publisher, Command = e.Command,
            Location = e.Location, Source = e.Source, Impact = e.Impact,
            IsEnabled = false, RiskLevel = e.RiskLevel,
            IsProtected = e.IsProtected, ProtectionReason = e.ProtectionReason,
        };
        return new StartupToggleResult(true, string.Empty);
    }

    public StartupToggleResult Enable(string entryId)
    {
        if (!Entries.TryGetValue(entryId, out var e)) return new StartupToggleResult(false, "missing");
        Entries[entryId] = new StartupEntry
        {
            Id = e.Id, Name = e.Name, Publisher = e.Publisher, Command = e.Command,
            Location = e.Location, Source = e.Source, Impact = e.Impact,
            IsEnabled = true, RiskLevel = e.RiskLevel,
            IsProtected = e.IsProtected, ProtectionReason = e.ProtectionReason,
        };
        return new StartupToggleResult(true, string.Empty);
    }
}

public sealed class FakeTaskManager : ITaskSchedulerManager
{
    public Dictionary<string, ScheduledTaskInfo> Tasks { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string path, bool enabled = true) =>
        Tasks[path] = new ScheduledTaskInfo { TaskPath = path, Name = path.Split('\\').Last(), IsEnabled = enabled };

    public IReadOnlyList<ScheduledTaskInfo> GetTasks(CancellationToken ct = default) => Tasks.Values.ToList();

    public void SetEnabled(string taskPath, bool enabled)
    {
        if (!Tasks.TryGetValue(taskPath, out var t))
            throw new NeyraOptimizer.Domain.Abstractions.ScheduledTaskException($"Task '{taskPath}' not found.");
        Tasks[taskPath] = t with { IsEnabled = enabled };
    }

    public string ExportTaskXml(string taskPath) => "<Task/>";
}

public sealed class FakePackageManager : IAppPackageManager
{
    public List<InstalledAppInfo> Apps { get; } = new();

    public void Add(string id, string family, long sizeMb = 30, RecommendationCategory cat = RecommendationCategory.Optional) =>
        Apps.Add(new InstalledAppInfo
        {
            Id = id,
            Kind = InstalledAppKind.Appx,
            DisplayName = id,
            PackageFamilyName = family,
            SizeBytes = sizeMb * 1024 * 1024,
            Category = cat,
        });

    public IReadOnlyList<InstalledAppInfo> GetInstalledApps(CancellationToken ct = default) => Apps;

    public Task UninstallPackageAsync(string packageFullName, CancellationToken ct)
    {
        Apps.RemoveAll(a => a.Id == packageFullName);
        return Task.CompletedTask;
    }

    public bool CanReinstallFromStore(InstalledAppInfo app) => app.PackageFamilyName?.EndsWith("8wekyb3d8bbwe", StringComparison.Ordinal) == true;
}

public sealed class FakeBackgroundApps : IBackgroundActivityManager
{
    public Dictionary<string, (string DisplayName, bool Enabled)> Apps { get; } = new();
    public IReadOnlyList<(string PackageFamilyName, string DisplayName, bool Enabled)> GetConfigurableApps() =>
        Apps.Select(kv => (kv.Key, kv.Value.DisplayName, kv.Value.Enabled)).ToList();
    public void SetBackgroundEnabled(string packageFamilyName, bool enabled)
    {
        if (Apps.ContainsKey(packageFamilyName))
            Apps[packageFamilyName] = (Apps[packageFamilyName].DisplayName, enabled);
    }
}

public sealed class FakeVisuals : IVisualEffectsManager
{
    public Dictionary<string, bool> States { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, bool> GetCurrentEffectStates() => States;
    public void ApplyEffect(string effectKey, bool enabled) => States[effectKey] = enabled;
}

public sealed class FakePowerManager : IPowerManager
{
    public PowerOverlayMode Overlay { get; set; } = PowerOverlayMode.Balanced;
    public string ActiveGuid { get; set; } = "balanced-guid";
    public BatteryInfo Battery { get; set; } = new() { IsPresent = false, PowerSource = PowerSource.AcPower };
    public bool IsOverlaySupported { get; set; } = true;

    public IReadOnlyList<PowerPlanInfo> GetPowerPlans() => new[]
    {
        new PowerPlanInfo { PlanGuid = "balanced-guid", Name = "Balanced", IsActive = true },
        new PowerPlanInfo { PlanGuid = "high-guid", Name = "High performance", IsActive = false },
    };

    public PowerPlanInfo? GetActivePlan() => GetPowerPlans().FirstOrDefault(p => p.PlanGuid == ActiveGuid);
    public void SetActivePlan(string planGuid) => ActiveGuid = planGuid;
    public string DuplicateActivePlan(string newName) => Guid.NewGuid().ToString();
    public void DeletePlan(string planGuid) { }
    public BatteryInfo GetBatteryInfo() => Battery;
    public PowerOverlayMode GetEffectiveOverlay() => Overlay;
    public void SetOverlay(PowerOverlayMode mode) => Overlay = mode;
}

public sealed class FakeRestorePoints : IRestorePointManager
{
    /// <summary>null = available; set to an exception to simulate failure.</summary>
    public Exception? Failure { get; set; }
    public bool Available { get; set; } = true;
    public int CreatedCount;

    public bool IsSystemRestoreAvailable() => Available;

    public Task<string> CreateRestorePointAsync(string description, CancellationToken ct)
    {
        if (Failure is not null) throw Failure;
        Interlocked.Increment(ref CreatedCount);
        return Task.FromResult((1000 + CreatedCount).ToString());
    }
}

/// <summary>
/// Applies elevated requests DIRECTLY against the provided fake managers — same validation the
/// real elevated child performs, minus the process hop.
/// </summary>
public sealed class RecordingElevationGateway : IElevationGateway
{
    private readonly FakeServiceManager _services;
    private readonly FakeTaskManager _tasks;
    public List<ElevatedOperationRequest> Requests { get; } = new();
    public bool SimulateUserCancel { get; set; }
    public bool Elevated { get; set; }

    public RecordingElevationGateway(FakeServiceManager services, FakeTaskManager tasks)
    {
        _services = services;
        _tasks = tasks;
    }

    public bool IsCurrentProcessElevated() => Elevated;

    public async Task<ElevatedOperationResult> RunAsync(ElevatedOperationRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        if (SimulateUserCancel)
            return new ElevatedOperationResult { OperationId = request.OperationId, Success = false, ErrorText = "Administrator approval was cancelled." };

        var result = Apply(request);
        await Task.Yield();
        return result with { OperationId = request.OperationId };
    }

    private ElevatedOperationResult Apply(ElevatedOperationRequest request)
    {
        switch (request.Kind)
        {
            case ElevatedOperationKind.ApplyBatch:
                foreach (var child in request.Operations) Apply(child);
                return new ElevatedOperationResult { Success = true, Detail = $"{request.Operations.Count} op(s)" };
            case ElevatedOperationKind.SetServiceStartMode:
                _services.SetStartMode(request.ServiceName!, (ServiceStartMode)request.StartModeValue);
                return new ElevatedOperationResult { Success = true };
            case ElevatedOperationKind.SetTaskEnabled:
                _tasks.SetEnabled(request.TaskPath!, request.TaskEnabled);
                return new ElevatedOperationResult { Success = true };
            case ElevatedOperationKind.CreateRestorePoint:
                return new ElevatedOperationResult { Success = true, Detail = "#1" };
            case ElevatedOperationKind.RemoveProvisionedPackage:
            case ElevatedOperationKind.StopService:
            case ElevatedOperationKind.DeleteDeliveryOptimizationCache:
            case ElevatedOperationKind.ApplyRegistryWrites:
                return new ElevatedOperationResult { Success = true };
            default:
                return new ElevatedOperationResult { Success = false, ErrorText = "unsupported" };
        }
    }
}
