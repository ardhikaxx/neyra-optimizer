using Microsoft.Win32;
using NeyraOptimizer.Domain.Models.Power;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.Domain.Abstractions;

public enum RegRoot
{
    CurrentUser = 0,
    LocalMachine = 1,
    Users = 2,
    ClassesRoot = 3,
}

public sealed record RegistryValueDto(string Name, object? Data, RegistryValueKind Kind);

public sealed class RegistryOperationException : Exception
{
    public string FullPath { get; }
    public RegistryOperationException(string message, string fullPath, Exception? inner = null)
        : base(message, inner) => FullPath = fullPath;
}

/// <summary>
/// Typed registry abstraction. Every write goes through here so previous values can be
/// captured and rolled back. No bulk key deletion is exposed by design.
/// </summary>
public interface IRegistryManager
{
    bool KeyExists(RegRoot root, string subKey);
    RegistryValueDto? GetValue(RegRoot root, string subKey, string valueName);
    IReadOnlyList<RegistryValueDto> GetValues(RegRoot root, string subKey);
    IReadOnlyList<string> GetSubKeyNames(RegRoot root, string subKey);
    void SetValue(RegRoot root, string subKey, string valueName, object data, RegistryValueKind kind);
    /// <summary>Deletes a single VALUE (never a key). Returns false when the value does not exist.</summary>
    bool DeleteValue(RegRoot root, string subKey, string valueName);
}

public interface IPowerManager
{
    IReadOnlyList<PowerPlanInfo> GetPowerPlans();
    PowerPlanInfo? GetActivePlan();
    void SetActivePlan(string planGuid);
    /// <summary>Duplicates the active plan with a Neyra-prefixed name and returns its GUID.</summary>
    string DuplicateActivePlan(string newName);
    void DeletePlan(string planGuid);
    BatteryInfo GetBatteryInfo();
    /// <summary>Overlay ("power mode") is supported on Windows 10 1709+; NotSupported otherwise.</summary>
    PowerOverlayMode GetEffectiveOverlay();
    void SetOverlay(PowerOverlayMode mode);
    /// <summary>True when overlay switching is available on this build/hardware.</summary>
    bool IsOverlaySupported { get; }
}

public interface IVisualEffectsManager
{
    IReadOnlyDictionary<string, bool> GetCurrentEffectStates();
    /// <summary>Applies one named effect. Effect keys are stable identifiers from VisualEffectsCatalog.</summary>
    void ApplyEffect(string effectKey, bool enabled);
}

public interface IRestorePointManager
{
    /// <summary>True when System Restore exists and is enabled for at least one volume.</summary>
    bool IsSystemRestoreAvailable();
    /// <summary>Creates a restore point. Requires elevation. Returns sequence number or throws.</summary>
    Task<string> CreateRestorePointAsync(string description, CancellationToken ct);
}
