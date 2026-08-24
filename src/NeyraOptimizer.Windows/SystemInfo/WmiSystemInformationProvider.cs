using System.Diagnostics;
using System.Management;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Windows.Native;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.Windows.SystemInfo;

/// <summary>
/// Read-only system information via WMI/CIM, the registry and Win32 APIs. Locale-independent:
/// build numbers and UBR come from registry values, not parsed strings. All failures degrade to
/// honest unknowns instead of fabricated data.
/// </summary>
public sealed class WmiSystemInformationProvider : ISystemInformationProvider
{
    public WindowsIdentityInfo GetWindowsIdentity()
    {
        var (build, ubr) = ReadBuildFromRegistry();
        string edition = string.Empty;
        string versionString = string.Empty;
        DateTime? boot = null;

        using (var searcher = new ManagementObjectSearcher(
                   "SELECT Caption, Version, LastBootUpTime FROM Win32_OperatingSystem"))
        {
            foreach (var os in searcher.Get())
            {
                edition = TryGetString(os, "Caption") ?? string.Empty;
                versionString = TryGetString(os, "Version") ?? string.Empty;
                if (TryGetString(os, "LastBootUpTime") is string bootStr)
                    boot = ManagementDateTimeConverter.ToDateTime(bootStr).ToUniversalTime();
                break;
            }
        }

        return new WindowsIdentityInfo
        {
            Edition = edition,
            DisplayVersion = ReadDisplayVersionFromRegistry(),
            VersionString = versionString,
            BuildNumber = build,
            UpdateBuildRevision = ubr,
            Architecture = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit",
            Is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
            LocaleName = System.Globalization.CultureInfo.CurrentUICulture.Name,
            IsVirtualMachine = DetectVirtualMachine(),
        };
    }

