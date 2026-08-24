using NeyraOptimizer.Optimization.Catalog;
using NeyraOptimizer.Security.Protection;

namespace NeyraOptimizer.Tests;

public class RulesCatalogTests
{
    [Fact]
    public void AllRuleIds_AreUnique()
    {
        var rules = RulesCatalog.GetAllRules();
        Assert.Equal(rules.Count, rules.Select(r => r.RuleId).Distinct().Count());
    }

    [Fact]
    public void EveryRule_CarriesTargetIdAndRollbackDescription()
    {
        foreach (var rule in RulesCatalog.GetAllRules())
        {
            Assert.True(rule.Payload.ContainsKey("TargetId"), $"{rule.RuleId} missing TargetId");
            Assert.False(string.IsNullOrWhiteSpace(rule.RollbackDescription), $"{rule.RuleId} missing rollback");
            Assert.True(rule.RuleVersion >= 1, "rules must be versioned");
        }
    }

    /// <summary>Catalog integrity: no shipped rule may target a protected component.</summary>
    [Fact]
    public void NoRule_TargetsProtectedComponents()
    {
        foreach (var rule in RulesCatalog.GetAllRules())
        {
            var target = rule.Payload["TargetId"];
            var protectedHit =
                (rule.Area == Domain.Rules.RuleArea.Services && ProtectedComponents.IsServiceProtected(target)) ||
                (rule.Area == Domain.Rules.RuleArea.ScheduledTasks && ProtectedComponents.IsTaskProtected(target));
            Assert.False(protectedHit, $"Rule {rule.RuleId} targets protected component '{target}'");
        }
    }
}
