using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Domain.Rules;

namespace NeyraOptimizer.Domain.Engines;

/// <summary>
/// Transparent performance score built only from measurable indicators. Every component lists its
/// weight and earned points so the UI/report can explain exactly how the number was produced.
/// No random or invented values are involved.
/// </summary>
public static class PerformanceScoreCalculator
{
    public const string BandExcellent = "Excellent";
    public const string BandGood = "Good";
    public const string BandNeedsAttention = "Needs Attention";
    public const string BandCritical = "Critical";

    public static PerformanceScoreResult Compute(PerformanceSnapshot snap)
    {
        var components = new List<ScoreComponent>();

        // 1. Available memory (weight 25)
        double availRatio = snap.TotalRamMb > 0 ? (double)snap.AvailableRamMb / snap.TotalRamMb : 0;
        double memPts = availRatio switch
        {
            >= 0.55 => 25,
            >= 0.40 => 21,
            >= 0.30 => 16,
            >= 0.20 => 10,
            >= 0.12 => 5,
            _ => 1,
        };
        components.Add(new ScoreComponent(
            "memory",
            "Memory availability",
            0.25,
            memPts,
            25,
            $"{snap.AvailableRamMb:N0} MB of {snap.TotalRamMb:N0} MB available ({availRatio * 100:0}% free)."));

        // 2. Commit charge (weight 10)
        double commitRatio = snap.CommitLimitMb > 0 ? (double)snap.CommitUsedMb / snap.CommitLimitMb : 0;
        double commitPts = commitRatio switch { < 0.55 => 10, < 0.70 => 7, < 0.85 => 4, _ => 1 };
        components.Add(new ScoreComponent(
            "commit",
            "Commit pressure",
            0.10,
            commitPts,
            10,
            $"Commit {snap.CommitUsedMb:N0}/{snap.CommitLimitMb:N0} MB ({commitRatio * 100:0}%)."));

        // 3. CPU load at measurement time (weight 15)
        double cpuPts = snap.CpuLoadPercent switch
        {
            null => 8, // unknown: neutral mid credit, honestly labeled
            <= 8 => 15,
            <= 20 => 12,
            <= 40 => 7,
            <= 65 => 3,
            _ => 1,
        };
        components.Add(new ScoreComponent(
            "cpu",
            "CPU load during measurement",
            0.15,
            cpuPts,
            15,
            snap.CpuLoadPercent is null
                ? "CPU counter unavailable on this machine; neutral points awarded."
                : $"Average load {snap.CpuLoadPercent:0}% over the sample window."));

        // 4. Startup footprint (weight 15)
        double startPts = snap.StartupEntriesEnabled switch
        {
            <= 5 => 15,
            <= 9 => 12,
            <= 13 => 9,
            <= 18 => 5,
            _ => 2,
        };
        components.Add(new ScoreComponent(
            "startup",
            "Startup footprint",
            0.15,
            startPts,
            15,
            $"{snap.StartupEntriesEnabled} enabled startup entries."));

        // 5. Auto-start services running (weight 10)
        double svcPts = snap.AutoStartServicesRunning switch
        {
            <= 110 => 10,
            <= 140 => 8,
            <= 170 => 5,
            <= 200 => 3,
            _ => 1,
        };
        components.Add(new ScoreComponent(
            "services",
            "Auto-start services",
            0.10,
            svcPts,
            10,
            $"{snap.AutoStartServicesRunning} automatic-start services currently running."));

        // 6. System drive free space (weight 10)
        double diskPts = snap.SystemDriveFreeGb switch
        {
            >= 40 => 10,
            >= 20 => 8,
            >= 10 => 5,
            >= 5 => 2,
            _ => 0,
        };
        components.Add(new ScoreComponent(
            "disk",
            "System drive free space",
            0.10,
            diskPts,
            10,
            $"{snap.SystemDriveFreeGb:0.#} GB free on the system drive."));

        // 7. Process count (weight 5)
        double procPts = snap.ProcessCount switch
        {
            <= 120 => 5,
            <= 160 => 4,
            <= 210 => 2,
            _ => 1,
        };
        components.Add(new ScoreComponent(
            "processes",
            "Process count",
            0.05,
            procPts,
            5,
            $"{snap.ProcessCount} processes running."));

        // 8. Disk activity during measurement (weight 10)
        double actPts = snap.DiskActivePercent switch
        {
            null => 5,
            <= 5 => 10,
            <= 15 => 7,
            <= 35 => 3,
            _ => 1,
        };
        components.Add(new ScoreComponent(
            "diskactivity",
            "Disk activity during measurement",
            0.10,
            actPts,
            10,
            snap.DiskActivePercent is null
                ? "Disk performance counters unavailable; neutral points awarded."
                : $"Active time {snap.DiskActivePercent:0}% during sampling."));

        int raw = (int)Math.Round(components.Sum(c => c.EarnedPoints));
        int score = Math.Clamp(raw, 0, 100);
        string band = score switch
        {
            >= 85 => BandExcellent,
            >= 70 => BandGood,
            >= 50 => BandNeedsAttention,
            _ => BandCritical,
        };
        return new PerformanceScoreResult { Score = score, Band = band, Components = components };
    }
}
