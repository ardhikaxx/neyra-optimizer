using NeyraOptimizer.Application.Measurement;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Diagnostics.Measurement;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Domain.Rules;
using NeyraOptimizer.Infrastructure.Logging;
using NeyraOptimizer.Optimization.Modes;
using NeyraOptimizer.Optimization.Pipeline;
using ModePlan = NeyraOptimizer.Optimization.Modes.ModePlanBuilder.ModePlan;

namespace NeyraOptimizer.Application.Optimization;

/// <summary>
/// Use-case facade over the operation pipeline. Enforces the OperationLock, read-only mode,
/// restore-point consent semantics and honest before/after measurement.
/// </summary>
public interface IOptimizationCoordinator
{
    OptimizationPreview Preview(IReadOnlyList<Recommendation> selected, SystemProfile profile);
    Task<OptimizationExecutionResult> ExecuteSelectedAsync(
        IReadOnlyList<Recommendation> selected,
        SystemProfile profile,
        UsageProfileKind? usageProfile,
        bool createRestorePoint,
        IProgress<(int current, int total, string step)>? progress,
        CancellationToken ct);
    Task<OptimizationExecutionResult> ExecuteModePlanAsync(ModePlan plan, SystemProfile profile, IProgress<(int current, int total, string step)>? progress, CancellationToken ct);
    IReadOnlyList<MetricComparison> MeasureAfterAsync(int sampleSeconds, CancellationToken ct);
}

public sealed class OptimizationCoordinator : IOptimizationCoordinator
{
    private readonly IOptimizationPipeline _pipeline;
    private readonly OperationLock _lock;
    private readonly SessionState _session;
    private readonly IBaselineMeasurementService _measurement;
    private readonly MeasurementStore _measurements;
    private readonly INeyraLogger _logger;

    public OptimizationCoordinator(
        IOptimizationPipeline pipeline,
        OperationLock lockObj,
        SessionState session,
        IBaselineMeasurementService measurement,
        MeasurementStore measurements,
        INeyraLogger logger)
    {
        _pipeline = pipeline;
        _lock = lockObj;
        _session = session;
        _measurement = measurement;
        _measurements = measurements;
        _logger = logger;
    }

    public OptimizationPreview Preview(IReadOnlyList<Recommendation> selected, SystemProfile profile) =>
        _pipeline.CreatePreview(selected, profile);

    public async Task<OptimizationExecutionResult> ExecuteSelectedAsync(
        IReadOnlyList<Recommendation> selected,
        SystemProfile profile,
        UsageProfileKind? usageProfile,
        bool createRestorePoint,
        IProgress<(int current, int total, string step)>? progress,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(nameof(profile));
        if (!_session.CanModifySystem)
            throw new InvalidOperationException("Aplikasi berjalan dalam mode baca-saja atau pengguna belum memberi persetujuan perubahan.");

        using var _ = await _lock.AcquireAsync("Optimasi sistem", ct).ConfigureAwait(false);
        _measurements.MarkAwaitingAfter();

        try
        {
            return await _pipeline.ExecuteAsync(selected, profile, createRestorePoint, usageProfile, progress, ct)
                .ConfigureAwait(false);
        }
        catch (RestorePointFailedException)
        {
            // Let the VM ask the user; do not mark awaiting-after for an aborted batch.
            _measurements.ClearAwaitingAfter();
            throw;
        }
    }

    public Task<OptimizationExecutionResult> ExecuteModePlanAsync(
        ModePlan plan, SystemProfile profile,
        IProgress<(int current, int total, string step)>? progress, CancellationToken ct) =>
        ExecuteSelectedAsync(
            plan.Recommendations.Where(r => r.IsSelected).ToList(),
            profile,
            MapModeNameToProfile(plan.Name),
            createRestorePoint: true,
            progress, ct);

    /// <summary>
    /// Captures the 'after' measurement and returns an honest comparison with the stored baseline.
    /// When nothing improved the caller must present that outcome as-is.
    /// </summary>
    public IReadOnlyList<MetricComparison> MeasureAfterAsync(int sampleSeconds, CancellationToken ct)
    {
        var before = _measurements.LoadBaseline();
        if (before is null)
        {
            _logger.Warning("Coordinator", "MeasureAfter", "Tidak ada baseline tersimpan; perbandingan dilewati.");
            return Array.Empty<MetricComparison>();
        }

        var after = _measurement.CaptureSnapshotAsync(sampleSeconds, ct).GetAwaiter().GetResult();
        _measurements.SaveAfter(after);
        _measurements.ClearAwaitingAfter();
        return _measurement.Compare(before, after);
    }

    private static UsageProfileKind? MapModeNameToProfile(string modeName) => modeName switch
    {
        "Low-End" => UsageProfileKind.LowEnd,
        "Office" => UsageProfileKind.Office,
        "Gaming" => UsageProfileKind.Gaming,
        "Battery Saver" => UsageProfileKind.BatterySaver,
        "Safe Windows" or "Balanced" or _ => UsageProfileKind.Balanced,
    };
}
