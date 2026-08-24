using System.Text.Json;
using NeyraOptimizer.Domain.Snapshots;
using NeyraOptimizer.Infrastructure.IO;
using SnapshotIntegrity = NeyraOptimizer.Infrastructure.Persistence.SnapshotIntegrity;

namespace NeyraOptimizer.Infrastructure.Persistence;

/// <summary>
/// Snapshots are stored as one JSON file + SHA-256 manifest per snapshot under ProgramData,
/// deliberately outside any database so the Emergency Restore page can enumerate and validate
/// them even if other application state is lost or corrupt.
/// </summary>
public sealed class SnapshotRepository : JsonFileRepositoryBase, ISnapshotRepository
{
    private readonly string _root;

    public SnapshotRepository(string? rootOverride = null)
    {
        _root = rootOverride ?? AppPaths.Snapshots;
        Directory.CreateDirectory(_root);
    }

    public void Save(OptimizationSnapshot snapshot)
    {
        var path = PathFor(snapshot.Id);
        WriteAtomic(path, JsonSerializer.Serialize(snapshot, Serializer), withIntegrityManifest: true);
    }

    public OptimizationSnapshot? Load(Guid id)
    {
        var (item, integrity) = ReadVerified<OptimizationSnapshot>(PathFor(id));
        return integrity is SnapshotIntegrity.Verified or SnapshotIntegrity.NoManifest ? item : null;
    }

    public IReadOnlyList<SnapshotSummaryEntry> List()
    {
        var result = new List<SnapshotSummaryEntry>();
        if (!Directory.Exists(_root)) return result;

        foreach (var file in Directory.EnumerateFiles(_root, "*.json"))
        {
            if (file.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)) continue;
            var (item, integrity) = ReadVerified<OptimizationSnapshot>(file);
            if (item is null)
            {
                result.Add(new SnapshotSummaryEntry(
                    Path.GetFileNameWithoutExtension(file),
                    File.GetLastWriteTimeUtc(file),
                    string.Empty, 0, 0, false, integrity));
                continue;
            }
            result.Add(new SnapshotSummaryEntry(
                item.Id.ToString(),
                item.CreatedUtc,
                item.Description,
                item.Changes.Count,
                item.AppliedCount,
                string.IsNullOrEmpty(item.RestorePointSequenceNumber) == false,
                integrity));
        }
        return result.OrderByDescending(s => s.CreatedUtc).ToList();
    }

    public bool Delete(Guid id)
    {
        try
        {
            var path = PathFor(id);
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".sha256")) File.Delete(path + ".sha256");
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private string PathFor(Guid id) => Path.Combine(_root, $"snapshot-{id:N}.json");
}

public sealed record SnapshotSummaryEntry(
    string Id,
    DateTime CreatedUtc,
    string Description,
    int ChangeCount,
    int AppliedCount,
    bool HadRestorePoint,
    SnapshotIntegrity Integrity);
