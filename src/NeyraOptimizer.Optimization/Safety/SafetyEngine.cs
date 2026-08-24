using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Domain.Rules;
using NeyraOptimizer.Security.Protection;

namespace NeyraOptimizer.Optimization.Safety;

public sealed record SafetyCheckResult
{
    public bool IsSafeToApply { get; init; }
    public IReadOnlyList<string> BlockingReasons { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool RequiresElevation { get; init; }
    public bool RequiresRestart { get; init; }
}

public interface ISafetyEngine
{
    SafetyCheckResult ValidateRecommendation(Recommendation recommendation, SystemProfile profile);
    SafetyCheckResult ValidateBatch(IReadOnlyList<Recommendation> recommendations, SystemProfile profile, bool isOneClickMode);
}

public sealed class SafetyEngine : ISafetyEngine
{
    public SafetyCheckResult ValidateRecommendation(Recommendation recommendation, SystemProfile profile)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        ArgumentNullException.ThrowIfNull(profile);

        var blockers = new List<string>();
        var warnings = new List<string>();

        // 1. Target protection check
        if (!string.IsNullOrWhiteSpace(recommendation.TargetId))
        {
            if (ProtectedComponents.IsServiceProtected(recommendation.TargetId))
            {
                blockers.Add($"Komponen '{recommendation.TargetId}' dilindungi oleh sistem keamanan inti Windows (Protected System Component).");
            }
            if (ProtectedComponents.IsPackageProtected(recommendation.TargetId))
            {
                blockers.Add($"Paket aplikasi '{recommendation.TargetId}' merupakan bagian penting dari ekosistem Windows dan tidak boleh dihapus.");
            }
            if (ProtectedComponents.IsTaskProtected(recommendation.TargetId))
            {
                blockers.Add($"Task Scheduler '{recommendation.TargetId}' dilindungi untuk menjaga integritas sistem operasi.");
            }
        }

        // 2. Risk level checks
        if (recommendation.RiskLevel >= RiskLevel.High)
        {
            warnings.Add($"Tindakan ini memiliki tingkat risiko {recommendation.RiskLevel}. Pastikan System Restore aktif sebelum melanjutkan.");
        }

        // 3. Hardware dependency checks (e.g. SysMain on HDD vs SSD)
        if (recommendation.RuleId.Equals("service_sysmain", StringComparison.OrdinalIgnoreCase))
        {
            if (!profile.HasSystemSsd)
            {
                // On HDD, disabling SysMain can degrade boot and launch performance!
                blockers.Add("Layanan SysMain (SuperFetch) sangat disarankan tetap aktif pada sistem yang menggunakan HDD untuk mempercepat loading program.");
            }
        }

        // 4. Category checks
        if (recommendation.Category == RecommendationCategory.DoNotModify)
        {
            blockers.Add("Rekomendasi ini ditandai sebagai 'Do Not Modify' untuk konfigurasi perangkat saat ini.");
        }

        var isSafe = blockers.Count == 0;

        return new SafetyCheckResult
        {
            IsSafeToApply = isSafe,
            BlockingReasons = blockers,
            Warnings = warnings,
            RequiresElevation = recommendation.RequiresAdministrator,
            RequiresRestart = recommendation.RequiresRestart
        };
    }

    public SafetyCheckResult ValidateBatch(IReadOnlyList<Recommendation> recommendations, SystemProfile profile, bool isOneClickMode)
    {
        ArgumentNullException.ThrowIfNull(recommendations);
        ArgumentNullException.ThrowIfNull(profile);

        var allBlockers = new List<string>();
        var allWarnings = new List<string>();
        bool reqElevation = false;
        bool reqRestart = false;

        foreach (var rec in recommendations)
        {
            if (isOneClickMode)
            {
                if (rec.Category != RecommendationCategory.Safe && rec.Category != RecommendationCategory.Recommended)
                {
                    allBlockers.Add($"Rekomendasi '{rec.Title}' tidak termasuk dalam kategori Safe/Recommended untuk One-Click Safe Optimization.");
                    continue;
                }

                if (rec.RiskLevel > RiskLevel.Low)
                {
                    allBlockers.Add($"Rekomendasi '{rec.Title}' memiliki risiko di atas batas aman One-Click.");
                    continue;
                }
            }

            var single = ValidateRecommendation(rec, profile);
            if (!single.IsSafeToApply)
            {
                allBlockers.AddRange(single.BlockingReasons.Select(b => $"[{rec.Title}] {b}"));
            }
            allWarnings.AddRange(single.Warnings.Select(w => $"[{rec.Title}] {w}"));

            if (rec.RequiresAdministrator) reqElevation = true;
            if (rec.RequiresRestart) reqRestart = true;
        }

        return new SafetyCheckResult
        {
            IsSafeToApply = allBlockers.Count == 0,
            BlockingReasons = allBlockers,
            Warnings = allWarnings,
            RequiresElevation = reqElevation,
            RequiresRestart = reqRestart
        };
    }
}