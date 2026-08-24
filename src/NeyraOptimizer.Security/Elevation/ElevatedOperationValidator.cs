using System.Text.RegularExpressions;
using NeyraOptimizer.Security.Protection;

using Microsoft.Win32;
using NeyraOptimizer.Domain.Abstractions;

namespace NeyraOptimizer.Security.Elevation;

/// <summary>
/// Validates elevated operation requests BEFORE any privileged action happens. The elevated child
/// re-runs this validation independently, so a tampered or malformed request can never execute.
/// </summary>
public static partial class ElevatedOperationValidator
{
    [GeneratedRegex(@"^[A-Za-z0-9_\-\.\$]{1,64}$")]
    private static partial Regex ServiceNameRegex();

    [GeneratedRegex(@"^\\[^\r\n\\/:*\?""<>|]{1,300}(\\[^\r\n\\/:*\?""<>|]{1,180})*$")]
    private static partial Regex TaskPathRegex();

    // AppX full name: Name_Version_Arch__PublisherHash (publisher hash is 13 chars).
    [GeneratedRegex(@"^[A-Za-z0-9\.\-_]{1,120}_[0-9]+\.[0-9]+(\.[0-9]+)*\.[0-9]+_(x64|x86|arm|arm64|neutral)__(8wekyb3d8bbwe|[a-z0-9]{13})$")]
    private static partial Regex PackageFullNameRegex();

    private static readonly string[] AllowedRegistryPrefixes =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\",
        @"SOFTWARE\Policies\Microsoft\Windows\",
        @"SYSTEM\CurrentControlSet\Services\",
    };

    public static (bool Valid, string Error) Validate(ElevatedOperationRequest request)
    {
        if (request is null) return (false, "Request is null.");
        if (request.OperationId == Guid.Empty) return (false, "OperationId missing.");

        switch (request.Kind)
        {
            case ElevatedOperationKind.CreateRestorePoint:
                var d = request.RestorePointDescription ?? string.Empty;
                if (d.Length == 0 || d.Length > 200 || d.Contains('\0'))
                    return (false, "Restore point description invalid.");
                return (true, string.Empty);

            case ElevatedOperationKind.SetServiceStartMode or ElevatedOperationKind.StopService:
                if (!ServiceNameRegex().IsMatch(request.ServiceName ?? string.Empty))
                    return (false, "Service name invalid.");
                if (ProtectedComponents.IsServiceProtected(request.ServiceName!))
                    return (false, $"Service '{request.ServiceName}' is protected and cannot be modified.");
                if (request.Kind == ElevatedOperationKind.SetServiceStartMode &&
                    request.StartModeValue is < 2 or > 6)
                    return (false, "Start mode out of allowed range.");
                return (true, string.Empty);

            case ElevatedOperationKind.SetTaskEnabled:
                var tp = request.TaskPath ?? string.Empty;
                if (!TaskPathRegex().IsMatch(tp))
                    return (false, "Task path invalid.");
                if (tp.Contains("..", StringComparison.Ordinal))
                    return (false, "Task path contains traversal segments.");
                if (!request.TaskEnabled && ProtectedComponents.IsTaskProtected(tp))
                    return (false, $"Scheduled task '{tp}' is protected and cannot be disabled.");
                return (true, string.Empty);

            case ElevatedOperationKind.RemoveProvisionedPackage:
                var pkg = request.PackageFullName ?? string.Empty;
                if (!PackageFullNameRegex().IsMatch(pkg))
                    return (false, "Package full name invalid.");
                if (ProtectedComponents.IsPackageProtected(pkg))
                    return (false, $"Package '{pkg}' belongs to a protected component family.");
                return (true, string.Empty);

            case ElevatedOperationKind.DeleteDeliveryOptimizationCache:
                return (true, string.Empty); // fixed operation, no parameters

            case ElevatedOperationKind.ApplyRegistryWrites:
                if (request.RegistryWrites.Count == 0) return (false, "No registry writes supplied.");
                if (request.RegistryWrites.Count > 64) return (false, "Too many registry writes in one batch.");
                foreach (var w in request.RegistryWrites)
                {
                    var err = ValidateRegistryWrite(w);
                    if (err is not null) return (false, err);
                }
                return (true, string.Empty);

            default:
                return (false, $"Unknown operation kind '{request.Kind}'.");
        }
    }

    private static string? ValidateRegistryWrite(ElevatedRegistryWrite w)
    {
        if (w.Root != RegRoot.LocalMachine && w.Root != RegRoot.Users)
            return "Only HKLM/HKU registry writes are permitted through elevation.";

        var sub = (w.SubKey ?? string.Empty).Trim();
        if (sub.Length == 0 || sub.Length > 260) return "Registry subkey length invalid.";
        if (sub.Contains("..", StringComparison.Ordinal)) return "Registry subkey contains traversal.";
        if (!AllowedRegistryPrefixes.Any(p => sub.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return $"Registry path '{sub}' is outside the approved prefix list.";

        if (w.DeleteValue) return null;

        if ((w.ValueName ?? string.Empty).Length > 16383) return "Value name too long.";
        return w.Kind switch
        {
            RegistryValueKind.DWord => null,
            RegistryValueKind.String or RegistryValueKind.ExpandString => (w.StringData?.Length ?? 0) <= 4096
                ? null
                : "String value exceeds size limit.",
            RegistryValueKind.Binary => (w.BinaryDataHex?.Length ?? 0) <= 8192 ? null : "Binary value exceeds size limit.",
            _ => "Unsupported registry value kind.",
        };
    }
}
