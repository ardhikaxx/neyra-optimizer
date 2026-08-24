using NeyraOptimizer.Domain.Snapshots;

namespace NeyraOptimizer.Infrastructure.CrashRecovery;

public interface IPendingOperationTracker
{
    void Begin(PendingOperationRecord record);
    void UpdatePhase(Guid operationId, string phase, int completedChanges);
    void Clear();
    PendingOperationRecord? ReadPending();
}