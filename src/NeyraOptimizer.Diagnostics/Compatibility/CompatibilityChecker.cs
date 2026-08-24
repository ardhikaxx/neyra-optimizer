using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.Diagnostics.Compatibility;

public sealed record CompatibilityResult
{
    public bool IsSupported { get; init; }
    public bool IsReadOnlyDiagnosticsMode { get; init; }
    public string OsSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BlockReasons { get; init; } = Array.Empty<string>();
}

public interface ICompatibilityChecker
{
    CompatibilityResult Check(WindowsIdentityInfo windows, bool isElevated);
}

public sealed class CompatibilityChecker : ICompatibilityChecker
{
    public const int MinimumSupportedBuild = 17763; // Windows 10 Version 1809
    public const int Windows11FirstBuild = 22000;   // Windows 11 21H2

    public CompatibilityResult Check(WindowsIdentityInfo windows, bool isElevated)
    {
        ArgumentNullException.ThrowIfNull(windows);

        var warnings = new List<string>();
        var blockReasons = new List<string>();
        var isReadOnly = false;

        var osName = windows.IsWindows11 ? "Windows 11" : (windows.IsWindows10 ? "Windows 10" : "Windows");
        var summary = $"{osName} {windows.Edition} ({windows.DisplayVersion}, Build {windows.BuildNumber}.{windows.UpdateBuildRevision}) - {windows.Architecture}";

        if (windows.BuildNumber < MinimumSupportedBuild)
        {
            blockReasons.Add($"Versi Windows build {windows.BuildNumber} berada di bawah batas minimum yang didukung (Build {MinimumSupportedBuild} / Windows 10 1809+).");
            isReadOnly = true;
        }

        if (!windows.Is64BitOperatingSystem)
        {
            warnings.Add("Sistem operasi 32-bit terdeteksi. Beberapa fitur optimasi memori dan integrasi Windows 64-bit dinonaktifkan.");
        }

        if (windows.IsVirtualMachine)
        {
            warnings.Add("Lingkungan Virtual Machine terdeteksi. Pengukuran performa hardware aktual (GPU/Power Plan) mungkin terbatas.");
        }

        if (!isElevated)
        {
            warnings.Add("Aplikasi berjalan dalam mode Standard User. Fitur analisis tetap aktif, namun operasi perubahan sistem akan meminta izin Administrator (UAC) saat diterapkan.");
        }

        var isSupported = blockReasons.Count == 0;

        return new CompatibilityResult
        {
            IsSupported = isSupported,
            IsReadOnlyDiagnosticsMode = isReadOnly,
            OsSummary = summary,
            Warnings = warnings,
            BlockReasons = blockReasons
        };
    }
}