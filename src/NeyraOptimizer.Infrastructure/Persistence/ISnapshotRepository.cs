using NeyraOptimizer.Domain.Snapshots;

namespace NeyraOptimizer.Infrastructure.Persistence;

public interface ISnapshotRepository
{
    void Save(OptimizationSnapshot snapshot);
    OptimizationSnapshot? Load(Guid id);
    IReadOnlyList<SnapshotSummaryEntry> List();
    bool Delete(Guid id);
}