using System.Runtime.InteropServices;
using Microsoft.Win32;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Windows.Native;

namespace NeyraOptimizer.Windows.Visuals;

/// <summary>
/// Visual effects via documented registry values and SystemParametersInfo where available.
/// Every effect key is stable; per-effect metadata declares whether changes apply immediately
/// or after sign-out so the UI can be honest about it.
/// </summary>
public sealed class WindowsVisualEffectsManager : IVisualEffectsManager
{
    public const string KeyMinAnimate = "MinAnimate";
    public const string KeyMenuAnimation = "MenuAnimation";
    public const string KeyDragFullWindows = "DragFullWindows";
    public const string KeyTaskbarAnimations = "TaskbarAnimations";
    public const string KeyListviewAlphaSelect = "ListviewAlphaSelect";
    public const string KeyListviewShadow = "ListviewShadow";
    public const string KeyIconsOnly = "IconsOnly"; // false = show thumbnails
    public const string KeyAeroPeek = "EnableAeroPeek";
    public const string KeyTransparency = "EnableTransparency";

    private const string DesktopKey = @"Control Panel\Desktop";
    private const string AdvancedKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string DwmKey = @"SOFTWARE\Microsoft\Windows\DWM";

    public IReadOnlyDictionary<string, bool> GetCurrentEffectStates()
    {
        var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        states[KeyMinAnimate] = ReadInt(RegRoot.CurrentUser, DesktopKey, "MinAnimate", 1) != 0;
        states[KeyMenuAnimation] = ReadInt(RegRoot.CurrentUser, DesktopKey, "MenuAnimation", 1) != 0;
        states[KeyDragFullWindows] = ReadInt(RegRoot.CurrentUser, DesktopKey, "DragFullWindows", 1) != 0;
        states[KeyTaskbarAnimations] = ReadInt(RegRoot.CurrentUser, AdvancedKey, "TaskbarAnimations", 1) != 0;
        states[KeyListviewAlphaSelect] = ReadInt(RegRoot.CurrentUser, AdvancedKey, "ListviewAlphaSelect", 1) != 0;
        states[KeyListviewShadow] = ReadInt(RegRoot.CurrentUser, AdvancedKey, "ListviewShadow", 1) != 0;
        states[KeyIconsOnly] = ReadInt(RegRoot.CurrentUser, AdvancedKey, "IconsOnly", 0) != 0;
        states[KeyAeroPeek] = ReadInt(RegRoot.CurrentUser, DwmKey, "EnableAeroPeek", 1) != 0;
        states[KeyTransparency] = ReadInt(RegRoot.CurrentUser, DwmKey, "EnableTransparency", 1) != 0;
        return states;
    }

    public void ApplyEffect(string effectKey, bool enabled)
    {
        switch (effectKey)
        {
            case KeyMinAnimate:
                WriteInt(RegRoot.CurrentUser, DesktopKey, "MinAnimate", enabled ? 1 : 0);
                SetAnimation(enabled);
                break;
            case KeyMenuAnimation:
                WriteInt(RegRoot.CurrentUser, DesktopKey, "MenuAnimation", enabled ? 1 : 0);
                break;
            case KeyDragFullWindows:
                WriteInt(RegRoot.CurrentUser, DesktopKey, "DragFullWindows", enabled ? 1 : 0);
                SystemParametersInfoSet(NativeMethods.SPI_SETDRAGFULLWINDOWS, enabled);
                break;
            case KeyTaskbarAnimations:
                WriteInt(RegRoot.CurrentUser, AdvancedKey, "TaskbarAnimations", enabled ? 1 : 0);
                BroadcastSettingChange();
                break;
            case KeyListviewAlphaSelect:
                WriteInt(RegRoot.CurrentUser, AdvancedKey, "ListviewAlphaSelect", enabled ? 1 : 0);
                break;
            case KeyListviewShadow:
                WriteInt(RegRoot.CurrentUser, AdvancedKey, "ListviewShadow", enabled ? 1 : 0);
                break;
            case KeyIconsOnly:
                WriteInt(RegRoot.CurrentUser, AdvancedKey, "IconsOnly", enabled ? 1 : 0);
                BroadcastSettingChange();
                break;
            case KeyAeroPeek:
                WriteInt(RegRoot.CurrentUser, DwmKey, "EnableAeroPeek", enabled ? 1 : 0);
                BroadcastSettingChange();
                break;
            case KeyTransparency:
                WriteInt(RegRoot.CurrentUser, DwmKey, "EnableTransparency", enabled ? 1 : 0);
                BroadcastSettingChange();
                break;
            default:
                throw new ArgumentException($"Unknown effect key '{effectKey}'.");
        }
    }

