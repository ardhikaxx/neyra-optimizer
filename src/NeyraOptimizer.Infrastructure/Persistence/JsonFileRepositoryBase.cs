using System.Text.Json;
using NeyraOptimizer.Security.Integrity;

namespace NeyraOptimizer.Infrastructure.Persistence;

/// <summary>Base repository for JSON files with atomic writes and optional integrity manifests.</summary>
public abstract class JsonFileRepositoryBase
{
    protected static readonly JsonSerializerOptions Serializer = NeyraOptimizer.Infrastructure.Json.JsonOptions.Default;

    /// <summary>Atomic write: temp file in the same directory, then File.Move with overwrite.</summary>
    protected static void WriteAtomic(string path, string content, bool withIntegrityManifest)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.tmp-{Guid.NewGuid():N}");
        if (withIntegrityManifest)
        {
            // Manifest must describe the final bytes; write via helper then move both.
            var tmpManifest = tmp + ".sha256";
            File.WriteAllText(tmp, content);
            File.WriteAllText(tmpManifest, IntegrityUtil.ComputeSha256Utf8(content));
            File.Move(tmp, path, overwrite: true);
            File.Move(tmpManifest, path + ".sha256", overwrite: true);
        }
        else
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
    }

    protected static T? Read<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, Serializer);
        }
        catch (JsonException)
        {
            return null; // corrupt file â†’ caller treats as missing (tolerant read for Emergency Restore)
        }
    }

    /// <summary>Tolerant read used by Emergency Restore: accepts files whose manifest is absent,
    /// but returns integrity status so the UI can warn before restoring unverified data.</summary>
    protected static (T? Item, SnapshotIntegrity Integrity) ReadVerified<T>(string path) where T : class
    {
        var manifestPath = path + ".sha256";
        bool hasManifest = File.Exists(manifestPath);

        T? item;
        try
        {
            if (!File.Exists(path)) return (null, SnapshotIntegrity.Missing);
            item = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Serializer);
        }
        catch (JsonException)
        {
            return (null, SnapshotIntegrity.Corrupt);
        }
        catch (IOException)
        {
            return (null, SnapshotIntegrity.Unreadable);
        }
        catch (UnauthorizedAccessException)
        {
            return (null, SnapshotIntegrity.Unreadable);
        }

        if (item is null) return (null, SnapshotIntegrity.Corrupt);
        if (!hasManifest) return (item, SnapshotIntegrity.NoManifest);
        var expected = File.ReadAllText(manifestPath).Trim();
        var content = File.ReadAllText(path);
        var actualFromDisk = IntegrityUtil.ComputeSha256(path); // hash of on-disk bytes
        var actualFromText = IntegrityUtil.ComputeSha256Utf8(content); // tolerant encoding round-trip
        var verified = string.Equals(expected, actualFromDisk, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(expected, actualFromText, StringComparison.OrdinalIgnoreCase);
        return verified ? (item, SnapshotIntegrity.Verified) : (item, SnapshotIntegrity.Corrupt);
    }
}

public enum SnapshotIntegrity
{
    Verified = 0,
    NoManifest = 1,
    Corrupt = 2,
    Unreadable = 3,
    Missing = 4,
}
