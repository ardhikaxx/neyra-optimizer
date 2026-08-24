using Xunit;
using NeyraOptimizer.Domain.Rules;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Optimization.Modes;
using NeyraOptimizer.Tests.Fakes;

namespace NeyraOptimizer.Tests;

public class ModePlanBuilderTests
{
    [Fact]
    public void GamingMode_UnavailableOnBatteryWithoutCharger()
    {
        var bundle = TestSystems.Bundle(TestSystems.Profile(
            batteryPresent: true, source: PowerSource.Battery));
        var plan = ModePlanBuilder.BuildGaming(bundle);
        Assert.False(plan.IsAvailable);
        Assert.Contains("charger", plan.UnavailabilityReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GamingMode_AvailableWhenPluggedIn()
    {
        var bundle = TestSystems.Bundle(TestSystems.Profile(batteryPresent: true, source: PowerSource.AcPower));
        var plan = ModePlanBuilder.BuildGaming(bundle);
        Assert.True(plan.IsAvailable);
    }

    [Fact]
    public void BatterySaver_UnavailableOnDesktopWithoutBattery()
    {
        var plan = ModePlanBuilder.BuildBatterySaver(TestSystems.Bundle());
        Assert.False(plan.IsAvailable);
    }

    [Fact]
    public void OfficeMode_NeverIncludesGamingRules()
    {
        // Provide the services/tasks the catalog rules need so they surface.
        var bundle = TestSystems.Bundle(services: CatalogServices(), tasks: CatalogTasks());
        var office = ModePlanBuilder.BuildOffice(bundle);
        Assert.DoesNotContain(office.Recommendations, r => r.RuleId == "power_game_mode");
        Assert.All(office.Recommendations, r => Assert.NotEqual(RiskLevel.High, r.RiskLevel));
    }

    [Fact]
    public void LowEnd_IncludesStartupAndVisualTuning()
    {
        var startup = new StartupEntry { Id = "steam", Name = "Steam", Command = "x", Source = StartupSource.RunKeyCurrentUser, IsEnabled = true };
        var protectedEntry = new StartupEntry
        {
            Id = "critical", Name = "Security Agent", Command = "x",
            Source = StartupSource.RunKeyCurrentUser, IsEnabled = true,
            IsProtected = true, ProtectionReason = "core",
        };
        var bundle = TestSystems.Bundle(startup: new[] { startup, protectedEntry });

        var lowEnd = ModePlanBuilder.BuildLowEnd(bundle);
        Assert.Contains(lowEnd.Recommendations, r => r.TargetId == "steam" && r.Area == RuleArea.Startup);
        Assert.DoesNotContain(lowEnd.Recommendations, r => r.TargetId == "critical");
        Assert.Contains(lowEnd.Recommendations, r => r.Area == RuleArea.VisualEffects);
    }

    [Fact]
    public void SafeWindows_OnlyContainsSafeCategory()
    {
        var bundle = TestSystems.Bundle(services: CatalogServices(), tasks: CatalogTasks());
        var safe = ModePlanBuilder.BuildSafeWindows(bundle);
        Assert.All(safe.Recommendations, r =>
            Assert.True(r.Category <= RecommendationCategory.Safe || r.RiskLevel <= RiskLevel.Safe));
    }

    private static ServiceInfo[] CatalogServices() =>
        new[]
        {
            "DiagTrack", "dmwappushservice", "SysMain", "RetailDemo", "RemoteRegistry", "MapsBroker",
        }.Select(n => new ServiceInfo
        {
            ServiceName = n, DisplayName = n,
            StartMode = ServiceStartMode.Automatic, Status = ServiceStatus.Running,
        }).ToArray();

    private static ScheduledTaskInfo[] CatalogTasks() =>
        new[]
        {
            @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
            @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
            @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
        }.Select(p => new ScheduledTaskInfo { TaskPath = p, Name = p.Split('\\').Last(), IsEnabled = true }).ToArray();
}
