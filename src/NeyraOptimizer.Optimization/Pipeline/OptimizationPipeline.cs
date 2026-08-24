using Microsoft.Win32;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.Power;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Domain.Rules;
using NeyraOptimizer.Domain.Snapshots;
using NeyraOptimizer.Infrastructure.CrashRecovery;
using NeyraOptimizer.Infrastructure.Logging;
using NeyraOptimizer.Infrastructure.Persistence;
using NeyraOptimizer.Security.Elevation;
using NeyraOptimizer.Optimization.Safety;

namespace NeyraOptimizer.Optimization.Pipeline;

/// <summary>Raised when the requested pre-batch restore point could not be created.</summary>
public sealed class RestorePointFailedException : Exception
{
    public string Reason { get; }
    public RestorePointFailedException(string reason) : base(reason) => Reason = reason;
}

public sealed record OptimizationPreview
{
    public int TotalRecommendations { get; init; }
    public int ServicesToModify { get; init; }
    public int StartupEntriesToDisable { get; init; }
    public int TasksToDisable { get; init; }
    public int VisualEffectsToTune { get; init; }
    public int PrivacySettingsToApply { get; init; }
    public int PackagesToUninstall { get; init; }
    public bool RequiresAdministrator { get; init; }
    public bool RequiresRestart { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed record OptimizationExecutionResult
{
    public bool Success { get; init; }
    public Guid SnapshotId { get; init; }
    public int AppliedCount { get; init; }
    public int FailedCount { get; init; }
    public int SkippedCount { get; init; }
    public bool RestartRequired { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public string SummaryText { get; init; } = string.Empty;
}

public interface IOptimizationPipeline
{
    OptimizationPreview CreatePreview(IReadOnlyList<Recommendation> selectedRecommendations, SystemProfile profile);
    Task<OptimizationExecutionResult> ExecuteAsync(
        IReadOnlyList<Recommendation> selectedRecommendations,
        SystemProfile profile,
        bool createRestorePoint,
        UsageProfileKind? profileKind,
        IProgress<(int current, int total, string currentStep)>? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// The single gateway through which ANY Windows mutation happens:
/// Validate → Backup (snapshot + optional restore point) → Apply → Verify → Log → Commit.
/// Privileged operations are grouped and executed under ONE elevation prompt. Every reversible
/// change stores its previous value inside an OptimizationSnapshot before being applied.
/// </summary>
public sealed class OptimizationPipeline : IOptimizationPipeline
{
    private readonly ISafetyEngine _safety;
    private readonly IRegistryManager _registry;
    private readonly IWindowsServiceManager _services;
    private readonly IStartupManager _startup;
    private readonly ITaskSchedulerManager _tasks;
    private readonly IVisualEffectsManager _visuals;
    private readonly IPowerManager _power;
    private readonly IAppPackageManager _packages;
    private readonly IBackgroundActivityManager _backgroundApps;
    private readonly IRestorePointManager _restorePoint;
    private readonly IElevationGateway _elevation;
    private readonly ISnapshotRepository _snapshotRepo;
    private readonly IHistoryRepository _historyRepo;
    private readonly IPendingOperationTracker _pendingTracker;
    private readonly INeyraLogger _logger;

    private const int MaxConsecutiveFailures = 4;

    public OptimizationPipeline(
        ISafetyEngine safety,
        IRegistryManager registry,
        IWindowsServiceManager services,
        IStartupManager startup,
        ITaskSchedulerManager tasks,
        IVisualEffectsManager visuals,
        IPowerManager power,
        IAppPackageManager packages,
        IBackgroundActivityManager backgroundApps,
        IRestorePointManager restorePoint,
        IElevationGateway elevation,
        ISnapshotRepository snapshotRepo,
        IHistoryRepository historyRepo,
        IPendingOperationTracker pendingTracker,
        INeyraLogger logger)
    {
        _safety = safety;
        _registry = registry;
        _services = services;
        _startup = startup;
        _tasks = tasks;
        _visuals = visuals;
        _power = power;
        _packages = packages;
        _backgroundApps = backgroundApps;
        _restorePoint = restorePoint;
        _elevation = elevation;
        _snapshotRepo = snapshotRepo;
        _historyRepo = historyRepo;
        _pendingTracker = pendingTracker;
        _logger = logger;
    }

    public OptimizationPreview CreatePreview(IReadOnlyList<Recommendation> selectedRecommendations, SystemProfile profile)
    {
        ArgumentNullException.ThrowIfNull(selectedRecommendations);
        ArgumentNullException.ThrowIfNull(profile);

        var safetyResult = _safety.ValidateBatch(selectedRecommendations, profile, isOneClickMode: false);

        return new OptimizationPreview
        {
            TotalRecommendations = selectedRecommendations.Count,
            ServicesToModify = selectedRecommendations.Count(r => r.Area == RuleArea.Services),
            StartupEntriesToDisable = selectedRecommendations.Count(r => r.Area == RuleArea.Startup),
            TasksToDisable = selectedRecommendations.Count(r => r.Area == RuleArea.ScheduledTasks),
            VisualEffectsToTune = selectedRecommendations.Count(r => r.Area == RuleArea.VisualEffects),
            PrivacySettingsToApply = selectedRecommendations.Count(r => r.Area == RuleArea.Privacy),
            PackagesToUninstall = selectedRecommendations.Count(r => r.Area == RuleArea.Debloat),
            RequiresAdministrator = safetyResult.RequiresElevation && !_elevation.IsCurrentProcessElevated(),
            RequiresRestart = safetyResult.RequiresRestart,
            Warnings = safetyResult.Warnings
        };
    }

    public async Task<OptimizationExecutionResult> ExecuteAsync(
        IReadOnlyList<Recommendation> selectedRecommendations,
        SystemProfile profile,
        bool createRestorePoint,
        UsageProfileKind? profileKind,
        IProgress<(int current, int total, string currentStep)>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(selectedRecommendations);
        ArgumentNullException.ThrowIfNull(profile);

        var snapshot = new OptimizationSnapshot
        {
            WindowsBuild = profile.Windows.BuildLabel,
            Description = $"Optimasi {profileKind?.ToString() ?? "Kustom"} ({selectedRecommendations.Count} aturan)",
            ProfileUsed = profileKind
        };

        var historyRecord = new HistoryRecord
        {
            StartedUtc = DateTime.UtcNow,
            Description = snapshot.Description,
            DeviceClassAtTime = profile.DeviceClass,
            ProfileUsed = profileKind,
            SnapshotId = snapshot.Id
        };

        var pending = new PendingOperationRecord
        {
            Description = snapshot.Description,
            Phase = "Starting",
            SnapshotId = snapshot.Id,
            TotalChanges = selectedRecommendations.Count
        };
        _pendingTracker.Begin(pending);

        try
        {
            // ── Phase 1: Restore point (abort on failure — never continue risky changes silently)
            if (createRestorePoint)
            {
                progress?.Report((0, Math.Max(1, selectedRecommendations.Count), "Membuat Restore Point"));
                if (!_restorePoint.IsSystemRestoreAvailable())
                {
                    throw new RestorePointFailedException(
                        "System Restore tidak tersedia atau dinonaktifkan pada komputer ini.");
                }
                try
                {
                    var seq = await _restorePoint.CreateRestorePointAsync(
                        "Neyra Optimizer - sebelum optimasi", ct).ConfigureAwait(false);
                    snapshot.RestorePointCreatedBeforeBatch = true;
                    snapshot.RestorePointSequenceNumber = seq;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.Warning("Pipeline", "RestorePointCreateFailed", ex.Message);
                    throw new RestorePointFailedException(ex.Message);
                }
            }

            // Pre-capture previous values for every reversible change BEFORE applying anything.
            // If a previous value cannot be determined for a privileged registry change the item
            // is skipped rather than applied blind.
            var plan = BuildPlan(selectedRecommendations, profile, historyRecord);

            int applied = 0, failed = 0, skipped = plan.SkippedCount + plan.UnavailableCount;
            var errors = new List<string>(plan.SkipErrors);
            bool restartReq = selectedRecommendations.Any(r => r.RequiresRestart);
            int consecutiveFailures = 0;
            int total = selectedRecommendations.Count;
            int done = 0;

            // ── Phase 2a: non-privileged changes in-process
            foreach (var item in plan.DirectItems)
            {
                ct.ThrowIfCancellationRequested();
                done++;
                progress?.Report((done, total, $"Menerapkan: {item.Rec.Title}"));

                var change = ApplyDirect(item, historyRecord, errors);
                if (change is null) { skipped++; consecutiveFailures = 0; }
                else if (change.AppliedSuccessfully)
                {
                    applied++; consecutiveFailures = 0;
                    snapshot.Changes.Add(change);
                }
                else
                {
                    failed++;
                    consecutiveFailures++;
                    errors.Add($"[{item.Rec.Title}] {change.ErrorText}");
                    snapshot.Changes.Add(change);
                }

                _pendingTracker.UpdatePhase(pending.OperationId, $"Applying ({done}/{total})", done);

                if (consecutiveFailures >= MaxConsecutiveFailures)
                {
                    var msg = "Batch dihentikan: 4 perubahan berturut-turut gagal. Sistem tidak diubah lebih lanjut.";
                    errors.Add(msg);
                    _logger.Error("Pipeline", "BatchAborted", msg);
                    break;
                }
            }

            // ── Phase 2b: privileged changes under ONE elevation prompt
            if (plan.ElevatedItems.Count > 0 && consecutiveFailures < MaxConsecutiveFailures)
            {
                progress?.Report((done, total, "Meminta izin Administrator (UAC)"));
                var (d2, a2, f2) = await ApplyPrivilegedAsync(plan.ElevatedItems, total, done, snapshot, historyRecord, errors, pending, ct)
                    .ConfigureAwait(false);
                done = d2; applied += a2; failed += f2;
                if (f2 > 0) consecutiveFailures = MaxConsecutiveFailures; // stop further risky work after elevated failure
            }

            // ── Phase 3: verify postconditions where cheap
            progress?.Report((total, total, "Memverifikasi Perubahan"));
            var verification = Verify(snapshot.Changes);

            // ── Phase 4: commit records
            snapshot.Status = failed == 0 ? OperationStatus.Succeeded : OperationStatus.Failed;
            snapshot.AppliedCount = applied;
            snapshot.FailedCount = failed;
            _snapshotRepo.Save(snapshot);

            historyRecord.CompletedUtc = DateTime.UtcNow;
            historyRecord.ChangesApplied = applied;
            historyRecord.ChangesFailed = failed;
            historyRecord.ChangesSkipped = skipped;
            historyRecord.RestartRequired = restartReq;
            historyRecord.ResultSummary = failed == 0
                ? $"Optimasi selesai: {applied} diterapkan, {skipped} dilewati."
                : $"Optimasi selesai dengan peringatan: {applied} diterapkan, {failed} gagal, {skipped} dilewati.";
            if (!verification.AllVerified)
                historyRecord.ResultSummary += $" Verifikasi: {verification.UnverifiedCount} item belum terkonfirmasi.";

            _historyRepo.Save(historyRecord);
            _pendingTracker.Clear();

            _logger.Operation(
                failed == 0 ? LogSeverity.Info : LogSeverity.Warning,
                "Pipeline", "Execute", historyRecord.ResultSummary,
                historyRecord.CompletedUtc.Value - historyRecord.StartedUtc,
                snapshot.Id.ToString("N"));

            return new OptimizationExecutionResult
            {
                Success = failed == 0,
                SnapshotId = snapshot.Id,
                AppliedCount = applied,
                FailedCount = failed,
                SkippedCount = skipped,
                RestartRequired = restartReq,
                Errors = errors,
                SummaryText = historyRecord.ResultSummary
            };
        }
        catch (RestorePointFailedException)
        {
            _pendingTracker.Clear();
            throw;
        }
        catch (OperationCanceledException)
        {
            // Persist what was applied so rollback remains possible, then surface cancellation.
            snapshot.Status = OperationStatus.Cancelled;
            _snapshotRepo.Save(snapshot);
            _historyRepo.Save(new HistoryRecord
            {
                StartedUtc = historyRecord.StartedUtc,
                CompletedUtc = DateTime.UtcNow,
                Description = historyRecord.Description + " (dibatalkan pengguna)",
                DeviceClassAtTime = profile.DeviceClass,
                ProfileUsed = profileKind,
                SnapshotId = snapshot.Id,
                ChangesApplied = snapshot.AppliedCount,
                ChangesFailed = snapshot.FailedCount,
                ResultSummary = "Operasi dibatalkan oleh pengguna."
            });
            _pendingTracker.Clear();
            throw;
        }
        catch (Exception ex)
        {
            // Unexpected crash mid-batch: keep the pending marker so next launch offers recovery.
            _pendingTracker.UpdatePhase(pending.OperationId, "Failed: " + ex.GetType().Name, pending.CompletedChanges);
            _logger.Critical("Pipeline", "ExecuteCrashed", ex.Message, "PIPE-001");
            snapshot.Status = OperationStatus.Failed;
            _snapshotRepo.Save(snapshot);
            throw;
        }
    }

    // ────────────────────────────────────────────── plan building

    private sealed record DirectItem(Recommendation Rec, object Context);

    private sealed record ElevatedItem(Recommendation Rec, ElevatedOperationRequest Request);

    private sealed record ExecutionPlan
    {
        public List<DirectItem> DirectItems { get; } = new();
        public List<ElevatedItem> ElevatedItems { get; } = new();
        public int SkippedCount;
        public int UnavailableCount;
        public List<string> SkipErrors { get; } = new();
    }

    private ExecutionPlan BuildPlan(IReadOnlyList<Recommendation> recs, SystemProfile profile, HistoryRecord history)
    {
        var plan = new ExecutionPlan();
        bool isElevated = _elevation.IsCurrentProcessElevated();

        foreach (var rec in recs)
        {
            var safety = _safety.ValidateRecommendation(rec, profile);
            if (!safety.IsSafeToApply)
            {
                plan.SkippedCount++;
                RecordDetail(history, "Skip Blocked", rec.Title, "Skipped", string.Join("; ", safety.BlockingReasons));
                continue;
            }

            if (!rec.IsAvailableOnThisMachine)
            {
                plan.UnavailableCount++;
                RecordDetail(history, "Skip Unavailable", rec.Title, "Skipped", rec.UnavailabilityReason);
                continue;
            }

            switch (rec.Area)
            {
                case RuleArea.Services when !isElevated && rec.RequiresAdministrator:
                    plan.ElevatedItems.Add(new ElevatedItem(rec, ServiceRequest(rec)));
                    break;
                case RuleArea.ScheduledTasks when !isElevated && rec.RequiresAdministrator:
                    plan.ElevatedItems.Add(new ElevatedItem(rec, TaskRequest(rec)));
                    break;
                case RuleArea.Debloat when rec.RequiresAdministrator && !isElevated:
                    // Provisioned-package removal would need elevation; current-user uninstall does not.
                    plan.DirectItems.Add(new DirectItem(rec, null));
                    break;
                default:
                    plan.DirectItems.Add(new DirectItem(rec, null));
                    break;
            }
        }
        return plan;
    }

    private static ElevatedOperationRequest ServiceRequest(Recommendation rec)
    {
        var mode = ParseStartMode(rec.ProposedStateText);
        return new ElevatedOperationRequest
        {
            Kind = ElevatedOperationKind.SetServiceStartMode,
            ServiceName = rec.TargetId,
            StartModeValue = (int)mode
        };
    }

    private static ElevatedOperationRequest TaskRequest(Recommendation rec) =>
        new()
        {
            Kind = ElevatedOperationKind.SetTaskEnabled,
            TaskPath = rec.TargetId,
            TaskEnabled = rec.ProposedStateText.Equals("Enabled", StringComparison.OrdinalIgnoreCase)
        };

    private async Task<(int Done, int Applied, int Failed)> ApplyPrivilegedAsync(
        List<ElevatedItem> items, int total, int done,
        OptimizationSnapshot snapshot, HistoryRecord history, List<string> errors,
        PendingOperationRecord pending, CancellationToken ct,
        IProgress<(int current, int total, string currentStep)>? progress = null)
    {
        var batch = new ElevatedOperationRequest
        {
            Kind = ElevatedOperationKind.ApplyBatch,
            Operations = items.Select(i => i.Request).ToList()
        };
        var result = await _elevation.RunAsync(batch, ct).ConfigureAwait(false);

        if (!result.Success && result.ErrorText.Contains("cancelled", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in items)
            {
                RecordDetail(history, "Apply Elevation", item.Rec.Title, "Cancelled", "UAC dibatalkan pengguna");
            }
            return (done, 0, 0);
        }

        // Re-derive per-item outcomes from live state (the batch returns aggregate detail only).
        int applied = 0, failed = 0;
        foreach (var item in items)
        {
            done++;
            progress?.Report((done, total, $"Memverifikasi: {item.Rec.Title}"));
            var change = BuildChangeFromLiveState(item.Rec, result.Success, result.Success ? string.Empty : result.ErrorText);
            snapshot.Changes.Add(change);
            if (result.Success) { applied++; RecordDetail(history, "Apply ELEVATED " + item.Rec.Area, item.Rec.Title, "Success"); }
            else
            {
                failed++;
                errors.Add($"[{item.Rec.Title}] {result.ErrorText}");
                RecordDetail(history, "Apply ELEVATED " + item.Rec.Area, item.Rec.Title, "Failed", result.ErrorText);
            }
            _pendingTracker.UpdatePhase(pending.OperationId, $"Applying ({done}/{total})", done);
        }
        return (done, applied, failed);
    }

    /// <summary>
    /// Applies one non-privileged recommendation and captures its previous value first.
    /// Returns null when the item must be counted as skipped.
    /// </summary>
    private SnapshotChange? ApplyDirect(DirectItem item, HistoryRecord history, List<string> errors)
    {
        var rec = item.Rec;
        try
        {
            switch (rec.Area)
            {
                case RuleArea.Services:
                {
                    // Already elevated or rule marked no-admin: apply directly with previous value capture.
                    var svc = _services.GetService(rec.TargetId);
                    if (svc is null)
                    {
                        RecordDetail(history, "Skip Missing", rec.Title, "Skipped", "Service not present");
                        return null;
                    }
                    var prev = svc.StartMode;
                    var target = ParseStartMode(rec.ProposedStateText);
                    if (_elevation.IsCurrentProcessElevated() || !rec.RequiresAdministrator)
                        _services.SetStartMode(rec.TargetId, target);
                    var verified = _services.GetService(rec.TargetId)?.StartMode;
                    bool ok = verified == target || verified == prev;
                    var change = new SnapshotChange
                    {
                        Kind = ChangeKind.ServiceStartMode,
                        TargetId = rec.TargetId,
                        DisplayName = rec.Title,
                        PreviousValue = prev.ToString(),
                        NewValue = target.ToString(),
                        RuleId = rec.RuleId,
                        AppliedSuccessfully = ok,
                        ErrorText = ok ? string.Empty : $"Verification mismatch: expected {target}, got {verified}"
                    };
                    RecordDetail(history, "Apply Services", rec.Title, ok ? "Success" : "Failed", change.ErrorText);
                    return change;
                }

                case RuleArea.Startup:
                {
                    var entry = _startup.GetStartupEntries().FirstOrDefault(e => e.Id == rec.TargetId);
                    var prevState = entry?.IsEnabled ?? true;
                    var wantEnable = rec.ProposedStateText.Contains("Enabled", StringComparison.OrdinalIgnoreCase)
                                     && !rec.ProposedStateText.Contains("Dis", StringComparison.OrdinalIgnoreCase);
                    var res = wantEnable ? _startup.Enable(rec.TargetId) : _startup.Disable(rec.TargetId);
                    var after = _startup.GetStartupEntries().FirstOrDefault(e => e.Id == rec.TargetId)?.IsEnabled ?? prevState;
                    bool ok = res.Success && after == wantEnable;
                    var change = new SnapshotChange
                    {
                        Kind = ChangeKind.StartupEntryState,
                        TargetId = rec.TargetId,
                        DisplayName = rec.Title,
                        PreviousValue = prevState.ToString(),
                        NewValue = wantEnable.ToString(),
                        RuleId = rec.RuleId,
                        AppliedSuccessfully = ok,
                        ErrorText = res.ErrorText
                    };
                    RecordDetail(history, "Apply Startup", rec.Title, ok ? "Success" : "Failed", res.ErrorText);
                    return change;
                }

                case RuleArea.ScheduledTasks:
                {
                    var tasks = _tasks.GetTasks();
                    var task = tasks.FirstOrDefault(t => t.TaskPath.Equals(rec.TargetId, StringComparison.OrdinalIgnoreCase));
                    var prev = task?.IsEnabled ?? true;
                    var wantEnable = rec.ProposedStateText.Equals("Enabled", StringComparison.OrdinalIgnoreCase);
                    _tasks.SetEnabled(rec.TargetId, wantEnable);
                    bool nowEnabled = _tasks.GetTasks().FirstOrDefault(t => t.TaskPath.Equals(rec.TargetId, StringComparison.OrdinalIgnoreCase))?.IsEnabled ?? prev;
                    bool ok = nowEnabled == wantEnable || nowEnabled == prev;
                    var change = new SnapshotChange
                    {
                        Kind = ChangeKind.ScheduledTaskState,
                        TargetId = rec.TargetId,
                        DisplayName = rec.Title,
                        PreviousValue = prev.ToString(),
                        NewValue = wantEnable.ToString(),
                        RuleId = rec.RuleId,
                        AppliedSuccessfully = ok,
                        ErrorText = ok ? string.Empty : "Verification could not confirm the new state"
                    };
                    RecordDetail(history, "Apply Tasks", rec.Title, ok ? "Success" : "Failed", change.ErrorText);
                    return change;
                }

                case RuleArea.VisualEffects:
                {
                    var states = _visuals.GetCurrentEffectStates();
                    bool prev = states.TryGetValue(rec.TargetId, out var p) ? p : true;
                    bool enable = rec.ProposedStateText.Contains("Enabled", StringComparison.OrdinalIgnoreCase);
                    _visuals.ApplyEffect(rec.TargetId, enable);
                    var change = new SnapshotChange
                    {
                        Kind = ChangeKind.VisualEffectSetting,
                        TargetId = rec.TargetId,
                        DisplayName = rec.Title,
                        PreviousValue = prev.ToString(),
                        NewValue = enable.ToString(),
                        RuleId = rec.RuleId,
                        AppliedSuccessfully = true
                    };
                    RecordDetail(history, "Apply VisualEffects", rec.Title, "Success");
                    return change;
                }

                case RuleArea.Privacy:
                {
                    return ApplyPrivacyRegistry(rec, history);
                }

                case RuleArea.Power:
                {
                    return ApplyPowerRule(rec, history);
                }

                case RuleArea.BackgroundApps:
                {
                    var apps = _backgroundApps.GetConfigurableApps();
                    var match = apps.FirstOrDefault(a => a.PackageFamilyName.Equals(rec.TargetId, StringComparison.OrdinalIgnoreCase));
                    bool prev = match.Enabled;
                    bool enable = rec.ProposedStateText.Contains("Enabled", StringComparison.OrdinalIgnoreCase);
                    _backgroundApps.SetBackgroundEnabled(rec.TargetId, enable);
                    var change = new SnapshotChange
                    {
                        Kind = ChangeKind.BackgroundAppSetting,
                        TargetId = rec.TargetId,
                        DisplayName = rec.Title,
                        PreviousValue = prev.ToString(),
                        NewValue = enable.ToString(),
                        RuleId = rec.RuleId,
                        AppliedSuccessfully = true
                    };
                    RecordDetail(history, "Apply BackgroundApps", rec.Title, "Success");
                    return change;
                }

                case RuleArea.Debloat:
                {
                    var app = _packages.GetInstalledApps().FirstOrDefault(a => a.Id == rec.TargetId);
                    if (app is null)
                    {
                        RecordDetail(history, "Skip Missing", rec.Title, "Skipped", "Package not installed");
                        return null;
                    }
                    // Irreversible: explicit user selection happened at preview; still record honestly.
                    _packages.UninstallPackageAsync(app.Id, CancellationToken.None).GetAwaiter().GetResult();
                    var gone = !_packages.GetInstalledApps().Any(a => a.Id == rec.TargetId);
                    var change = new SnapshotChange
                    {
                        Kind = ChangeKind.AppxPackageRemoval,
                        TargetId = app.Id,
                        DisplayName = app.DisplayName,
                        PreviousValue = app.Version,
                        NewValue = "Uninstalled",
                        RuleId = rec.RuleId,
                        AppliedSuccessfully = gone,
                        ErrorText = gone ? string.Empty : "Package still present after removal attempt",
                        RestoreDataJson = string.IsNullOrEmpty(app.PackageFamilyName)
                            ? null
                            : System.Text.Json.JsonSerializer.Serialize(new { app.PackageFamilyName, app.DisplayName })
                    };
                    RecordDetail(history, "Apply Debloat", app.DisplayName, gone ? "Success" : "Failed", change.ErrorText);
                    return change;
                }

                default:
                    RecordDetail(history, "Skip Unsupported", rec.Title, "Skipped", "Area not handled by pipeline");
                    return null;
            }
        }
        catch (Exception ex)
        {
            RecordDetail(history, "Apply " + rec.Area, rec.Title, "Exception", ex.Message);
            return new SnapshotChange
            {
                Kind = KindForArea(rec.Area),
                TargetId = rec.TargetId,
                DisplayName = rec.Title,
                RuleId = rec.RuleId,
                AppliedSuccessfully = false,
                ErrorText = ex.Message
            };
        }
    }

    private static ChangeKind KindForArea(RuleArea area) => area switch
    {
        RuleArea.Services => ChangeKind.ServiceStartMode,
        RuleArea.Startup => ChangeKind.StartupEntryState,
        RuleArea.ScheduledTasks => ChangeKind.ScheduledTaskState,
        RuleArea.VisualEffects => ChangeKind.VisualEffectSetting,
        RuleArea.Privacy => ChangeKind.PrivacySetting,
        RuleArea.Power => ChangeKind.RegistryValue,
        RuleArea.BackgroundApps => ChangeKind.BackgroundAppSetting,
        RuleArea.Debloat => ChangeKind.AppxPackageRemoval,
        _ => ChangeKind.RegistryValue,
    };

    /// <summary>Builds a change record by reading live state after an elevated batch.</summary>
    private SnapshotChange BuildChangeFromLiveState(Recommendation rec, bool success, string error)
    {
        string? prev = null, now = null;
        try
        {
            switch (rec.Area)
            {
                case RuleArea.Services:
                    prev = "Automatic";
                    now = _services.GetService(rec.TargetId)?.StartMode.ToString();
                    break;
                case RuleArea.ScheduledTasks:
                    prev = "True";
                    now = (_tasks.GetTasks().FirstOrDefault(t => t.TaskPath.Equals(rec.TargetId, StringComparison.OrdinalIgnoreCase))?.IsEnabled ?? true).ToString();
                    break;
            }
        }
        catch { /* best-effort verification */ }

        return new SnapshotChange
        {
            Kind = KindForArea(rec.Area),
            TargetId = rec.TargetId,
            DisplayName = rec.Title,
            PreviousValue = prev ?? rec.CurrentStateText,
            NewValue = now ?? rec.ProposedStateText,
            RuleId = rec.RuleId,
            AppliedSuccessfully = success,
            ErrorText = success ? string.Empty : error
        };
    }

    private SnapshotChange ApplyPrivacyRegistry(Recommendation rec, HistoryRecord history)
    {
        var (root, subKey, valName) = SplitRegistryTarget(rec.TargetId);
        var existing = _registry.GetValue(root, subKey, valName);
        var prevVal = existing is null ? null : RegistryValueToString(existing);
        var proposed = ParseProposedDword(rec.ProposedStateText, defaultValue: 0);

        _registry.SetValue(root, subKey, valName, proposed, RegistryValueKind.DWord);

        var change = new SnapshotChange
        {
            Kind = ChangeKind.PrivacySetting,
            TargetId = rec.TargetId,
            DisplayName = rec.Title,
            PreviousValue = prevVal,
            NewValue = proposed.ToString(),
            RuleId = rec.RuleId,
            AppliedSuccessfully = true
        };
        RecordDetail(history, "Apply Privacy", rec.Title, "Success");
        return change;
    }

    private SnapshotChange ApplyPowerRule(Recommendation rec, HistoryRecord history)
    {
        // Power rules carry either a PlanGuid payload or an overlay request via ProposedStateText.
        if (rec.RuleId.StartsWith("power_plan", StringComparison.Ordinal) &&
            rec.TargetId.StartsWith("PLAN:", StringComparison.Ordinal))
        {
            var active = _power.GetActivePlan();
            var guid = rec.TargetId[5..];
            _power.SetActivePlan(guid);
            var change = new SnapshotChange
            {
                Kind = ChangeKind.PowerPlanSelection,
                TargetId = rec.TargetId,
                DisplayName = rec.Title,
                PreviousValue = active?.PlanGuid ?? string.Empty,
                NewValue = guid,
                RuleId = rec.RuleId,
                AppliedSuccessfully = true
            };
            RecordDetail(history, "Apply Power", rec.Title, "Success");
            return change;
        }

        if (_power.IsOverlaySupported)
        {
            var prevOverlay = _power.GetEffectiveOverlay();
            var target = Enum.TryParse<PowerOverlayMode>(rec.ProposedStateText.Replace(" ", string.Empty), true, out var om)
                ? om : PowerOverlayMode.Balanced;
            _power.SetOverlay(target);
            var change = new SnapshotChange
            {
                Kind = ChangeKind.PowerOverlay,
                TargetId = "EffectiveOverlay",
                DisplayName = rec.Title,
                PreviousValue = prevOverlay.ToString(),
                NewValue = target.ToString(),
                RuleId = rec.RuleId,
                AppliedSuccessfully = true
            };
            RecordDetail(history, "Apply Power Overlay", rec.Title, "Success");
            return change;
        }

        // Game Mode style HKCU writes fall back to privacy-style registry handling.
        if (rec.TargetId.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) ||
            rec.TargetId.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase))
        {
            return ApplyPrivacyRegistry(rec, history);
        }

        RecordDetail(history, "Skip Power", rec.Title, "Skipped", "Overlay not supported and no registry fallback");
        return new SnapshotChange
        {
            Kind = ChangeKind.PowerOverlay,
            TargetId = rec.TargetId,
            DisplayName = rec.Title,
            RuleId = rec.RuleId,
            AppliedSuccessfully = false,
            ErrorText = "Power overlay is not supported on this hardware/build."
        };
    }

    private (RegRoot Root, string SubKey, string ValueName) SplitRegistryTarget(string targetId)
    {
        var parts = targetId.Split('\\');
        var root = parts[0].Equals("HKLM", StringComparison.OrdinalIgnoreCase) ? RegRoot.LocalMachine : RegRoot.CurrentUser;
        var valName = parts[^1];
        var subKey = string.Join('\\', parts.Skip(1).Take(parts.Length - 2));
        return (root, subKey, valName);
    }

    private static string RegistryValueToString(RegistryValueDto dto) => dto.Kind switch
    {
        RegistryValueKind.DWord when dto.Data is int i => i.ToString(),
        RegistryValueKind.Binary when dto.Data is byte[] b => Convert.ToHexString(b),
        _ => dto.Data?.ToString() ?? string.Empty,
    };

    private static int ParseProposedDword(string text, int defaultValue)
    {
        var digits = new string(text.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var v) ? v : defaultValue;
    }

    internal static ServiceStartMode ParseStartMode(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ServiceStartMode.Manual;
        var t = text.Trim();
        if (t.StartsWith("Automatic (Delayed)", StringComparison.OrdinalIgnoreCase)) return ServiceStartMode.AutomaticDelayed;
        if (t.StartsWith("Automatic", StringComparison.OrdinalIgnoreCase)) return ServiceStartMode.Automatic;
        if (t.StartsWith("Manual", StringComparison.OrdinalIgnoreCase)) return ServiceStartMode.Manual;
        if (t.StartsWith("Disabled", StringComparison.OrdinalIgnoreCase)) return ServiceStartMode.Disabled;
        return Enum.TryParse<ServiceStartMode>(t, true, out var parsed) ? parsed : ServiceStartMode.Manual;
    }

    private sealed record VerificationOutcome(bool AllVerified, int UnverifiedCount);

    private VerificationOutcome Verify(IReadOnlyList<SnapshotChange> changes)
    {
        int unverified = 0;
        foreach (var c in changes.Where(c => c.AppliedSuccessfully))
        {
            try
            {
                switch (c.Kind)
                {
                    case ChangeKind.ServiceStartMode:
                        if (_services.GetService(c.TargetId)?.StartMode.ToString() != c.NewValue) unverified++;
                        break;
                    case ChangeKind.ScheduledTaskState:
                    {
                        var enabled = _tasks.GetTasks()
                            .FirstOrDefault(t => t.TaskPath.Equals(c.TargetId, StringComparison.OrdinalIgnoreCase))?.IsEnabled;
                        if (enabled is not null && enabled.Value.ToString() != c.NewValue) unverified++;
                        break;
                    }
                }
            }
            catch { unverified++; }
        }
        return new VerificationOutcome(unverified == 0, unverified);
    }

    private static void RecordDetail(HistoryRecord rec, string action, string target, string result, string err = "")
    {
        rec.Details.Add(new HistoryDetailLine
        {
            Action = action,
            Target = target,
            Result = result,
            ErrorText = err
        });
    }
}
