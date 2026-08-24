using System.Text.Json;
using NeyraOptimizer.Infrastructure.Json;
using NeyraOptimizer.Domain.Snapshots;
using NeyraOptimizer.Infrastructure.IO;

namespace NeyraOptimizer.Infrastructure.CrashRecovery;

/// <summary>
/// Tracks in-flight optimization batches. The pending-operation file is written before any system
/// change and removed only after the batch commits. If the app crashes mid-batch the next launch
/// detects the file, warns the user and offers rollback from the linked snapshot — never an
/// automatic re-run.
/// </summary>
public sealed class PendingOperationTracker
{
    private readonly string _path;

    public PendingOperationTracker(string? pathOverride = null)
    {
        _path = pathOverride ?? AppPaths.PendingOperationFile;
    }

    public void Begin(PendingOperationRecord record)
    {
        AppPaths.EnsureDirectories();
        Write(record);
    }

    public void UpdatePhase(Guid operationId, string phase, int completedChanges)
    {
        var current = ReadCurrent();
        if (current is null || current.OperationId != operationId) return;
        current.Phase = phase;
        current.CompletedChanges = completedChanges;
        Write(current);
    }

    /// <summary>Called ONLY when a batch completes successfully or the user dismisses recovery.</summary>
    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public PendingOperationRecord? ReadPending() => ReadCurrent();

    private PendingOperationRecord? ReadCurrent()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<PendingOperationRecord>(File.ReadAllText(_path), JsonOptions.Default);
        }
        catch (JsonException)
        {
            // Corrupt marker: treat as no pending op but leave the file for inspection? Safer to remove it:
            // a corrupt marker cannot be trusted as evidence of an interrupted batch.
            try { File.Delete(_path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return null;
        }
    }

    private void Write(PendingOperationRecord record)
    {
        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(record, JsonOptions.Default));
        File.Move(tmp, _path, overwrite: true);
    }
}
