using System.Diagnostics;
using System.ServiceProcess;
using NeyraOptimizer.Windows.Native;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.Power;

namespace NeyraOptimizer.Windows.Cleanup;

/// <summary>
/// Safe cleanup scanning and deletion. Only fixed, whitelisted locations are ever touched —
/// never user documents, Downloads, Desktop, browser profiles or application data beyond the
/// explicit cache folders listed below. Scan is a dry run; deletion happens per candidate.
/// </summary>
public sealed class CleanupScanner : ICleanupScanner
{
    public IReadOnlyList<CleanupCandidate> Scan(CancellationToken ct)
    {
        var candidates = new List<CleanupCandidate>();

        var userTemp = Environment.ExpandEnvironmentVariables("%TEMP%");
        candidates.Add(Make(CleanupCategory.UserTempFiles, "User temporary files",
            "Temporary files created by your own applications.", new[] { userTemp },
            requiresAdmin: false, ct));

        var winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        candidates.Add(Make(CleanupCategory.WindowsTempFiles, "Windows temporary files",
            "Temporary files created by Windows components and services.", new[] { winTemp },
            requiresAdmin: true, ct));

        candidates.Add(RecycleBinCandidate(ct));

        var doCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache");
        var doSize = ComputeDirectorySize(doCache, ct);
        candidates.Add(new CleanupCandidate
        {
            Category = CleanupCategory.DeliveryOptimizationCache,
            DisplayName = "Delivery Optimization cache",
            Description = "Files Windows downloaded for peer-to-peer update sharing. Removed via the official cmdlet; updates are unaffected.",
            Locations = new[] { doCache },
            EstimatedSizeBytes = doSize,
            RequiresAdministrator = true,
            RiskLevel = RiskLevel.Safe,
            IsAvailableOnThisMachine = doSize > 0,
            UnavailabilityReason = doSize > 0 ? string.Empty : "The Delivery Optimization cache is empty.",
        });

        candidates.Add(UpdateDownloadCandidate(ct));
        candidates.Add(ThumbnailCandidate(ct));
        candidates.Add(WerCandidate(ct));
        candidates.Add(DirectXShaderCacheCandidate(ct));

        return candidates;
    }

    private static CleanupCandidate Make(CleanupCategory category, string name, string desc,
        string[] roots, bool requiresAdmin, CancellationToken ct)
    {
        long size = 0;
        foreach (var root in roots)
        {
            size += ComputeDirectorySize(root, ct);
        }
        return new CleanupCandidate
        {
            Category = category,
            DisplayName = name,
            Description = desc,
            Locations = roots,
            EstimatedSizeBytes = size,
            RequiresAdministrator = requiresAdmin,
        };
    }

