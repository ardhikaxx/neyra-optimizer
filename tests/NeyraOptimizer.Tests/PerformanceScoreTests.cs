using NeyraOptimizer.Domain.Engines;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.Tests;

public class PerformanceScoreTests
{
    private static PerformanceSnapshot Snap(
        long totalMb = 8192, long availMb = 4096, long commitUsed = 4000, long commitLimit = 16000,
        double? cpu = 5, int processes = 120, int startup = 8, int autoServices = 60)
        => new()
        {
            TotalRamMb = totalMb,
            AvailableRamMb = availMb,
            CommitLimitMb = commitLimit,
            CommitUsedMb = commitUsed,
            CpuLoadPercent = cpu,
            ProcessCount = processes,
            StartupEntriesEnabled = startup,
            AutoStartServicesRunning = autoServices,
            SystemDriveFreeGb = 100,
        };

    [Fact]
    public void HealthySystem_ScoresExcellentOrGood()
    {
        var result = PerformanceScoreCalculator.Compute(Snap(availMb: 6000, cpu: 3, processes: 95, startup: 4));
        Assert.True(result.Score >= 75, $"expected high score, got {result.Score}");
        Assert.NotEqual(PerformanceScoreCalculator.BandCritical, result.Band);
    }

    [Fact]
    public void SaturatedSystem_IsCritical()
    {
        var result = PerformanceScoreCalculator.Compute(Snap(totalMb: 4096, availMb: 300, commitUsed: 7000, commitLimit: 8000, cpu: 90, processes: 220, startup: 20, autoServices: 110));
        Assert.Equal(PerformanceScoreCalculator.BandCritical, result.Band);
    }

    [Fact]
    public void ScoreIsDeterministic_NoRandomness()
    {
        var a = PerformanceScoreCalculator.Compute(Snap());
        var b = PerformanceScoreCalculator.Compute(Snap());
        Assert.Equal(a.Score, b.Score);
    }

    [Fact]
    public void ComponentsExplainTheirWeights()
    {
        var result = PerformanceScoreCalculator.Compute(Snap());
        Assert.NotEmpty(result.Components);
        Assert.All(result.Components, c => Assert.True(c.MaxPoints > 0 && c.Weight > 0));
    }
}
