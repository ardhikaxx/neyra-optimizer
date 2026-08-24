using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.App.ViewModels;
using NeyraOptimizer.Application.Analysis;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Settings;
using NeyraOptimizer.Infrastructure.Persistence;

namespace NeyraOptimizer.App.Views;

/// <summary>
/// First-run onboarding. Explains the tool, requests explicit consent BEFORE any change is ever
/// possible, and offers an initial read-only scan. No system modification happens here.
/// </summary>
public partial class OnboardingWindow : Window
{
    private readonly SessionState _session;
    private readonly IServiceProvider _services;

    public OnboardingWindow(SessionState session, IServiceProvider services)
    {
        InitializeComponent();
        Owner = null;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _session = session;
        _services = services;
    }

    private void Consent_Changed(object sender, RoutedEventArgs e)
    {
        AgreeBtn.IsEnabled = ConsentCheck.IsChecked == true;
    }

    private async void Agree_Click(object sender, RoutedEventArgs e)
    {
        _session.Settings.UserConsentedToChanges = true;
        _session.IsReadOnlyMode = false;
        _session.Settings.OnboardingCompleted = true;
        new SettingsRepository().Save(_session.Settings);

        DialogResult = true;

        // Initial read-only scan right after consent.
        var analyzer = _services.GetRequiredService<IAnalysisOrchestrator>();
        try
        {
            var result = await analyzer.AnalyzeAsync(2);
            _session.LastAnalysis = result.Bundle;
            _session.LastRecommendations = result.Recommendations;
        }
        catch
        {
            // Scan failure must never block onboarding; user can analyze later from the dashboard.
        }
    }

    private void ReadOnly_Click(object sender, RoutedEventArgs e)
    {
        _session.Settings.OnboardingCompleted = true;
        _session.Settings.UserConsentedToChanges = false;
        _session.IsReadOnlyMode = true;
        new SettingsRepository().Save(_session.Settings);
        DialogResult = true;
    }
}
