using System.Text.Json;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Infrastructure.IO;

namespace NeyraOptimizer.Application.Measurement;

/// <summary>
/// Persists before/after PerformanceSnapshots as standalone JSON files under ProgramData so
/// measurements survive restarts and can be compared after reboot.
/// </summary>
public sealed class MeasurementStore
{
    private readonly string _root;

    public MeasurementStore(string? rootOverride = null)
    {
        _root = rootOverride ?? AppPaths.Measurements;
        Directory.CreateDirectory(_root);
    }

    public void SaveBaseline(PerformanceSnapshot snapshot)
    {
        Write("baseline.json", snapshot);
        // Keep a rolling history of the last 25 baselines for context.
        AppendHistory(snapshot);
    }

    public void SaveAfter(PerformanceSnapshot snapshot) => Write("after.json", snapshot);

    public PerformanceSnapshot? LoadBaseline() => Read("baseline.json");
    public PerformanceSnapshot? LoadAfter() => Read("after.json");

    /// <summary>True when a baseline exists that has no matching 'after' measurement yet.</summary>
    public bool HasPendingComparison() => LoadBaseline() is not null && File.Exists(Path.Combine(_root, "awaiting-after.flag"));

    public void MarkAwaitingAfter()
    {
        var flag = Path.Combine(_root, "awaiting-after.flag");
        File.WriteAllText(flag, DateTime.UtcNow.ToString("O"));
    }

    public void ClearAwaitingAfter()
    {
        try { File.Delete(Path.Combine(_root, "awaiting-after.flag")); }
        catch (IOException) { }
    }

    public IReadOnlyList<PerformanceSnapshot> RecentBaselines(int max = 10)
    {
        var dir = Path.Combine(_root, "history");
        if (!Directory.Exists(dir)) return Array.Empty<PerformanceSnapshot>();
        return Directory.EnumerateFiles(dir, "*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(max)
            .Select(f => TryRead(f))
            .Where(s => s is not null)
            .Cast<PerformanceSnapshot>()
            .ToList();
    }

    private void AppendHistory(PerformanceSnapshot snapshot)
    {
        var dir = Path.Combine(_root, "history");
        Directory.CreateDirectory(dir);
        Write(Path.Combine(dir, $"baseline-{snapshot.Id:N}.json"), snapshot);

        // Prune beyond 25 files.
        var files = Directory.EnumerateFiles(dir, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).ToList();
        foreach (var old in files.Skip(25))
        {
            try { File.Delete(old); } catch (IOException) { }
        }
    }

    private void Write(string fileName, PerformanceSnapshot snapshot)
    {
        var path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(_root, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonOpts));
    }

    private PerformanceSnapshot? Read(string fileName)
    {
        var path = Path.Combine(_root, fileName);
        return TryRead(path);
    }

    private static PerformanceSnapshot? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<PerformanceSnapshot>(File.ReadAllText(path), JsonOpts);
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
}