    private CleanupCandidate RecycleBinCandidate(CancellationToken ct)
    {
        long total = 0;
        foreach (var drive in DriveInfo.GetDrives())
        {
            ct.ThrowIfCancellationRequested();
            if (!drive.IsReady || drive.DriveType != System.IO.DriveType.Fixed) continue;
            try
            {
                var info = new NativeMethods.SHQUERYRBINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>() };
                if (NativeMethods.SHQueryRecycleBin(drive.Name, ref info) == 0)
                    total += info.i64Size;
            }
            catch (DllNotFoundException) { break; }
        }
        return new CleanupCandidate
        {
            Category = CleanupCategory.RecycleBin,
            DisplayName = "Recycle Bin",
            Description = "Empties the Recycle Bin on all local drives. Deleted items cannot be recovered afterwards.",
            Locations = Array.Empty<string>(),
            EstimatedSizeBytes = total,
            RequiresAdministrator = false,
            RiskLevel = RiskLevel.Low,
        };
    }

    private CleanupCandidate UpdateDownloadCandidate(CancellationToken ct)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"SoftwareDistribution\Download");
        long size = ComputeDirectorySize(dir, ct);

        // Only offered when the update service is NOT running, so we never fight the updater.
        var wuRunning = IsServiceRunning("wuauserv");
        var available = size > 0 && !wuRunning;
        return new CleanupCandidate
        {
            Category = CleanupCategory.WindowsUpdateDownloadCache,
            DisplayName = "Windows Update download leftovers",
            Description = "Already-installed or superseded update downloads. Offered only while Windows Update is idle.",
            Locations = new[] { dir },
            EstimatedSizeBytes = size,
            RequiresAdministrator = true,
            RiskLevel = RiskLevel.Low,
            IsAvailableOnThisMachine = available,
            UnavailabilityReason = wuRunning
                ? "Windows Update is currently active; retry after it finishes."
                : size > 0 ? string.Empty : "Nothing to clean in this location.",
        };
    }

    private static CleanupCandidate ThumbnailCandidate(CancellationToken ct) =>
        Make(CleanupCategory.ThumbnailCache, "Thumbnail cache",
            "Explorer thumbnail images. They are rebuilt automatically when folders are opened.",
            new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer") },
            requiresAdmin: false, ct);

    private static CleanupCandidate WerCandidate(CancellationToken ct) =>
        Make(CleanupCategory.ErrorReports, "Error reports and queues",
            "Archived crash reports queued for Microsoft. Contains no personal documents.",
            new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\WER"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\WER"),
            },
            requiresAdmin: false, ct);

    private static CleanupCandidate DirectXShaderCacheCandidate(CancellationToken ct) =>
        Make(CleanupCategory.DirectXShaderCache, "DirectX shader cache",
            "Compiled shader caches. Rebuilt by games automatically; first launches may be slightly slower.",
            new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D3DSCache") },
            requiresAdmin: false, ct);

    public async Task<long> DeleteCandidateAsync(CleanupCandidate candidate,
        IProgress<(long freedBytes, string currentPath)>? progress, CancellationToken ct)
    {
        if (candidate.Category == CleanupCategory.RecycleBin)
            return await Task.Run(() => EmptyRecycleBin(progress), ct).ConfigureAwait(false);

        if (candidate.Category == CleanupCategory.DeliveryOptimizationCache)
        {
            // Executed through the elevated helper using the official PowerShell cmdlet.
            throw new InvalidOperationException(
                "Delivery Optimization cleanup must run through the elevated operation pipeline.");
        }

        return await Task.Run(() =>
        {
            long freed = 0;
            foreach (var root in candidate.Locations)
            {
                if (!Directory.Exists(root)) continue;

                foreach (var file in EnumerateDeletableFiles(candidate.Category, root))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var length = new FileInfo(file).Length;
                        File.Delete(file);
                        freed += length;
                        progress?.Report((freed, file));
                    }
                    catch (IOException) { /* file locked — skipped */ }
                    catch (UnauthorizedAccessException) { }
                }
            }
            return freed;
        }, ct).ConfigureAwait(false);
    }

    private static IEnumerable<string> EnumerateDeletableFiles(CleanupCategory category, string root)
    {
        if (category == CleanupCategory.ThumbnailCache)
        {
            return Directory.EnumerateFiles(root, "thumbcache_*.db")
                .Concat(Directory.Exists(root) ? Directory.EnumerateFiles(root, "thumbcache_*.db") : Enumerable.Empty<string>());
        }
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
    }

    private static long EmptyRecycleBin(IProgress<(long, string)>? progress)
    {
        // Size was measured during scan; SHEmptyRecycleBin returns no byte counts.
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != System.IO.DriveType.Fixed) continue;
            var hr = NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, drive.Name,
                NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND);
            if (hr != 0 && (hr & 0xFFFF) is not (0x0002 or 0x0003)) // ignore "empty" results
                System.Diagnostics.Debug.WriteLine($"SHEmptyRecycleBin({drive.Name}) -> 0x{hr:X8}");
        }
        return 0; // actual freed amount reported from pre-scan estimate by caller
    }

    internal static long ComputeDirectorySize(string path, CancellationToken ct)
    {
        try
        {
            if (!Directory.Exists(path)) return 0;
            long total = 0;
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
            };
            foreach (var file in Directory.EnumerateFiles(path, "*", options))
            {
                ct.ThrowIfCancellationRequested();
                try { total += new FileInfo(file).Length; }
                catch (IOException) { }
            }
            return total;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static bool IsServiceRunning(string serviceName)
    {
        using var sc = new ServiceController(serviceName);
        return sc.Status == ServiceControllerStatus.Running;
    }
}
