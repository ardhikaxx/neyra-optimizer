namespace NeyraOptimizer.Domain.Enums;

/// <summary>Risk level attached to every rule, recommendation and change.</summary>
public enum RiskLevel
{
    Safe = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

/// <summary>Visibility category of a recommendation in the Optimization Center.</summary>
public enum RecommendationCategory
{
    Safe = 0,
    Recommended = 1,
    Optional = 2,
    Advanced = 3,
    DoNotModify = 4,
}

/// <summary>Automatic device class computed by the analyzer. Never based on RAM alone.</summary>
public enum DeviceClass
{
    Unknown = 0,
    LowEnd = 1,
    EntryLevel = 2,
    Balanced = 3,
    MidRange = 4,
    HighPerformance = 5,
    Gaming = 6,
    Custom = 7,
}

/// <summary>User selected usage profile that steers recommendation weighting.</summary>
public enum UsageProfileKind
{
    Balanced = 0,
    LowEnd = 1,
    Office = 2,
    Gaming = 3,
    BatterySaver = 4,
}

/// <summary>Classification of a Windows service by the rule engine.</summary>
public enum ServiceClassification
{
    Required = 0,
    Recommended = 1,
    Optional = 2,
    Advanced = 3,
    Protected = 4,
}

public enum StorageMediaType
{
    Unknown = 0,
    Ssd = 1,
    Hdd = 2,
    Hybrid = 3,
}

public enum ChassisKind
{
    Unknown = 0,
    Desktop = 1,
    Laptop = 2,
    Tablet = 3,
}

public enum PowerSource
{
    Unknown = 0,
    AcPower = 1,
    Battery = 2,
}

public enum OperationStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4,
    Cancelled = 5,
    RolledBack = 6,
    RequiresRestart = 7,
}

public enum LogSeverity
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4,
}

public enum ThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2,
}

public enum LanguagePreference
{
    English = 0,
    Indonesian = 1,
}

public enum VisualEffectsPreset
{
    Current = 0,
    BestAppearance = 1,
    Balanced = 2,
    BestPerformance = 3,
}

public enum StartupImpact
{
    Unknown = 0,
    None = 1,
    Low = 2,
    Medium = 3,
    High = 4,
}
