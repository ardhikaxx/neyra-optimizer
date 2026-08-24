using Xunit;
using NeyraOptimizer.Security.Protection;

namespace NeyraOptimizer.Tests;

public class ProtectedComponentsTests
{
    [Theory]
    [InlineData("WinDefend")]
    [InlineData("RpcSs")]
    [InlineData("wuauserv")]
    [InlineData("AudioSrv")]
    [InlineData("mpssvc")]
    [InlineData("TrustedInstaller")]
    public void CoreSecurityAndSystemService_IsProtected(string service)
        => Assert.True(ProtectedComponents.IsServiceProtected(service));

    [Theory]
    [InlineData("DiagTrack")]
    [InlineData("SysMain")]
    [InlineData("RetailDemo")]
    [InlineData("MapsBroker")]
    public void OptionalTelemetryServices_AreNotProtected(string service)
        => Assert.False(ProtectedComponents.IsServiceProtected(service));

    [Fact]
    public void EmptyServiceName_TreatedAsProtected_Defensive()
        => Assert.True(ProtectedComponents.IsServiceProtected(""));

    [Theory]
    [InlineData(@"Microsoft.WindowsStore_8wekyb3d8bbwe")]
    [InlineData(@"Microsoft.SecHealthUI_8wekyb3d8bbwe")]
    [InlineData(@"Microsoft.VCLibs.140.00_8wekyb3d8bbwe")]
    public void StoreSecurityAndFrameworks_AreProtectedPackages(string family)
        => Assert.True(ProtectedComponents.IsPackageProtected(family));

    [Fact]
    public void TaskUpdateOrchestratorPrefix_Protected()
        => Assert.True(ProtectedComponents.IsTaskProtected(
            @"\Microsoft\Windows\UpdateOrchestrator\Universal Orchestrator Start"));

    [Fact]
    public void CeipTask_NotProtected()
        => Assert.False(ProtectedComponents.IsTaskProtected(
            @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator"));

    [Theory]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64)")]
    [InlineData("Microsoft Edge")]
    [InlineData("McAfee LiveSafe")]
    public void RuntimesDriversAndAntivirus_Win32Apps_Protected(string name)
        => Assert.True(ProtectedComponents.IsWin32AppProtected(name));

    [Fact]
    public void NormalUserApp_NotProtected()
        => Assert.False(ProtectedComponents.IsWin32AppProtected("Contoso Photo Studio 2024"));
}
