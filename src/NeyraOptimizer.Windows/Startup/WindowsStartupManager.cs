using System.Diagnostics;
using Microsoft.Win32;
using NeyraOptimizer.Windows.Native;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.Windows.Startup;

/// <summary>
/// Detects startup entries from registry Run keys (HKCU/HKLM incl. WOW64) and startup folders.
/// Disable uses the official StartupApproved mechanism — identical to what Task Manager does — so
/// definitions are preserved and re-enabling is lossless. Entries are never deleted here.
/// </summary>
public sealed class WindowsStartupManager : IStartupManager
{
    private const string RunKeyC = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunKeyWow64 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRun = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    private const string ApprovedRun32 = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public IReadOnlyList<StartupEntry> GetStartupEntries()
    {
        var entries = new List<StartupEntry>();

        CollectRunKey(entries, RegRoot.CurrentUser, RunKeyC, "run|hkcu", StartupSource.RunKeyCurrentUser, ApprovedRun, RegRoot.CurrentUser);
        CollectRunKey(entries, RegRoot.LocalMachine, RunKeyC, "run|hklm", StartupSource.RunKeyLocalMachine, ApprovedRun, RegRoot.CurrentUser);
        if (Environment.Is64BitOperatingSystem)
            CollectRunKey(entries, RegRoot.LocalMachine, RunKeyWow64, "run|hklm-wow", StartupSource.RunKeyLocalMachineWow64, ApprovedRun, RegRoot.CurrentUser);

        CollectFolderEntries(entries, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "folder|user",
            StartupSource.StartupFolderUser);
        var commonStartMenu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            "Programs", "StartUp");
        CollectFolderEntries(entries, commonStartMenu, "folder|common", StartupSource.StartupFolderCommon);

        return entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void CollectRunKey(List<StartupEntry> sink, RegRoot root, string subKey, string idPrefix,
        StartupSource source, string approvedSubKey, RegRoot approvedRoot)
    {
        IReadOnlyList<RegistryValueDto> values;
        try
        {
            using var baseKey = RegistryViewHelper.OpenBase(root, writable: false);
            using var key = baseKey.OpenSubKey(subKey);
            if (key is null) return;
            values = key.GetValueNames()
                .Select(n => new RegistryValueDto(n, key.GetValue(n), RegistryValueKind.String))
                .Where(v => v.Data is string s && !string.IsNullOrWhiteSpace(s))
                .ToList();
        }
        catch (System.Security.SecurityException)
        {
            return;
        }

        foreach (var v in values)
        {
            var command = ((string?)v.Data)?.Trim() ?? string.Empty;
            var exePath = ExtractExecutablePath(command);
            var (publisher, impact) = ProbeExecutable(exePath);
            var enabled = IsEnabledViaApproved(approvedRoot, approvedSubKey, v.Name);
            var risk = ClassifyRisk(exePath, command);

            sink.Add(new StartupEntry
            {
                Id = $"{idPrefix}|{v.Name}",
                Name = v.Name,
                Publisher = publisher,
                Command = command,
                Source = source,
                IsEnabled = enabled,
                Impact = impact,
                RiskLevel = risk.Risk,
                IsProtected = risk.ProtectedFlag,
                ProtectionReason = risk.Reason,
            });
        }
    }

