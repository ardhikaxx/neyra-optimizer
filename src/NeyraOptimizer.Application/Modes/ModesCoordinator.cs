using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Domain.Rules;
using NeyraOptimizer.Infrastructure.Logging;
using NeyraOptimizer.Optimization.Modes;
using NeyraOptimizer.Optimization.Pipeline;
using ModePlan = NeyraOptimizer.Optimization.Modes.ModePlanBuilder.ModePlan;

namespace NeyraOptimizer.Application.Modes;

public sealed record ModeStatus(string ModeName, bool IsActive, string DetailText);

/// <summary>
/// Builds and applies the special usage modes. Mode application always goes through the full
/// operation pipeline (snapshot + safety + optional restore point) so every mode change is
/// reversible from the Restore Center.
/// </summary>
public interface IModesCoordinator
{
    ModePlan BuildPlan(string modeName, AnalysisBundle bundle);
    IReadOnlyList<ModeStatus> GetModeStatuses(AnalysisBundle bundle);
}

public sealed class ModesCoordinator : IModesCoordinator
{
    private readonly SessionState _session;
    private readonly INeyraLogger _logger;

    public ModesCoordinator(SessionState session, INeyraLogger logger)
    {
        _session = session;
        _logger = logger;
    }

    public ModePlan BuildPlan(string modeName, AnalysisBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return modeName switch
        {
            "Safe Windows" => ModePlanBuilder.BuildSafeWindows(bundle),
            "Low-End" => ModePlanBuilder.BuildLowEnd(bundle),
            "Office" => ModePlanBuilder.BuildOffice(bundle),
            "Gaming" => ModePlanBuilder.BuildGaming(bundle),
            "Battery Saver" => ModePlanBuilder.BuildBatterySaver(bundle),
            "Balanced" or _ => ModePlanBuilder.BuildBalanced(bundle),
        };
    }

    public IReadOnlyList<ModeStatus> GetModeStatuses(AnalysisBundle bundle)
    {
        var battery = bundle.Profile.Battery;
        bool plugged = !battery.IsPresent || battery.PowerSource == PowerSource.AcPower;

        var list = new List<ModeStatus>
        {
            new("Safe Windows", false, "Hanya aturan kategori paling aman."),
            new("Balanced", true, "Keseimbangan default untuk penggunaan umum."),
            new("Low-End", bundle.Profile.DeviceClass is DeviceClass.LowEnd or DeviceClass.EntryLevel,
                bundle.Profile.DeviceClass is DeviceClass.LowEnd or DeviceClass.EntryLevel
                    ? "Perangkat terklasifikasi low-end/entry-level."
                    : "Rekomendasi untuk perangkat spesifikasi rendah."),
            new("Office", false, plugged ? "Stabilitas aplikasi produktivitas diprioritaskan." : "Stabilitas aplikasi produktivitas; printer & cloud tetap aman."),
            new("Gaming", false,
                !bundle.Profile.HasDedicatedGpu
                    ? "GPU dedicated tidak terdeteksi â€” peningkatan performa mungkin terbatas."
                    : plugged ? "Siap: perangkat terhubung daya." : "Butuh charger terpasang."),
            new("Battery Saver", !battery.IsPresent ? false : (!plugged && battery.ChargePercent < 40),
                !battery.IsPresent
                    ? "Baterai tidak terdeteksi."
                    : plugged ? "Baterai tidak aktif (terhubung AC)." : $"Baterai {battery.ChargePercent}%."),
        };
        return list;
    }
}
