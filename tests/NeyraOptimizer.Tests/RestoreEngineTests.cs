using Xunit;
using NeyraOptimizer.Domain.Snapshots;
using NeyraOptimizer.Domain.Models.System;
using Microsoft.Win32;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Infrastructure.Logging;
using NeyraOptimizer.Infrastructure.Persistence;
using NeyraOptimizer.Optimization.Restore;
using NeyraOptimizer.Tests.Fakes;

namespace NeyraOptimizer.Tests;

public class RestoreEngineTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "neyra-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeRegistry _registry = new();
    private readonly FakeServiceManager _services = new();
    private readonly FakeStartupManager _startup = new();
    private readonly FakeTaskManager _tasks = new();
    private readonly SnapshotRepository _snapshots;

    public RestoreEngineTests()
    {
        Directory.CreateDirectory(_tmp);
        _snapshots = new SnapshotRepository(_tmp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch (IOException) { }
    }

    private RestoreEngine CreateEngine() => new(
        _registry, _services, _startup, _tasks,
        visuals: new Fakes.FakeVisuals(),
        power: new Fakes.FakePowerManager(),
        snapshotRepo: _snapshots,
        historyRepo: new HistoryRepository(_tmp),
        logger: new StructuredFileLogger(LogSeverity.Debug, Path.Combine(_tmp, "logs")));

    [Fact]
    public async Task Rollback_RestoresPreviousServiceAndStartupAndTaskStates()
    {
        _services.Add("DiagTrack", ServiceStartMode.Automatic);
        _startup.Add("spotify", enabled: true);
        _tasks.Add(@"\Test\Task", enabled: true);

        // Simulate a batch: change everything.
        _services.SetStartMode("DiagTrack", ServiceStartMode.Manual);
        _startup.Disable("spotify");
        _tasks.SetEnabled(@"\Test\Task", false);

        var snapshot = new OptimizationSnapshot
        {
            WindowsBuild = "22631.3155",
            Description = "test",
            Changes =
            {
                new SnapshotChange
                {
                    Kind = ChangeKind.ServiceStartMode, TargetId = "DiagTrack",
                    DisplayName = "DiagTrack", PreviousValue = "Automatic",
                    NewValue = "Manual", AppliedSuccessfully = true,
                },
                new SnapshotChange
                {
                    Kind = ChangeKind.StartupEntryState, TargetId = "spotify",
                    DisplayName = "spotify", PreviousValue = "True",
                    NewValue = "False", AppliedSuccessfully = true,
                },
                new SnapshotChange
                {
                    Kind = ChangeKind.ScheduledTaskState, TargetId = @"\Test\Task",
                    DisplayName = "Task", PreviousValue = "True",
                    NewValue = "False", AppliedSuccessfully = true,
                },
            },
            Status = OperationStatus.Succeeded,
        };

        var result = await CreateEngine().RestoreSnapshotAsync(snapshot);

        Assert.True(result.Success, string.Join(";", result.Errors));
        Assert.Equal(3, result.RestoredCount);
        Assert.Equal(ServiceStartMode.Automatic, _services.GetService("DiagTrack")!.StartMode);
        Assert.True(_startup.Entries["spotify"].IsEnabled);
        Assert.True(_tasks.Tasks[@"\Test\Task"].IsEnabled);
        Assert.Equal(OperationStatus.RolledBack, snapshot.Status);
    }

    [Fact]
    public async Task Rollback_DeletesNewlyCreatedRegistryValues_AndRestoresExisting()
    {
        // Existing value gets restored to its previous data; missing value is deleted on rollback.
        _registry.SetValue(RegRoot.CurrentUser, @"Software\Test", "Existing", 1, RegistryValueKind.DWord);

        var snapshot = new OptimizationSnapshot
        {
            WindowsBuild = "22631",
            Description = "reg",
            Changes =
            {
                new SnapshotChange
                {
                    Kind = ChangeKind.PrivacySetting,
                    TargetId = @"HKCU\Software\Test\Existing",
                    DisplayName = "existing", PreviousValue = "1",
                    NewValue = "0", AppliedSuccessfully = true,
                },
                new SnapshotChange
                {
                    Kind = ChangeKind.PrivacySetting,
                    TargetId = @"HKCU\Software\Test\BrandNew",
                    DisplayName = "new", PreviousValue = null,
                    NewValue = "0", AppliedSuccessfully = true,
                },
            },
        };
        _registry.SetValue(RegRoot.CurrentUser, @"Software\Test", "Existing", 0, RegistryValueKind.DWord);
        _registry.SetValue(RegRoot.CurrentUser, @"Software\Test", "BrandNew", 0, RegistryValueKind.DWord);

        var result = await CreateEngine().RestoreSnapshotAsync(snapshot);

        Assert.Equal(2, result.RestoredCount);
        Assert.Equal(1, _registry.GetValue(RegRoot.CurrentUser, @"Software\Test", "Existing")!.Data);
        Assert.Null(_registry.GetValue(RegRoot.CurrentUser, @"Software\Test", "BrandNew"));
    }
}
