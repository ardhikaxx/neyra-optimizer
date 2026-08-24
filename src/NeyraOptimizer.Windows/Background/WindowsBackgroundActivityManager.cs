using Microsoft.Win32;
using NeyraOptimizer.Windows.Native;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Windows.RegOps;
using NeyraOptimizer.Windows.Tasks;

namespace NeyraOptimizer.Windows.Background;

/// <summary>
/// Background execution control via the documented BackgroundAccessApplications registry keys —
/// the same mechanism the Settings app uses for per-app background permissions. Availability of
/// the master toggle differs between Windows 10 and newer Windows 11 builds; per-app state
/// remains honored for most store apps, which is what this manager controls.
/// </summary>
public sealed class WindowsBackgroundActivityManager : IBackgroundActivityManager
{
    private const string BaaKey = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";

    public IReadOnlyList<(string PackageFamilyName, string DisplayName, bool Enabled)> GetConfigurableApps()
    {
        var results = new List<(string, string, bool)>();

        IReadOnlyList<string> subKeys;
        try
        {
            using var baseKey = RegistryViewHelper.OpenBase(RegRoot.CurrentUser, writable: false);
            using var key = baseKey.OpenSubKey(BaaKey);
            if (key is null) return results;
            subKeys = key.GetSubKeyNames();
        }
        catch (System.Security.SecurityException)
        {
            return results;
        }

        foreach (var sub in subKeys)
        {
            if (!TryExtractFamilyName(sub, out var family)) continue;

            var disabled = ReadDisabledValue(sub);
            var displayName = TryResolveDisplayName(family) ?? family;
            results.Add((family, displayName, !disabled));
        }
        return results.OrderBy(r => r.Item2, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void SetBackgroundEnabled(string packageFamilyName, bool enabled)
    {
        // Find the matching full-name subkeys (there may be several resource variants).
        var matches = ListMatchingSubKeys(packageFamilyName);
        if (matches.Count == 0)
            throw new InvalidOperationException($"No background-controllable installation found for '{packageFamilyName}'.");

        foreach (var sub in matches)
        {
            var path = $@"{BaaKey}\{sub}";
            if (enabled)
            {
                // Removing "Disabled" restores the app-managed default.
                if (!_registry.DeleteValue(RegRoot.CurrentUser, path, "Disabled"))
                {
                    // Value may already be absent.
                }
            }
            else
            {
                _registry.SetValue(RegRoot.CurrentUser, path, "Disabled", 1, RegistryValueKind.DWord);
            }
        }
    }

    private readonly IRegistryManager _registry;

    public WindowsBackgroundActivityManager(IRegistryManager registry) => _registry = registry;

    private IReadOnlyList<string> ListMatchingSubKeys(string familyName)
    {
        using var baseKey = RegistryViewHelper.OpenBase(RegRoot.CurrentUser, writable: false);
        using var key = baseKey.OpenSubKey(BaaKey);
        if (key is null) return Array.Empty<string>();
        return key.GetSubKeyNames()
            .Where(s => s.StartsWith(familyName + ".", StringComparison.OrdinalIgnoreCase) ||
                        s.StartsWith(familyName + "_", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private bool ReadDisabledValue(string subKeyName)
    {
        var dto = _registry.GetValue(RegRoot.CurrentUser, $@"{BaaKey}\{subKeyName}", "Disabled");
        return dto?.Data is int d && d != 0;
    }

    internal static bool TryExtractFamilyName(string subKeyName, out string familyName)
    {
        familyName = string.Empty;
        if (string.IsNullOrEmpty(subKeyName)) return false;
        // Format: Family.Version_Arch__Hash  → strip everything after first '_' following version digits.
        // Safer: split on '.' after removing trailing version+arch+hash segments.
        var idx = subKeyName.IndexOf("_", StringComparison.Ordinal);
        if (idx <= 0) return false;

        // Family names look like "Microsoft.BingWeather_8wekyb3d8bbwe"; full keys append ".1.2.3...x64__hash".
        // Take the part up to the LAST underscore before "__" hash when present, else up to first '_'.
        var doubleUnderscore = subKeyName.IndexOf("__", StringComparison.Ordinal);
        if (doubleUnderscore > 13) // full name form
        {
            var archStart = subKeyName.LastIndexOf('_', doubleUnderscore - 1);
            if (archStart > 0)
            {
                var dotBeforeArch = subKeyName.LastIndexOf('.', archStart - 1);
                if (dotBeforeArch > 0)
                {
                    familyName = subKeyName[..dotBeforeArch];
                    return true;
                }
            }
        }
        else
        {
            familyName = subKeyName;
            return true;
        }
        return false;
    }

    private string? TryResolveDisplayName(string familyName)
    {
        try
        {
            var pm = new global::Windows.Management.Deployment.PackageManager();
            foreach (var pkg in pm.FindPackagesForUser(string.Empty, familyName))
            {
                var dn = pkg?.DisplayName;
                return string.IsNullOrWhiteSpace(dn) ? null : dn;
            }
        }
        catch (SystemException)
        {
            return null;
        }
        return null;
    }
}
