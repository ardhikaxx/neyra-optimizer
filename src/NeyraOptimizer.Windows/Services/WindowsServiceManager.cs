using System.ComponentModel;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Windows.Native;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using ServiceStartMode = NeyraOptimizer.Domain.Models.System.ServiceStartMode;
using System.ServiceProcess;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Windows.Native;
using NeyraOptimizer.Windows.Native;

namespace NeyraOptimizer.Windows.Services;

/// <summary>
/// Windows Service Control Manager integration. Enumeration uses ServiceController; configuration
/// changes use ChangeServiceConfig so SCM ACLs are always honored. All operations address services
/// by their key name, never by localized display name.
/// </summary>
public sealed class WindowsServiceManager : IWindowsServiceManager
{
    public IReadOnlyList<ServiceInfo> GetServices()
    {
        var result = new List<ServiceInfo>(256);
        foreach (var sc in ServiceController.GetServices())
        {
            result.Add(ToServiceInfo(sc));
            sc.Dispose();
        }
        return result.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public ServiceInfo? GetService(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            return ToServiceInfo(sc);
        }
        catch (InvalidOperationException)
        {
            return null; // service does not exist
        }
    }

    private static ServiceInfo ToServiceInfo(ServiceController sc)
    {
        // Read start mode incl. delayed-auto from the registry (SCM API has no delayed flag).
        var startMode = ReadStartMode(sc.ServiceName);

        string exePath = string.Empty;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{sc.ServiceName}");
            var image = key?.GetValue("ImagePath") as string;
            if (!string.IsNullOrWhiteSpace(image))
                exePath = Environment.ExpandEnvironmentVariables(image.Trim('"'));
        }
        catch (SystemException) { /* path stays empty */ }

        return new ServiceInfo
        {
            ServiceName = sc.ServiceName,
            DisplayName = sc.DisplayName ?? sc.ServiceName,
            StartMode = startMode,
            Status = MapStatus(sc.Status),
            AccountName = SafeAccountName(sc),
            ExecutablePath = exePath,
            ServicesDependedOn = SafeNames(() => sc.ServicesDependedOn.Select(d => d.ServiceName)),
            DependentServices = SafeNames(() => sc.DependentServices.Select(d => d.ServiceName)),
            CanPauseAndContinue = sc.CanPauseAndContinue,
            Classification = ServiceClassification.Optional,
        };
    }

    private static string[] SafeNames(Func<IEnumerable<string>> selector)
    {
        try { return selector().ToArray(); }
        catch (InvalidOperationException) { return Array.Empty<string>(); }
    }

    private static string SafeAccountName(ServiceController sc)
    {
        try { return sc.ServiceAccountDisplayName(); }
        catch { return string.Empty; }
    }

    internal static ServiceStartMode ReadStartMode(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            if (key is null) return ServiceStartMode.Unknown;
            if (key.GetValue("Start") is not int start) return ServiceStartMode.Unknown;
            var delayed = key.GetValue("DelayedAutostart") is int d && d != 0;
            return start switch
            {
                NativeMethods.SERVICE_AUTO_START when delayed => ServiceStartMode.AutomaticDelayed,
                NativeMethods.SERVICE_AUTO_START => ServiceStartMode.Automatic,
                NativeMethods.SERVICE_DEMAND_START => ServiceStartMode.Manual,
                NativeMethods.SERVICE_DISABLED => ServiceStartMode.Disabled,
                NativeMethods.SERVICE_BOOT_START => ServiceStartMode.Boot,
                NativeMethods.SERVICE_SYSTEM_START => ServiceStartMode.System,
                _ => ServiceStartMode.Unknown,
            };
        }
        catch (System.Security.SecurityException)
        {
            return ServiceStartMode.Unknown;
        }
    }

    private static ServiceStatus MapStatus(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Stopped => ServiceStatus.Stopped,
        ServiceControllerStatus.StartPending => ServiceStatus.StartPending,
        ServiceControllerStatus.StopPending => ServiceStatus.StopPending,
        ServiceControllerStatus.Running => ServiceStatus.Running,
        ServiceControllerStatus.PausePending => ServiceStatus.PausePending,
        ServiceControllerStatus.Paused => ServiceStatus.Paused,
        _ => ServiceStatus.Unknown,
    };

    public void SetStartMode(string serviceName, ServiceStartMode mode)
    {
        var scmHandle = IntPtr.Zero;
        var serviceHandle = IntPtr.Zero;
        try
        {
            scmHandle = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_ALL_ACCESS);
            if (scmHandle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the Service Control Manager.");

            serviceHandle = NativeMethods.OpenService(scmHandle, serviceName, NativeMethods.SERVICE_CHANGE_CONFIG | NativeMethods.SERVICE_QUERY_CONFIG);
            if (serviceHandle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open service '{serviceName}'.");

            var startValue = mode switch
            {
                ServiceStartMode.Automatic => NativeMethods.SERVICE_AUTO_START,
                ServiceStartMode.AutomaticDelayed => NativeMethods.SERVICE_AUTO_START,
                ServiceStartMode.Manual => NativeMethods.SERVICE_DEMAND_START,
                ServiceStartMode.Disabled => NativeMethods.SERVICE_DISABLED,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), "Boot/System start types cannot be set."),
            };

            if (!NativeMethods.ChangeServiceConfig(
                    serviceHandle,
                    NativeMethods.SERVICE_NO_CHANGE,
                    startValue,
                    NativeMethods.SERVICE_NO_CHANGE,
                    null, null, IntPtr.Zero, null, null, null, null))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"ChangeServiceConfig failed for '{serviceName}'.");
            }

            // Delayed-autostart is a separate registry-backed flag under HKLM\Services.
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: true);
            if (key is not null)
            {
                if (mode == ServiceStartMode.AutomaticDelayed)
                    key.SetValue("DelayedAutostart", 1, RegistryValueKind.DWord);
                else if (key.GetValue("DelayedAutostart") is not null)
                    key.SetValue("DelayedAutostart", 0, RegistryValueKind.DWord);
            }
        }
        finally
        {
            if (serviceHandle != IntPtr.Zero) NativeMethods.CloseServiceHandle(serviceHandle);
            if (scmHandle != IntPtr.Zero) NativeMethods.CloseServiceHandle(scmHandle);
        }
    }

    public async Task StopAsync(string serviceName, int timeoutSeconds, CancellationToken ct)
    {
        using var sc = new ServiceController(serviceName);
        if (sc.Status == ServiceControllerStatus.Stopped) return;

        if (sc.CanStop)
        {
            sc.Stop();
        }
        else
        {
            // Service refuses stop requests; do not force — report honestly via timeout path.
            throw new InvalidOperationException($"Service '{serviceName}' reports that it cannot be stopped.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var deadlineTicks = DateTime.UtcNow.Ticks + TimeSpan.FromSeconds(timeoutSeconds).Ticks;
        while (true)
        {
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            sc.Refresh();
            if (sc.Status == ServiceControllerStatus.Stopped) break;
            if (DateTime.UtcNow.Ticks >= deadlineTicks)
                throw new System.TimeoutException($"Timed out stopping service '{serviceName}' after {timeoutSeconds}s.");
            await Task.Delay(250, timeoutCts.Token).ConfigureAwait(false);
        }
    }
}

internal static class ServiceAccountExtensions
{
    /// <summary>ServiceController does not expose the account without WMI; read it from the registry.</summary>
    public static string ServiceAccountDisplayName(this ServiceController sc)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{sc.ServiceName}");
            return key?.GetValue("ObjectName") as string ?? string.Empty;
        }
        catch (SystemException)
        {
            return string.Empty;
        }
    }
}
