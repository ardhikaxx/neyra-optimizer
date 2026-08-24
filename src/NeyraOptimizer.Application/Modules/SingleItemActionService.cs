using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Infrastructure.Logging;
using NeyraOptimizer.Domain.Models.Power;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Optimization.Pipeline;
using NeyraOptimizer.Domain.Rules;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Application.Optimization;
using NeyraOptimizer.Application.Session;

namespace NeyraOptimizer.Application.Modules;

/// <summary>
/// Applies single-item changes from manager pages (one startup entry, one service, one task,
/// one visual effect, one power plan). Each action is wrapped as a one-item recommendation so it
/// flows through the standard pipeline: snapshot â†’ safety â†’ apply â†’ verify â†’ history.
/// </summary>
public interface ISingleItemActionService
{
    Task<OptimizationExecutionResult> ToggleStartupAsync(StartupEntry entry, bool enable, CancellationToken ct);
    Task<OptimizationExecutionResult> SetServiceStartModeAsync(ServiceInfo service, ServiceStartMode mode, CancellationToken ct);
    Task<OptimizationExecutionResult> SetTaskEnabledAsync(ScheduledTaskInfo task, bool enable, CancellationToken ct);
    Task<OptimizationExecutionResult> UninstallAppAsync(InstalledAppInfo app, CancellationToken ct);
    Task<OptimizationExecutionResult> SetBackgroundAppEnabledAsync(string packageFamilyName, string displayName, bool enable, CancellationToken ct);
    Task<OptimizationExecutionResult> ApplyVisualEffectAsync(VisualEffectItem effect, CancellationToken ct);
    Task<OptimizationExecutionResult> SetPowerPlanAsync(PowerPlanInfo plan, CancellationToken ct);
    Task<OptimizationExecutionResult> SetPowerOverlayAsync(PowerOverlayMode mode, PowerOverlayMode current, CancellationToken ct);
    Task<OptimizationExecutionResult> ApplyPrivacyAsync(Domain.Rules.Recommendation rec, CancellationToken ct);
}

public sealed class SingleItemActionService : ISingleItemActionService
{
    private readonly IOptimizationCoordinator _coordinator;
    private readonly SessionState _session;

    public SingleItemActionService(IOptimizationCoordinator coordinator, SessionState session)
    {
        _coordinator = coordinator;
        _session = session;
    }

    private SystemProfile? Profile => _session.LastAnalysis?.Profile
        ?? throw new InvalidOperationException("Jalankan analisis terlebih dahulu sebelum mengubah sistem.");

    public Task<OptimizationExecutionResult> ToggleStartupAsync(StartupEntry entry, bool enable, CancellationToken ct) =>
        Execute(new Recommendation
        {
            RuleId = "item_startup_toggle",
            Title = (enable ? "Aktifkan startup: " : "Nonaktifkan startup: ") + entry.Name,
            Description = "Perubahan manual dari halaman Startup Manager.",
            Reason = "Tindakan pengguna.",
            Category = RecommendationCategory.Safe,
            RiskLevel = RiskLevel.Safe,
            RequiresAdministrator = entry.Source is StartupSource.RunKeyLocalMachine or StartupSource.RunKeyLocalMachineWow64 or StartupSource.StartupFolderCommon,
            AffectedComponents = new[] { entry.Name },
            RollbackDescription = "Gunakan tombol Enable/Disable yang sama untuk membalik.",
            TargetId = entry.Id,
            CurrentStateText = (!enable).ToString(),
            ProposedStateText = enable ? "Enabled" : "Disabled",
            Area = RuleArea.Startup,
        }, ct);

    public Task<OptimizationExecutionResult> SetServiceStartModeAsync(ServiceInfo service, ServiceStartMode mode, CancellationToken ct) =>
        Execute(new Recommendation
        {
            RuleId = "item_service_mode",
            Title = $"Ubah service '{service.ServiceName}': {mode}",
            Description = "Perubahan manual dari halaman Services Manager.",
            Reason = "Tindakan pengguna.",
            Category = RecommendationCategory.Optional,
            RiskLevel = RiskLevel.Medium,
            RequiresAdministrator = true,
            AffectedComponents = new[] { service.ServiceName },
            RollbackDescription = $"Kembalikan ke {service.StartMode}.",
            TargetId = service.ServiceName,
            CurrentStateText = service.StartMode.ToString(),
            ProposedStateText = ModeText(mode),
            Area = RuleArea.Services,
        }, ct);

    public Task<OptimizationExecutionResult> SetTaskEnabledAsync(ScheduledTaskInfo task, bool enable, CancellationToken ct) =>
        Execute(new Recommendation
        {
            RuleId = "item_task_toggle",
            Title = (enable ? "Aktifkan task: " : "Nonaktifkan task: ") + task.Name,
            Description = "Perubahan manual dari halaman Scheduled Tasks.",
            Reason = "Tindakan pengguna.",
            Category = RecommendationCategory.Optional,
            RiskLevel = RiskLevel.Medium,
            RequiresAdministrator = true,
            AffectedComponents = new[] { task.TaskPath },
            RollbackDescription = "Balik status enabled dari halaman yang sama.",
            TargetId = task.TaskPath,
            CurrentStateText = (!enable).ToString(),
            ProposedStateText = enable ? "Enabled" : "Disabled",
            Area = RuleArea.ScheduledTasks,
        }, ct);

