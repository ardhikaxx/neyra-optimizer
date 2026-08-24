using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Domain.Snapshots;
using NeyraOptimizer.Infrastructure.Logging;
using NeyraOptimizer.Infrastructure.Persistence;
using NeyraOptimizer.Optimization.Restore;

namespace NeyraOptimizer.Application.Restore;

/// <summary>Use-case facade for the Restore Center and Emergency Restore.</summary>
public interface IRestoreCenterService
{
    IReadOnlyList<SnapshotSummaryEntry> ListSnapshots();
    OptimizationSnapshot? LoadSnapshot(Guid id);
    Task<RestoreResult> RestoreAsync(OptimizationSnapshot snapshot, IProgress<(int current, int total, string item)>? progress, CancellationToken ct);
    bool DeleteSnapshot(Guid id);
    /// <summary>Restores every change Neyra ever made (used by the uninstall assistant).</summary>
    Task<RestoreResult> RestoreEverythingAsync(IProgress<(int current, int total, string item)>? progress, CancellationToken ct);
}

public sealed class RestoreCenterService : IRestoreCenterService
{
    private readonly ISnapshotRepository _snapshots;
    private readonly IRestoreEngine _restoreEngine;
    private readonly INeyraLogger _logger;

    public RestoreCenterService(
        ISnapshotRepository snapshots,
        IRestoreEngine restoreEngine,
        INeyraLogger logger)
    {
        _snapshots = snapshots;
        _restoreEngine = restoreEngine;
        _logger = logger;
    }

    public IReadOnlyList<SnapshotSummaryEntry> ListSnapshots() => _snapshots.List();

    public OptimizationSnapshot? LoadSnapshot(Guid id)
    {
        var snap = _snapshots.Load(id);
        if (snap is null)
            _logger.Warning("RestoreCenter", "LoadSnapshot", $"Snapshot {id} tidak valid atau gagal integrity check.");
        return snap;
    }

    public async Task<RestoreResult> RestoreAsync(
        OptimizationSnapshot snapshot,
        IProgress<(int current, int total, string item)>? progress,
        CancellationToken ct) =>
        await _restoreEngine.RestoreSnapshotAsync(snapshot, progress, ct).ConfigureAwait(false);

    public bool DeleteSnapshot(Guid id) => _snapshots.Delete(id);

    public async Task<RestoreResult> RestoreEverythingAsync(
        IProgress<(int current, int total, string item)>? progress, CancellationToken ct)
    {
        RestoreResult totalResult = new() { Success = true };
        foreach (var summary in ListSnapshots())
        {
            if (!Guid.TryParse(summary.Id, out var gid)) continue;
            var snap = LoadSnapshot(gid);
            if (snap is null || snap.Status is OperationStatus.RolledBack or OperationStatus.Pending or OperationStatus.Cancelled)
                continue;

            var result = await RestoreAsync(snap, progress, ct).ConfigureAwait(false);
            totalResult = new RestoreResult
            {
                Success = totalResult.Success && result.Success,
                RestoredCount = totalResult.RestoredCount + result.RestoredCount,
                FailedCount = totalResult.FailedCount + result.FailedCount,
                Errors = totalResult.Errors.Concat(result.Errors).ToList(),
                RestartRequired = totalResult.RestartRequired || result.RestartRequired,
            };
        }
        return totalResult;
    }
}
