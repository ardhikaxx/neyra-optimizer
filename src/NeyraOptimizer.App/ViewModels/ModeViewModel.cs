using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Infrastructure;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.App.Views.Dialogs;
using NeyraOptimizer.Application.Modes;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Rules;
using NeyraOptimizer.Optimization.Modes;

namespace NeyraOptimizer.App.ViewModels;

public partial class ModeViewModel : ViewModelBase
{
    private readonly IModesCoordinator _modes;
    private readonly IServiceProvider _sp;

    public string ModeName { get; }
    public string ModeNote { get; }

    public ObservableCollection<RecommendationRowAdapter> Items { get; } = new();
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _unavailabilityReason = string.Empty;
    [ObservableProperty] private bool _isAvailable = true;
    [ObservableProperty] private bool _hasItems;
    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>Read-only adapter so mode checklists reuse the same visual row style.</summary>
    public sealed class RecommendationRowAdapter
    {
        public required Recommendation Model { get; init; }
        public string Title => Model.Title;
        public string Description => Model.Description;
        public string RiskText => $"{Translator.Instance["Common.RiskLevel"]}: {Model.RiskLevel}";
    }

    public ModeViewModel(string modeName, SessionState session, IServiceProvider sp) : base(session)
    {
        ModeName = modeName;
        _modes = sp.GetRequiredService<IModesCoordinator>();
        _sp = sp;
        ModeNote = modeName switch
        {
            "Gaming" => Translator.Instance["Modes.GamingNote"],
            "Office" => Translator.Instance["Modes.OfficeNote"],
            _ => string.Empty,
        };
        Build();
    }

    [RelayCommand]
    private void Build()
    {
        var bundle = Session.LastAnalysis;
        if (bundle is null)
        {
            IsAvailable = false;
            UnavailabilityReason = Translator.Instance["Common.EmptyState"];
            return;
        }

        var plan = _modes.BuildPlan(ModeName, bundle);
        Items.Clear();
        foreach (var rec in plan.Recommendations)
            Items.Add(new RecommendationRowAdapter { Model = rec });
        HasItems = Items.Count > 0;
        Description = plan.Description ?? string.Empty;

        if (!plan.IsAvailable)
        {
            IsAvailable = false;
            UnavailabilityReason = plan.UnavailabilityReason ?? string.Empty;
        }
        else if (bundle.Profile.Battery.IsPresent && ModeName == "Gaming" &&
                 bundle.Profile.Battery.PowerSource == Domain.Enums.PowerSource.Battery)
        {
            // Live re-check at enable time happens in EnableAsync; show hint here.
            StatusText = Translator.Instance["Modes.PluggedInOnly"];
        }
    }

    [RelayCommand]
    private async Task EnableAsync()
    {
        if (!CanModify) return;

        var bundle = Session.LastAnalysis;
        if (bundle is null) return;

        var plan = _modes.BuildPlan(ModeName, bundle);
        if (!plan.IsAvailable)
        {
            ConfirmDialog.Ask("Notify.Warning", plan.UnavailabilityReason!, confirmText: Translator.Instance["Common.OK"]);
            return;
        }

        var activeWin = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive)
            ?? System.Windows.Application.Current.MainWindow!;
        await OptimizationFlow.RunAsync(activeWin, _sp, plan.Recommendations, MapProfile(ModeName));
    }

    private static Domain.Enums.UsageProfileKind MapProfile(string name) => name switch
    {
        "Low-End" => Domain.Enums.UsageProfileKind.LowEnd,
        "Office" => Domain.Enums.UsageProfileKind.Office,
        "Gaming" => Domain.Enums.UsageProfileKind.Gaming,
        "Battery Saver" => Domain.Enums.UsageProfileKind.BatterySaver,
        _ => Domain.Enums.UsageProfileKind.Balanced,
    };
}
