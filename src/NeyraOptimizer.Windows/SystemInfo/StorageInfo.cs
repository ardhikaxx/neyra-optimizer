using System.ComponentModel;
using System.Management;
using Microsoft.Win32;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.Windows.SystemInfo;

/// <summary>
/// Maps physical disk media types (SSD/HDD) from the Storage namespace onto logical volumes via
/// Win32_LogicalDiskToPartition. Falls back to Unknown media type when WMI data is unavailable.
/// </summary>
internal static class StorageInfo
{
    public static IReadOnlyList<StorageVolumeInfo> ReadVolumes()
    {
        var result = new List<StorageVolumeInfo>();

        // 1. Disk index → media type from MSFT_PhysicalDisk (root\Microsoft\Windows\Storage).
        var diskMediaType = new Dictionary<uint, StorageMediaType>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage",
                "SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk");
            foreach (var d in searcher.Get())
            {
                if (TryGetString(d, "DeviceId") is not string idStr || !uint.TryParse(idStr, out var idx)) continue;
                diskMediaType[idx] = TryGetNumber<ushort>(d, "MediaType") switch
                {
                    3 => StorageMediaType.Hdd,
                    4 => StorageMediaType.Ssd,
                    5 => StorageMediaType.Ssd,
                    _ => StorageMediaType.Unknown,
                };
            }
        }
        catch (ManagementException)
        {
            // Storage namespace unavailable (older builds / restricted WMI).
        }

        // 2. Partition → drive letter and partition → physical disk index.
        var letterToDiskIdx = new Dictionary<char, uint>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition");
            foreach (var rel in searcher.Get())
            {
                var antecedent = TryGetString(rel, "Antecedent") ?? string.Empty;   // partition path
                var dependent = TryGetString(rel, "Dependent") ?? string.Empty;     // logical disk path

                var driveLetter = Extract(dependent, "DeviceID");                    // "C:"
                var partitionId = Extract(antecedent, "DeviceID");                   // "Disk #0, Partition #1"
                if (driveLetter is null || partitionId is null || driveLetter.Length == 0) continue;

                var diskMarker = partitionId.IndexOf("Disk #", StringComparison.OrdinalIgnoreCase);
                if (diskMarker >= 0 &&
                    uint.TryParse(partitionId.AsSpan(diskMarker + 6).SliceWhileDigit(), out var di))
                {
                    letterToDiskIdx[driveLetter[0]] = di;
                }
            }
        }
        catch (ManagementException)
        {
            // Mapping unavailable — media type falls back to Unknown below.
        }

        // 3. Logical volumes with sizes.
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, VolumeName, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType = 3");
            foreach (var vol in searcher.Get())
            {
                var letterStr = TryGetString(vol, "DeviceID");
                if (string.IsNullOrEmpty(letterStr) || letterStr[0] is < 'A' or > 'Z') continue;
                char letter = letterStr[0];

                long sizeBytes = (long?)(TryGetNumber<ulong>(vol, "Size")) ?? 0;
                long freeBytes = (long?)(TryGetNumber<ulong>(vol, "FreeSpace")) ?? 0;

                bool isSystem = letter == System.IO.Path.GetPathRoot(Environment.SystemDirectory)![0];
                _ = letterToDiskIdx.TryGetValue(letter, out uint diskIdx);

                result.Add(new StorageVolumeInfo
                {
                    DriveLetter = letter,
                    Label = TryGetString(vol, "VolumeName") ?? string.Empty,
                    TotalGb = (long)Math.Round(sizeBytes / 1073741824.0),
                    FreeGb = (long)Math.Round(freeBytes / 1073741824.0),
                    MediaType = ResolveMediaType(letter, diskIdx, diskMediaType, isSystem),
                    IsSystemVolume = isSystem,
                });
            }
        }
        catch (ManagementException)
        {
            // Fall through with whatever was collected.
        }

        if (result.Count == 0)
        {
            // Last-resort: enumerate drives without media-type knowledge rather than showing nothing.
            foreach (var drive in System.IO.DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != System.IO.DriveType.Fixed) continue;
                bool isSystem = string.Equals(drive.Name, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase);
                result.Add(new StorageVolumeInfo
                {
                    DriveLetter = drive.Name[0],
                    Label = drive.VolumeLabel,
                    TotalGb = (long)(drive.TotalSize / 1073741824.0),
                    FreeGb = (long)(drive.AvailableFreeSpace / 1073741824.0),
                    MediaType = StorageMediaType.Unknown,
                    IsSystemVolume = isSystem,
                });
            }
        }
        return result.OrderBy(v => v.IsSystemVolume ? 0 : 1).ThenBy(v => v.DriveLetter).ToList();
    }

    private static StorageMediaType ResolveMediaType(char letter, uint diskIdx,
        Dictionary<uint, StorageMediaType> diskMediaType, bool isSystem)
    {
        if (diskMediaType.TryGetValue(diskIdx, out var mt) && mt != StorageMediaType.Unknown)
            return mt;

        // Heuristic fallback for the system volume when MSFT_PhysicalDisk is unavailable:
        // write-speed detection would require benchmarks; report Unknown honestly instead of guessing.
        return StorageMediaType.Unknown;
    }

    private static string? Extract(string wmiPath, string property)
    {
        const string marker = "=";
        var idx = wmiPath.IndexOf(property + marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var start = idx + property.Length + marker.Length;
        var quote = wmiPath.IndexOf('"', start);
        var end = quote >= 0 ? wmiPath.IndexOf('"', quote + 1) : -1;
        if (quote < 0 || end <= quote) return null;
        return wmiPath[(quote + 1)..end];
    }

    private static string? TryGetString(ManagementBaseObject obj, string property)
    {
        try { return obj[property]?.ToString(); }
        catch (ManagementException) { return null; }
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

/// <summary>Slices the leading digit run out of a span ("12abc" → "12").</summary>
internal static class SpanDigitExtensions
{
    public static System.ReadOnlySpan<char> SliceWhileDigit(this System.ReadOnlySpan<char> span)
    {
        var end = 0;
        while (end < span.Length && char.IsDigit(span[end])) end++;
        return span[..end];
    }
}
