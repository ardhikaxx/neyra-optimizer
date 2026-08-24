using System.Security;
using Microsoft.Win32;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Security.Protection;
using NeyraOptimizer.Windows.Native;

namespace NeyraOptimizer.Windows.Packages;

/// <summary>
/// Enumerates installed software from two sources: Windows.Management.Deployment (AppX/MSIX) and
/// the documented uninstall registry keys (Win32). Uninstall of AppX packages runs for the current
/// user only; provisioned-package removal is a separate, explicitly-requested elevated operation.
/// </summary>
public sealed class AppxPackageManager : IAppPackageManager
{
    private static readonly HashSet<string> StoreReinstallableFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.BingNews_", "Microsoft.BingWeather_", "Microsoft.BingSearch_",
        "Microsoft.MicrosoftStickyNotes_", "Microsoft.MixedReality.Portal_",
        "Microsoft.News_", "Microsoft.Office.OneNote_", "Microsoft.OneDriveSync_",
        "Microsoft.People_", "Microsoft.SkypeApp_", "Microsoft.Todos_",
        "Microsoft.Whiteboard_", "Microsoft.WindowsFeedbackHub_", "Microsoft.WindowsMaps_",
        "Microsoft.WindowsSoundRecorder_", "Microsoft.YourPhone_", "Microsoft.ZuneMusic_",
        "Microsoft.ZuneVideo_", "Microsoft.GetHelp_", "Microsoft.Getstarted_",
        "Microsoft.549981C3F5F10_" /* Cortana */, "MicrosoftTeams_",
        "Clipchamp.Clipchamp_", "Microsoft.Copilot_", "MSTeams_",
    };

    public IReadOnlyList<InstalledAppInfo> GetInstalledApps(CancellationToken ct = default)
    {
        var apps = new List<InstalledAppInfo>();
        ct.ThrowIfCancellationRequested();

        apps.AddRange(GetAppxPackages(ct));
        apps.AddRange(GetWin32Apps(ct));
        return apps.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IEnumerable<InstalledAppInfo> GetAppxPackages(CancellationToken ct)
    {
        var collected = new List<InstalledAppInfo>();
        try
        {
            var pm = new global::Windows.Management.Deployment.PackageManager();
            foreach (var pkg in pm.FindPackages())
            {
                ct.ThrowIfCancellationRequested();
                if (pkg is null) continue;

                string? familyName = SafeGet(() => pkg.Id.FamilyName);
                string displayName = SafeGet(() => pkg.DisplayName as string) ?? familyName ?? "Unknown";
                string publisher = SafeGet(() => pkg.PublisherDisplayName as string) ?? string.Empty;
                string version = SafeGet(() => pkg.Id.Version.Major + "." + pkg.Id.Version.Minor + "." +
                                               pkg.Id.Version.Build + "." + pkg.Id.Version.Revision) ?? "0.0.0.0";
                var isFramework = SafeGet(() => (bool)(pkg.IsFramework || pkg.IsResourcePackage || pkg.IsBundle)) == true;

                var protectedFlag = ProtectedComponents.IsPackageProtected(familyName);

                collected.Add(new InstalledAppInfo
                {
                    Id = SafeGet(() => pkg.Id.FullName) ?? familyName ?? Guid.NewGuid().ToString("N"),
                    Kind = InstalledAppKind.Appx,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? familyName ?? "Unknown" : displayName,
                    Publisher = publisher,
                    Version = version,
                    InstallLocation = SafeGet(() => pkg.InstalledLocation?.Path),
                    PackageFamilyName = familyName,
                    IsProtected = protectedFlag || isFramework,
                    ProtectionReason = protectedFlag
                        ? "Core system component, framework or store infrastructure."
                        : isFramework ? "Framework/resource package required by other applications." : string.Empty,
                    RiskLevel = protectedFlag ? RiskLevel.Critical : RiskLevel.Low,
                    ReinstallNote = CanReinstallFamily(familyName)
                        ? "Can typically be reinstalled from the Microsoft Store after uninstall."
                        : string.Empty,
                    Category = protectedFlag ? RecommendationCategory.DoNotModify : RecommendationCategory.Optional,
                });
            }
        }
        catch (SecurityException)
        {
            // Package enumeration denied — Win32 list still works.
        }
        return collected;
    }

    private IEnumerable<InstalledAppInfo> GetWin32Apps(CancellationToken ct)
    {
        const string uninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        var roots = new List<(RegRoot Root, string SubKey)>
        {
            (RegRoot.LocalMachine, uninstallKey),
            (RegRoot.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegRoot.CurrentUser, uninstallKey),
        };

        foreach (var (root, subKey) in roots)
        {
            foreach (var keyName in _registry.GetSubKeyNames(root, subKey))
            {
                ct.ThrowIfCancellationRequested();
                var full = $@"{subKey}\{keyName}";
                var dto = _registry.GetValue(root, full, "DisplayName");
                if (dto?.Data is not string name || name.Length < 2) continue;

                var uninstallString = _registry.GetValue(root, full, "UninstallString")?.Data as string;
                if (string.IsNullOrWhiteSpace(uninstallString)) continue; // cannot be uninstalled → not user-manageable

                var systemComponent = _registry.GetValue(root, full, "SystemComponent")?.Data is int sc && sc == 1;
                long? sizeKb = _registry.GetValue(root, full, "EstimatedSize")?.Data as int?;
                var displayVersion = _registry.GetValue(root, full, "DisplayVersion")?.Data as string ?? string.Empty;
                var publisher = _registry.GetValue(root, full, "Publisher")?.Data as string ?? string.Empty;

                var protectedFlag = ProtectedComponents.IsWin32AppProtected(name);

                yield return new InstalledAppInfo
                {
                    Id = $"{(root == RegRoot.CurrentUser ? "HKCU" : "HKLM")}:{keyName}",
                    Kind = InstalledAppKind.Win32,
                    DisplayName = name,
                    Publisher = publisher,
                    Version = displayVersion,
                    SizeBytes = sizeKb is > 0 ? sizeKb * 1024L : null,
                    IsProtected = protectedFlag,
                    ProtectionReason = protectedFlag ? "Runtime, driver, security product or OS-integrated component." : string.Empty,
                    RiskLevel = protectedFlag ? RiskLevel.Critical : RiskLevel.Medium,
                    Category = protectedFlag ? Domain.Enums.RecommendationCategory.DoNotModify
                                             : Domain.Enums.RecommendationCategory.Optional,
                    ReinstallNote = string.Empty, // Win32 reinstall depends on vendor; never guessed here.
                };
                _ = systemComponent; // hidden components are still listed but never auto-selected.
            }
        }
    }

    private readonly IRegistryManager _registry;

    public AppxPackageManager(IRegistryManager registry) => _registry = registry;

    public async Task UninstallPackageAsync(string packageFullName, CancellationToken ct)
    {
        if (ProtectedComponents.IsPackageProtected(packageFullName))
            throw new PackageOperationException($"Package '{packageFullName}' is protected and cannot be removed.");

        try
        {
            var pm = new global::Windows.Management.Deployment.PackageManager();
            var op = pm.RemovePackageAsync(packageFullName);
            using var cancelReg = ct.Register(() => { try { op.Cancel(); } catch (InvalidOperationException) { } });

            global::Windows.Management.Deployment.DeploymentResult result;
            try
            {
                // Awaiting a failed WinRT deployment operation surfaces its HRESULT as an exception.
                result = await op.AsTask(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (global::System.Runtime.InteropServices.COMException comEx)
            {
                throw new PackageOperationException(
                    $"Package '{packageFullName}' could not be removed: 0x{comEx.HResult:X8}. " +
                    "It may be in use, protected by policy, or require removal for all users first.", comEx);
            }

            if (!string.IsNullOrEmpty(result.ErrorText) && result.ExtendedErrorCode.HResult != 0)
            {
                throw new PackageOperationException(
                    $"Package removal failed: {result.ErrorText} (0x{result.ExtendedErrorCode.HResult:X}).");
            }
        }
        catch (PackageOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PackageOperationException($"Package '{packageFullName}' could not be removed: {ex.Message}", ex);
        }
    }

    public bool CanReinstallFromStore(InstalledAppInfo app) =>
        app.Kind == InstalledAppKind.Appx && CanReinstallFamily(app.PackageFamilyName);

    private static bool CanReinstallFamily(string? familyName) =>
        !string.IsNullOrEmpty(familyName) &&
        StoreReinstallableFamilies.Any(p => familyName.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Bloat classification uses conservative heuristics; everything defaults to Optional.</summary>
    internal static Domain.Enums.RecommendationCategory ClassifyAppx(string displayName, string? familyName) =>
        Domain.Enums.RecommendationCategory.Optional;

    private static T? SafeGet<T>(Func<T?> getter)
    {
        try { return getter(); } catch (SystemException) { return default; }
    }
}
