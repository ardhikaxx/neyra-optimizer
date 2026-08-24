namespace NeyraOptimizer.Security.Protection;

/// <summary>
/// Catalog of components Neyra Optimizer will NEVER modify through normal optimization flows.
/// This is the backbone of the Safety Engine: rules touching these targets are rejected before
/// they reach any Windows API. Lists use stable identifiers (service key names, package family
/// names, task path prefixes) — never localized display names.
/// </summary>
public static class ProtectedComponents
{
    /// <summary>Services essential to boot, security, networking, storage, audio or update integrity.</summary>
    public static readonly IReadOnlyCollection<string> ServiceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Core boot / RPC / session infrastructure
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "BrokerInfrastructure", "Power", "PlugPlay",
        "SamSS", "ProfSvc", "gpsvc", "Schedule", "EventLog",
        // Security stack — never disabled
        "WinDefend", "WdNisSvc", "WdNisDrv", "WdFilter", "WdBoot", "SecurityHealthService",
        "wscsvc", "mpssvc", "BFE", "TrustedInstaller", "CryptSvc", "KeyIso",
        // Networking core
        "NSI", "Dhcp", "Dnscache", "NlaSvc", "netprofm", "LanmanWorkstation", "LanmanServer",
        // Storage / filesystem
        "FltMgr", "MountMgr", "PartMgr", "volmgr", "volsnap", "stornvme", "storahci",
        // Audio stack
        "AudioSrv", "AudioEndpointBuilder",
        // Update & servicing dependencies
        "UsoSvc", "DoSvc", "wuauserv",
        // Misc critical
        "Winmgmt", "StateRepository", "UserManager", "AppInfo", "CoreMessagingRegistrar",
        "SystemEventsBroker", "DispBrokerDesktopSvc",
    };

    /// <summary>Process image names that must never be terminated.</summary>
    public static readonly IReadOnlyCollection<string> ProcessImageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass",
        "svchost", "dwm", "explorer", "fontdrvhost", "audiodg", "conhost", "openconsole",
        "memory compression", "msmpeng", "nissrv", "securityhealthservice", "securityhealthsystray",
        "sihost", "ctfmon", "taskhostw", "wmiprvse", "dllhost", "rundll32", "searchindexer",
        "startmenuexperiencehost", "shellexperiencehost", "textinputhost", "searchhost",
        "applicationframehost", "runtimebroker", "widgetservice", "widgets",
    };

    /// <summary>
    /// Task path prefixes that are protected. Matching is case-insensitive prefix comparison on the
    /// full task path. These tasks are required for update integrity, recovery or system state.
    /// </summary>
    public static readonly IReadOnlyCollection<string> TaskPathPrefixes = new[]
    {
        @"\microsoft\windows\updateorchestrator\",
        @"\microsoft\windows\windowsupdate\",
        @"\microsoft\windows\windows defender\",
        @"\microsoft\windows\systemrestore\",
        @"\microsoft\windows\staterepository\",
        @"\microsoft\windows\clip\",
        @"\microsoft\windows\application experience\startupapptask",
        @"\microsoft\windows\shell\family safety",
        @"\microsoftedgeupdate\tasks\",
    }.ToArray();

    /// <summary>AppX package family names that must never be uninstalled.</summary>
    public static readonly IReadOnlyCollection<string> PackageFamilyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Store & commerce
        "Microsoft.WindowsStore_8wekyb3d8bbwe",
        "Microsoft.StorePurchaseApp_8wekyb3d8bbwe",
        // Security
        "Microsoft.SecHealthUI_8wekyb3d8bbwe",
        // Frameworks & runtimes other apps depend on
        "Microsoft.VCLibs.140.00_8wekyb3d8bbwe",
        "Microsoft.VCLibs.140.00.UVCDesktop_8wekyb3d8bbwe",
        "Microsoft.NET.Native.Framework.11_8wekyb3d8bbwe",
        "Microsoft.NET.Native.Runtime.11_8wekyb3d8bbwe",
        "Microsoft.UI.Xaml.2.0_8wekyb3d8bbwe",
        "Microsoft.UI.Xaml.2.1_8wekyb3d8bbwe",
        "Microsoft.UI.Xaml.2.3_8wekyb3d8bbwe",
        "Microsoft.UI.Xaml.2.4_8wekyb3d8bbwe",
        "Microsoft.UI.Xaml.2.7_8wekyb3d8bbwe",
        "Microsoft.UI.Xaml.2.8_8wekyb3d8bbwe",
        "Microsoft.WindowsAppRuntime.1.2_8wekyb3d8bbwe",
        "Microsoft.WindowsAppRuntime.1.3_8wekyb3d8bbwe",
        "Microsoft.WindowsAppRuntime.1.4_8wekyb3d8bbwe",
        "Microsoft.WindowsAppRuntime.1.5_8wekyb3d8bbwe",
        "Microsoft.WindowsAppRuntime.1.6_8wekyb3d8bbwe",
        "Microsoft.WindowsAppRuntime.1.7_8wekyb3d8bbwe",
        // Tooling the OS and users rely on
        "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe",
        "Microsoft.WebMediaExtensions_8wekyb3d8bbwe",
        "Microsoft.HEIFImageExtension_8wekyb3d8bbwe",
        "Microsoft.HEVCVideoExtension_8wekyb3d8bbwe",
        "Microsoft.VP9VideoExtensions_8wekyb3d8bbwe",
        "Microsoft.WebpImageExtension_8wekyb3d8bbwe",
        "Microsoft.AV1VideoExtension_8wekyb3d8bbwe",
        "Microsoft.MPEG2VideoExtension_8wekyb3d8bbwe",
        "Microsoft.RawImageExtension_8wekyb3d8bbwe",
        // Shell hosts
        "MicrosoftWindows.Client.CBS_1000.19060.x64__cw5n1h2txyewy",
        "Microsoft.Windows.ShellExperienceHost_cw5n1h2txyewy",
        "Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy",
        "Microsoft.Windows.Search_cw5n1h2txyewy",
        "Microsoft.Windows.FileExplorer_cw5n1h2txyewy",
    };

    /// <summary>Win32 uninstall display-name patterns treated as protected (runtimes, drivers, security software).</summary>
    public static readonly IReadOnlyList<string> Win32ProtectedPatterns = new[]
    {
        "^Microsoft Visual C\\+\\+.*Redistributable",
        "^DirectX",
        "^Microsoft Edge$",
        "^Microsoft Edge WebView2",
        "^Microsoft Edge Update",
        "^Windows Driver Package",
        "^Intel.*Driver",
        "^NVIDIA.*(Driver|Graphics)",
        "^AMD.*(Driver|Chipset|Software)",
        "^Realtek",
        "^Conexant",
        "Antivirus", "Anti-Virus", "Internet Security", "Total Security", "Firewall",
        "^Norton", "^McAfee", "^Kaspersky", "^Bitdefender", "^Avast", "^AVG ", "^ESET ",
        "^Trend Micro", "^Malwarebytes", "^Sophos", "^F-Secure", "^Panda ",
    };

    /// <summary>Returns true when a service may be considered for modification at all.</summary>
    public static bool IsServiceProtected(string serviceName) =>
        ServiceNames.Contains(serviceName ?? string.Empty);

    public static bool IsTaskProtected(string taskPath)
    {
        if (string.IsNullOrWhiteSpace(taskPath)) return true;
        var normalized = taskPath.Replace('/', '\\').ToLowerInvariant();
        if (!normalized.EndsWith("\\")) normalized += "\\";
        return TaskPathPrefixes.Any(p => normalized.StartsWith(p, StringComparison.Ordinal));
    }

    public static bool IsPackageProtected(string? packageFamilyOrFullName)
    {
        if (string.IsNullOrWhiteSpace(packageFamilyOrFullName)) return true;
        foreach (var family in PackageFamilyNames)
        {
            if (packageFamilyOrFullName.StartsWith(family, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool IsWin32AppProtected(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return true;
        return Win32ProtectedPatterns.Any(p =>
            System.Text.RegularExpressions.Regex.IsMatch(displayName, p, System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }
}