    private static void SetAnimation(bool enabled)
    {
        var info = new NativeMethods.ANIMATIONINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.ANIMATIONINFO>(),
            iMinAnimate = enabled,
        };
        NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETANIMATION, (uint)info.cbSize, ref info, NativeMethods.SPI_UPDATEINIFILE);
    }

    private static void SystemParametersInfoSet(uint action, bool value)
    {
        var v = value;
        NativeMethods.SystemParametersInfo(action, 0u, ref v, NativeMethods.SPI_UPDATEINIFILE);
    }

    internal static void BroadcastSettingChange()
    {
        NativeMethods.SendMessageTimeout(
            NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE, UIntPtr.Zero,
            "Policy", NativeMethods.SMTO_ABORTIFHUNG, 2000, out _);
    }

    private static int ReadInt(RegRoot root, string subKey, string name, int fallback)
    {
        try
        {
            using var baseKey = RegistryViewHelper.OpenBase(root, writable: false);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(name) is int v ? v : fallback;
        }
        catch (System.Security.SecurityException)
        {
            return fallback;
        }
    }

    private static void WriteInt(RegRoot root, string subKey, string name, int value)
    {
        using var baseKey = RegistryViewHelper.OpenBase(root, writable: true);
        using var key = baseKey.CreateSubKey(subKey, writable: true);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }
}

/// <summary>Named presets mapping effect keys → on/off. Current is intentionally absent.</summary>
public static class VisualEffectsPresets
{
    /// <summary>Ordered effect definitions used by the UI and by preset application.</summary>
    public static readonly IReadOnlyList<(string Key, string DisplayName, string Description, bool Immediate)> Effects =
        new[]
        {
            ("MinAnimate", "Window minimize/maximize animation", "Animates windows when minimized or restored.", true),
            ("MenuAnimation", "Menu fade/slide animation", "Fade and slide effects for context and drop-down menus.", true),
            ("DragFullWindows", "Show window contents while dragging", "Renders full window content during a drag operation.", true),
            ("TaskbarAnimations", "Taskbar animations", "Icon hover and launch animations on the taskbar.", true),
            ("ListviewAlphaSelect", "Smooth-select list items", "Translucent selection rectangle in file lists.", false),
            ("ListviewShadow", "Shadows under list items", "Drop shadows for desktop and explorer labels.", false),
            ("IconsOnly", "Always show icons, never thumbnails", "Disables thumbnail rendering in Explorer (on = performance).", true),
            ("EnableAeroPeek", "Peek / taskbar previews", "Live window previews when hovering the taskbar.", true),
            ("EnableTransparency", "Transparency effects", "Acrylic/transparent surfaces across the shell.", true),
        };

    public static IReadOnlyDictionary<string, bool> GetPreset(Domain.Enums.VisualEffectsPreset preset) =>
        preset switch
        {
            Domain.Enums.VisualEffectsPreset.BestAppearance => new Dictionary<string, bool>
            {
                ["MinAnimate"] = true, ["DragFullWindows"] = true, ["TaskbarAnimations"] = true,
                ["ListviewAlphaSelect"] = true, ["ListviewShadow"] = true, ["IconsOnly"] = false,
                ["EnableAeroPeek"] = true, ["EnableTransparency"] = true,
            },
            Domain.Enums.VisualEffectsPreset.BestPerformance => new Dictionary<string, bool>
            {
                ["MinAnimate"] = false, ["DragFullWindows"] = false, ["TaskbarAnimations"] = false,
                ["ListviewAlphaSelect"] = false, ["ListviewShadow"] = false, ["IconsOnly"] = true,
                ["EnableAeroPeek"] = false, ["EnableTransparency"] = false,
            },
            _ => new Dictionary<string, bool>
            {
                // Balanced: keep modern feel, drop the most expensive bits.
                ["MinAnimate"] = true, ["DragFullWindows"] = true, ["TaskbarAnimations"] = false,
                ["ListviewAlphaSelect"] = true, ["ListviewShadow"] = true, ["IconsOnly"] = false,
                ["EnableAeroPeek"] = false, ["EnableTransparency"] = true,
            },
        };
}
