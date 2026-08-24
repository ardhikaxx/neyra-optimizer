using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Snapshots;
using NeyraOptimizer.Infrastructure.CrashRecovery;
using NeyraOptimizer.Infrastructure.Persistence;
using NeyraOptimizer.Infrastructure.Logging;
using NeyraOptimizer.Optimization.Restore;

namespace NeyraOptimizer.Application.Recovery;

public sealed record PendingRecoveryInfo(Guid OperationId, string Description, string Phase, Guid? SnapshotId, int Completed, int Total);

/// <summary>
/// Detects an interrupted optimization batch from the previous run and offers rollback.
/// NEVER re-applies changes automatically â€” the user decides between rollback or dismiss.
/// </summary>
public interface ICrashRecoveryService
{
    PendingRecoveryInfo? DetectPendingOperation();
    OptimizationSnapshot? LoadSnapshotFor(PendingRecoveryInfo info);
    Task<RestoreResult> RollbackAsync(OptimizationSnapshot snapshot, IProgress<(int current, int total, string item)>? progress, CancellationToken ct);
    void Dismiss(PendingRecoveryInfo info);
}

public sealed class CrashRecoveryService : ICrashRecoveryService
{
    private readonly IPendingOperationTracker _tracker;
    private readonly ISnapshotRepository _snapshots;
    private readonly IRestoreEngine _restoreEngine;
    private readonly OperationLock _lock;
    private readonly INeyraLogger _logger;

    public CrashRecoveryService(
        IPendingOperationTracker tracker,
        ISnapshotRepository snapshots,
        IRestoreEngine restoreEngine,
        OperationLock lockObj,
        INeyraLogger logger)
    {
        _tracker = tracker;
        _snapshots = snapshots;
        _restoreEngine = restoreEngine;
        _lock = lockObj;
        _logger = logger;
    }

    public PendingRecoveryInfo? DetectPendingOperation()
    {
        var pending = _tracker.ReadPending();
        if (pending is null) return null;
        return new PendingRecoveryInfo(pending.OperationId, pending.Description, pending.Phase,
            pending.SnapshotId, pending.CompletedChanges, pending.TotalChanges);
    }

    public OptimizationSnapshot? LoadSnapshotFor(PendingRecoveryInfo info)
    {
        if (info.SnapshotId is not Guid id) return null;
        var snap = _snapshots.Load(id);
        if (snap is null)
            _logger.Warning("Recovery", "LoadSnapshot", "Snapshot operasi terputus tidak ditemukan atau corrupt.");
        return snap;
    }

    public async Task<RestoreResult> RollbackAsync(
        OptimizationSnapshot snapshot,
        IProgress<(int current, int total, string item)>? progress,
        CancellationToken ct)
    {
        using var _ = await _lock.AcquireAsync("Rollback operasi terputus", ct).ConfigureAwait(false);
        return await _restoreEngine.RestoreSnapshotAsync(snapshot, progress, ct).ConfigureAwait(false);
    }

    public void Dismiss(PendingRecoveryInfo info)
    {
        _logger.Info("Recovery", "Dismiss", $"Pengguna menutup recovery untuk operasi {info.OperationId:N} tanpa rollback.");
        _tracker.Clear();
    }
}
