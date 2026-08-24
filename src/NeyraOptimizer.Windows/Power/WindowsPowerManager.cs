using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Management;
using Microsoft.Win32;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.Power;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Windows.Native;

namespace NeyraOptimizer.Windows.Power;

/// <summary>
/// Power plan and power-mode (overlay) management through PowrProf P/Invoke — a structured API,
/// not localized command output. Overlay calls degrade gracefully to NotSupported on builds or
/// hardware where they are unavailable.
/// </summary>
public sealed class WindowsPowerManager : IPowerManager
{
    private static readonly Guid BalancedPlan = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid HighPerformancePlan = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid PowerSaverPlan = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    private static readonly Guid UltimatePlan = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    private static readonly Guid OverlayBetterBattery = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid OverlayBalanced = new("ded574b5-45a0-4f42-8737-46345c09c238");
    private static readonly Guid OverlayBestPerformance = new("3af9b8d9-7a97-11ea-aef0-00224f01ad0c");

    public bool IsOverlaySupported { get; } = DetectOverlaySupport();

    public IReadOnlyList<PowerPlanInfo> GetPowerPlans()
    {
        var active = SafeActiveGuid();
        var plans = new List<PowerPlanInfo>();
        uint index = 0;
        uint size = 16;
        var buffer = new byte[16];

        while (true)
        {
            Guid guid = Guid.Empty;
            size = 16;
            var status = NativeMethods.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                NativeMethods.ACCESS_SCHEME, index, ref guid, ref size);
            if (status != 0) break; // ERROR_NO_MORE_ITEMS (259) ends enumeration
            plans.Add(new PowerPlanInfo
            {
                PlanGuid = guid.ToString("D"),
                Name = ReadFriendlyName(guid),
                IsActive = guid == active,
                WellKnownKind = ClassifyPlan(guid),
            });
            index++;
        }
        return plans;
    }

    public PowerPlanInfo? GetActivePlan()
    {
        if (NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0 || ptr == IntPtr.Zero)
            return null;
        try
        {
            var guid = Marshal.PtrToStructure<Guid>(ptr);
            return new PowerPlanInfo
            {
                PlanGuid = guid.ToString("D"),
                Name = ReadFriendlyName(guid),
                IsActive = true,
                WellKnownKind = ClassifyPlan(guid),
            };
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public void SetActivePlan(string planGuid)
    {
        if (!Guid.TryParse(planGuid, out var guid))
            throw new ArgumentException($"'{planGuid}' is not a valid power plan GUID.");
        var err = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref guid);
        if (err != 0)
            throw new Win32Exception((int)err, $"PowerSetActiveScheme failed (error {err}).");
    }

    public string DuplicateActivePlan(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName.Length > 64)
            throw new ArgumentException("Plan name must be 1..64 characters.");
        // Only safe characters allowed in the friendly name we set later.
        if (newName.Any(c => char.IsControl(c)))
            throw new ArgumentException("Plan name contains invalid characters.");

        var source = SafeActiveGuid();
        if (source == Guid.Empty)
            throw new InvalidOperationException("No active power scheme detected.");

        var newGuid = Guid.NewGuid();
        // Duplicate from the active scheme: root pointer must be NULL per API contract.
        var err = NativeMethods.PowerDuplicateScheme(IntPtr.Zero, ref newGuid);
        if (err != 0)
            throw new Win32Exception((int)err, $"PowerDuplicateScheme failed (error {err}).");

        WriteFriendlyName(newGuid, newName);
        return newGuid.ToString("D");
    }

    public void DeletePlan(string planGuid)
    {
        if (!Guid.TryParse(planGuid, out var guid))
            throw new ArgumentException("Invalid plan GUID.");
        var active = SafeActiveGuid();
        if (guid == active)
            throw new InvalidOperationException("The active plan cannot be deleted.");
        var err = NativeMethods.PowerDeleteScheme(IntPtr.Zero, ref guid);
        if (err != 0 && err != 2) // tolerate already-missing
            throw new Win32Exception((int)err, $"PowerDeleteScheme failed (error {err}).");
    }

    public BatteryInfo GetBatteryInfo()
    {
        NativeMethods.SYSTEM_POWER_STATUS ps;
        if (!NativeMethods.GetSystemPowerStatus(out ps) || ps.BatteryFlag == 128)
            return new BatteryInfo { IsPresent = false };

        return new BatteryInfo
        {
            IsPresent = true,
            ChargePercent = ps.BatteryLifePercent <= 100 ? ps.BatteryLifePercent : 0,
            IsCharging = (ps.BatteryFlag & 8) != 0,
            PowerSource = ps.ACLineStatus == 1 ? PowerSource.AcPower : PowerSource.Battery,
            EstimatedRuntimeMinutes =
                ps.BatteryLifeTime is not 0xFFFFFFFF and > 0 ? (int)(ps.BatteryLifeTime / 60) : null,
        };
    }

    public PowerOverlayMode GetEffectiveOverlay()
    {
        if (!IsOverlaySupported) return PowerOverlayMode.NotSupported;
        var err = NativeMethods.PowerGetEffectiveOverlay(IntPtr.Zero, out var overlay);
        if (err != 0) return PowerOverlayMode.NotSupported;
        if (overlay == OverlayBetterBattery) return PowerOverlayMode.BetterBattery;
        if (overlay == OverlayBestPerformance) return PowerOverlayMode.BestPerformance;
        return PowerOverlayMode.Balanced;
    }

    public void SetOverlay(PowerOverlayMode mode)
    {
        if (!IsOverlaySupported)
            throw new PlatformNotSupportedException("Power mode overlays are not supported on this system.");
        var overlay = mode switch
        {
            PowerOverlayMode.BetterBattery => OverlayBetterBattery,
            PowerOverlayMode.BestPerformance => OverlayBestPerformance,
            PowerOverlayMode.Balanced => OverlayBalanced,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        var g = overlay;
        var err = NativeMethods.PowerSetActiveOverlay(IntPtr.Zero, ref g);
        if (err != 0)
            throw new Win32Exception((int)err, $"PowerSetActiveOverlay failed (error {err}).");
    }

    private static bool DetectOverlaySupport()
    {
        try
        {
            return NativeMethods.PowerGetEffectiveOverlay(IntPtr.Zero, out _) == 0;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    private static Guid SafeActiveGuid()
    {
        if (NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0 || ptr == IntPtr.Zero)
            return Guid.Empty;
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    private static string ReadFriendlyName(Guid planGuid)
    {
        uint size = 0;
        _ = NativeMethods.PowerReadFriendlyName(IntPtr.Zero, ref planGuid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
        if (size == 0) return "(unknown plan)";
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            var err = NativeMethods.PowerReadFriendlyName(IntPtr.Zero, ref planGuid, IntPtr.Zero, IntPtr.Zero, buffer, ref size);
            if (err != 0) return "(unknown plan)";
            return Marshal.PtrToStringUni(buffer)?.TrimEnd('\0') ?? "(unknown plan)";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Names are written via the registry-backed API so no shell tooling is involved.</summary>
    private static void WriteFriendlyName(Guid planGuid, string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            $@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\{planGuid:D}", writable: true);
        key?.SetValue("FriendlyName", name, RegistryValueKind.String);
    }

    private static WellKnownPowerPlan ClassifyPlan(Guid guid) =>
        guid switch
        {
            var g when g == BalancedPlan => WellKnownPowerPlan.Balanced,
            var g when g == HighPerformancePlan => WellKnownPowerPlan.HighPerformance,
            var g when g == PowerSaverPlan => WellKnownPowerPlan.PowerSaver,
            var g when g == UltimatePlan => WellKnownPowerPlan.UltimatePerformance,
            _ => WellKnownPowerPlan.Other,
        };
}