    public CpuInfo GetCpu()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
        foreach (var cpu in searcher.Get())
        {
            var name = TryGetString(cpu, "Name")?.Trim() ?? "Unknown CPU";
            int cores = (int?)TryGetNumber<uint>(cpu, "NumberOfCores") ?? 0;
            int logical = (int?)TryGetNumber<uint>(cpu, "NumberOfLogicalProcessors") ?? 0;
            double maxMhz = TryGetNumber<uint>(cpu, "MaxClockSpeed") ?? 0;
            return new CpuInfo
            {
                Name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " "),
                Manufacturer = TryGetString(cpu, "Manufacturer") ?? string.Empty,
                PhysicalCores = cores,
                LogicalProcessors = logical > 0 ? logical : Math.Max(1, cores),
                BaseClockGhz = maxMhz / 1000.0,
                MaxClockGhz = maxMhz / 1000.0,
            };
        }
        return new CpuInfo { Name = "Unknown CPU", LogicalProcessors = Environment.ProcessorCount };
    }

    public MemoryInfo GetMemory()
    {
        long totalMb;
        int? speed = null;
        int slotsUsed = 0;

        totalMb = Native.GlobalMemoryReader.TotalPhysicalMb();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Speed FROM Win32_PhysicalMemory");
            foreach (var stick in searcher.Get())
            {
                slotsUsed++;
                speed ??= (int?)TryGetNumber<uint>(stick, "Speed");
            }
        }
        catch (ManagementException) { }
        return new MemoryInfo { TotalPhysicalMb = totalMb, SpeedMHz = speed, SlotsUsed = slotsUsed };
    }

    public IReadOnlyList<GpuInfo> GetGpus()
    {
        var gpus = new List<GpuInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterCompatibility, AdapterRAM, DriverVersion FROM Win32_VideoController");
            foreach (var gpu in searcher.Get())
            {
                var name = TryGetString(gpu, "Name") ?? "Unknown GPU";
                var vendor = TryGetString(gpu, "AdapterCompatibility") ?? string.Empty;
                // AdapterRAM is a 32-bit field and caps at 4 GB — used as a hint only.
                uint ramBytesRaw = TryGetNumber<uint>(gpu, "AdapterRAM") ?? 0;
                long vramMb = ramBytesRaw / (1024 * 1024);
                bool dedicated = IsLikelyDedicated(name, vramMb);

                gpus.Add(new GpuInfo
                {
                    Name = name.Trim(),
                    Vendor = vendor.Trim(),
                    VramMb = dedicated && vramMb > 0 ? vramMb : 0,
                    IsDedicated = dedicated,
                    DriverVersion = TryGetString(gpu, "DriverVersion") ?? string.Empty,
                });
            }
        }
        catch (ManagementException)
        {
            // GPU info unavailable: return whatever was collected (possibly none).
        }
        return gpus;
    }

    internal static bool IsLikelyDedicated(string name, long vramMb)
    {
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) &&
            (name.Contains("UHD", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("Iris", StringComparison.OrdinalIgnoreCase)))
            return false;
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("Arc", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase))
        {
            if (name.Contains("RX", StringComparison.OrdinalIgnoreCase)) return true;
            return false; // Radeon-branded APUs are integrated by default
        }
        return vramMb >= 2048;
    }

    public IReadOnlyList<StorageVolumeInfo> GetStorageVolumes() => StorageInfo.ReadVolumes();

    public BatteryInfo GetBattery()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var ps))
            return new BatteryInfo { IsPresent = false };

        if (ps.BatteryFlag == 128) // no system battery
            return new BatteryInfo
            {
                IsPresent = false,
                PowerSource = ps.ACLineStatus == 1 ? PowerSource.AcPower : PowerSource.Unknown,
            };

        uint? design = null;
        uint? fullCharge = null;
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\wmi",
                "SELECT DesignedCapacity, FullChargedCapacity FROM BatteryStaticData");
            foreach (var b in searcher.Get())
            {
                design = TryGetNumber<uint>(b, "DesignedCapacity");
                fullCharge = TryGetNumber<uint>(b, "FullChargedCapacity");
                break;
            }
        }
        catch (ManagementException) { }

        return new BatteryInfo
        {
            IsPresent = true,
            ChargePercent = ps.BatteryLifePercent <= 100 ? ps.BatteryLifePercent : 0,
            IsCharging = (ps.BatteryFlag & 8) != 0,
            PowerSource = ps.ACLineStatus == 1 ? PowerSource.AcPower : PowerSource.Battery,
            EstimatedRuntimeMinutes =
                ps.BatteryLifeTime is not 0xFFFFFFFF and > 0 ? (int)(ps.BatteryLifeTime / 60) : null,
            DesignCapacityMilliWattHours = design is > 0 ? design : null,
            FullChargeCapacityMilliWattHours = fullCharge is > 0 ? fullCharge : null,
        };
    }

    public SecurityStatusInfo GetSecurityStatus()
    {
        bool uacEnabled = RegistryValue(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA")
            is int lua && lua == 1;
        bool defenderEnabled = ServiceRunning("WinDefend");

        // DisableRealtimeMonitoring absent or 0 → real-time protection considered on.
        object? rtValue = RegistryValue(
            @"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring");
        bool rtOn = rtValue is not int rt || rt == 0;

        var (avRegistered, avName) = ReadRegisteredAntivirus();

        return new SecurityStatusInfo
        {
            DefenderEnabled = defenderEnabled,
            RealTimeProtectionEnabled = defenderEnabled && rtOn,
            FirewallEnabled = ServiceRunning("mpssvc"),
            UacEnabled = uacEnabled,
            AntivirusRegistered = avRegistered,
            AntivirusProductName = avName,
            TamperProtectionEnabled = false, // not readable without Defender cmdlets — shown as "unknown" upstream
        };
    }

    private static (bool Registered, string ProductName) ReadRegisteredAntivirus()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\SecurityCenter2",
                "SELECT displayName FROM AntiVirusProduct");
            foreach (var av in searcher.Get())
            {
                var name = TryGetString(av, "displayName") ?? string.Empty;
                return (true, string.IsNullOrWhiteSpace(name) ? "(registered)" : name);
            }
        }
        catch (ManagementException)
        {
            // SecurityCenter2 namespace unavailable (Server SKUs).
        }
        return (false, string.Empty);
    }

    public ChassisKind GetChassisKind()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT PCSystemType FROM Win32_ComputerSystem");
            foreach (var cs in searcher.Get())
            {
                switch (TryGetNumber<uint>(cs, "PCSystemType"))
                {
                    case 2: return ChassisKind.Laptop;
                    case 1: return ChassisKind.Desktop;
                    case 8 or 9 or 10 or 11: return ChassisKind.Tablet;
                    default: return ChassisKind.Unknown;
                }
            }
        }
        catch (ManagementException) { }
        return ChassisKind.Unknown;
    }

    public DateTime GetBootTimeUtc()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
            foreach (var os in searcher.Get())
            {
                if (TryGetString(os, "LastBootUpTime") is string s)
                    return ManagementDateTimeConverter.ToDateTime(s).ToUniversalTime();
            }
        }
        catch (ManagementException) { }
        return DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    public bool IsCurrentProcessElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    public ProcessSnapshotSummary GetProcessSummary()
    {
        int count = 0;
        foreach (var p in Process.GetProcesses()) { count++; p.Dispose(); }
        return new ProcessSnapshotSummary { ProcessCount = count };
    }

    private static bool ServiceRunning(string serviceName)
    {
        using var searcher = new ManagementObjectSearcher(
            $"SELECT State FROM Win32_Service WHERE Name = '{serviceName}'");
        foreach (var svc in searcher.Get())
        {
            return string.Equals(TryGetString(svc, "State"), "Running", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static object? RegistryValue(string subKey, string valueName)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(subKey);
            return key?.GetValue(valueName);
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }

    private static (int Build, int Ubr) ReadBuildFromRegistry()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var buildStr = key?.GetValue("CurrentBuildNumber") as string ?? "0";
            var ubr = key?.GetValue("UBR") as int? ?? 0;
            return (int.TryParse(buildStr, out var b) ? b : 0, ubr);
        }
        catch (System.Security.SecurityException)
        {
            return (Environment.OSVersion.Version.Build, 0);
        }
    }

    private static string ReadDisplayVersionFromRegistry()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue("DisplayVersion") as string
                   ?? key?.GetValue("ReleaseId") as string
                   ?? string.Empty;
        }
        catch (System.Security.SecurityException)
        {
            return string.Empty;
        }
    }

    private static bool DetectVirtualMachine()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
            foreach (var cs in searcher.Get())
            {
                var manufacturer = TryGetString(cs, "Manufacturer") ?? string.Empty;
                var model = TryGetString(cs, "Model") ?? string.Empty;
                return ContainsVmMarker(manufacturer) || ContainsVmMarker(model);
            }
        }
        catch (ManagementException) { }
        return false;
    }

    private static bool ContainsVmMarker(string s) =>
        new[] { "virtualbox", "vmware", "kvm", "qemu", "xen", "hyper-v", "virtual machine", "bochs", "parallels" }
            .Any(m => s.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static string? TryGetString(ManagementBaseObject obj, string property)
    {
        try
        {
            return obj[property]?.ToString();
        }
        catch (ManagementException)
        {
            return null;
        }
    }

    private static T? TryGetNumber<T>(ManagementBaseObject obj, string property) where T : struct
    {
        try
        {
            var v = obj[property];
            if (v is null) return null;
            return (T)Convert.ChangeType(v, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (ManagementException) { return null; }
        catch (InvalidCastException) { return null; }
        catch (FormatException) { return null; }
    }
}
