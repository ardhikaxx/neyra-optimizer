using System.Text.Json.Serialization;
using Microsoft.Win32;
using NeyraOptimizer.Domain.Abstractions;

namespace NeyraOptimizer.Security.Elevation;

public enum ElevatedOperationKind
{
    None = 0,
    CreateRestorePoint = 1,
    SetServiceStartMode = 2,
    StopService = 3,
    SetTaskEnabled = 4,
    RemoveProvisionedPackage = 5,
    DeleteDeliveryOptimizationCache = 6,
    ApplyRegistryWrites = 7,
    /// <summary>Runs a list of child operations under ONE elevation prompt. Children may nest.</summary>
    ApplyBatch = 8,
}

/// <summary>
/// Strictly typed payload passed to the elevated helper process. There is intentionally NO
/// free-form command field: every operation kind has dedicated validated properties.
/// </summary>
public sealed class ElevatedOperationRequest
{
    public Guid OperationId { get; init; } = Guid.NewGuid();
    public ElevatedOperationKind Kind { get; init; }

    // Service operations
    public string? ServiceName { get; init; }
    public int StartModeValue { get; init; } = -1;

    // Scheduled task operation
    public string? TaskPath { get; init; }
    public bool TaskEnabled { get; init; }

    // Package operation
    public string? PackageFullName { get; init; }

    // Restore point
    public string? RestorePointDescription { get; init; }

    // Registry batch
    public List<ElevatedRegistryWrite> RegistryWrites { get; init; } = new();

    // Nested batch (Kind == ApplyBatch). Depth-limited and recursively validated.
    public List<ElevatedOperationRequest> Operations { get; init; } = new();
}

public sealed record BatchOperationOutcome(ElevatedOperationRequest Request, ElevatedOperationResult Result);

public sealed class ElevatedRegistryWrite
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RegRoot Root { get; init; }
    public string SubKey { get; init; } = string.Empty;
    public string ValueName { get; init; } = string.Empty;
    public string? StringData { get; init; }
    public int DWordData { get; init; }
    /// <summary>Hex-encoded bytes for REG_BINARY.</summary>
    public string? BinaryDataHex { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RegistryValueKind Kind { get; init; }
    /// <summary>When true the value is deleted instead of written.</summary>
    public bool DeleteValue { get; init; }
}

public sealed record ElevatedOperationResult
{
    public Guid OperationId { get; init; }
    public bool Success { get; init; }
    public string ErrorText { get; init; } = string.Empty;
    /// <summary>Human readable outcome detail (e.g. restore point sequence number).</summary>
    public string Detail { get; init; } = string.Empty;
}
