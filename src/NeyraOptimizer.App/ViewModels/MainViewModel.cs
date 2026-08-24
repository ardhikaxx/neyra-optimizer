using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.Application.Session;

namespace NeyraOptimizer.App.ViewModels;

public sealed record NavItem(string Key, string LocKey, string Glyph, string GroupKey, Type PageType, string? ModeKey = null);

/// <summary>Shell view model: sidebar navigation + global busy state.</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    public SessionState Session { get; }
    public IReadOnlyList<NavEntry> NavItems { get; }

    [ObservableProperty] private NavEntry? _selectedItem;
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyText = string.Empty;
    [ObservableProperty] private bool _showReadOnlyBanner;

    public event EventHandler? ActivateRequested;

    public string AppVersion =>
        "v" + (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0");

    public MainViewModel(IServiceProvider services)
    {
        _services = services;
        Session = services.GetRequiredService<SessionState>();
        ShowReadOnlyBanner = !Session.CanModifySystem;
        NavItems = BuildNav();
        SelectedItem = NavItems[0];
    }

    partial void OnSelectedItemChanged(NavEntry? value)
    {
        if (value is null) return;
        CurrentPage = value.PageType == typeof(Views.Pages.ModePage)
            ? new Views.Pages.ModePage(_services, value.Definition.ModeKey!)
            : Activator.CreateInstance(value.PageType, _services);
    }

    internal void RefreshBanners()
    {
        ShowReadOnlyBanner = !Session.CanModifySystem;
        OnPropertyChanged(nameof(BannerText));
    }

    public string BannerText => Session.Compatibility.IsSupported
        ? Translator.Instance["Common.ReadOnlyBanner"]
        : Translator.Instance["Common.UnsupportedBanner"];

    public void SignalActivate()
    {
        System.Windows.Application.Current.MainWindow?.Activate();
        ActivateRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Live-localized wrapper around the immutable navigation definition.</summary>
    public sealed class NavEntry : INotifyPropertyChanged
    {
        public NavItem Definition { get; }
        public string Glyph => Definition.Glyph;
        public string GroupKey => Definition.GroupKey;
        public Type PageType => Definition.PageType;

        private string _label;
        public string Label
        {
            get => _label;
            private set { _label = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label))); }
        }

        public NavEntry(NavItem def)
        {
            Definition = def;
            _label = Translator.Instance[def.LocKey];
            Translator.Instance.PropertyChanged += (_, __) =>
                Label = Translator.Instance[def.LocKey];
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private static IReadOnlyList<NavEntry> BuildNav()
    {
        NavItem[] defs =
        {
            new("dashboard",  "Nav.Dashboard",  "\uE9D9", "0", typeof(Views.Pages.DashboardPage)),
            new("analyze",    "Nav.Analyze",    "\uE9F9", "0", typeof(Views.Pages.AnalyzePage)),
            new("optcenter",  "Nav.OptimizationCenter", "\uE90F", "0", typeof(Views.Pages.OptimizationCenterPage)),

            new("startup",    "Nav.Startup",    "\uE7B8", "1", typeof(Views.Pages.StartupPage)),
            new("services",   "Nav.Services",   "\uE95E", "1", typeof(Views.Pages.ServicesPage)),
            new("tasks",      "Nav.ScheduledTasks", "\uE9D5", "1", typeof(Views.Pages.ScheduledTasksPage)),
            new("debloat",    "Nav.Debloat",    "\uE74D", "1", typeof(Views.Pages.DebloatPage)),
            new("background", "Nav.BackgroundApps", "\uE7EE", "1", typeof(Views.Pages.BackgroundAppsPage)),

            new("visuals",    "Nav.VisualEffects", "\uE790", "2", typeof(Views.Pages.VisualEffectsPage)),
            new("power",      "Nav.Power",      "\uE956", "2", typeof(Views.Pages.PowerPage)),
            new("gaming",     "Nav.GamingMode", "\uE7FC", "2", typeof(Views.Pages.ModePage), "Gaming"),
            new("office",     "Nav.OfficeMode", "\uE8F4", "2", typeof(Views.Pages.ModePage), "Office"),
            new("battery",    "Nav.BatteryMode","\uE83F", "2", typeof(Views.Pages.ModePage), "Battery Saver"),
            new("lowend",     "Nav.LowEndMode", "\uE9E9", "2", typeof(Views.Pages.ModePage), "Low-End"),
            new("safewin",    "Nav.SafeWindowsMode", "\uEA18", "2", typeof(Views.Pages.ModePage), "Safe Windows"),
            new("privacy",    "Nav.Privacy",    "\uE72E", "2", typeof(Views.Pages.PrivacyPage)),

            new("cleanup",    "Nav.Cleanup",    "\uEC27", "3", typeof(Views.Pages.CleanupPage)),
            new("restore",    "Nav.RestoreCenter", "\uE777", "3", typeof(Views.Pages.RestoreCenterPage)),
            new("history",    "Nav.History",    "\uE81C", "3", typeof(Views.Pages.HistoryPage)),
            new("logs",       "Nav.Logs",       "\uE9F5", "3", typeof(Views.Pages.LogsPage)),

            new("settings",   "Nav.Settings",   "\uE713", "4", typeof(Views.Pages.SettingsPage)),
            new("help",       "Nav.HelpSafety", "\uE897", "4", typeof(Views.Pages.HelpSafetyPage)),
            new("about",      "Nav.About",      "\uE946", "4", typeof(Views.Pages.AboutPage)),
        };
        return defs.Select(d => new NavEntry(d)).ToList();
    }
}
