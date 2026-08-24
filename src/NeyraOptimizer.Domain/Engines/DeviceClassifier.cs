using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.Domain.Engines;

/// <summary>
/// Classifies the device into a DeviceClass using CPU, RAM, GPU, storage type and chassis.
/// RAM alone is never sufficient: identical 4 GB machines get different recommendations when one
/// has an HDD + weak CPU and the other an SSD + capable CPU.
/// </summary>
public static class DeviceClassifier
{
    public static (DeviceClass Class, int Score, IReadOnlyList<string> Reasons) Classify(SystemProfile p)
    {
        var reasons = new List<string>();
        int cpu = ScoreCpu(p.Cpu, reasons);
        int ram = ScoreRam(p.Memory.TotalPhysicalMb, reasons);
        int storage = ScoreStorage(p, reasons);
        int gpu = ScoreGpu(p, reasons);

        int total = (int)Math.Round(cpu * 0.34 + ram * 0.28 + storage * 0.22 + gpu * 0.16);
        total = Math.Clamp(total, 0, 100);

        DeviceClass cls;
        if (p.HasDedicatedGpu && cpu >= 60 && p.Memory.TotalPhysicalMb >= 8192 && total >= 62)
        {
            cls = DeviceClass.Gaming;
            reasons.Add("Dedicated GPU with a capable CPU qualifies this machine for the Gaming class.");
        }
        else if (total < 28)
        {
            cls = DeviceClass.LowEnd;
            reasons.Add("Combined hardware score is in the lowest band; conservative optimization only.");
        }
        else if (total < 42)
        {
            cls = DeviceClass.EntryLevel;
        }
        else if (total < 58)
        {
            cls = DeviceClass.Balanced;
        }
        else if (total < 74)
        {
            cls = DeviceClass.MidRange;
        }
        else
        {
            cls = DeviceClass.HighPerformance;
        }

        return (cls, total, reasons);
    }

    private static int ScoreCpu(CpuInfo cpu, List<string> reasons)
    {
        int score = cpu.LogicalProcessors switch
        {
            <= 2 => 15,
            4 => 40,
            6 => 58,
            8 => 74,
            >= 12 => 88,
            _ => 65,
        };

        // Clock contributes modestly.
        score += (int)Math.Clamp((cpu.BaseClockGhz - 1.2) * 12, -6, 14);

        string n = cpu.Name ?? string.Empty;
        bool weakFamily = ContainsAny(n, "celeron", "pentium", "atom", "a4", "a6", "a9", "n30", "n40", "n50", "j41", "j40", "n95", "n97", "silvermont", "ahtek");
        bool strongFamily = ContainsAny(n, "i9", "ryzen 9", "i7-1", "i7-8", "i7-9", "ryzen 7", "threadripper", "xeon w", "ultra 7", "ultra 9");
        if (weakFamily) score -= 14;
        if (strongFamily) score += 10;

        score = Math.Clamp(score, 5, 100);
        reasons.Add($"CPU '{cpu.Name}': {cpu.LogicalProcessors} logical processors at {cpu.BaseClockGhz:0.0#} GHz → compute score {score}/100.");
        return score;
    }

    private static int ScoreRam(long totalMb, List<string> reasons)
    {
        double gb = totalMb / 1024.0;
        int score = gb switch
        {
            <= 3 => 10,
            <= 5 => 30,
            <= 7 => 52,
            <= 11 => 70,
            <= 15 => 84,
            _ => 95,
        };
        reasons.Add($"RAM {gb:0.#} GB → memory score {score}/100.");
        return score;
    }

    private static int ScoreStorage(SystemProfile p, List<string> reasons)
    {
        var sys = p.Volumes.FirstOrDefault(v => v.IsSystemVolume);
        if (sys is null)
        {
            reasons.Add("System volume not detected; neutral storage score.");
            return 45;
        }
        int score = sys.MediaType switch
        {
            StorageMediaType.Ssd => 90,
            StorageMediaType.Hybrid => 65,
            StorageMediaType.Hdd => 20,
            _ => 45,
        };
        // Small SSDs (<128 GB) often cause space pressure.
        if (sys.MediaType == StorageMediaType.Ssd && sys.TotalGb < 128) score -= 12;
        reasons.Add($"System drive {(sys.MediaType == StorageMediaType.Ssd ? "SSD" : sys.MediaType == StorageMediaType.Hdd ? "HDD" : "hybrid/unknown")} ({sys.TotalGb:N0} GB) → storage score {Math.Clamp(score,0,100)}/100.");
        return Math.Clamp(score, 0, 100);
    }

    private static int ScoreGpu(SystemProfile p, List<string> reasons)
    {
        if (!p.Gpus.Any())
        {
            reasons.Add("No GPU information available; neutral graphics score.");
            return 45;
        }
        int best = 40; // integrated baseline
        foreach (var g in p.Gpus)
        {
            int s = g.IsDedicated ? 80 : 40;
            if (g.VramMb >= 6144) s += 10;
            else if (g.VramMb >= 2048) s += 5;
            string n = g.Name ?? string.Empty;
            if (ContainsAny(n, "rtx ", "rx 6", "rx 7", "gtx 16", "gtx 10")) s += 8;
            best = Math.Max(best, s);
        }
        reasons.Add(p.HasDedicatedGpu
            ? $"Dedicated GPU detected ({p.Gpus.First(g => g.IsDedicated).Name}) → graphics score {best}/100."
            : "Integrated graphics only → graphics score limited by design.");
        return Math.Clamp(best, 0, 100);
    }

    private static bool ContainsAny(string haystack, params string[] needles) =>
        needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));
}
