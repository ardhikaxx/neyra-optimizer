using System.Text;
using System.Text.Json;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Domain.Rules;
using NeyraOptimizer.Infrastructure.Json;

namespace NeyraOptimizer.Diagnostics.Reporting;

public interface IHealthReportGenerator
{
    string GenerateJson(AnalysisBundle bundle, IReadOnlyList<Recommendation>? recommendations, IReadOnlyList<MetricComparison>? comparisons);
    string GenerateHtml(AnalysisBundle bundle, IReadOnlyList<Recommendation>? recommendations, IReadOnlyList<MetricComparison>? comparisons);
    string GenerateMarkdown(AnalysisBundle bundle, IReadOnlyList<Recommendation>? recommendations, IReadOnlyList<MetricComparison>? comparisons);
}

public sealed class HealthReportGenerator : IHealthReportGenerator
{
    public string GenerateJson(AnalysisBundle bundle, IReadOnlyList<Recommendation>? recommendations, IReadOnlyList<MetricComparison>? comparisons)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var reportObj = new
        {
            GeneratedAtUtc = DateTime.UtcNow,
            bundle.Profile.Windows,
            bundle.Profile.Cpu,
            bundle.Profile.Memory,
            bundle.Profile.Gpus,
            bundle.Profile.Volumes,
            bundle.Profile.DeviceClass,
            bundle.Profile.HardwareScore,
            bundle.Profile.ClassificationReasons,
            Baseline = bundle.Baseline,
            Recommendations = recommendations ?? Array.Empty<Recommendation>(),
            Comparisons = comparisons ?? Array.Empty<MetricComparison>()
        };

