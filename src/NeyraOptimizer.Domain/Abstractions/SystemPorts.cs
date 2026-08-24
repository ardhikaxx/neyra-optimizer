using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.Domain.Abstractions;

public interface IWindowsServiceManager
{
    IReadOnlyList<ServiceInfo> GetServices();
    ServiceInfo? GetService(string serviceName);
    /// <summary>Changes the start type via the Service Control Manager. Throws on failure with Win32 error.</summary>
    void SetStartMode(string serviceName, ServiceStartMode mode);
    /// <summary>Stops a service waiting up to timeoutSeconds. Only called for explicitly consented targets.</summary>
    Task StopAsync(string serviceName, int timeoutSeconds, CancellationToken ct);
}

public sealed record StartupToggleResult(bool Success, string ErrorText);

public interface IStartupManager
{
    IReadOnlyList<StartupEntry> GetStartupEntries();
    /// <summary>Disables an entry without deleting its definition where the OS supports it.</summary>
    StartupToggleResult Disable(string entryId);
    StartupToggleResult Enable(string entryId);
}

public sealed class ScheduledTaskException : Exception
{
    public ScheduledTaskException(string message, Exception? inner = null) : base(message, inner) { }
}

public interface ITaskSchedulerManager
{
    /// <summary>Returns tasks visible to the current user. Protected/system tasks are included for analysis.</summary>
    IReadOnlyList<ScheduledTaskInfo> GetTasks(CancellationToken ct = default);
    /// <summary>Disables a task by full path. The task definition is never deleted here.</summary>
    void SetEnabled(string taskPath, bool enabled);
    /// <summary>Exports the raw task XML for backup purposes.</summary>
    string ExportTaskXml(string taskPath);
}

public sealed class PackageOperationException : Exception
{
    public PackageOperationException(string message, Exception? inner = null) : base(message, inner) { }
}

public interface IAppPackageManager
{
    IReadOnlyList<InstalledAppInfo> GetInstalledApps(CancellationToken ct = default);
    /// <summary>Removes an AppX/MSIX package for the current user only.</summary>
    Task UninstallPackageAsync(string packageFullName, CancellationToken ct);
    /// <summary>True when the store product can plausibly reinstall this family name.</summary>
    bool CanReinstallFromStore(InstalledAppInfo app);
}

public interface IProcessAnalyzer
{
    IReadOnlyList<BackgroundProcessInfo> GetProcessesWithClassification(CancellationToken ct = default);
    /// <summary>Terminates a process that has been classified as safely terminable. Returns false otherwise.</summary>
    bool TryTerminate(int processId, out string errorText);
}

public interface IBackgroundActivityManager
{
    /// <summary>Package family names of apps with a controllable background execution state.</summary>
    IReadOnlyList<(string PackageFamilyName, string DisplayName, bool Enabled)> GetConfigurableApps();
    void SetBackgroundEnabled(string packageFamilyName, bool enabled);
}
