using Xunit;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Security.Elevation;

namespace NeyraOptimizer.Tests;

public class ElevatedOperationValidatorTests
{
    [Fact]
    public void ValidServiceChange_Passes()
    {
        var req = new ElevatedOperationRequest
        {
            Kind = ElevatedOperationKind.SetServiceStartMode,
            ServiceName = "MapsBroker",
            StartModeValue = 5, // Manual
        };
        Assert.True(ElevatedOperationValidator.Validate(req).Valid);
    }

    [Theory]
    [InlineData("WinDefend")] // protected service can never pass validation
    [InlineData("evil;format")] // invalid characters
    [InlineData("")]
    public void InvalidOrProtectedService_Fails(string serviceName)
    {
        var req = new ElevatedOperationRequest
        {
            Kind = ElevatedOperationKind.SetServiceStartMode,
            ServiceName = serviceName,
            StartModeValue = 5,
        };
        Assert.False(ElevatedOperationValidator.Validate(req).Valid);
    }

    [Fact]
    public void TaskPathTraversal_Fails()
    {
        var req = new ElevatedOperationRequest
        {
            Kind = ElevatedOperationKind.SetTaskEnabled,
            TaskPath = @"\Microsoft\..\..\Windows\System32\evil",
            TaskEnabled = false,
        };
        Assert.False(ElevatedOperationValidator.Validate(req).Valid);
    }

    [Fact]
    public void RegistryWrite_OutsideAllowedPrefixes_Fails()
    {
        var req = new ElevatedOperationRequest
        {
            Kind = ElevatedOperationKind.ApplyRegistryWrites,
            RegistryWrites =
            {
                new ElevatedRegistryWrite
                {
                    Root = NeyraOptimizer.Domain.Abstractions.RegRoot.LocalMachine,
                    SubKey = @"SAM\SomeWhere",
                    ValueName = "v",
                    Kind = Microsoft.Win32.RegistryValueKind.DWord,
                },
            },
        };
        Assert.False(ElevatedOperationValidator.Validate(req).Valid);
    }

    [Fact]
    public void BatchWithInvalidChild_IsRejected()
    {
        var req = new ElevatedOperationRequest
        {
            Kind = ElevatedOperationKind.ApplyBatch,
            Operations =
            {
                new ElevatedOperationRequest { Kind = ElevatedOperationKind.SetTaskEnabled, TaskPath = "no-lead-slash", TaskEnabled = false },
            },
        };
        Assert.False(ElevatedOperationValidator.Validate(req).Valid);
    }

    [Fact]
    public void EmptyBatch_Rejected()
    {
        Assert.False(ElevatedOperationValidator.Validate(new ElevatedOperationRequest
        {
            Kind = ElevatedOperationKind.ApplyBatch,
        }).Valid);
    }
}
