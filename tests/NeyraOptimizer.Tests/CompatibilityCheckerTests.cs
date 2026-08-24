using Xunit;
using NeyraOptimizer.Diagnostics.Compatibility;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.Tests;

public class CompatibilityCheckerTests
{
    private readonly CompatibilityChecker _checker = new();

    [Fact]
    public void Windows10_1809_IsSupported()
    {
        var win = new WindowsIdentityInfo { BuildNumber = 17763, Is64BitOperatingSystem = true, Edition = "Pro" };
        Assert.True(_checker.Check(win, false).IsSupported);
    }

    [Fact]
    public void Windows10_1507_IsBlocked()
    {
        var win = new WindowsIdentityInfo { BuildNumber = 10240, Is64BitOperatingSystem = true };
        var result = _checker.Check(win, false);
        Assert.False(result.IsSupported);
        Assert.True(result.IsReadOnlyDiagnosticsMode);
        Assert.NotEmpty(result.BlockReasons);
    }

    [Fact]
    public void Windows11_Supported()
    {
        var win = new WindowsIdentityInfo { BuildNumber = 22631, Is64BitOperatingSystem = true };
        Assert.True(_checker.Check(win, false).IsSupported);
    }

    [Fact]
    public void VirtualMachine_ProducesWarning_ButNotBlocked()
    {
        var win = new WindowsIdentityInfo { BuildNumber = 22631, Is64BitOperatingSystem = true, IsVirtualMachine = true };
        var result = _checker.Check(win, false);
        Assert.True(result.IsSupported);
        Assert.Contains(result.Warnings, w => w.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || w.Contains("VM", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OsSummaryContainsBuildNumber()
    {
        var win = new WindowsIdentityInfo { BuildNumber = 26100, UpdateBuildRevision = 1742, Edition = "Home", DisplayVersion = "24H2" };
        var summary = _checker.Check(win, false).OsSummary;
        Assert.Contains("26100.1742", summary);
        Assert.Contains("24H2", summary);
    }
}
