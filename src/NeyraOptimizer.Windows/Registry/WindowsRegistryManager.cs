using Microsoft.Win32;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Windows.Native;

namespace NeyraOptimizer.Windows.RegOps;

/// <summary>
/// Typed registry abstraction over Microsoft.Win32.Registry. All writes are single-value,
/// auditable operations — bulk key deletion is deliberately not exposed.
/// </summary>
public sealed class WindowsRegistryManager : IRegistryManager
{
    public bool KeyExists(RegRoot root, string subKey)
    {
        using var baseKey = RegistryViewHelper.OpenBase(root, writable: false);
        using var key = baseKey.OpenSubKey(subKey);
        return key is not null;
    }

    public RegistryValueDto? GetValue(RegRoot root, string subKey, string valueName)
    {
        using var baseKey = RegistryViewHelper.OpenBase(root, writable: false);
        using var key = baseKey.OpenSubKey(subKey);
        if (key is null) return null;
        var data = key.GetValue(valueName);
        if (data is null) return null;
        return new RegistryValueDto(valueName, data, key.GetValueKind(valueName));
    }

    public IReadOnlyList<RegistryValueDto> GetValues(RegRoot root, string subKey)
    {
        var list = new List<RegistryValueDto>();
        using var baseKey = RegistryViewHelper.OpenBase(root, writable: false);
        using var key = baseKey.OpenSubKey(subKey);
        if (key is null) return list;
        foreach (var name in key.GetValueNames())
        {
            var data = key.GetValue(name);
            if (data is null) continue;
            RegistryValueKind kind;
            try { kind = key.GetValueKind(name); }
            catch (SystemException) { kind = RegistryValueKind.Unknown; }
            list.Add(new RegistryValueDto(name, data, kind));
        }
        return list;
    }

    public IReadOnlyList<string> GetSubKeyNames(RegRoot root, string subKey)
    {
        using var baseKey = RegistryViewHelper.OpenBase(root, writable: false);
        using var key = baseKey.OpenSubKey(subKey);
        if (key is null) return Array.Empty<string>();
        return key.GetSubKeyNames();
    }

    public void SetValue(RegRoot root, string subKey, string valueName, object data, RegistryValueKind kind)
    {
        try
        {
            using var baseKey = RegistryViewHelper.OpenBase(root, writable: true);
            using var key = baseKey.CreateSubKey(subKey, writable: true);
            key.SetValue(string.IsNullOrEmpty(valueName) ? "" : valueName, data, kind);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new RegistryOperationException("Access denied writing registry value.", FullPath(root, subKey, valueName), ex);
        }
        catch (System.Security.SecurityException ex)
        {
            throw new RegistryOperationException("Security policy denied the registry write.", FullPath(root, subKey, valueName), ex);
        }
        catch (IOException ex)
        {
            throw new RegistryOperationException("I/O failure while writing registry value.", FullPath(root, subKey, valueName), ex);
        }
    }

    public bool DeleteValue(RegRoot root, string subKey, string valueName)
    {
        using var baseKey = RegistryViewHelper.OpenBase(root, writable: true);
        using var key = baseKey.OpenSubKey(subKey, writable: true);
        if (key is null) return false;
        var names = key.GetValueNames();
        var exists = names.Any(n => n.Equals(valueName, StringComparison.OrdinalIgnoreCase));
        if (!exists) return false;
        key.DeleteValue(valueName, throwOnMissingValue: false);
        return true;
    }

    internal static string FullPath(RegRoot root, string subKey, string? valueName = null)
    {
        var hive = root switch
        {
            RegRoot.CurrentUser => "HKCU",
            RegRoot.LocalMachine => "HKLM",
            RegRoot.Users => "HKU",
            RegRoot.ClassesRoot => "HKCR",
            _ => "?",
        };
        return $"{hive}\\{subKey}{(string.IsNullOrEmpty(valueName) ? string.Empty : $"\\{valueName}")}";
    }
}

