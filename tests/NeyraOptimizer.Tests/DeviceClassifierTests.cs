using NeyraOptimizer.Domain.Engines;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Tests.Fakes;

namespace NeyraOptimizer.Tests;

public class DeviceClassifierTests
{
    private static (DeviceClass, int) Classify(SystemProfile p)
    {
        var (cls, score, _) = DeviceClassifier.Classify(p);
        p.DeviceClass = cls;
        return (cls, score);
    }

    [Fact]
    public void LowEndMachine_WithHddAndWeakCpu_IsClassifiedLowEnd()
    {
        var profile = TestSystems.Profile(ramMb: 4096, logical: 2, clock: 1.1,
            cpuName: "Intel Celeron N4020", media: StorageMediaType.Hdd);
        Assert.Equal(DeviceClass.LowEnd, Classify(profile).Item1);
    }

    [Fact]
    public void SameRam_WithSsdAndBetterCpu_ScoresHigherThanHddVariant()
    {
        // The requirement: identical RAM must NOT imply identical treatment.
        var hdd = TestSystems.Profile(ramMb: 4096, logical: 2, clock: 1.5, cpuName: "Pentium Silver", media: StorageMediaType.Hdd);
        var ssd = TestSystems.Profile(ramMb: 4096, logical: 4, clock: 2.4, cpuName: "Core i3-8130U", media: StorageMediaType.Ssd);

        var (hddClass, hddScore) = Classify(hdd);
        var (ssdClass, ssdScore) = Classify(ssd);

        Assert.True(ssdScore > hddScore, $"SSD variant ({ssdScore}) should outscore HDD variant ({hddScore})");
        Assert.NotEqual(ssdClass, hddClass);
    }

    [Fact]
    public void DedicatedGpuWithStrongCpuAndRam_QualifiesForGaming()
    {
        var gaming = TestSystems.Profile(ramMb: 16384, logical: 12, clock: 3.6,
            cpuName: "Ryzen 7 5800X", dedicatedGpu: true);
        Assert.Equal(DeviceClass.Gaming, Classify(gaming).Item1);
    }

    [Fact]
    public void ScoreIsClampedToHundred()
    {
        var monster = TestSystems.Profile(ramMb: 131072, logical: 32, clock: 5.7,
            cpuName: "Threadripper PRO", dedicatedGpu: true);
        var (_, score) = Classify(monster);
        Assert.InRange(score, 0, 100);
    }

    [Fact]
    public void ClassificationReasonsAreProduced()
    {
        var profile = TestSystems.Profile();
        DeviceClassifier.Classify(profile);
        Assert.NotEmpty(profile.ClassificationReasons);
    }
}
