using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.Power;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Security.Protection;

namespace NeyraOptimizer.Tests.Fakes;

/// <summary>Builders for consistent test system profiles.</summary>
public static class TestSystems
{
    public const string DiagTrackRule = "service_diagtrack";

    public static SystemProfile Profile(
        long ramMb = 8192,
        int logical = 4,
        double clock = 2.4,
        string cpuName = "Intel Core i5",
        StorageMediaType media = StorageMediaType.Ssd,
        bool dedicatedGpu = false,
        bool batteryPresent = false,
        PowerSource source = PowerSource.AcPower,
        int build = 22631)
    {
        var gpus = new List<GpuInfo> { new() { Name = "UHD Graphics", Vendor = "Intel" } };
        if (dedicatedGpu)
            gpus.Add(new GpuInfo { Name = "GeForce RTX 3060", Vendor = "NVIDIA", VramMb = 6144, IsDedicated = true });

        return new SystemProfile
        {
            Windows = new WindowsIdentityInfo
            {
                Edition = "Pro", DisplayVersion = "23H2", VersionString = "10.0",
                BuildNumber = build, UpdateBuildRevision = 3155, Architecture = "x64",
                Is64BitOperatingSystem = true,
            },
            Cpu = new CpuInfo { Name = cpuName, LogicalProcessors = logical, PhysicalCores = Math.Max(1, logical / 2), BaseClockGhz = clock },
            Memory = new MemoryInfo { TotalPhysicalMb = ramMb },
            Gpus = gpus,
            Volumes = new[] { new StorageVolumeInfo { DriveLetter = 'C', Label = "System", TotalGb = 476, FreeGb = 120, MediaType = media, IsSystemVolume = true } },
            Battery = new BatteryInfo { IsPresent = batteryPresent, ChargePercent = 55, PowerSource = source },
            Chassis = batteryPresent ? ChassisKind.Laptop : ChassisKind.Desktop,
            BootTimeUtc = DateTime.UtcNow.AddMinutes(-30),
        };
    }

    public static AnalysisBundle Bundle(
        SystemProfile? profile = null,
        IEnumerable<ServiceInfo>? services = null,
        IEnumerable<StartupEntry>? startup = null,
        IEnumerable<ScheduledTaskInfo>? tasks = null,
        IEnumerable<InstalledAppInfo>? apps = null)
    {
        profile ??= Profile();
        return new AnalysisBundle
        {
            Profile = profile,
            Services = services?.ToList() ?? Array.Empty<ServiceInfo>(),
            StartupEntries = startup?.ToList() ?? Array.Empty<StartupEntry>(),
            Tasks = tasks?.ToList() ?? Array.Empty<ScheduledTaskInfo>(),
            InstalledApps = apps?.ToList() ?? Array.Empty<InstalledAppInfo>(),
        };
    }
}
