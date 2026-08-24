using Xunit;
using NeyraOptimizer.Domain.Engines;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Domain.Rules;
using NeyraOptimizer.Optimization.Catalog;
using NeyraOptimizer.Tests.Fakes;

namespace NeyraOptimizer.Tests;

public class RecommendationEngineTests
{
    private static RecommendationEngine Engine { get; } = new();

    private static IReadOnlyList<RuleDefinition> Catalog => RulesCatalog.GetAllRules();

    [Fact]
    public void ServiceRule_ProducedWhenServiceExistsAndModeDiffers()
    {
        var bundle = TestSystems.Bundle(services: new[]
        {
            new ServiceInfo { ServiceName = "DiagTrack", DisplayName = "DiagTrack", StartMode = ServiceStartMode.Automatic },
        });
        var recs = Engine.BuildRecommendations(bundle, Catalog, UsageProfileKind.Balanced, advancedModeEnabled: false);

        Assert.Contains(recs, r => r.RuleId == "service_diagtrack" && r.TargetId == "DiagTrack");
    }

    [Fact]
    public void ServiceRule_SkippedWhenAlreadyManual()
    {
        var bundle = TestSystems.Bundle(services: new[]
        {
            new ServiceInfo { ServiceName = "DiagTrack", DisplayName = "x", StartMode = ServiceStartMode.Manual },
        });
        var recs = Engine.BuildRecommendations(bundle, Catalog, UsageProfileKind.Balanced, false);
        Assert.DoesNotContain(recs, r => r.RuleId == "service_diagtrack");
    }

    [Fact]
    public void TaskRule_OnlyForEnabledTasks()
    {
        var disabled = new ScheduledTaskInfo
        {
            TaskPath = @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
            Name = "Consolidator", IsEnabled = false,
        };
        var bundle = TestSystems.Bundle(tasks: new[] { disabled });
        var recs = Engine.BuildRecommendations(bundle, Catalog, UsageProfileKind.Balanced, false);
        Assert.DoesNotContain(recs, r => r.RuleId == "task_ceip_consolidator");
    }

    [Fact]
    public void RulesOutsideBuildRange_AreSkipped()
    {
        // Win10 build 17763 vs a hypothetical rule limited to modern builds is covered by the
        // catalog itself; here we verify the engine honors the constraint.
        var oldProfile = TestSystems.Profile(build: 17134); // below minimum supported
        var bundle = TestSystems.Bundle(profile: oldProfile);
        var recs = Engine.BuildRecommendations(bundle, Catalog, UsageProfileKind.Balanced, false);
        Assert.Empty(recs);
    }

    [Fact]
    public void DoNotModifyRules_HiddenUnlessAdvancedMode()
    {
        var catalog = Catalog.Append(new RuleDefinition
        {
            RuleId = "test_dnm",
            Name = "Do not modify",
            Description = "doc-only",
            Area = RuleArea.Services,
            Category = RecommendationCategory.DoNotModify,
            Payload = new Dictionary<string, string> { ["TargetId"] = "DiagTrack" },
        }).ToList();

        var bundle = TestSystems.Bundle(services: new[]
        {
            new ServiceInfo { ServiceName = "DiagTrack", DisplayName = "DiagTrack", StartMode = ServiceStartMode.Automatic },
        });
        var normal = Engine.BuildRecommendations(bundle, catalog, UsageProfileKind.Balanced, false);
        var advanced = Engine.BuildRecommendations(bundle, catalog, UsageProfileKind.Balanced, true);
        Assert.DoesNotContain(normal, r => r.Category == RecommendationCategory.DoNotModify);
        Assert.Contains(advanced, r => r.RuleId == "test_dnm");
    }
}