        return JsonSerializer.Serialize(reportObj, JsonOptions.Default);
    }

    public string GenerateMarkdown(AnalysisBundle bundle, IReadOnlyList<Recommendation>? recommendations, IReadOnlyList<MetricComparison>? comparisons)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var p = bundle.Profile;
        var sb = new StringBuilder();

        sb.AppendLine("# Neyra Optimizer - Laporan Kesehatan Performa Sistem");
        sb.AppendLine($"*Waktu Pembuatan: {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");
        sb.AppendLine();

        sb.AppendLine("## 1. Spesifikasi Perangkat");
        sb.AppendLine($"- **Sistem Operasi**: {p.Windows.Edition} ({p.Windows.DisplayVersion}, Build {p.Windows.BuildNumber}) - {p.Windows.Architecture}");
        sb.AppendLine($"- **Prosesor**: {p.Cpu.Name} ({p.Cpu.PhysicalCores} Cores / {p.Cpu.LogicalProcessors} Threads)");
        sb.AppendLine($"- **RAM Total**: {p.Memory.TotalPhysicalMb / 1024.0:F1} GB");
        if (p.Gpus.Count > 0)
        {
            sb.AppendLine($"- **GPU**: {string.Join(", ", p.Gpus.Select(g => $"{g.Name} ({(g.IsDedicated ? "Dedicated" : "Integrated")})"))}");
        }
        sb.AppendLine($"- **Penyimpanan Sistem**: {(p.HasSystemSsd ? "SSD" : "HDD")} ({p.Volumes.FirstOrDefault(v => v.IsSystemVolume)?.FreeGb ?? 0} GB Bebas)");
        sb.AppendLine($"- **Klasifikasi Perangkat**: {p.DeviceClass} (Skor Hardware: {p.HardwareScore}/100)");
        sb.AppendLine();

        if (bundle.Baseline != null)
        {
            var b = bundle.Baseline;
            sb.AppendLine("## 2. Pengukuran Kondisi Awal");
            sb.AppendLine($"- **Penggunaan RAM**: {b.UsedRamMb} MB / {b.TotalRamMb} MB ({b.RamUsagePercent}%)");
            if (b.CpuLoadPercent.HasValue) sb.AppendLine($"- **Beban CPU**: {b.CpuLoadPercent:F1}%");
            sb.AppendLine($"- **Jumlah Proses Berjalan**: {b.ProcessCount}");
            sb.AppendLine($"- **Aplikasi Startup Aktif**: {b.StartupEntriesEnabled}");
            sb.AppendLine($"- **Service Otomatis**: {b.AutoStartServicesRunning}");
            sb.AppendLine();
        }

        if (comparisons != null && comparisons.Count > 0)
        {
            sb.AppendLine("## 3. Perbandingan Sebelum & Sesudah Optimasi");
            sb.AppendLine("| Metrik | Sebelum | Sesudah | Perubahan | Status |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var c in comparisons)
            {
                var status = c.Improved ? " Meningkat" : (c.Degraded ? " Menurun" : " Stabil");
                sb.AppendLine($"| {c.MetricName} | {c.Before} {c.Unit} | {c.After} {c.Unit} | {c.DeltaText} | {status} |");
            }
            sb.AppendLine();
        }

        if (recommendations != null && recommendations.Count > 0)
        {
            sb.AppendLine("## 4. Rekomendasi Optimasi");
            foreach (var r in recommendations)
            {
                sb.AppendLine($"### [{r.Category}] {r.Title}");
                sb.AppendLine($"- **Deskripsi**: {r.Description}");
                sb.AppendLine($"- **Alasan**: {r.Reason}");
                if (!string.IsNullOrWhiteSpace(r.EstimatedImpact))
                    sb.AppendLine($"- **Estimasi Dampak**: {r.EstimatedImpact}");
                sb.AppendLine($"- **Tingkat Risiko**: {r.RiskLevel} | **Memerlukan Admin**: {(r.RequiresAdministrator ? "Ya" : "Tidak")} | **Perlu Restart**: {(r.RequiresRestart ? "Ya" : "Tidak")}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("---");
        sb.AppendLine("*Neyra Optimizer - Alat Penyetelan Performa Windows 10 & 11 yang Aman dan Reversible.*");

        return sb.ToString();
    }

    public string GenerateHtml(AnalysisBundle bundle, IReadOnlyList<Recommendation>? recommendations, IReadOnlyList<MetricComparison>? comparisons)
    {
        var md = GenerateMarkdown(bundle, recommendations, comparisons);
        var bodyLines = md.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        var htmlBody = new StringBuilder();
        foreach (var line in bodyLines)
        {
            if (line.StartsWith("# ", StringComparison.Ordinal)) htmlBody.AppendLine($"<h1>{Escape(line[2..])}</h1>");
            else if (line.StartsWith("## ", StringComparison.Ordinal)) htmlBody.AppendLine($"<h2>{Escape(line[3..])}</h2>");
            else if (line.StartsWith("### ", StringComparison.Ordinal)) htmlBody.AppendLine($"<h3>{Escape(line[4..])}</h3>");
            else if (line.StartsWith("- ", StringComparison.Ordinal)) htmlBody.AppendLine($"<li>{Escape(line[2..])}</li>");
            else if (line.Trim() == "---") htmlBody.AppendLine("<hr/>");
            else if (!string.IsNullOrWhiteSpace(line)) htmlBody.AppendLine($"<p>{Escape(line)}</p>");
        }

        return $@"<!DOCTYPE html>
<html lang=""id"">
<head>
    <meta charset=""utf-8"">
    <title>Neyra Optimizer - Laporan Kesehatan Sistem</title>
    <style>
        body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 40px; background: #0f172a; color: #f8fafc; line-height: 1.6; }}
        h1 {{ color: #38bdf8; border-bottom: 2px solid #1e293b; padding-bottom: 10px; }}
        h2 {{ color: #7dd3fc; margin-top: 30px; }}
        h3 {{ color: #93c5fd; }}
        li {{ margin-bottom: 4px; }}
        hr {{ border: 0; border-top: 1px solid #334155; margin: 30px 0; }}
        p {{ color: #cbd5e1; }}
        .card {{ background: #1e293b; border-radius: 8px; padding: 20px; margin-bottom: 20px; }}
    </style>
</head>
<body>
    <div class=""card"">
        {htmlBody}
    </div>
</body>
</html>";
    }

    private static string Escape(string text) => System.Net.WebUtility.HtmlEncode(text);
}