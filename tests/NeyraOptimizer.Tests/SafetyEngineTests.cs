using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Optimization.Safety;
using NeyraOptimizer.Tests.Fakes;

namespace NeyraOptimizer.Tests;

public class SafetyEngineTests
{
    private readonly SafetyEngine _engine = new();
    private readonly SystemProfile _profile = TestSystems.Profile();

    [Fact]
    public void ProtectedService_IsBlocked()
    {
        var rec = new Domain.Rules.Recommendation
        {
            RuleId = "x", Title = "t", Description = "d", Reason = "r",
            TargetId = "WinDefend", Area = RuleArea.Services,
        };
        var result = _engine.ValidateRecommendation(rec, _profile);
        Assert.False(result.IsSafeToApply);
        Assert.Contains(result.BlockingReasons, b => b.Contains("WinDefend"));
    }

    [Fact]
    public void SysMainDisable_OnHdd_IsBlocked()
    {
        var hddProfile = TestSystems.Profile(media: StorageMediaType.Hdd);
        var rec = new Domain.Rules.Recommendation
        {
            RuleId = "service_sysmain", Title = "SysMain", Description = "", Reason = "",
            TargetId = "SysMain", Area = RuleArea.Services,
        };
        Assert.False(_engine.ValidateRecommendation(rec, hddProfile).IsSafeToApply);

        var ssdProfile = TestSystems.Profile(media: StorageMediaType.Ssd);
        Assert.True(_engine.ValidateRecommendation(rec, ssdProfile).IsSafeToApply);
    }

    [Fact]
    public void OneClick_RejectsAdvancedAndHighRisk()
    {
        var advanced = new Domain.Rules.Recommendation
        {
            RuleId = "a1", Title = "Advanced", Description = "", Reason = "",
            Category = RecommendationCategory.Advanced, TargetId = "SomeOptionalThing", Area = RuleArea.Services,
        };
        Assert.False(_engine.ValidateBatch(new[] { advanced }, _profile, isOneClickMode: true).IsSafeToApply);
    }

    [Fact]
    public void OneClick_AcceptsSafeLowRisk()
    {
        var safe = new Domain.Rules.Recommendation
        {
            RuleId = "s1", Title = "Safe", Description = "", Reason = "",
            Category = RecommendationCategory.Safe, RiskLevel = RiskLevel.Safe,
            TargetId = "DiagTrack", Area = RuleArea.Services,
        };
        Assert.True(_engine.ValidateBatch(new[] { safe }, _profile, isOneClickMode: true).IsSafeToApply);
    }

    [Fact]
    public void BatchAggregatesElevationAndRestartFlags()
    {
        var rec = new Domain.Rules.Recommendation
        {
            RuleId = "r1", Title = "T", Description = "", Reason = "",
            RequiresAdministrator = true, RequiresRestart = true,
            TargetId = "MapsBroker", Area = RuleArea.Services, ProposedStateText = "Manual",
        };
        var result = _engine.ValidateBatch(new[] { rec }, _profile, false);
        Assert.True(result.RequiresElevation);
        Assert.True(result.RequiresRestart);
    }
}