    public Task<OptimizationExecutionResult> UninstallAppAsync(InstalledAppInfo app, CancellationToken ct) =>
        Execute(new Recommendation
        {
            RuleId = "item_app_uninstall",
            Title = $"Uninstall: {app.DisplayName}",
            Description = "Penghapusan aplikasi pilihan pengguna. Tindakan ini tidak dapat dibalik otomatis oleh Neyra.",
            Reason = "Konfirmasi eksplisit pengguna pada halaman Debloat.",
            EstimatedImpact = app.SizeBytes is long s && s > 0 ? $"~{s / (1024.0 * 1024):0.#} MB disk space." : string.Empty,
            Category = RecommendationCategory.Optional,
            RiskLevel = RiskLevel.Medium,
            AffectedComponents = new[] { app.DisplayName },
            RollbackDescription = string.IsNullOrEmpty(app.ReinstallNote) ? "Reinstall manual dari sumber resmi." : app.ReinstallNote,
            TargetId = app.Id,
            CurrentStateText = "Installed",
            ProposedStateText = "Uninstalled",
            Area = RuleArea.Debloat,
        }, ct);

    public Task<OptimizationExecutionResult> SetBackgroundAppEnabledAsync(string packageFamilyName, string displayName, bool enable, CancellationToken ct) =>
        Execute(new Recommendation
        {
            RuleId = "item_background_toggle",
            Title = (enable ? "Izinkan background: " : "Batasi background: ") + displayName,
            Description = "Mengatur izin eksekusi background melalui mekanisme resmi Windows.",
            Reason = "Tindakan pengguna.",
            Category = RecommendationCategory.Safe,
            RiskLevel = RiskLevel.Safe,
            AffectedComponents = new[] { displayName },
            RollbackDescription = "Balik toggle dari halaman Background Apps.",
            TargetId = packageFamilyName,
            CurrentStateText = (!enable).ToString(),
            ProposedStateText = enable ? "Enabled" : "Disabled",
            Area = RuleArea.BackgroundApps,
        }, ct);

    public Task<OptimizationExecutionResult> ApplyVisualEffectAsync(VisualEffectItem effect, CancellationToken ct) =>
        Execute(new Recommendation
        {
            RuleId = "item_visual_effect",
            Title = $"{effect.DisplayName}: {(effect.ProposedEnabled ? "ON" : "OFF")}",
            Description = "Perubahan efek visual dari halaman Visual Effects.",
            Reason = "Tindakan pengguna.",
            Category = RecommendationCategory.Safe,
            RiskLevel = RiskLevel.Safe,
            AffectedComponents = new[] { effect.Key },
            RollbackDescription = $"Kembalikan ke {(effect.CurrentEnabled ? "ON" : "OFF")}.",
            TargetId = effect.Key,
            CurrentStateText = effect.CurrentEnabled.ToString(),
            ProposedStateText = effect.ProposedEnabled.ToString(),
            Area = RuleArea.VisualEffects,
        }, ct);

    public Task<OptimizationExecutionResult> SetPowerPlanAsync(PowerPlanInfo plan, CancellationToken ct) =>
        Execute(new Recommendation
        {
            RuleId = "power_plan_select",
            Title = "Power plan: " + plan.Name,
            Description = "Mengaktifkan skema daya pilihan pengguna.",
            Reason = "Tindakan pengguna.",
            Category = RecommendationCategory.Safe,
            RiskLevel = RiskLevel.Safe,
            AffectedComponents = new[] { plan.PlanGuid },
            RollbackDescription = "Pilih kembali plan sebelumnya dari halaman Power.",
            TargetId = "PLAN:" + plan.PlanGuid,
            CurrentStateText = string.Empty,
            ProposedStateText = plan.Name,
            Area = RuleArea.Power,
        }, ct);

    public Task<OptimizationExecutionResult> SetPowerOverlayAsync(PowerOverlayMode mode, PowerOverlayMode current, CancellationToken ct) =>
        Execute(new Recommendation
        {
            RuleId = "power_overlay_manual",
            Title = "Power overlay: " + mode,
            Description = "Mengubah power mode (overlay) Windows dari halaman Power & Performance.",
            Reason = "Tindakan pengguna.",
            Category = RecommendationCategory.Safe,
            RiskLevel = RiskLevel.Safe,
            AffectedComponents = new[] { "Power Overlay" },
            RollbackDescription = $"Kembalikan ke {current}.",
            TargetId = "EffectiveOverlay",
            CurrentStateText = current.ToString(),
            ProposedStateText = mode.ToString(),
            Area = RuleArea.Power,
        }, ct);

    public Task<OptimizationExecutionResult> ApplyPrivacyAsync(Domain.Rules.Recommendation rec, CancellationToken ct) =>
        Execute(rec, ct);

    private async Task<OptimizationExecutionResult> Execute(Recommendation rec, CancellationToken ct) =>
        await _coordinator.ExecuteSelectedAsync(
            new[] { rec }, Profile, usageProfile: null,
            createRestorePoint: false,
            progress: null, ct).ConfigureAwait(false);

    private static string ModeText(ServiceStartMode mode) => mode switch
    {
        ServiceStartMode.AutomaticDelayed => "Automatic (Delayed)",
        _ => mode.ToString(),
    };
}
