using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Infrastructure;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Rules;

namespace NeyraOptimizer.App.ViewModels;

public partial class RecommendationRow : ObservableObject
{
    public required Recommendation Model { get; init; }

    public string Title => Model.Title;
    public string Description => Model.Description;
    public string Reason => Model.Reason;
    public string EstimatedImpact => Model.EstimatedImpact;
    public string CategoryKey => "Cat." + Model.Category;

    public string CategoryText => Translator.Instance["Cat." + Model.Category];
    public string RiskText => $"{Translator.Instance["Common.RiskLevel"]}: {Model.RiskLevel}" +
        (Model.RequiresAdministrator ? $" · {Translator.Instance["Common.RequiresAdmin"]}" : string.Empty);
    public bool IsProtectedItem => Model.Category == RecommendationCategory.DoNotModify;

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (IsProtectedItem) _isSelected = false;
    }
}

public partial class OptimizationCenterViewModel : ViewModelBase
{
    private readonly IServiceProvider _sp;

    public ObservableCollection<RecommendationRow> Items { get; } = new();
    public ObservableCollection<RecommendationRow> SelectedItems => new(Items.Where(i => i.IsSelected));

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _selectedCount;

    public OptimizationCenterViewModel(SessionState session, IServiceProvider sp) : base(session)
    {
        _sp = sp;
        LoadItems();
        Items.CollectionChanged += (_, _) => UpdateCount();
    }

    public void LoadItems()
    {
        Items.Clear();
        foreach (var rec in Session.LastRecommendations)
            Items.Add(new RecommendationRow { Model = rec, IsSelected = rec.IsSelected });
        UpdateCount();
    }

    private void UpdateCount()
    {
        SelectedCount = Items.Count(i => i.IsSelected);
        OnPropertyChanged(nameof(SelectedSummary));
    }

    public string SelectedSummary =>
        string.Format(Translator.Instance["OptCenter.SelectedCount"], SelectedCount, Items.Count);

    public IEnumerable<IGrouping<string, RecommendationRow>> GroupedVisibleItems =>
        Items
            .Where(i => SearchText.Length == 0 ||
                        i.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                        i.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => i.Model.Category.ToString())
            .OrderBy(g => CategoryOrder(g.Key));

    private static int CategoryOrder(string c) => c switch
    {
        nameof(RecommendationCategory.Safe) => 0,
        nameof(RecommendationCategory.Recommended) => 1,
        nameof(RecommendationCategory.Optional) => 2,
        nameof(RecommendationCategory.Advanced) => 3,
        _ => 4,
    };

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(GroupedVisibleItems));

    [RelayCommand]
    private void SelectSafeRecommended()
    {
        foreach (var i in Items)
            i.IsSelected = i.Model.Category is RecommendationCategory.Safe or RecommendationCategory.Recommended;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var i in Items) i.IsSelected = false;
    }

    [RelayCommand]
    private async Task OneClickSafeAsync()
    {
        var selected = Items
            .Where(i => i.Model.Category is RecommendationCategory.Safe or RecommendationCategory.Recommended &&
                        i.Model.RiskLevel <= RiskLevel.Low)
            .Select(i => i.Model with { IsSelected = true })
            .ToList();
        var activeWin = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive)
            ?? System.Windows.Application.Current.MainWindow!;
        await OptimizationFlow.RunAsync(activeWin, _sp, selected, Session.UsageProfile);
    }

    [RelayCommand]
    private async Task ApplySelectedAsync()
    {
        var rows = Items.Where(i => i.IsSelected).ToList();
        if (rows.Count == 0) return;
        var selected = rows.Select(r => r.Model).ToList();
        var activeWin = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive)
            ?? System.Windows.Application.Current.MainWindow!;
        await OptimizationFlow.RunAsync(activeWin, _sp, selected, Session.UsageProfile);
    }

    [RelayCommand]
    private void RefreshList() => LoadItems();
}
