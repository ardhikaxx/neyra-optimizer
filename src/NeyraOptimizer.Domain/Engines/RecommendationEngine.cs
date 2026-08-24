using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Domain.Rules;

namespace NeyraOptimizer.Domain.Engines;

public interface IRecommendationEngine
{
    /// <summary>
    /// Produces recommendations by combining the rules catalog with live machine state.
    /// Pure function of its inputs: fully unit-testable.
    /// </summary>
    IReadOnlyList<Recommendation> BuildRecommendations(
        AnalysisBundle bundle,
        IReadOnlyList<RuleDefinition> catalog,
        UsageProfileKind usageProfile,
        bool advancedModeEnabled);
}

/// <summary>
/// Default implementation. Filters by OS build, availability and protection status; adjusts default
/// selection by usage profile; NEVER invents impact numbers — estimates come from rule metadata only.
/// </summary>
public sealed class RecommendationEngine : IRecommendationEngine
{
    public IReadOnlyList<Recommendation> BuildRecommendations(
        AnalysisBundle bundle,
        IReadOnlyList<RuleDefinition> catalog,
        UsageProfileKind usageProfile,
        bool advancedModeEnabled)
    {
        var results = new List<Recommendation>();
        int build = bundle.Profile.Windows.BuildNumber;

        foreach (var rule in catalog)
        {
            if (rule.Category == RecommendationCategory.DoNotModify && !advancedModeEnabled)
                continue; // documentation-only rules stay invisible outside advanced mode

            if (build < rule.MinBuild || build > rule.MaxBuild)
                continue;

            // Advanced rules are only surfaced when advanced mode is explicitly enabled.
            if (rule.Category == RecommendationCategory.Advanced && !advancedModeEnabled)
                continue;

            var rec = TryBuild(bundle, rule, usageProfile);
            if (rec is not null)
                results.Add(rec);
        }

        return results
            .OrderBy(r => CategoryOrder(r.Category))
            .ThenBy(r => (int)r.RiskLevel)
            .ToList();
    }

    private static int CategoryOrder(RecommendationCategory c) => c switch
    {
        RecommendationCategory.Safe => 0,
        RecommendationCategory.Recommended => 1,
        RecommendationCategory.Optional => 2,
        RecommendationCategory.Advanced => 3,
        _ => 4,
    };

    private Recommendation? TryBuild(AnalysisBundle bundle, RuleDefinition rule, UsageProfileKind profile)
    {
        switch (rule.Area)
        {
            case RuleArea.Services:
                return BuildServiceRule(bundle, rule, profile);
            case RuleArea.ScheduledTasks:
                return BuildTaskRule(bundle, rule, profile);
            case RuleArea.Debloat:
                return BuildDebloatRule(bundle, rule, profile);
            case RuleArea.Privacy:
            case RuleArea.VisualEffects:
            case RuleArea.Power:
            case RuleArea.BackgroundApps:
            case RuleArea.Cleanup:
            case RuleArea.Startup:
                // These areas generate dynamic per-item recommendations inside their modules;
                // static catalog entries are surfaced through those modules instead.
                return null;
            default:
                return null;
        }
    }

