using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.Application.Analysis;
using NeyraOptimizer.Application.Measurement;
using NeyraOptimizer.Application.Optimization;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Engines;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly IAnalysisOrchestrator _analyzer;
    private readonly ISystemInformationProvider _sysInfo;
    private readonly IPerformanceMonitor _perf;
    private readonly IOptimizationCoordinator _optimizer;
    private readonly MeasurementStore _measurements;

    private DispatcherTimer? _timer;

    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private int _score;
    [ObservableProperty] private string _scoreBand = "—";
    [ObservableProperty] private string _scoreBandColor = "Brush.Accent";
    [ObservableProperty] private double _ramUsedPercent;
    [ObservableProperty] private string _ramText = "—";
    [ObservableProperty] private double? _cpuPercent;
    [ObservableProperty] private double? _gpuPercent;
    [ObservableProperty] private double? _diskPercent;
    [ObservableProperty] private int _processCount;
    [ObservableProperty] private int _startupCount;
    [ObservableProperty] private string _freeStorageText = "—";
    [ObservableProperty] private string _uptimeText = "—";
    [ObservableProperty] private string _deviceClassText = "—";
    [ObservableProperty] private string _lastAnalysisText = Translator.Instance["Dash.NeverAnalyzed"];
    [ObservableProperty] private bool _isAnalyzing;
    [ObservableProperty] private string _analyzeStatus = string.Empty;
    [ObservableProperty] private bool _monitoringEnabled = true;

    public DashboardViewModel(SessionState session, IServiceProvider sp) : base(session)
    {
        _analyzer = sp.GetRequiredService<IAnalysisOrchestrator>();
        _sysInfo = sp.GetRequiredService<ISystemInformationProvider>();
        _perf = sp.GetRequiredService<IPerformanceMonitor>();
        _optimizer = sp.GetRequiredService<IOptimizationCoordinator>();
        _measurements = sp.GetRequiredService<MeasurementStore>();

        MonitoringEnabled = session.Settings.DashboardMonitoringEnabled;
        StartTimer();
        RefreshLive();
    }

    public void StopMonitoring()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void StartTimer()
    {
        if (!MonitoringEnabled) return;
        var seconds = Math.Clamp(Session.Settings.DashboardRefreshSeconds, 2, 60);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _timer.Tick += async (_, _) => await SampleAsync();
        _timer.Start();
    }

    public void RestartMonitoring()
    {
        StopMonitoring();
        MonitoringEnabled = Session.Settings.DashboardMonitoringEnabled && Session.Settings.DashboardRefreshSeconds >= 2;
        if (MonitoringEnabled) StartTimer();
    }

    private async Task SampleAsync()
    {
        try
        {
            var mem = _perf.SampleMemory();
            RamUsedPercent = mem.TotalMb <= 0 ? 0 : Math.Round((mem.TotalMb - mem.AvailableMb) * 100.0 / mem.TotalMb, 1);
            RamText = $"{(mem.TotalMb - mem.AvailableMb) / 1024.0:0.0} / {mem.TotalMb / 1024.0:0.0} GB";

            CpuPercent = await _perf.SampleCpuLoadAsync(1, CancellationToken.None);
            GpuPercent = _perf.SampleGpuUsagePercent();
            DiskPercent = _perf.SampleDiskActivePercent();

            var summary = _sysInfo.GetProcessSummary();
            ProcessCount = summary.ProcessCount;
        }
        catch
        {
            // Sampling is best-effort; never crash the dashboard.
        }
    }

    /// <summary>Immediate light refresh (no CPU sampling window).</summary>
    public void RefreshLive()
    {
        var mem = _perf.SampleMemory();
        RamUsedPercent = mem.TotalMb <= 0 ? 0 : Math.Round((mem.TotalMb - mem.AvailableMb) * 100.0 / mem.TotalMb, 1);
        RamText = $"{(mem.TotalMb - mem.AvailableMb) / 1024.0:0.0} / {mem.TotalMb / 1024.0:0.0} GB";

        var vol = _sysInfo.GetStorageVolumes().FirstOrDefault(v => v.IsSystemVolume);
        FreeStorageText = vol is null ? "—" : $"{vol.FreeGb:N0} GB / {vol.TotalGb:N0} GB";

        var boot = _sysInfo.GetBootTimeUtc();
        var up = DateTime.UtcNow - boot;
        UptimeText = $"{(int)up.TotalHours}h {up.Minutes}m";

        if (Session.LastAnalysis is not null)
        {
            HasData = true;
            var b = Session.LastAnalysis;
            DeviceClassText = b.Profile.DeviceClass.ToString();
            StartupCount = b.StartupEntries.Count(e => e.IsEnabled);

            if (_measurements.LoadBaseline() is { } snap)
            {
                var score = PerformanceScoreCalculator.Compute(snap);
                Score = score.Score;
                ScoreBand = TranslateBand(score.Band);
                ScoreBandColor = BandColor(score.Band);
            }
            LastAnalysisText = b.Profile.AnalyzedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    private static string TranslateBand(string band) => band switch
    {
        PerformanceScoreCalculator.BandExcellent => Translator.Instance["Band.Excellent"],
        PerformanceScoreCalculator.BandGood => Translator.Instance["Band.Good"],
        PerformanceScoreCalculator.BandNeedsAttention => Translator.Instance["Band.NeedsAttention"],
        PerformanceScoreCalculator.BandCritical => Translator.Instance["Band.Critical"],
        _ => band,
    };

    private static string BandColor(string band) => band switch
    {
        PerformanceScoreCalculator.BandExcellent => "Brush.Success",
        PerformanceScoreCalculator.BandGood => "Brush.Success",
        PerformanceScoreCalculator.BandNeedsAttention => "Brush.Warning",
        _ => "Brush.Danger",
    };

    [RelayCommand]
    private async Task AnalyzeAsync(CancellationToken ct)
    {
        if (IsAnalyzing) return;
        IsAnalyzing = true;
        AnalyzeStatus = Translator.Instance["Analyze.ScanInProgress"];
        try
        {
            var result = await _analyzer.AnalyzeAsync(baselineSampleSeconds: 2, ct);
            Session.LastAnalysis = result.Bundle;
            Session.LastRecommendations = result.Recommendations;
            RefreshLive();
            AnalyzeStatus = result.Score is null ? string.Empty :
                $"{Translator.Instance["Dash.PerformanceScore"]}: {result.Score.Score} — {TranslateBand(result.Score.Band)}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AnalyzeStatus = ex.Message;
        }
        finally { IsAnalyzing = false; }
    }
}
