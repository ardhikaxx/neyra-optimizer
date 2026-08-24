using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.App.Views.Dialogs;
using NeyraOptimizer.Application.Modules;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.Power;
using NeyraOptimizer.Domain.Rules;

namespace NeyraOptimizer.App.ViewModels;

public partial class PowerViewModel : ViewModelBase
{
    private readonly IModuleDataService _modules;
    private readonly ISingleItemActionService _actions;
    private readonly IPowerManager _power;

    public ObservableCollection<PowerPlanInfo> Plans { get; } = new();
    [ObservableProperty] private PowerPlanInfo? _selectedPlan;
    [ObservableProperty] private string _batteryText = "—";
    [ObservableProperty] private bool _overlaySupported;
    [ObservableProperty] private PowerOverlayMode _currentOverlay = PowerOverlayMode.NotSupported;
    [ObservableProperty] private string _statusText = string.Empty;

    public IEnumerable<PowerOverlayMode> OverlayOptions => new[]
        { PowerOverlayMode.BetterBattery, PowerOverlayMode.Balanced, PowerOverlayMode.BestPerformance };

    public PowerViewModel(SessionState session, IServiceProvider sp) : base(session)
    {
        _modules = sp.GetRequiredService<IModuleDataService>();
        _actions = sp.GetRequiredService<ISingleItemActionService>();
        _power = sp.GetRequiredService<IPowerManager>();
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        Plans.Clear();
        foreach (var p in _modules.GetPowerPlans())
            Plans.Add(p);
        SelectedPlan = Plans.FirstOrDefault(p => p.IsActive) ?? _modules.GetActivePowerPlan();

        var b = Session.LastAnalysis?.Profile.Battery ?? _power.GetBatteryInfo();
        BatteryText = !b.IsPresent ? "—" :
            $"{b.ChargePercent}% · {(b.IsCharging ? Translator.Instance["Power.Charging"] : Translator.Instance["Power.OnBattery"])}" +
            (b.BatteryHealthPercent is int h ? $" · {Translator.Instance["Power.Health"]}: {h}%" : string.Empty);

        OverlaySupported = _power.IsOverlaySupported;
        CurrentOverlay = OverlaySupported ? _power.GetEffectiveOverlay() : PowerOverlayMode.NotSupported;
    }

    [RelayCommand]
    private async Task ActivatePlanAsync()
    {
        if (SelectedPlan is not { } plan || plan.IsActive || !CanModify) return;
        try
        {
            await _actions.SetPowerPlanAsync(plan, CancellationToken.None);
            Refresh();
        }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    [RelayCommand]
    private async Task SetOverlayAsync()
    {
        if (!CanModify || !OverlaySupported) return;
        try
        {
            await _actions.SetPowerOverlayAsync(CurrentOverlay, _power.GetEffectiveOverlay(), CancellationToken.None);
            Refresh();
        }
        catch (Exception ex) { StatusText = ex.Message; }
    }
}
