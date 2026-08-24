using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Infrastructure.Logging;
using NeyraOptimizer.Domain.Models.Power;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Optimization.Pipeline;

namespace NeyraOptimizer.Application.Modules;

/// <summary>Dry-run scan + explicit-apply storage cleanup.</summary>
public interface ICleanupCoordinator
{
    IReadOnlyList<CleanupCandidate> Scan(CancellationToken ct);
    /// <summary>Returns freed bytes. The candidate was previewed and explicitly confirmed by the user.</summary>
    Task<long> DeleteAsync(CleanupCandidate candidate, IProgress<string>? progress, CancellationToken ct);
}

public sealed class CleanupCoordinator : ICleanupCoordinator
{
    private readonly ICleanupScanner _scanner;
    private readonly INeyraLogger _logger;

    public CleanupCoordinator(ICleanupScanner scanner, INeyraLogger logger)
    {
        _scanner = scanner;
        _logger = logger;
    }

    public IReadOnlyList<CleanupCandidate> Scan(CancellationToken ct) =>
        _scanner.Scan(ct);

    public async Task<long> DeleteAsync(CleanupCandidate candidate, IProgress<string>? progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long freed = await _scanner.DeleteCandidateAsync(candidate,
            new Progress<(long freedBytes, string currentPath)>(t => progress?.Report(t.currentPath)), ct)
            .ConfigureAwait(false);
        _logger.Operation(LogSeverity.Info, "Cleanup", "Delete",
            $"Membersihkan '{candidate.DisplayName}': ~{freed / (1024.0 * 1024):0.#} MB dibebaskan.", sw.Elapsed);
        return freed;
    }
}
