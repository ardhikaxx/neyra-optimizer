using NeyraOptimizer.Domain.Snapshots;

namespace NeyraOptimizer.Infrastructure.Persistence;

public interface IHistoryRepository
{
    void Save(HistoryRecord record);
    IReadOnlyList<HistoryRecord> LoadAll();
    HistoryRecord? FindBySnapshot(Guid snapshotId);
}