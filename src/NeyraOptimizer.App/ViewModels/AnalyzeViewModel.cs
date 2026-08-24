using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.Application.Analysis;
using NeyraOptimizer.Application.Session;

namespace NeyraOptimizer.App.ViewModels;

public partial class AnalyzeViewModel : ViewModelBase
{
    private readonly IAnalysisOrchestrator _analyzer;

    [ObservableProperty] private string _osText = "—";
    [ObservableProperty] private string _cpuText = "—";
    [ObservableProperty] private string _ramText = "—";
    [ObservableProperty] private string _gpuText = "—";
    [ObservableProperty] private string _storageText = "—";
    [ObservableProperty] private string _batteryText = "—";
    [ObservableProperty] private string _powerPlanText = "—";
    [ObservableProperty] private string _securityText = "—";
    [ObservableProperty] private string _elevationText = "—";
    [ObservableProperty] private string _deviceClassText = "—";
    [ObservableProperty] private string _classificationReasons = string.Empty;
    [ObservableProperty] private bool _isAnalyzing;
    [ObservableProperty] private string _statusText = Translator.Instance["Dash.NeverAnalyzed"];

    public AnalyzeViewModel(SessionState session, IServiceProvider sp) : base(session)
    {
        _analyzer = sp.GetRequiredService<IAnalysisOrchestrator>();
        PopulateFromSession();
    }

    private void PopulateFromSession()
    {
        var bundle = Session.LastAnalysis;
        if (bundle is null) return;
        var p = bundle.Profile;
        OsText = $"{p.Windows.Edition} {p.Windows.DisplayVersion} — Build {p.Windows.BuildNumber}.{p.Windows.UpdateBuildRevision} ({p.Windows.Architecture})";
        CpuText = $"{p.Cpu.Name} — {p.Cpu.PhysicalCores}C / {p.Cpu.LogicalProcessors}T @ {p.Cpu.BaseClockGhz:0.0#} GHz";
        RamText = $"{p.Memory.TotalPhysicalMb / 1024.0:0.#} GB";
        GpuText = p.Gpus.Count == 0 ? "—" :
            string.Join(" | ", p.Gpus.Select(FormatGpu));
        StorageText = string.Join(" | ", p.Volumes.Select(v =>
            $"{v.DriveLetter}: {(v.MediaType == Domain.Enums.StorageMediaType.Ssd ? "SSD" : v.MediaType == Domain.Enums.StorageMediaType.Hdd ? "HDD" : "?")} {v.FreeGb:N0}/{v.TotalGb:N0} GB"));
        BatteryText = p.Battery.IsPresent
            ? $"{p.Battery.ChargePercent}% ({(p.Battery.IsCharging ? Translator.Instance["Power.Charging"] : Translator.Instance["Power.OnBattery"])})"
            : Translator.Instance["Common.No"];
        PowerPlanText = p.ActivePowerPlanName;
        SecurityText = $"Defender: {On(p.Security.DefenderEnabled)} · RTP: {On(p.Security.RealTimeProtectionEnabled)} · Firewall: {On(p.Security.FirewallEnabled)}";
        ElevationText = p.IsRunningAsAdministrator
            ? Translator.Instance["Analyze.Elevated"]
            : Translator.Instance["Analyze.StandardUser"];
        DeviceClassText = $"{p.DeviceClass} — score {p.HardwareScore}/100";
        ClassificationReasons = string.Join("\n", p.ClassificationReasons);
        StatusText = p.Windows.BuildLabel;
    }

    private static string On(bool b) => b ? "✓" : "✗";

    private static string FormatGpu(Domain.Models.System.GpuInfo gpu)
    {
        if (gpu.VramMb <= 0) return gpu.Name;
        var kind = gpu.IsDedicated ? "dedicated" : "integrated";
        return $"{gpu.Name} ({gpu.VramMb:N0} MB, {kind})";
    }

    [RelayCommand]
    private async Task RunAsync(CancellationToken ct)
    {
        if (IsAnalyzing) return;
        IsAnalyzing = true;
        StatusText = Translator.Instance["Analyze.ScanInProgress"];
        try
        {
            var result = await _analyzer.AnalyzeAsync(2, ct);
            Session.LastAnalysis = result.Bundle;
            Session.LastRecommendations = result.Recommendations;
            PopulateFromSession();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusText = ex.Message; }
        finally { IsAnalyzing = false; }
    }
}
