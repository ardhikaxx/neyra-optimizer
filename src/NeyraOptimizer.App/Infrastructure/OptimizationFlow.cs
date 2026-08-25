using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.App.Views.Dialogs;
using NeyraOptimizer.Application.Optimization;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Rules;
using NeyraOptimizer.Optimization.Pipeline;

namespace NeyraOptimizer.App.Infrastructure;

/// <summary>
/// Shared end-to-end apply flow: Preview → Confirm → Execute (progress) → honest Result.
/// Handles restore-point-failure consent and restart prompts. Used by Optimization Center,
/// One-Click Safe and every usage mode so behavior is identical everywhere.
/// </summary>
public static class OptimizationFlow
{
    public sealed record FlowOutcome(bool Applied, bool RestartRequired);

    public static async Task<FlowOutcome?> RunAsync(
        Window owner,
        IServiceProvider sp,
        IReadOnlyList<Recommendation> selected,
        UsageProfileKind? profileKind,
        bool skipPreview = false)
    {
        if (selected.Count == 0) return null;

        var session = sp.GetRequiredService<SessionState>();
        var coordinator = sp.GetRequiredService<IOptimizationCoordinator>();

        // The pipeline needs a device profile. If the user skipped the onboarding scan or
        // automatic scan is off, run a quick analysis here instead of crashing.
        if (session.LastAnalysis is null)
        {
            var analyzer = sp.GetRequiredService<Application.Analysis.IAnalysisOrchestrator>();
            var scanWindow = new ProgressWindow(Translator.Instance["Analyze.ScanInProgress"]) { Owner = owner };
            scanWindow.Show();
            try
            {
                var result = await analyzer.AnalyzeAsync(2, CancellationToken.None);
                session.LastAnalysis = result.Bundle;
                session.LastRecommendations = result.Recommendations;
            }
            catch (Exception ex)
            {
                ConfirmDialog.Ask("Notify.Error", ex.Message, danger: false, confirmText: Translator.Instance["Common.Close"]);
                return null;
            }
            finally
            {
                scanWindow.Close();
            }
        }

        var preview = coordinator.Preview(selected, session.LastAnalysis!.Profile);

        bool createRestorePoint = session.Settings.CreateRestorePointBeforeChanges;
        if (!skipPreview)
        {
            var result = PreviewChangesDialog.Show(preview, selected);
            if (result is null || !result.Confirmed) return null;
            createRestorePoint = result.CreateRestorePoint;
        }

        // Execute with progress; on RestorePointFailedException offer explicit continue-without.
        while (true)
        {
            var progressWindow = new ProgressWindow(Translator.Instance["Progress.Applying"]) { Owner = owner };
            progressWindow.Show();
            try
            {
                var progress = new Progress<(int current, int total, string step)>(t =>
                {
                    ((IProgress<string>)progressWindow.StepProgress)
                        .Report($"{t.step} ({Math.Min(t.current, t.total)}/{t.total})");
                    progressWindow.ReportPercent(t.total == 0 ? 100 : t.current * 100.0 / t.total);
                });

                var execution = await coordinator.ExecuteSelectedAsync(
                    selected, session.LastAnalysis!.Profile, profileKind, createRestorePoint, progress, CancellationToken.None);

                progressWindow.Close();
                ShowResult(owner, sp, execution);
                return new FlowOutcome(true, execution.RestartRequired);
            }
            catch (RestorePointFailedException rpEx)
            {
                progressWindow.Close();
                var message = string.Format(Translator.Instance["Result.RestorePointFailed"], rpEx.Reason);
                var continueAnyway = ConfirmDialog.Ask("Result.AbortedTitle", message,
                    confirmLocKey: "Result.ContinueWithoutRP", danger: true);
                if (!continueAnyway) return null;
                createRestorePoint = false; // user explicitly accepted
                continue;
            }
            catch (OperationCanceledException)
            {
                progressWindow.Close();
                return null;
            }
            catch (Exception ex)
            {
                progressWindow.Close();
                ConfirmDialog.Ask("Notify.Error", ex.Message, danger: false, confirmText: Translator.Instance["Common.Close"]);
                return null;
            }
        }
    }

    private static void ShowResult(Window owner, IServiceProvider sp, OptimizationExecutionResult execution)
    {
        var titleKey = execution.Success ? "Result.SuccessTitle" : "Result.PartialTitle";
        var lines = new System.Text.StringBuilder();
        lines.AppendLine(string.Format(Translator.Instance["Result.AppliedCount"], execution.AppliedCount));
        lines.AppendLine(string.Format(Translator.Instance["Result.FailedCount"], execution.FailedCount));
        lines.AppendLine(string.Format(Translator.Instance["Result.SkippedCount"], execution.SkippedCount));
        foreach (var e in execution.Errors.Take(8))
            lines.AppendLine("• " + e);
        if (execution.Errors.Count > 8)
            lines.AppendLine($"… (+{execution.Errors.Count - 8})");

        var restart = execution.RestartRequired
            ? ConfirmDialog.Ask(titleKey, lines.ToString(), confirmLocKey: "Result.RestartNow")
            : ConfirmDialog.Ask(titleKey, lines.ToString(), confirmText: Translator.Instance["Common.OK"]);

        if (execution.RestartRequired && restart)
        {
            var psi = new System.Diagnostics.ProcessStartInfo("shutdown", "/r /t 5")
            {
                UseShellExecute = true,
                Verb = "runas", // restart needs elevation; UAC shown once
            };
            try { System.Diagnostics.Process.Start(psi); }
            catch { /* user declined UAC — stay open */ }
        }
    }
}
