using Xunit;
using NeyraOptimizer.Domain.Snapshots;
using NeyraOptimizer.Infrastructure.CrashRecovery;
using NeyraOptimizer.Infrastructure.Persistence;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Settings;
using NeyraOptimizer.Application.Measurement;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.Tests;

public class PersistenceTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "neyra-tests-" + Guid.NewGuid().ToString("N"));

    public PersistenceTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch (IOException) { }
    }

    // â”€â”€ Snapshots â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [Fact]
    public void SnapshotRepository_RoundTrip_PreservesChanges()
    {
        var repo = new SnapshotRepository(_tmp);
        var snap = new OptimizationSnapshot
        {
            WindowsBuild = "22631.3155",
            Description = "roundtrip",
            Changes =
            {
                new SnapshotChange { Kind = ChangeKind.ServiceStartMode, TargetId = "S1", DisplayName = "s", PreviousValue = "Automatic", NewValue = "Manual", AppliedSuccessfully = true },
            },
        };
        repo.Save(snap);

        var loaded = repo.Load(snap.Id);
        Assert.NotNull(loaded);
        Assert.Equal(snap.Description, loaded!.Description);
        Assert.Equal("Automatic", loaded.Changes[0].PreviousValue);
    }

    [Fact]
    public void TamperedSnapshot_IsRejected()
    {
        var repo = new SnapshotRepository(_tmp);
        var snap = new OptimizationSnapshot { WindowsBuild = "22631", Description = "integrity" };
        repo.Save(snap);

        // Corrupt the stored JSON after saving (simulating tampering/disk corruption).
        var path = Path.Combine(_tmp, $"snapshot-{snap.Id:N}.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("integrity", "tampered"));

        Assert.Null(repo.Load(snap.Id));
    }

    [Fact]
    public void List_ReturnsNewestFirst()
    {
        var repo = new SnapshotRepository(_tmp);
        var older = new OptimizationSnapshot { WindowsBuild = "22631", Description = "older" };
        Thread.Sleep(30);
        var newer = new OptimizationSnapshot { WindowsBuild = "22631", Description = "newer" };
        repo.Save(older);
        repo.Save(newer);

        var list = repo.List();
        Assert.True(list.Count >= 2);
        Assert.Equal(newer.Id.ToString(), list[0].Id);
    }

    // â”€â”€ Pending operation / crash recovery â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [Fact]
    public void PendingOperation_BeginUpdateClear_Lifecycle()
    {
        var tracker = new PendingOperationTracker(Path.Combine(_tmp, "pending.json"));
        var record = new Domain.Snapshots.PendingOperationRecord
        {
            Description = "batch",
            Phase = "Applying",
            TotalChanges = 5,
        };
        tracker.Begin(record);
        tracker.UpdatePhase(record.OperationId, "Verifying", 3);

        var pending = tracker.ReadPending();
        Assert.NotNull(pending);
        Assert.Equal("Verifying", pending!.Phase);
        Assert.Equal(3, pending.CompletedChanges);

        tracker.Clear();
        Assert.Null(tracker.ReadPending());
    }

    [Fact]
    public void PendingOperation_CorruptFile_IsTreatedAsNoPending()
    {
        var path = Path.Combine(_tmp, "pending-corrupt.json");
        File.WriteAllText(path, "{ not valid json");
        var tracker = new PendingOperationTracker(path);
        Assert.Null(tracker.ReadPending());
    }

    // â”€â”€ Settings migration â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [Fact]
    public void SettingsRepository_MigratesAndClamps()
    {
        var path = Path.Combine(_tmp, "settings.json");
        File.WriteAllText(path, """{""SchemaVersion"":1,""DashboardRefreshSeconds"":500}""");
        var loaded = new SettingsRepository(path).Load();
        Assert.InRange(loaded.DashboardRefreshSeconds, 2, 60);
        Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    // â”€â”€ Measurement store â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [Fact]
    public void MeasurementStore_BaselineAfterRoundTrip()
    {
        var store = new MeasurementStore(_tmp);
        var baseline = new PerformanceSnapshot { TotalRamMb = 8192, AvailableRamMb = 4000 };
        store.SaveBaseline(baseline);
        store.MarkAwaitingAfter();

        var loaded = store.LoadBaseline();
        Assert.Equal(8192, loaded!.TotalRamMb);
        Assert.True(store.HasPendingComparison());

        store.SaveAfter(new PerformanceSnapshot { TotalRamMb = 8192, AvailableRamMb = 4500 });
        store.ClearAwaitingAfter();
        Assert.False(store.HasPendingComparison());
        Assert.NotNull(store.LoadAfter());
    }
}