    private static void CollectFolderEntries(List<StartupEntry> sink, string folder, string idPrefix, StartupSource source)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".lnk" && ext != ".exe") continue;

            string target = file;
            string resolvedTarget = ResolveShortcut(file) ?? file;
            var (publisher, impact) = ProbeExecutable(resolvedTarget);
            var risk = ClassifyRisk(resolvedTarget, file);
            var name = ext == ".lnk" ? Path.GetFileNameWithoutExtension(file) : Path.GetFileName(file);

            sink.Add(new StartupEntry
            {
                Id = $"{idPrefix}|{file}",
                Name = name,
                Publisher = publisher,
                Command = target,
                Location = folder,
                Source = source,
                IsEnabled = IsFolderItemEnabled(Path.GetFileName(file)),
                Impact = impact,
                RiskLevel = risk.Risk,
                IsProtected = risk.ProtectedFlag,
                ProtectionReason = risk.Reason,
            });
        }
    }

    private static bool IsFolderItemEnabled(string fileName) =>
        !IsDisabledInApproved(RegRoot.CurrentUser, ApprovedFolder, fileName);

    internal static bool IsEnabledViaApproved(RegRoot root, string approvedSubKey, string valueName) =>
        !IsDisabledInApproved(root, approvedSubKey, valueName);

    private static bool IsDisabledInApproved(RegRoot root, string approvedSubKey, string valueName)
    {
        try
        {
            using var baseKey = RegistryViewHelper.OpenBase(root, writable: false);
            using var key = baseKey.OpenSubKey(approvedSubKey);
            if (key?.GetValue(valueName) is not byte[] data || data.Length < 1) return true; // missing => enabled
            // First byte bit0 set => disabled (Task Manager semantics).
            return (data[0] & 1) == 1;
        }
        catch (SystemException)
        {
            return false; // cannot determine → assume enabled, never silently hide an entry
        }
    }

    private static byte[] DisabledBlob() =>
        new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    public StartupToggleResult Disable(string entryId)
    {
        try
        {
            var parts = entryId.Split('|');
            if (parts.Length < 3) return new StartupToggleResult(false, $"Unrecognized entry id '{entryId}'.");

            switch (parts[0])
            {
                case "run":
                {
                    var valueName = entryId[(parts[0].Length + 1 + parts[1].Length + 1)..];
                    using var key = Registry.CurrentUser.CreateSubKey(ApprovedRun, writable: true);
                    key.SetValue(valueName, DisabledBlob(), RegistryValueKind.Binary);
                    return new StartupToggleResult(true, string.Empty);
                }
                case "folder":
                {
                    var fileName = Path.GetFileName(parts[2]);
                    using var key = Registry.CurrentUser.CreateSubKey(ApprovedFolder, writable: true);
                    key.SetValue(fileName, DisabledBlob(), RegistryValueKind.Binary);
                    return new StartupToggleResult(true, string.Empty);
                }
                default:
                    return new StartupToggleResult(false, $"Unknown source kind '{parts[0]}'.");
            }
        }
        catch (UnauthorizedAccessException)
        {
            return new StartupToggleResult(false, "Access denied while updating startup approval state.");
        }
        catch (IOException ex)
        {
            return new StartupToggleResult(false, "I/O failure: " + ex.Message);
        }
    }

    public StartupToggleResult Enable(string entryId)
    {
        try
        {
            var parts = entryId.Split('|');
            switch (parts[0])
            {
                case "run":
                {
                    var valueName = entryId[(parts[0].Length + 1 + parts[1].Length + 1)..];
                    using var key = Registry.CurrentUser.CreateSubKey(ApprovedRun, writable: true);
                    key.DeleteValue(valueName, throwOnMissingValue: false);
                    return new StartupToggleResult(true, string.Empty);
                }
                case "folder":
                {
                    var fileName = Path.GetFileName(parts[2]);
                    using var key = Registry.CurrentUser.CreateSubKey(ApprovedFolder, writable: true);
                    key.DeleteValue(fileName, throwOnMissingValue: false);
                    return new StartupToggleResult(true, string.Empty);
                }
                default:
                    return new StartupToggleResult(false, $"Unknown source kind '{parts[0]}'.");
            }
        }
        catch (UnauthorizedAccessException)
        {
            return new StartupToggleResult(false, "Access denied while updating startup approval state.");
        }
        catch (IOException ex)
        {
            return new StartupToggleResult(false, "I/O failure: " + ex.Message);
        }
    }

    internal static string ExtractExecutablePath(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : command.Trim('"');
        }
        var spaceIdx = command.IndexOf(' ');
        if (spaceIdx > 0 && File.Exists(command[..spaceIdx])) return command[..spaceIdx];
        if (File.Exists(command)) return command;
        // Command may carry arguments like "app.exe -flag"
        var tokenEnd = spaceIdx > 0 ? spaceIdx : command.Length;
        return command[..tokenEnd];
    }

    private static (string? Publisher, StartupImpact Impact) ProbeExecutable(string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return (null, StartupImpact.Unknown);
            var info = FileVersionInfo.GetVersionInfo(exePath);
            var publisher = string.IsNullOrWhiteSpace(info.CompanyName) ? null : info.CompanyName!.Trim();
            var impact = ClassifyImpact(publisher, exePath);
            return (publisher, impact);
        }
        catch (SystemException)
        {
            return (null, StartupImpact.Unknown);
        }
    }

    private static StartupImpact ClassifyImpact(string? publisher, string exePath)
    {
        var name = Path.GetFileNameWithoutExtension(exePath) ?? string.Empty;
        if (name.Contains("update", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("agent", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("helper", StringComparison.OrdinalIgnoreCase))
            return StartupImpact.Medium;
        if (name.Contains("sync", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("cloud", StringComparison.OrdinalIgnoreCase))
            return StartupImpact.High;
        return StartupImpact.Low;
    }

    private static (RiskLevel Risk, bool ProtectedFlag, string Reason) ClassifyRisk(string exePath, string rawCommand)
    {
        if (exePath.Contains(@"Windows\System32", StringComparison.OrdinalIgnoreCase) ||
            exePath.Contains(@"Windows\SysWOW64", StringComparison.OrdinalIgnoreCase))
            return (RiskLevel.Critical, true, "Resides inside the Windows system directory.");

        foreach (var secVendor in new[] { "msmpeng", "nissrv", "securityhealth", "mcshield", "avp.", "norton", "avast", "avgnt" })
        {
            if (rawCommand.Contains(secVendor, StringComparison.OrdinalIgnoreCase))
                return (RiskLevel.Critical, true, "Belongs to security software that must keep running.");
        }
        return (RiskLevel.Safe, false, string.Empty);
    }

    /// <summary>
    /// Resolves .lnk shortcut targets by parsing the documented shell-link binary format
    /// (LocalBasePath / LocalBasePathUnicode fields). No COM or scripting dependencies involved.
    /// </summary>
    private static string? ResolveShortcut(string file)
    {
        try
        {
            var bytes = File.ReadAllBytes(file);
            if (bytes.Length < 0x4C) return null;

            var headerSize = BitConverter.ToInt32(bytes, 0);
            if (headerSize < 0x4C || headerSize > bytes.Length) return null;
            var flags = BitConverter.ToUInt32(bytes, 0x14);
            const uint hasLinkInfo = 0x02;

            if ((flags & hasLinkInfo) == 0) return null;

            var offset = headerSize;
            if (offset + 12 > bytes.Length) return null;

            var linkInfoSize = BitConverter.ToUInt32(bytes, (int)offset);
            var linkInfoHeaderSize = BitConverter.ToUInt32(bytes, (int)(offset + 4));
            var linkInfoFlags = BitConverter.ToUInt32(bytes, (int)(offset + 8));
            const uint localBasePathFlag = 0x01;
            const uint commonPathSuffixFlag = 0x08;
            if (linkInfoSize == 0 || offset + linkInfoSize > bytes.Length) return null;
            if ((linkInfoFlags & localBasePathFlag) == 0) return null;
            if (linkInfoHeaderSize < 0x1C) return null;

            var basePathOffset = BitConverter.ToUInt32(bytes, (int)(offset + 0x10));
            var absolute = (long)offset + basePathOffset;
            if (absolute >= bytes.Length) return null;

            // ANSI base path first (legacy field), then Unicode variant when present.
            var endAnsi = Array.IndexOf(bytes, (byte)0, (int)absolute, Math.Min(1024, bytes.Length - (int)absolute));
            if (endAnsi > 0)
            {
                var ansi = System.Text.Encoding.ASCII.GetString(bytes, (int)absolute, endAnsi - (int)absolute);
                if (!string.IsNullOrWhiteSpace(ansi)) return ansi;
            }

            if (linkInfoHeaderSize >= 0x24 && (linkInfoFlags & 0x80000000) != 0) // IsCommonPathSuffixAndLinkInfoValid? unicode flag
            {
                var unicodeOffsetField = BitConverter.ToUInt32(bytes, (int)(offset + 0x20));
                if (unicodeOffsetField > 0)
                {
                    var absUni = (long)offset + unicodeOffsetField;
                    if (absUni + 1 < bytes.Length)
                    {
                        var uniEnd = Array.IndexOf(bytes, new byte[] { 0, 0 }, (int)absUni, Math.Min(2048, bytes.Length - (int)absUni));
                        if (uniEnd > 0)
                        {
                            var uni = System.Text.Encoding.Unicode.GetString(bytes, (int)absUni, uniEnd - (int)absUni);
                            if (!string.IsNullOrWhiteSpace(uni)) return uni;
                        }
                    }
                }
            }
            _ = commonPathSuffixFlag;
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
