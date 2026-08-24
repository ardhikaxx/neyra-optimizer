using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.App.Infrastructure;
using NeyraOptimizer.App.Views.Dialogs;
using NeyraOptimizer.Application.Modules;
using NeyraOptimizer.Application.Optimization;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.Power;
using NeyraOptimizer.Domain.Rules;

namespace NeyraOptimizer.App.ViewModels;

public partial class VisualEffectsViewModel : ViewModelBase
{
    private readonly IModuleDataService _modules;
    private readonly ISingleItemActionService _actions;
    private readonly IServiceProvider _sp;

    public ObservableCollection<VisualEffectItem> Effects { get; } = new();
    [ObservableProperty] private string _statusText = string.Empty;

    public VisualEffectsViewModel(SessionState session, IServiceProvider sp) : base(session)
    {
        _modules = sp.GetRequiredService<IModuleDataService>();
        _actions = sp.GetRequiredService<ISingleItemActionService>();
        _sp = sp;
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        Effects.Clear();
        var states = _modules.GetVisualEffectStates();
        foreach (var (key, name, desc, immediate) in Windows.Visuals.VisualEffectsPresets.Effects)
        {
            states.TryGetValue(key, out var on);
            Effects.Add(new VisualEffectItem
            {
                Key = key,
                DisplayName = name,
                CurrentEnabled = on,
                ProposedEnabled = on,
                TakesEffectImmediately = immediate,
                EffectDescription = desc,
            });
        }
    }

    private async Task ApplyPresetAsync(VisualEffectsPreset preset)
    {
        if (!CanModify) return;
        var target = Windows.Visuals.VisualEffectsPresets.GetPreset(preset);

        var recs = new List<Recommendation>();
        foreach (var effect in Effects)
        {
            if (!target.TryGetValue(effect.Key, out var want) || effect.CurrentEnabled == want) continue;
            recs.Add(new Recommendation
            {
                RuleId = "visual_preset_" + effect.Key,
                Title = $"{effect.DisplayName}: {(want ? "ON" : "OFF")}",
                Description = effect.EffectDescription,
                Reason = Translator.Instance["Visuals.Title"],
                Category = RecommendationCategory.Safe,
                RiskLevel = RiskLevel.Safe,
                AffectedComponents = new[] { effect.Key },
                RollbackDescription = "Kembalikan lewat halaman Visual Effects.",
                TargetId = effect.Key,
                CurrentStateText = effect.CurrentEnabled.ToString(),
                ProposedStateText = want.ToString(),
                Area = RuleArea.VisualEffects,
            });
        }
        if (recs.Count == 0)
        {
            StatusText = "Preset already matches current state.";
            return;
        }

        var activeWin = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive)
            ?? System.Windows.Application.Current.MainWindow!;
        await OptimizationFlow.RunAsync(activeWin, _sp, recs, Session.UsageProfile);
        Refresh();
    }

    [RelayCommand] private Task BestAppearance() => ApplyPresetAsync(VisualEffectsPreset.BestAppearance);
    [RelayCommand] private Task Balanced() => ApplyPresetAsync(VisualEffectsPreset.Balanced);
    [RelayCommand] private Task BestPerformance() => ApplyPresetAsync(VisualEffectsPreset.BestPerformance);

    /// <summary>Single-effect toggle from the list.</summary>
    public async Task ToggleEffectAsync(VisualEffectItem effect)
    {
        if (!CanModify) return;
        try
        {
            await _actions.ApplyVisualEffectAsync(effect, CancellationToken.None);
            Refresh();
        }
        catch (Exception ex) { StatusText = ex.Message; }
    }
}
