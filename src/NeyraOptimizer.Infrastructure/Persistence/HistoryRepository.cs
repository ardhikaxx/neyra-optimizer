using System.Text.Json;
using NeyraOptimizer.Domain.Snapshots;
using NeyraOptimizer.Infrastructure.IO;

namespace NeyraOptimizer.Infrastructure.Persistence;

public sealed class HistoryRepository : JsonFileRepositoryBase
{
    private readonly string _root;
    private const int MaxRecords = 500; // prune oldest to keep storage bounded

    public HistoryRepository(string? rootOverride = null)
    {
        _root = rootOverride ?? AppPaths.History;
        Directory.CreateDirectory(_root);
    }

    public void Save(HistoryRecord record)
    {
        var path = Path.Combine(_root, $"history-{record.Id:N}.json");
        WriteAtomic(path, JsonSerializer.Serialize(record, Serializer), withIntegrityManifest: true);
        PruneIfNeeded();
    }

    public IReadOnlyList<HistoryRecord> LoadAll()
    {
        var list = new List<HistoryRecord>();
        if (!Directory.Exists(_root)) return list;

        foreach (var file in Directory.EnumerateFiles(_root, "history-*.json"))
        {
            try
            {
                var rec = JsonSerializer.Deserialize<HistoryRecord>(File.ReadAllText(file), Serializer);
                if (rec is not null) list.Add(rec);
            }
            catch (JsonException) { /* skip corrupt entry, keep the rest */ }
            catch (IOException) { }
        }
        return list.OrderByDescending(r => r.StartedUtc).ToList();
    }

    public HistoryRecord? FindBySnapshot(Guid snapshotId) =>
        LoadAll().FirstOrDefault(r => r.SnapshotId == snapshotId);

    private void PruneIfNeeded()
    {
        var files = Directory.GetFiles(_root, "history-*.json")
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .ToList();
        for (var i = MaxRecords; i < files.Count; i++)
        {
            try { File.Delete(files[i]); } catch (IOException) { }
        }
    }
}
