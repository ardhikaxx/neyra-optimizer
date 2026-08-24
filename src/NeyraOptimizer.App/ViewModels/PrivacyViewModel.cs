using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.App.Views.Dialogs;
using NeyraOptimizer.Application.Modules;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Rules;

namespace NeyraOptimizer.App.ViewModels;

public partial class PrivacyRow : ObservableObject
{
    public required Recommendation Model { get; init; }
    public string Title => Model.Title;
    public string Description => Model.Description;
    public string RiskText => $"{Translator.Instance["Common.RiskLevel"]}: {Model.RiskLevel}";

    [ObservableProperty] private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        // Reflect the proposed state used by Apply.
        Model.IsSelected = value;
    }
}

/// <summary>
/// Privacy toggles built from the rules catalog (Area=Privacy). Each item carries current state,
/// proposed state, risk level and rollback description. Security components are never listed.
/// </summary>
public partial class PrivacyViewModel : ViewModelBase
{
    private readonly ISingleItemActionService _actions;

    public ObservableCollection<PrivacyRow> Rows { get; } = new();
    [ObservableProperty] private string _statusText = string.Empty;

    public PrivacyViewModel(SessionState session, IServiceProvider sp) : base(session)
    {
        _actions = sp.GetRequiredService<ISingleItemActionService>();
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        Rows.Clear();
        foreach (var rec in Session.LastRecommendations.Where(r => r.Area == RuleArea.Privacy))
            Rows.Add(new PrivacyRow
            {
                Model = rec,
                IsEnabled = rec.CurrentStateText == "0" || rec.ProposedStateText.Contains("1"),
            });
    }

    /// <summary>Applies one privacy toggle through the pipeline (snapshot + verify + history).</summary>
    public async Task ToggleAsync(PrivacyRow row)
    {
        if (!CanModify) return;
        try
        {
            var enable = row.IsEnabled;
            var rec = row.Model with
            {
                ProposedStateText = enable ? "1" : "0",
                CurrentStateText = enable ? "0" : "1",
            };
            var result = await _actions.ApplyPrivacyAsync(rec, CancellationToken.None);
            if (result.FailedCount > 0)
                StatusText = string.Join("; ", result.Errors.Take(3));
            else
                StatusText = string.Empty;
        }
        catch (Exception ex) { StatusText = ex.Message; row.IsEnabled = !row.IsEnabled; }
    }
}
