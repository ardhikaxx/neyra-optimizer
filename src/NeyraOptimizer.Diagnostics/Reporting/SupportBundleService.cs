using System.IO.Compression;
using System.Text.Json;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Infrastructure.IO;
using NeyraOptimizer.Infrastructure.Json;

namespace NeyraOptimizer.Diagnostics.Reporting;

public interface ISupportBundleService
{
    IReadOnlyList<string> GetIncludedItems();
    Task<string> CreateSupportBundleAsync(SystemProfile profile, string targetZipPath, CancellationToken ct = default);
}

public sealed class SupportBundleService : ISupportBundleService
{
    public IReadOnlyList<string> GetIncludedItems()
    {
        return new[]
        {
            "Spesifikasi Sistem Anonim (OS, CPU, RAM, Disk Type, Battery)",
            "Berkas Log Aplikasi Terbaru (tanpa path pribadi/kredensial)",
            "Metadata Riwayat Optimasi & Status Rollback",
            "Metadata Snapshot Optimasi (struktur perubahan tanpa data sensitif)",
            "Status Kompatibilitas Windows"
        };
    }

    public async Task<string> CreateSupportBundleAsync(SystemProfile profile, string targetZipPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetZipPath);

        var targetDir = Path.GetDirectoryName(targetZipPath);
        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        if (File.Exists(targetZipPath))
        {
            File.Delete(targetZipPath);
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "NeyraSupportBundle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. System Info
            var sysInfoJson = JsonSerializer.Serialize(new
            {
                ExportedAtUtc = DateTime.UtcNow,
                profile.Windows,
                profile.Cpu,
                profile.Memory,
                profile.Gpus,
                profile.Volumes,
                profile.DeviceClass,
                profile.HardwareScore,
                profile.ClassificationReasons,
                profile.Chassis,
                profile.BootTimeUtc,
                profile.Uptime
            }, JsonOptions.Default);

            await File.WriteAllTextAsync(Path.Combine(tempDir, "system_info.json"), sysInfoJson, ct).ConfigureAwait(false);

            // 2. Logs
            var logsDir = AppPaths.LogsDir;
            if (Directory.Exists(logsDir))
            {
                var bundleLogsDir = Path.Combine(tempDir, "logs");
                Directory.CreateDirectory(bundleLogsDir);
                foreach (var logFile in Directory.EnumerateFiles(logsDir, "*.log").Take(5))
                {
                    var dest = Path.Combine(bundleLogsDir, Path.GetFileName(logFile));
                    File.Copy(logFile, dest, overwrite: true);
                }
            }

            // 3. History
            var historyDir = AppPaths.History;
            if (Directory.Exists(historyDir))
            {
                var bundleHistoryDir = Path.Combine(tempDir, "history");
                Directory.CreateDirectory(bundleHistoryDir);
                foreach (var historyFile in Directory.EnumerateFiles(historyDir, "*.json").Take(10))
                {
                    var dest = Path.Combine(bundleHistoryDir, Path.GetFileName(historyFile));
                    File.Copy(historyFile, dest, overwrite: true);
                }
            }

            // 4. Snapshots metadata
            var snapshotsDir = AppPaths.Snapshots;
            if (Directory.Exists(snapshotsDir))
            {
                var bundleSnapshotsDir = Path.Combine(tempDir, "snapshots_metadata");
                Directory.CreateDirectory(bundleSnapshotsDir);
                foreach (var snapFile in Directory.EnumerateFiles(snapshotsDir, "*.json").Take(10))
                {
                    var dest = Path.Combine(bundleSnapshotsDir, Path.GetFileName(snapFile));
                    File.Copy(snapFile, dest, overwrite: true);
                }
            }

            // Create Zip
            await Task.Run(() => ZipFile.CreateFromDirectory(tempDir, targetZipPath), ct).ConfigureAwait(false);

            return targetZipPath;
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }
}