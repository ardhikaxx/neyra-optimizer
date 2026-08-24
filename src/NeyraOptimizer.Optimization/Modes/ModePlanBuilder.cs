using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Engines;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Domain.Rules;

namespace NeyraOptimizer.Optimization.Modes;

/// <summary>
/// Composes recommendation batches for the special modes (Low-End, Office, Gaming, Battery Saver,
/// Balanced, Safe Windows). Modes never disable security, audio, networking, printer, Bluetooth or
/// update components: the Safety Engine additionally blocks anything on the protected list.
/// </summary>
public static class ModePlanBuilder
{
    /// <summary>Canonical visual-effect keys shared with the Windows integration layer.</summary>
    public static readonly IReadOnlyList<string> EffectKeys = new[]
    {
        "MinAnimate", "DragFullWindows", "TaskbarAnimations", "ListviewAlphaSelect",
        "ListviewShadow", "IconsOnly", "EnableAeroPeek", "EnableTransparency",
    };

    public sealed record ModePlan(
        string Name,
        string Description,
        IReadOnlyList<Recommendation> Recommendations,
        bool RequiresPluggedIn,
        string? UnavailabilityReason)
    {
        public bool IsAvailable => UnavailabilityReason is null;
    }

    public static ModePlan BuildSafeWindows(AnalysisBundle bundle) =>
        BuildFromCatalog(bundle, UsageProfileKind.Balanced, "Safe Windows",
            "Hanya aturan kategori Safe dari katalog resmi. Tidak mengubah mode power atau efek visual.",
            maxCategory: RecommendationCategory.Safe, includeVisuals: false);

    public static ModePlan BuildBalanced(AnalysisBundle bundle) =>
        BuildFromCatalog(bundle, UsageProfileKind.Balanced, "Balanced",
            "Aturan Safe dan Recommended sesuai kondisi perangkat tanpa penurunan kenyamanan visual yang agresif.",
            maxCategory: RecommendationCategory.Recommended, includeVisuals: false);

    public static ModePlan BuildLowEnd(AnalysisBundle bundle)
    {
        var recs = new List<Recommendation>(BuildFromCatalog(
            bundle, UsageProfileKind.LowEnd, "Low-End",
            string.Empty, RecommendationCategory.Recommended, includeVisuals: true).Recommendations);
        recs.AddRange(VisualEffectsForPerformance());
        recs.AddRange(DisableNonProtectedStartup(bundle));
        return Finalize("Low-End",
            "Paket hemat resource untuk perangkat spesifikasi rendah: telemetri minimal, efek visual performa, startup dirampingkan.",
            recs);
    }

    public static ModePlan BuildOffice(AnalysisBundle bundle)
    {
        // Office prioritizes stability: no aggressive visuals; only safe catalog rules.
        return BuildFromCatalog(bundle, UsageProfileKind.Office, "Office",
            "Menjaga stabilitas Office, browser, PDF, dan printer. Tidak menerapkan optimasi gaming.",
            RecommendationCategory.Recommended, includeVisuals: false);
    }

    public static ModePlan BuildGaming(AnalysisBundle bundle)
    {
        var battery = bundle.Profile.Battery;
        bool pluggedIn = !battery.IsPresent || battery.PowerSource == PowerSource.AcPower;
        if (bundle.Profile.Battery.IsPresent && !pluggedIn)
        {
            return new ModePlan("Gaming", string.Empty, Array.Empty<Recommendation>(),
                RequiresPluggedIn: true,
                UnavailabilityReason: "Laptop sedang menggunakan baterai. Colokkan charger untuk mengaktifkan Gaming Mode agar tidak menurunkan performa dan merusak siklus baterai.");
        }
        if (!bundle.Profile.HasDedicatedGpu && bundle.Profile.Memory.TotalPhysicalMb < 6144)
        {
            // Still allowed, but the UI should show expectations honestly.
        }

        var recs = new List<Recommendation>(BuildFromCatalog(
            bundle, UsageProfileKind.Gaming, "Gaming",
            string.Empty, RecommendationCategory.Recommended, includeVisuals: true).Recommendations);

        recs.Add(PowerOverlayRec("Best performance saat bermain game", "BestPerformance"));
        recs.Add(GameModeRec());

        return Finalize("Gaming",
            "Mengurangi beban background dan memprioritaskan game. Antivirus, jaringan, audio, driver, dan Windows Update TIDAK disentuh." +
            (bundle.Profile.HasDedicatedGpu ? "" : " Catatan: GPU dedicated tidak terdeteksi; peningkatan FPS mungkin terbatas."),
            recs);
    }

    public static ModePlan BuildBatterySaver(AnalysisBundle bundle)
    {
        if (!bundle.Profile.Battery.IsPresent)
        {
            return new ModePlan("Battery Saver", string.Empty, Array.Empty<Recommendation>(),
                RequiresPluggedIn: false,
                UnavailabilityReason: "Baterai tidak terdeteksi pada perangkat ini.");
        }

        var recs = new List<Recommendation>(BuildFromCatalog(
            bundle, UsageProfileKind.BatterySaver, "Battery Saver",
            string.Empty, RecommendationCategory.Recommended, includeVisuals: false).Recommendations);

        recs.Add(PowerOverlayRec("Hemat daya saat menggunakan baterai", "BetterBattery"));
        recs.AddRange(DisableNonProtectedStartup(bundle));

        return Finalize("Battery Saver",
            $"Mengurangi aktivitas background tanpa mematikan fungsi hardware (baterai {bundle.Profile.Battery.ChargePercent}%).",
            recs);
    }

