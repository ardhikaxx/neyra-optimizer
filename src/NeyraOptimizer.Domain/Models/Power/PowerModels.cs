using NeyraOptimizer.Domain.Enums;

namespace NeyraOptimizer.Domain.Models.Power;

public sealed class PowerPlanInfo
{
    public required string PlanGuid { get; init; }
    public required string Name { get; init; }
    public bool IsActive { get; init; }
    /// <summary>Well-known plan kind when it maps to one of the built-in plans.</summary>
    public WellKnownPowerPlan WellKnownKind { get; init; }
    public string RecommendationNote { get; init; } = string.Empty;
}

public enum WellKnownPowerPlan
{
    Other = 0,
    Balanced = 1,
    PowerSaver = 2,
    HighPerformance = 3,
    UltimatePerformance = 4,
}

public enum PowerOverlayMode
{
    NotSupported = 0,
    BetterBattery = 1,
    Balanced = 2,
    BestPerformance = 3,
}

public sealed class VisualEffectItem
{
    /// <summary>Stable key such as "MinAnimate", "TaskbarAnimations", "EnableTransparency".</summary>
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public bool CurrentEnabled { get; init; }
    public bool ProposedEnabled { get; init; }
    /// <summary>True when the change takes effect immediately without sign-out.</summary>
    public bool TakesEffectImmediately { get; init; }
    public string EffectDescription { get; init; } = string.Empty;
}

public enum CleanupCategory
{
    UserTempFiles = 0,
    WindowsTempFiles = 1,
    RecycleBin = 2,
    DeliveryOptimizationCache = 3,
    WindowsUpdateDownloadCache = 4,
    ThumbnailCache = 5,
    ErrorReports = 6,
    DirectXShaderCache = 7,
}

public sealed class CleanupCandidate
{
    public CleanupCategory Category { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    /// <summary>Concrete locations scanned for this candidate. Never user document folders.</summary>
    public IReadOnlyList<string> Locations { get; init; } = Array.Empty<string>();
    public long EstimatedSizeBytes { get; init; }
    public bool RequiresAdministrator { get; init; }
    public bool SafeByDefault { get; init; } = true;
    public RiskLevel RiskLevel { get; init; } = RiskLevel.Safe;
    public bool IsAvailableOnThisMachine { get; init; } = true;
    public string UnavailabilityReason { get; init; } = string.Empty;
}
