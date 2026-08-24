using NeyraOptimizer.Diagnostics.Analyzer;
using NeyraOptimizer.Domain.Engines;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Domain.Rules;
using NeyraOptimizer.Infrastructure.Logging;
using NeyraOptimizer.Optimization.Catalog;
using NeyraOptimizer.Application.Measurement;

namespace NeyraOptimizer.Application.Analysis;

public sealed record FullAnalysisResult(
    AnalysisBundle Bundle,
    PerformanceScoreResult? Score,
    IReadOnlyList<Recommendation> Recommendations);

public interface IAnalysisOrchestrator
{
    Task<FullAnalysisResult> AnalyzeAsync(int baselineSampleSeconds, CancellationToken ct = default);
}

/// <summary>
/// Runs a full local analysis: hardware/OS profile, services, startup, tasks, packages,
/// processes, baseline performance measurement, transparent score and rule-based
/// recommendations. Everything happens locally; nothing is sent anywhere.
/// </summary>
public sealed class AnalysisOrchestrator : IAnalysisOrchestrator
{
    private readonly ISystemAnalyzer _analyzer;
    private readonly IRecommendationEngine _engine;
    private readonly INeyraLogger _logger;
    private readonly MeasurementStore _measurements;

    public AnalysisOrchestrator(
        ISystemAnalyzer analyzer,
        IRecommendationEngine engine,
        INeyraLogger logger,
        MeasurementStore measurements)
    {
        _analyzer = analyzer;
        _engine = engine;
        _logger = logger;
        _measurements = measurements;
    }

    public async Task<FullAnalysisResult> AnalyzeAsync(int baselineSampleSeconds, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var bundle = await _analyzer.AnalyzeFullSystemAsync(baselineSampleSeconds, ct).ConfigureAwait(false);

        PerformanceScoreResult? score = null;
        if (bundle.Baseline is not null)
        {
            score = PerformanceScoreCalculator.Compute(bundle.Baseline);
            _measurements.SaveBaseline(bundle.Baseline);
        }

        var recommendations = bundle.Profile.Windows.BuildNumber >= WindowsIdentityInfo.MinimumSupportedBuild
            ? _engine.BuildRecommendations(bundle, RulesCatalog.GetAllRules(), UsageProfileKind.Balanced, advancedModeEnabled: false)
            : Array.Empty<Recommendation>();

        _logger.Operation(LogSeverity.Info, "Analysis", "FullScan",
            $"Analisis selesai: {bundle.Services.Count} service, {bundle.StartupEntries.Count} startup, {bundle.InstalledApps.Count} aplikasi, {recommendations.Count} rekomendasi.",
            sw.Elapsed);

        return new FullAnalysisResult(bundle, score, recommendations);
    }
}
