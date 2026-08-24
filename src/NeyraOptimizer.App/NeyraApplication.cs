using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.App.Infrastructure;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.App.Views;
using NeyraOptimizer.App.ViewModels;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Infrastructure.IO;
using NeyraOptimizer.Infrastructure.Persistence;
using NeyraOptimizer.Diagnostics.Compatibility;

namespace NeyraOptimizer.App;

/// <summary>
/// WPF application shell. Composes DI, applies theme/language from persisted settings,
/// runs onboarding on first launch, offers crash recovery for interrupted batches,
/// and supports an independent Emergency Restore entry point.
/// </summary>
public sealed class NeyraApplication : System.Windows.Application
{
    private readonly bool _emergencyMode;
    private IServiceProvider? _services;

    /// <summary>Global service provider, available after OnStartup completes.</summary>
    public IServiceProvider Services => _services ?? throw new InvalidOperationException("App not started.");

    public NeyraApplication(bool emergencyMode)
    {
        _emergencyMode = emergencyMode;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.EnsureDirectories();

        // Static control styles live in one dictionary; palettes are swapped over it.
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Themes/Controls.xaml", UriKind.Absolute)
        });

        // Settings must load before DI so logging level/theme apply immediately.
        var settingsRepo = new SettingsRepository();
        var settings = settingsRepo.Load();
        var session = new SessionState();
        session.LoadSettings(settings);

        _services = ServiceConfigurator.Build(session);

        ApplyTheme(settings.Theme);

        // Compatibility gate: unsupported systems get read-only diagnostics mode.
        var checker = _services.GetRequiredService<ICompatibilityChecker>();
        var sysInfo = _services.GetRequiredService<NeyraOptimizer.Domain.Abstractions.ISystemInformationProvider>();
        var compat = checker.Check(sysInfo.GetWindowsIdentity(), sysInfo.IsCurrentProcessElevated());
        session.Compatibility = compat;
        if (!compat.IsSupported)
            session.IsReadOnlyMode = true;

        DispatcherUnhandledException += (_, args) =>
        {
            _services.GetRequiredService<NeyraOptimizer.Infrastructure.Logging.INeyraLogger>()
                .Critical("UI", "DispatcherException", args.Exception.Message);
            args.Handled = true;
            MessageBox.Show(
                "Terjadi kesalahan tak terduga namun aplikasi tetap berjalan.\n\n" +
                args.Exception.Message,
                "Neyra Optimizer", MessageBoxButton.OK, MessageBoxImage.Warning);
        };

        if (_emergencyMode)
        {
            MainWindow = new EmergencyRestoreWindow(_services);
            MainWindow.Show();
            return;
        }

        // First-run onboarding (no system changes happen during it).
        if (!session.Settings.OnboardingCompleted)
        {
            var onboarding = new OnboardingWindow(session, _services);
            if (onboarding.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }
        else if (settings.UserConsentedToChanges)
        {
            session.IsReadOnlyMode = false;
        }

        var main = new MainWindow(_services);
        MainWindow = main;
        main.Show();

        // Crash recovery prompt AFTER the shell is visible.
        var recovery = _services.GetRequiredService<NeyraOptimizer.Application.Recovery.ICrashRecoveryService>();
        var pending = recovery.DetectPendingOperation();
        if (pending is not null)
        {
            main.Dispatcher.InvokeAsync(async () => await main.ShowCrashRecoveryAsync(pending));
        }

        // Automatic initial scan (read-only) when enabled.
        if (session.Settings.AutomaticScanOnStart && session.LastAnalysis is null)
        {
            main.Dispatcher.InvokeAsync(async () => await main.RunStartupScanAsync());
        }
    }

    /// <summary>Swaps the merged palette dictionary at runtime.</summary>
    public static void ApplyTheme(ThemePreference preference)
    {
        bool dark = preference switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => IsSystemDark(),
        };
        var uri = new Uri(dark
            ? "pack://application:,,,/Themes/Palette.Dark.xaml"
            : "pack://application:,,,/Themes/Palette.Light.xaml", UriKind.Absolute);

        var dictionaries = Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("Palette.") == true);
        var newDict = new ResourceDictionary { Source = uri };
        if (existing is not null)
            dictionaries[dictionaries.IndexOf(existing)] = newDict;
        else
            dictionaries.Insert(0, newDict);
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }
}
