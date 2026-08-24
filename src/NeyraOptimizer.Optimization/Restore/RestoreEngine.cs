using System.Text.Json;
using Microsoft.Win32;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.Power;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Domain.Snapshots;
using NeyraOptimizer.Infrastructure.Logging;
using NeyraOptimizer.Infrastructure.Persistence;

namespace NeyraOptimizer.Optimization.Restore;

public sealed record RestoreResult
{
    public bool Success { get; init; }
    public int RestoredCount { get; init; }
    public int FailedCount { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public bool RestartRequired { get; init; }
}

public interface IRestoreEngine
{
    Task<RestoreResult> RestoreSnapshotAsync(OptimizationSnapshot snapshot, IProgress<(int current, int total, string item)>? progress = null, CancellationToken ct = default);
}

public sealed class RestoreEngine : IRestoreEngine
{
    private readonly IRegistryManager _registry;
    private readonly IWindowsServiceManager _services;
    private readonly IStartupManager _startup;
    private readonly ITaskSchedulerManager _tasks;
    private readonly IVisualEffectsManager _visuals;
    private readonly IPowerManager _power;
    private readonly ISnapshotRepository _snapshotRepo;
    private readonly IHistoryRepository _historyRepo;
    private readonly INeyraLogger _logger;

    public RestoreEngine(
        IRegistryManager registry,
        IWindowsServiceManager services,
        IStartupManager startup,
        ITaskSchedulerManager tasks,
        IVisualEffectsManager visuals,
        IPowerManager power,
        ISnapshotRepository snapshotRepo,
        IHistoryRepository historyRepo,
        INeyraLogger logger)
    {
        _registry = registry;
        _services = services;
        _startup = startup;
        _tasks = tasks;
        _visuals = visuals;
        _power = power;
        _snapshotRepo = snapshotRepo;
        _historyRepo = historyRepo;
        _logger = logger;
    }

    public async Task<RestoreResult> RestoreSnapshotAsync(
        OptimizationSnapshot snapshot,
        IProgress<(int current, int total, string item)>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        int restored = 0;
        int failed = 0;
        var errors = new List<string>();
        bool restartReq = false;

        var historyRecord = new HistoryRecord
        {
            StartedUtc = DateTime.UtcNow,
            Description = $"Rollback Snapshot: {snapshot.Description}",
            SnapshotId = snapshot.Id,
            ProfileUsed = snapshot.ProfileUsed
        };

        var changes = snapshot.Changes.Where(c => c.AppliedSuccessfully).Reverse().ToList();
        int total = changes.Count;

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var change = changes[i];
            progress?.Report((i + 1, total, change.DisplayName));

            try
            {
                switch (change.Kind)
                {
                    case ChangeKind.ServiceStartMode:
                        if (Enum.TryParse<ServiceStartMode>(change.PreviousValue, out var prevStartMode))
                        {
                            _services.SetStartMode(change.TargetId, prevStartMode);
                            restored++;
                            RecordDetail(historyRecord, "Restore Service", change.TargetId, "Success");
                        }
                        break;

                    case ChangeKind.StartupEntryState:
                        if (bool.TryParse(change.PreviousValue, out var prevStartupEnabled))
                        {
                            if (prevStartupEnabled) _startup.Enable(change.TargetId);
                            else _startup.Disable(change.TargetId);
                            restored++;
                            RecordDetail(historyRecord, "Restore Startup", change.TargetId, "Success");
                        }
                        break;

                    case ChangeKind.ScheduledTaskState:
                        if (bool.TryParse(change.PreviousValue, out var prevTaskEnabled))
                        {
                            _tasks.SetEnabled(change.TargetId, prevTaskEnabled);
                            restored++;
                            RecordDetail(historyRecord, "Restore Task", change.TargetId, "Success");
                        }
                        break;

                    case ChangeKind.VisualEffectSetting:
                        if (bool.TryParse(change.PreviousValue, out var prevEffectEnabled))
                        {
                            _visuals.ApplyEffect(change.TargetId, prevEffectEnabled);
                            restored++;
                            RecordDetail(historyRecord, "Restore Visual Effect", change.TargetId, "Success");
                        }
                        break;

                    case ChangeKind.PowerPlanSelection:
                        if (!string.IsNullOrWhiteSpace(change.PreviousValue))
                        {
                            _power.SetActivePlan(change.PreviousValue);
                            restored++;
                            RecordDetail(historyRecord, "Restore Power Plan", change.TargetId, "Success");
                        }
                        break;

                    case ChangeKind.PowerOverlay:
                        if (Enum.TryParse<PowerOverlayMode>(change.PreviousValue, out var prevOverlay))
                        {
                            _power.SetOverlay(prevOverlay);
                            restored++;
                            RecordDetail(historyRecord, "Restore Power Overlay", change.TargetId, "Success");
                        }
                        break;

                    case ChangeKind.RegistryValue:
                    case ChangeKind.PrivacySetting:
                        RestoreRegistryValue(change);
                        restored++;
                        RecordDetail(historyRecord, "Restore Registry", change.TargetId, "Success");
                        break;

                    default:
                        // Some changes like Appx uninstalls are irreversible via direct registry restore
                        RecordDetail(historyRecord, "Skip Irreversible Change", change.TargetId, "Skipped");
                        break;
                }
            }
            catch (Exception ex)
            {
                failed++;
                var err = $"Gagal memulihkan {change.DisplayName}: {ex.Message}";
                errors.Add(err);
                RecordDetail(historyRecord, "Restore " + change.Kind, change.TargetId, "Failed", ex.Message);
                _logger.Warning("RestoreEngine", "RestoreStepFailed", err);
            }
        }

        historyRecord.CompletedUtc = DateTime.UtcNow;
        historyRecord.ChangesApplied = restored;
        historyRecord.ChangesFailed = failed;
        historyRecord.RestartRequired = restartReq;
        historyRecord.ResultSummary = $"Pemulihan selesai: {restored} berhasil, {failed} gagal.";

        _historyRepo.Save(historyRecord);

        snapshot.Status = failed == 0 ? OperationStatus.RolledBack : OperationStatus.Failed;
        _snapshotRepo.Save(snapshot);

        return new RestoreResult
        {
            Success = failed == 0,
            RestoredCount = restored,
            FailedCount = failed,
            Errors = errors,
            RestartRequired = restartReq
        };
    }

    private void RestoreRegistryValue(SnapshotChange change)
    {
        // TargetId format: ROOT\SubKey\ValueName or Root\SubKey
        var parts = change.TargetId.Split('\\');
        if (parts.Length < 2) return;

        var rootStr = parts[0];
        var root = rootStr.Equals("HKCU", StringComparison.OrdinalIgnoreCase) || rootStr.Equals("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase)
            ? RegRoot.CurrentUser
            : RegRoot.LocalMachine;

        var valName = parts[^1];
        var subKey = string.Join('\\', parts.Skip(1).Take(parts.Length - 2));

        if (change.PreviousValue == null)
        {
            // Value was newly created, delete it to restore
            _registry.DeleteValue(root, subKey, valName);
        }
        else if (int.TryParse(change.PreviousValue, out var intVal))
        {
            _registry.SetValue(root, subKey, valName, intVal, RegistryValueKind.DWord);
        }
        else
        {
            _registry.SetValue(root, subKey, valName, change.PreviousValue, RegistryValueKind.String);
        }
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