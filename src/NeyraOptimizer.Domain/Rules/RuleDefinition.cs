using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.Domain.Rules;

public enum RuleArea
{
    Startup = 0,
    Services = 1,
    ScheduledTasks = 2,
    Debloat = 3,
    BackgroundApps = 4,
    VisualEffects = 5,
    Power = 6,
    Privacy = 7,
    Cleanup = 8,
}

/// <summary>
/// Metadata for one optimization rule. Detectors/appliers live in typed modules keyed by
/// <see cref="RuleId"/>; this model is the serializable contract shared with persistence.
/// Every rule carries its own version so rule updates are visible to the user.
/// </summary>
public sealed class RuleDefinition
{
    public required string RuleId { get; init; }
    public int RuleVersion { get; init; } = 1;
    public required string Name { get; init; }
    public required string Description { get; init; }
    /// <summary>Why the engine recommends (or warns about) this change.</summary>
    public string Rationale { get; init; } = string.Empty;
    public RuleArea Area { get; init; }
    public RecommendationCategory Category { get; init; }
    public RiskLevel RiskLevel { get; init; }

    /// <summary>Minimum Windows 10 build this rule applies to. Rules outside the range are skipped and logged.</summary>
    public int MinBuild { get; init; } = WindowsIdentityInfo.MinimumSupportedBuild;
    /// <summary>Inclusive maximum build. Int32.MaxValue means all known builds.</summary>
    public int MaxBuild { get; init; } = int.MaxValue;

    public bool RequiresAdministrator { get; init; }
    public bool RequiresRestart { get; init; }

    /// <summary>Protected rules touch components the Safety Engine refuses to modify by default.</summary>
    public bool IsProtected { get; init; }

    /// <summary>Human readable list of affected components/services/packages.</summary>
    public IReadOnlyList<string> AffectedComponents { get; init; } = Array.Empty<string>();

    public string RollbackDescription { get; init; } = string.Empty;

    /// <summary>Opaque rule payload understood by the implementing module (e.g. service name, registry path).</summary>
    public IReadOnlyDictionary<string, string> Payload { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Usage profiles for which this rule is proposed as a default selection.</summary>
    public UsageProfileKind SuggestedForProfiles { get; init; } = UsageProfileKind.Balanced
        | UsageProfileKind.LowEnd | UsageProfileKind.Office | UsageProfileKind.Gaming | UsageProfileKind.BatterySaver;

    [Flags]
    public enum UsageProfileKind
    {
        None = 0,
        Balanced = 1,
        LowEnd = 2,
        Office = 4,
        Gaming = 8,
        BatterySaver = 16,
    }
}

public static class UsageProfileMap
{
    public static RuleDefinition.UsageProfileKind ToFlag(UsageProfileKind kind) => kind switch
    {
        UsageProfileKind.Balanced => RuleDefinition.UsageProfileKind.Balanced,
        UsageProfileKind.LowEnd => RuleDefinition.UsageProfileKind.LowEnd,
        UsageProfileKind.Office => RuleDefinition.UsageProfileKind.Office,
        UsageProfileKind.Gaming => RuleDefinition.UsageProfileKind.Gaming,
        UsageProfileKind.BatterySaver => RuleDefinition.UsageProfileKind.BatterySaver,
        _ => RuleDefinition.UsageProfileKind.None,
    };
}
