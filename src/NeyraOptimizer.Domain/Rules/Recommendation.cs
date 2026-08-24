using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.Domain.Rules;

/// <summary>A concrete, user-visible proposal derived from a rule plus live system state.</summary>
public sealed class Recommendation
{
    public required string RuleId { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    /// <summary>Evidence from the current machine that produced this recommendation.</summary>
    public required string Reason { get; init; }
    /// <summary>Estimated impact phrased honestly ("estimated", ranges) or empty when unknown.</summary>
    public string EstimatedImpact { get; init; } = string.Empty;
    public RecommendationCategory Category { get; init; }
    public RiskLevel RiskLevel { get; init; }
    public bool RequiresAdministrator { get; init; }
    public bool RequiresRestart { get; init; }
    public IReadOnlyList<string> AffectedComponents { get; init; } = Array.Empty<string>();
    public string RollbackDescription { get; init; } = string.Empty;
    public bool IsSelected { get; set; }
    public bool IsAvailableOnThisMachine { get; init; } = true;
    public string UnavailabilityReason { get; init; } = string.Empty;
    public string CurrentStateText { get; init; } = string.Empty;
    public string ProposedStateText { get; init; } = string.Empty;
    /// <summary>Target identifier used by the applying module (service name / task path / package full name...).</summary>
    public string TargetId { get; init; } = string.Empty;
    public RuleArea Area { get; init; }
}

public sealed class AnalysisBundle
{
    public required SystemProfile Profile { get; init; }
    public IReadOnlyList<StartupEntry> StartupEntries { get; init; } = Array.Empty<StartupEntry>();
    public IReadOnlyList<ServiceInfo> Services { get; init; } = Array.Empty<ServiceInfo>();
    public IReadOnlyList<ScheduledTaskInfo> Tasks { get; init; } = Array.Empty<ScheduledTaskInfo>();
    public IReadOnlyList<InstalledAppInfo> InstalledApps { get; init; } = Array.Empty<InstalledAppInfo>();
    public IReadOnlyList<BackgroundProcessInfo> BackgroundProcesses { get; init; } = Array.Empty<BackgroundProcessInfo>();
    public PerformanceSnapshot? Baseline { get; init; }
}

/// <summary>Transparent, deterministic performance score computed from measurable indicators only.</summary>
public sealed class PerformanceScoreResult
{
    public int Score { get; init; }
    public required string Band { get; init; }
    public IReadOnlyList<ScoreComponent> Components { get; init; } = Array.Empty<ScoreComponent>();
}

public sealed record ScoreComponent(string Key, string Label, double Weight, double EarnedPoints, double MaxPoints, string Detail);
