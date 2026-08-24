using NeyraOptimizer.Domain.Enums;

namespace NeyraOptimizer.Domain.Snapshots;

public enum ChangeKind
{
    RegistryValue = 0,
    ServiceStartMode = 1,
    StartupEntryState = 2,
    ScheduledTaskState = 3,
    AppxPackageRemoval = 4,
    VisualEffectSetting = 5,
    PowerPlanSelection = 6,
    PowerOverlay = 7,
    BackgroundAppSetting = 8,
    FileDeletion = 9,
    ProcessTermination = 10,
    PrivacySetting = 11,
}

/// <summary>One reversible change recorded inside an Optimization Snapshot.</summary>
public sealed class SnapshotChange
{
    public required ChangeKind Kind { get; init; }
    /// <summary>Precise target: full registry path incl. value name, service key name, full task path, package full name.</summary>
    public required string TargetId { get; init; }
    public required string DisplayName { get; init; }
    public string? PreviousValue { get; init; }
    public string? NewValue { get; init; }

    /// <summary>Opaque JSON restore data (e.g. raw REG_BINARY bytes, exported task XML). Never contains credentials.</summary>
    public string? RestoreDataJson { get; init; }

    public string RuleId { get; init; } = string.Empty;
    public bool AppliedSuccessfully { get; set; }
    public string ErrorText { get; set; } = string.Empty;
}

/// <summary>
/// An Optimization Snapshot is created BEFORE any batch of changes. It is written to ProgramData as
/// a standalone JSON file plus a SHA-256 sidecar so the Emergency Restore page can enumerate and
/// validate snapshots even if the main database is corrupt.
/// </summary>
public sealed class OptimizationSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public string AppVersion { get; init; } = string.Empty;
    public int RulesVersion { get; init; }
    public required string WindowsBuild { get; init; }
    public required string Description { get; init; }
    public UsageProfileKind? ProfileUsed { get; init; }
    public List<SnapshotChange> Changes { get; init; } = new();
    public bool RestorePointCreatedBeforeBatch { get; set; }
    public string RestorePointSequenceNumber { get; set; } = string.Empty;
    public OperationStatus Status { get; set; } = OperationStatus.Pending;
    public int AppliedCount { get; set; }
    public int FailedCount { get; set; }
}

public sealed class HistoryDetailLine
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public required string Action { get; init; }
    public required string Target { get; init; }
    public required string Result { get; init; }
    public string ErrorText { get; init; } = string.Empty;
}

public sealed class HistoryRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime StartedUtc { get; init; }
    public DateTime? CompletedUtc { get; set; }
    public required string Description { get; init; }
    public DeviceClass DeviceClassAtTime { get; init; }
    public UsageProfileKind? ProfileUsed { get; init; }
    public int ChangesApplied { get; set; }
    public int ChangesFailed { get; set; }
    public int ChangesSkipped { get; set; }
    public bool RestartRequired { get; set; }
    public Guid? SnapshotId { get; init; }
    public string ResultSummary { get; set; } = string.Empty;
    public List<HistoryDetailLine> Details { get; init; } = new();
}

/// <summary>Written before a batch starts, updated per phase, deleted on commit. Enables crash recovery.</summary>
public sealed class PendingOperationRecord
{
    public Guid OperationId { get; init; } = Guid.NewGuid();
    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;
    public required string Description { get; init; }
    public required string Phase { get; set; }
    public Guid? SnapshotId { get; init; }
    public string SnapshotPath { get; init; } = string.Empty;
    public int TotalChanges { get; init; }
    public int CompletedChanges { get; set; }
}
