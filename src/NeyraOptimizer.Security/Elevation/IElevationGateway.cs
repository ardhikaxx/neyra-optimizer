using NeyraOptimizer.Security.Elevation;

namespace NeyraOptimizer.Security.Elevation;

/// <summary>
/// Abstraction over the UAC elevation mechanism so the optimization pipeline stays testable.
/// The production implementation launches a one-shot elevated helper process; fakes apply the
/// request directly against test doubles.
/// </summary>
public interface IElevationGateway
{
    /// <summary>True when the current process already runs with an administrator token.</summary>
    bool IsCurrentProcessElevated();

    Task<ElevatedOperationResult> RunAsync(ElevatedOperationRequest request, CancellationToken ct);
}

/// <summary>Production gateway delegating to <see cref="ElevationManager"/>.</summary>
public sealed class ElevationGateway : IElevationGateway
{
    private readonly ElevationManager _manager = new();

    public bool IsCurrentProcessElevated() => _manager.IsCurrentProcessElevated();

    public Task<ElevatedOperationResult> RunAsync(ElevatedOperationRequest request, CancellationToken ct) =>
        _manager.RunElevatedAsync(request, ct);
}
