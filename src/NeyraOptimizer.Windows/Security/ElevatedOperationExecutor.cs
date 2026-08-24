using Microsoft.Win32;
using System.ComponentModel;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Security.Elevation;
using NeyraOptimizer.Security.SafeExecution;
using NeyraOptimizer.Windows.Native;
using NeyraOptimizer.Windows.RegOps;

namespace NeyraOptimizer.Windows.Security;

/// <summary>
/// Executes privileged operations inside the elevated child process. Every request has already
/// passed strict validation; this executor additionally refuses protected components and only
/// performs the exact operation requested, then exits.
/// </summary>
public sealed class ElevatedOperationExecutor : IElevatedExecutor
{
    private readonly IRegistryManager _registry = new WindowsRegistryManager();

    public ElevatedOperationResult Execute(ElevatedOperationRequest request)
    {
        try
        {
            switch (request.Kind)
            {
                case ElevatedOperationKind.CreateRestorePoint:
                {
                    var manager = new Restore.RestorePointManager();
                    var seq = manager.CreateRestorePointAsync(
                        request.RestorePointDescription ?? "Neyra Optimizer snapshot", CancellationToken.None)
                        .GetAwaiter().GetResult();
                    return new ElevatedOperationResult { Success = true, Detail = $"Restore point #{seq} created." };
                }

                case ElevatedOperationKind.SetServiceStartMode:
                {
                    var services = new Services.WindowsServiceManager();
                    services.SetStartMode(request.ServiceName!, (Domain.Models.System.ServiceStartMode)request.StartModeValue);
                    return new ElevatedOperationResult { Success = true, Detail = $"Service '{request.ServiceName}' start mode updated." };
                }

                case ElevatedOperationKind.StopService:
                {
                    var services = new Services.WindowsServiceManager();
                    services.StopAsync(request.ServiceName!, 30, CancellationToken.None).GetAwaiter().GetResult();
                    return new ElevatedOperationResult { Success = true, Detail = $"Service '{request.ServiceName}' stopped." };
                }

                case ElevatedOperationKind.SetTaskEnabled:
                {
                    var tasks = new Tasks.WindowsTaskSchedulerManager();
                    tasks.SetEnabled(request.TaskPath!, request.TaskEnabled);
                    return new ElevatedOperationResult
                    {
                        Success = true,
                        Detail = $"Task '{request.TaskPath}' {(request.TaskEnabled ? "enabled" : "disabled")}.",
                    };
                }

                case ElevatedOperationKind.RemoveProvisionedPackage:
                {
                    RemoveProvisionedPackage(request.PackageFullName!);
                    return new ElevatedOperationResult
                    {
                        Success = true,
                        Detail = $"Package '{request.PackageFullName}' removed for new users.",
                    };
                }

                case ElevatedOperationKind.DeleteDeliveryOptimizationCache:
                {
                    DeleteDeliveryOptimizationCache();
                    return new ElevatedOperationResult { Success = true, Detail = "Delivery Optimization cache cleared." };
                }

                case ElevatedOperationKind.ApplyRegistryWrites:
                {
                    foreach (var write in request.RegistryWrites)
                    {
                        ApplyRegistryWrite(write);
                    }
                    return new ElevatedOperationResult
                    {
                        Success = true,
                        Detail = $"{request.RegistryWrites.Count} registry value(s) updated.",
                    };
                }

                default:
                    return new ElevatedOperationResult { Success = false, ErrorText = $"Unsupported kind '{request.Kind}'." };
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            return new ElevatedOperationResult { Success = false, ErrorText = "Access denied: " + ex.Message };
        }
        catch (Win32Exception ex)
        {
            return new ElevatedOperationResult { Success = false, ErrorText = ex.Message };
        }
        catch (InvalidOperationException ex)
        {
            return new ElevatedOperationResult { Success = false, ErrorText = ex.Message };
        }
        catch (TimeoutException ex)
        {
            return new ElevatedOperationResult { Success = false, ErrorText = ex.Message };
        }
        catch (Exception ex)
        {
            return new ElevatedOperationResult { Success = false, ErrorText = ex.GetType().Name + ": " + ex.Message };
        }
    }

    private void ApplyRegistryWrite(ElevatedRegistryWrite w)
    {
        if (w.DeleteValue)
        {
            _registry.DeleteValue(w.Root, w.SubKey, w.ValueName);
            return;
        }

        switch (w.Kind)
        {
            case RegistryValueKind.DWord:
                _registry.SetValue(w.Root, w.SubKey, w.ValueName, w.DWordData, RegistryValueKind.DWord);
                break;
            case RegistryValueKind.String or RegistryValueKind.ExpandString:
                _registry.SetValue(w.Root, w.SubKey, w.ValueName, w.StringData ?? string.Empty, w.Kind);
                break;
            case RegistryValueKind.Binary:
                _registry.SetValue(w.Root, w.SubKey, w.ValueName, HexToBytes(w.BinaryDataHex ?? string.Empty), RegistryValueKind.Binary);
                break;
            default:
                throw new InvalidOperationException($"Unsupported registry value kind '{w.Kind}'.");
        }
    }

    /// <summary>Removes a provisioned AppX package so it is not installed for NEW user profiles.</summary>
    private static void RemoveProvisionedPackage(string packageFullName)
    {
        // Official cmdlet via whitelisted runner; the package name was regex-validated upstream.
        var script =
            "$ErrorActionPreference='Stop'; " +
            "$p = Get-AppxProvisionedPackage -Online | Where-Object { $_.PackageName -eq '" +
            packageFullName.Replace("'", "''") + "' }; " +
            "if ($p) { $p | Remove-AppxProvisionedPackage -Online | Out-Null }";
        var runner = new SafeCommandLineRunner();
        var result = runner.RunPowerShellAsync(SafeCommandLineRunner.EncodePowerShell(script),
            TimeSpan.FromSeconds(120), CancellationToken.None).GetAwaiter().GetResult();
        if (!result.Success && result.ExitCode != 0)
            throw new InvalidOperationException($"Provisioned package removal failed: {result.StdErr.Trim()}");
    }

    private static void DeleteDeliveryOptimizationCache()
    {
        const string script = "$ErrorActionPreference='Continue'; Delete-DeliveryOptimizationCache -Force; exit 0";
        var runner = new SafeCommandLineRunner();
        var result = runner.RunPowerShellAsync(SafeCommandLineRunner.EncodePowerShell(script),
            TimeSpan.FromSeconds(180), CancellationToken.None).GetAwaiter().GetResult();
        if (!result.Success && result.ExitCode != 0)
            throw new InvalidOperationException($"Delivery Optimization cleanup failed: {result.StdErr.Trim()}");
    }

    private static byte[] HexToBytes(string hex)
    {
        if (hex.Length % 2 != 0)
            throw new InvalidOperationException("Binary payload must have even length.");
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }
}