    // ────────────────────────────────────────────── helpers

    private static ModePlan BuildFromCatalog(
        AnalysisBundle bundle, UsageProfileKind kind, string name, string description,
        RecommendationCategory maxCategory, bool includeVisuals)
    {
        var engine = new RecommendationEngine();
        var catalog = Catalog.RulesCatalog.GetAllRules();
        var baseRecs = engine.BuildRecommendations(bundle, catalog, kind, advancedModeEnabled: false);

        var selected = baseRecs
            .Where(r => r.Category <= maxCategory && r.RiskLevel <= RiskLevel.Low)
            .ToList();

        foreach (var r in selected) r.IsSelected = true;

        if (includeVisuals)
            selected.AddRange(VisualEffectsForPerformance());

        return Finalize(name, description, selected);
    }

    private static ModePlan Finalize(string name, string description, List<Recommendation> recs) =>
        new(name, description, recs.DistinctBy(r => r.RuleId + "|" + r.TargetId).ToList(),
            RequiresPluggedIn: false, UnavailabilityReason: null);

    /// <summary>Per-item recommendations for enabled, non-protected startup entries.</summary>
    public static IEnumerable<Recommendation> DisableNonProtectedStartup(AnalysisBundle bundle)
    {
        foreach (var entry in bundle.StartupEntries.Where(e => e.IsEnabled && !e.IsProtected))
        {
            yield return new Recommendation
            {
                RuleId = "startup_disable_" + entry.Id,
                Title = $"Nonaktifkan startup: {entry.Name}",
                Description = "Menonaktifkan aplikasi agar tidak berjalan otomatis saat Windows menyala. Aplikasi tetap bisa dibuka manual.",
                Reason = $"'{entry.Name}' aktif saat startup ({entry.SourceDisplay}).",
                EstimatedImpact = "Mengurangi RAM/CPU/disk pada 2–3 menit pertama setelah boot.",
                Category = RecommendationCategory.Safe,
                RiskLevel = RiskLevel.Safe,
                RequiresAdministrator = entry.Source is StartupSource.RunKeyLocalMachine or StartupSource.RunKeyLocalMachineWow64 or StartupSource.StartupFolderCommon,
                AffectedComponents = new[] { entry.Name },
                RollbackDescription = "Entry dapat diaktifkan kembali dari halaman Startup.",
                IsSelected = true,
                TargetId = entry.Id,
                CurrentStateText = "Enabled",
                ProposedStateText = "Disabled",
                Area = RuleArea.Startup,
            };
        }
    }

    private static IEnumerable<Recommendation> VisualEffectsForPerformance()
    {
        foreach (var key in new[] { "MinAnimate", "TaskbarAnimations", "MenuAnimation" })
        {
            yield return new Recommendation
            {
                RuleId = "mode_visual_" + key,
                Title = "Efek visual: matikan " + key,
                Description = "Mengurangi animasi shell untuk responsivitas lebih baik pada GPU terbatas.",
                Reason = "Bagian dari preset performa mode ini.",
                EstimatedImpact = "Dampak kecil namun nyata pada perangkat integrated graphics.",
                Category = RecommendationCategory.Safe,
                RiskLevel = RiskLevel.Safe,
                AffectedComponents = new[] { "Explorer / DWM" },
                RollbackDescription = "Aktifkan kembali di halaman Visual Effects.",
                IsSelected = true,
                TargetId = key,
                CurrentStateText = "Enabled",
                ProposedStateText = "Disabled",
                Area = RuleArea.VisualEffects,
            };
        }
    }

    private static Recommendation PowerOverlayRec(string reason, string overlay) => new()
    {
        RuleId = "mode_power_overlay",
        Title = "Power mode: " + overlay,
        Description = "Mengubah power mode Windows (overlay) sesuai skenario mode ini. Dapat dikembalikan kapan saja.",
        Reason = reason,
        EstimatedImpact = "Trade-off antara performa dan konsumsi daya/panas.",
        Category = RecommendationCategory.Recommended,
        RiskLevel = RiskLevel.Safe,
        AffectedComponents = new[] { "Power Overlay" },
        RollbackDescription = "Kembalikan lewat halaman Power & Performance.",
        IsSelected = true,
        TargetId = "EffectiveOverlay",
        CurrentStateText = "Balanced",
        ProposedStateText = overlay,
        Area = RuleArea.Power,
    };

    private static Recommendation GameModeRec() => new()
    {
        RuleId = "power_game_mode",
        Title = "Aktifkan Windows Game Mode",
        Description = "Windows memprioritaskan CPU/GPU untuk game dan menunda beberapa aktivitas background.",
        Reason = "Fitur resmi Windows; aman dan reversible.",
        EstimatedImpact = "Framerate lebih stabil pada sebagian game.",
        Category = RecommendationCategory.Recommended,
        RiskLevel = RiskLevel.Safe,
        AffectedComponents = new[] { @"HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled" },
        RollbackDescription = "Ubah nilai registri kembali ke 0.",
        IsSelected = true,
        TargetId = @"HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled",
        CurrentStateText = "0",
        ProposedStateText = "1",
        Area = RuleArea.Power,
    };
}