    private Recommendation? BuildServiceRule(AnalysisBundle bundle, RuleDefinition rule, UsageProfileKind profile)
    {
        if (!rule.Payload.TryGetValue("ServiceName", out var serviceName) &&
            !rule.Payload.TryGetValue("TargetId", out serviceName))
            return null;

        var svc = bundle.Services.FirstOrDefault(s =>
            s.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
        if (svc is null)
            return null; // service not present on this build → skip silently but module logs it

        if (!Enum.TryParse<ServiceStartMode>(rule.Payload.GetValueOrDefault("ProposedStartMode", "Manual"), true, out var proposed))
            proposed = ServiceStartMode.Manual;

        if (svc.StartMode == ServiceStartMode.Disabled && proposed != ServiceStartMode.Disabled)
            return null; // already more restrictive than proposal

        if (svc.StartMode == proposed)
            return null; // nothing to do

        bool suggested = (rule.SuggestedForProfiles & UsageProfileMap.ToFlag(profile)) != 0;
        string currentText = svc.StartMode == ServiceStartMode.AutomaticDelayed ? "Automatic (Delayed)" : svc.StartMode.ToString();

        return new Recommendation
        {
            RuleId = rule.RuleId,
            Title = rule.Name,
            Description = rule.Description,
            Reason = $"Service '{svc.ServiceName}' is currently {currentText}. {rule.Rationale}",
            EstimatedImpact = rule.Payload.GetValueOrDefault("ImpactNote", string.Empty),
            Category = rule.Category,
            RiskLevel = rule.RiskLevel,
            RequiresAdministrator = rule.RequiresAdministrator,
            RequiresRestart = rule.RequiresRestart,
            AffectedComponents = rule.AffectedComponents,
            RollbackDescription = rule.RollbackDescription,
            IsSelected = suggested && !rule.IsProtected && rule.Category <= RecommendationCategory.Recommended,
            TargetId = svc.ServiceName,
            CurrentStateText = currentText,
            ProposedStateText = proposed == ServiceStartMode.AutomaticDelayed ? "Automatic (Delayed)" : proposed.ToString(),
            Area = rule.Area,
        };
    }

    private Recommendation? BuildTaskRule(AnalysisBundle bundle, RuleDefinition rule, UsageProfileKind profile)
    {
        if (!rule.Payload.TryGetValue("TaskPath", out var taskPath) &&
            !rule.Payload.TryGetValue("TargetId", out taskPath))
            return null;

        var task = bundle.Tasks.FirstOrDefault(t =>
            t.TaskPath.Equals(taskPath, StringComparison.OrdinalIgnoreCase));
        if (task is null || !task.IsEnabled)
            return null;

        bool suggested = (rule.SuggestedForProfiles & UsageProfileMap.ToFlag(profile)) != 0;

        return new Recommendation
        {
            RuleId = rule.RuleId,
            Title = rule.Name,
            Description = rule.Description,
            Reason = $"Scheduled task '{task.TaskPath}' is enabled. {rule.Rationale}",
            EstimatedImpact = rule.Payload.GetValueOrDefault("ImpactNote", string.Empty),
            Category = rule.Category,
            RiskLevel = rule.RiskLevel,
            RequiresAdministrator = rule.RequiresAdministrator,
            RequiresRestart = rule.RequiresRestart,
            AffectedComponents = rule.AffectedComponents,
            RollbackDescription = rule.RollbackDescription,
            IsSelected = suggested && rule.Category <= RecommendationCategory.Recommended,
            TargetId = task.TaskPath,
            CurrentStateText = "Enabled",
            ProposedStateText = "Disabled",
            Area = rule.Area,
        };
    }

    private Recommendation? BuildDebloatRule(AnalysisBundle bundle, RuleDefinition rule, UsageProfileKind profile)
    {
        if (!rule.Payload.TryGetValue("MatchName", out var match) &&
            !rule.Payload.TryGetValue("TargetId", out match))
            return null;

        var app = bundle.InstalledApps.FirstOrDefault(a =>
            a.DisplayName.Contains(match, StringComparison.OrdinalIgnoreCase) ||
            (a.PackageFamilyName?.Contains(match, StringComparison.OrdinalIgnoreCase) ?? false) ||
            a.Id.Contains(match, StringComparison.OrdinalIgnoreCase));
        if (app is null || app.IsProtected)
            return null;

        bool suggested = (rule.SuggestedForProfiles & UsageProfileMap.ToFlag(profile)) != 0 &&
                         rule.Category <= RecommendationCategory.Optional;

        return new Recommendation
        {
            RuleId = rule.RuleId,
            Title = rule.Name,
            Description = rule.Description,
            Reason = $"'{app.DisplayName}' ({app.Publisher}) version {app.Version} is installed. {rule.Rationale}",
            EstimatedImpact = app.SizeBytes is long size && size > 0
                ? $"Estimated disk space freed: ~{size / (1024.0 * 1024):0.#} MB."
                : string.Empty,
            Category = rule.Category,
            RiskLevel = rule.RiskLevel,
            RequiresAdministrator = rule.RequiresAdministrator,
            RequiresRestart = false,
            AffectedComponents = rule.AffectedComponents,
            RollbackDescription = string.IsNullOrEmpty(app.ReinstallNote)
                ? "Uninstalling cannot be reversed automatically by this tool."
                : app.ReinstallNote,
            IsSelected = false, // uninstall always requires explicit user opt-in
            TargetId = app.Id,
            CurrentStateText = "Installed",
            ProposedStateText = "Uninstalled",
            Area = rule.Area,
        };
    }
}
