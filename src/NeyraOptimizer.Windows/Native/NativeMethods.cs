using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NeyraOptimizer.Windows.Native;

internal static class NativeMethods
{
    // ---------- Kernel32 ----------
    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll")]
    internal static extern ulong GetTickCount64();

    // ---------- User32 (visual effects) ----------
    [StructLayout(LayoutKind.Sequential)]
    internal struct ANIMATIONINFO
    {
        public uint cbSize;
        public bool iMinAnimate;
    }

    internal const int SPI_GETANIMATION = 0x0016;
    internal const int SPI_SETANIMATION = 0x0017;
    internal const int SPI_GETDRAGFULLWINDOWS = 0x0038;
    internal const int SPI_SETDRAGFULLWINDOWS = 0x0037;
    internal const int SPI_UPDATEINIFILE = 0x0023;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref ANIMATIONINFO pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref bool pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
        uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    internal const uint SMTO_ABORTIFHUNG = 0x0002;
    internal static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    internal const uint WM_SETTINGCHANGE = 0x001A;

    // ---------- AdvApi32 (Service Control Manager) ----------
    internal const int SC_MANAGER_ALL_ACCESS = 0xF003F;
    internal const int SERVICE_ALL_ACCESS = 0xF01FF;
    internal const int SERVICE_CHANGE_CONFIG = 0x0002;
    internal const int SERVICE_QUERY_CONFIG = 0x0001;
    internal const int SERVICE_NO_CHANGE = -1;

    internal const int SERVICE_BOOT_START = 0;
    internal const int SERVICE_SYSTEM_START = 1;
    internal const int SERVICE_AUTO_START = 2;
    internal const int SERVICE_DEMAND_START = 3;
    internal const int SERVICE_DISABLED = 4;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr OpenSCManager(string? machineName, string? databaseName, int access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr OpenService(IntPtr scManager, string serviceName, int access);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool ChangeServiceConfig(
        IntPtr service,
        int serviceType,
        int startType,
        int errorControl,
        string? binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password,
        string? displayName);

    // ---------- SrClient (restore points) ----------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct RESTOREPOINTINFOW
    {
        public uint dwEventType;      // BEGIN_SYSTEM_CHANGE = 100 / END_SYSTEM_CHANGE = 101
        public uint dwRestorePtType;  // MODIFY_SETTINGS = 12 etc.
        public long llSequenceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STATEMGRSTATUS
    {
        public uint dwStatus;  // ERROR_SUCCESS = 0
        public long llSequenceNumber;
    }

    internal const uint BEGIN_SYSTEM_CHANGE = 100;
    internal const uint END_SYSTEM_CHANGE = 101;
    internal const uint MODIFY_SETTINGS = 12;
    internal const uint APPLICATION_INSTALL = 0;
    internal const uint CRITICAL = 1;

    [DllImport("srclient.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SRSetRestorePointW(ref RESTOREPOINTINFOW pRestorePtSpec, ref STATEMGRSTATUS pSMgrStatus);

    // ---------- PowrProf (power plans & overlays) ----------
    [DllImport("powrprof.dll", SetLastError = true)]
    internal static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupOfPowerSetting,
        int accessFlag, uint index, ref Guid buffer, ref uint bufferSize);

    internal const int ACCESS_SCHEME = 16;

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid,
        IntPtr subGroupOfPowerSettingGuid, IntPtr powerSettingGuid, IntPtr buffer, ref uint bufferSize);

    [DllImport("powrprof.dll", SetLastError = true)]
    internal static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    internal static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    internal static extern uint PowerDuplicateScheme(IntPtr rootSchemeGuid, ref Guid destinationSchemeGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    internal static extern uint PowerDeleteScheme(IntPtr rootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    internal static extern uint PowerSetActiveOverlay(nint rootKey, ref Guid overlayGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    internal static extern uint PowerGetEffectiveOverlay(nint rootKey, out Guid overlayGuid);

    // ---------- Shell (recycle bin) ----------
    [StructLayout(LayoutKind.Sequential)]
    internal struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    internal const int SHERB_NOCONFIRMATION = 0x1;
    internal const int SHERB_NOPROGRESSUI = 0x2;
    internal const int SHERB_NOSOUND = 0x4;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    // ---------- System power status ----------
    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;         // 0 offline, 1 online, 255 unknown
        public byte BatteryFlag;          // 1 high, 2 low, 4 critical, 8 charging, 128 no battery, 255 unknown
        public byte BatteryLifePercent;   // 0..100, 255 unknown
        public byte Reserved1;
        public uint BatteryLifeTime;      // seconds, 0xFFFFFFFF unknown
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);
}

/// <summary>Maps Win32 errors thrown by SCM operations into typed failures.</summary>
public sealed class NativeMethodException : Exception
{
    public int Win32Error { get; }
    public NativeMethodException(string message, int win32Error) : base($"{message} (Win32 error {win32Error})")
        => Win32Error = win32Error;
}

internal static class RegistryViewHelper
{
    /// <summary>The app runs as 64-bit on x64; use Registry64 explicitly so WOW6432Node views stay predictable.</summary>
    internal static RegistryKey OpenBase(NeyraOptimizer.Domain.Abstractions.RegRoot root, bool writable)
    {
        var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default;
        return root switch
        {
            NeyraOptimizer.Domain.Abstractions.RegRoot.CurrentUser => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view),
            NeyraOptimizer.Domain.Abstractions.RegRoot.LocalMachine => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view),
            NeyraOptimizer.Domain.Abstractions.RegRoot.Users => RegistryKey.OpenBaseKey(RegistryHive.Users, view),
            NeyraOptimizer.Domain.Abstractions.RegRoot.ClassesRoot => RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view),
            _ => throw new ArgumentOutOfRangeException(nameof(root)),
        };
    }
}
