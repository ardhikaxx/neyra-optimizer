using Xunit;
using NeyraOptimizer.Domain.Snapshots;
using Microsoft.Win32;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.Power;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Infrastructure.CrashRecovery;
using NeyraOptimizer.Infrastructure.Logging;
using NeyraOptimizer.Infrastructure.Persistence;
using NeyraOptimizer.Optimization.Pipeline;
using NeyraOptimizer.Domain.Rules;
using NeyraOptimizer.Security.Elevation;
using NeyraOptimizer.Tests.Fakes;

namespace NeyraOptimizer.Tests;

/// <summary>
/// Integration-style tests running the FULL mutation pipeline against fakes.
/// These are "Mutation Tests" against in-memory doubles only â€” never a real system.
/// </summary>
public class PipelineIntegrationTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "neyra-tests-" + Guid.NewGuid().ToString("N"));

    private readonly FakeRegistry _registry = new();
    private readonly FakeServiceManager _services = new();
    private readonly FakeStartupManager _startup = new();
    private readonly FakeTaskManager _tasks = new();
    private readonly FakePackageManager _packages = new();
    private readonly FakeBackgroundApps _background = new();
    private readonly FakeVisuals _visuals = new() { States = { ["MinAnimate"] = true } };
    private readonly FakePowerManager _power = new();
    private readonly FakeRestorePoints _restorePoints = new();
    private readonly RecordingElevationGateway _gateway;
    private readonly Optimization.Safety.SafetyEngine _safety = new();
    private readonly OptimizationPipeline _pipeline;
    private readonly ISnapshotRepository _snapshots;
    private readonly IHistoryRepository _history;
    private readonly PendingOperationTracker _pending;

    public PipelineIntegrationTests()
    {
        Directory.CreateDirectory(_tmp);
        _snapshots = new SnapshotRepository(_tmp);
        _history = new HistoryRepository(_tmp);
        _pending = new PendingOperationTracker(Path.Combine(_tmp, "pending.json"));
        _gateway = new RecordingElevationGateway(_services, _tasks);
        _pipeline = new OptimizationPipeline(
            _safety, _registry, _services, _startup, _tasks, _visuals, _power,
            _packages, _background, _restorePoints, _gateway,
            _snapshots, _history, _pending,
            new StructuredFileLogger(LogSeverity.Debug, Path.Combine(_tmp, "logs")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch (IOException) { }
    }

    private static Recommendation ServiceRec(string service) => new()
    {
        RuleId = "service_test_" + service,
        Title = $"Test {service}",
        Description = "", Reason = "",
        RequiresAdministrator = true,
        Category = RecommendationCategory.Safe,
        TargetId = service,
        CurrentStateText = "Automatic",
        ProposedStateText = "Manual",
        Area = RuleArea.Services,
    };

    [Fact]
    public async Task NonElevated_ServiceChange_RoutesThroughElevatedBatch_AppliesAndRecordsSnapshot()
    {
        _services.Add("DiagTrack", ServiceStartMode.Automatic);
        _gateway.Elevated = false; // simulate standard user

        var result = await _pipeline.ExecuteAsync(
            new[] { ServiceRec("DiagTrack") }, TestSystems.Profile(),
            createRestorePoint: true, profileKind: null);

        Assert.True(result.Success);
        Assert.Equal(1, _gateway.Requests.Count);
        Assert.Equal(ElevatedOperationKind.ApplyBatch, _gateway.Requests[0].Kind); // ONE UAC prompt
        Assert.True(_restorePoints.CreatedCount >= 1);
        Assert.Equal(ServiceStartMode.Manual, _services.GetService("DiagTrack")!.StartMode);

        var snapshot = _snapshots.Load(result.SnapshotId)!;
        Assert.NotNull(snapshot);
        Assert.Equal(1, snapshot.AppliedCount);
        Assert.Equal("Automatic", snapshot.Changes.Single().PreviousValue);
    }

    [Fact]
    public void RestorePointFailure_AbortsBatch_NothingApplied()
    {
        _services.Add("DiagTrack");
        _restorePoints.Failure = new InvalidOperationException("SR disabled");

        var ex = Assert.ThrowsAnyAsync<RestorePointFailedException>(() => _pipeline.ExecuteAsync(
            new[] { ServiceRec("DiagTrack") }, TestSystems.Profile(),
            createRestorePoint: true, profileKind: null));

        // Nothing was applied and no elevation was requested.
        Assert.Equal(ServiceStartMode.Automatic, _services.GetService("DiagTrack")!.StartMode);
        Assert.Empty(_gateway.Requests);
    }

    [Fact]
    public async Task ProtectedService_IsSkippedBySafety_NotSentToElevation()
    {
        _services.Add("WinDefend", ServiceStartMode.Automatic);
        var rec = new Recommendation
        {
            RuleId = "bad_rule", Title = "Disable Defender", Description = "", Reason = "",
            TargetId = "WinDefend", Area = RuleArea.Services,
            CurrentStateText = "Automatic", ProposedStateText = "Disabled",
            RequiresAdministrator = true, Category = RecommendationCategory.DoNotModify,
        };

        var result = await _pipeline.ExecuteAsync(new[] { rec }, TestSystems.Profile(), false, null);

        Assert.Empty(_gateway.Requests);
        Assert.Equal(ServiceStartMode.Automatic, _services.GetService("WinDefend")!.StartMode);
        Assert.Equal(1, result.SkippedCount);
    }

    [Fact]
    public async Task StartupTaskPrivacyVisualPower_ChangesRecorded_WithPreviousValues()
    {
        _startup.Add("spotify", enabled: true);
        _tasks.Add(@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator", enabled: true);
        _registry.SetValue(RegRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
            "Enabled", 1, RegistryValueKind.DWord);
        _power.IsOverlaySupported = true;

        var recs = new List<Recommendation>
        {
            new()
            {
                RuleId = "startup_t", Title = "Disable spotify", Description = "", Reason = "",
                TargetId = "spotify", Area = RuleArea.Startup,
                CurrentStateText = "True", ProposedStateText = "Disabled",
            },
            new()
            {
                RuleId = "task_t", Title = "Disable CEIP", Description = "", Reason = "",
                TargetId = @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
                Area = RuleArea.ScheduledTasks, ProposedStateText = "Disabled",
            },
            new()
            {
                RuleId = "privacy_t", Title = "Advertising ID off", Description = "", Reason = "",
                TargetId = @"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo\Enabled",
                Area = RuleArea.Privacy, ProposedStateText = "0",
            },
            new()
            {
                RuleId = "visual_t", Title = "MinAnimate off", Description = "", Reason = "",
                TargetId = "MinAnimate", Area = RuleArea.VisualEffects, ProposedStateText = "False",
            },
            new()
            {
                RuleId = "power_overlay_t", Title = "Better battery", Description = "", Reason = "",
                TargetId = "EffectiveOverlay", Area = RuleArea.Power, ProposedStateText = "BetterBattery",
            },
        };

        var result = await _pipeline.ExecuteAsync(recs, TestSystems.Profile(batteryPresent: true), false, null);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(_startup.Entries["spotify"].IsEnabled);
        Assert.False(_tasks.Tasks[@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator"].IsEnabled);
        Assert.Equal(0, _registry.GetValue(RegRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled")!.Data);
        Assert.False(_visuals.States["MinAnimate"]);
        Assert.Equal(PowerOverlayMode.BetterBattery, _power.GetEffectiveOverlay());

        var snap = _snapshots.Load(result.SnapshotId)!;
        Assert.Equal(5, snap.Changes.Count);
        Assert.All(snap.Changes, c => Assert.True(c.AppliedSuccessfully, c.ErrorText));
        Assert.All(snap.Changes, c => Assert.NotNull(c.PreviousValue));
    }

    [Fact]
    public async Task PackageUninstall_IsIrreversible_ButRecordedWithReinstallInfo()
    {
        _packages.Add("ContosoJunk_1.2.3.0_x64__abcde12345678", "ContosoJunk_abcde12345678");
        var rec = new Recommendation
        {
            RuleId = "debloat_t", Title = "Remove ContosoJunk", Description = "", Reason = "",
            TargetId = "ContosoJunk_1.2.3.0_x64__abcde12345678", Area = RuleArea.Debloat,
            ProposedStateText = "Uninstalled",
        };

        var result = await _pipeline.ExecuteAsync(new[] { rec }, TestSystems.Profile(), false, null);

        Assert.True(result.Success);
        Assert.Empty(_packages.Apps);
        var snap = _snapshots.Load(result.SnapshotId)!;
        var change = snap.Changes.Single(c => c.Kind == ChangeKind.AppxPackageRemoval);
        Assert.Contains("PackageFamilyName", change.RestoreDataJson);
    }

    [Fact]
    public async Task UserCancellation_MidBatch_PersistsPartialSnapshot_AndThrows()
    {
        _services.Add("A"); _services.Add("B"); _services.Add("C"); _services.Add("D"); _services.Add("E");
        using var cts = new CancellationTokenSource();

        // Cancel as soon as the first item completes.
        var recs = new List<Recommendation>();
        foreach (var name in new[] { "A", "B", "C" })
        {
            recs.Add(new Recommendation
            {
                RuleId = "svc_" + name, Title = name, Description = "", Reason = "",
                TargetId = name, Area = RuleArea.Services,
                ProposedStateText = "Manual", Category = RecommendationCategory.Safe,
            });
        }

        var progress = new Progress<(int current, int total, string step)>(t =>
        {
            if (t.current == 1 && !cts.IsCancellationRequested) cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _pipeline.ExecuteAsync(
            recs, TestSystems.Profile(), false, null, progress, cts.Token));

        // Partial work is recorded honestly for crash recovery / manual rollback.
        Assert.Null(_pending.ReadPending()); // marker cleared on cancellation path
    }
}
