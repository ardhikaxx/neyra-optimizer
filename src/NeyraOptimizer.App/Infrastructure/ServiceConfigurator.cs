using Microsoft.Extensions.DependencyInjection;
using NeyraOptimizer.Application.Analysis;
using NeyraOptimizer.Application.Measurement;
using NeyraOptimizer.Application.Modes;
using NeyraOptimizer.Application.Modules;
using NeyraOptimizer.Application.Optimization;
using NeyraOptimizer.Application.Recovery;
using NeyraOptimizer.Application.Restore;
using NeyraOptimizer.Application.Session;
using NeyraOptimizer.Diagnostics.Analyzer;
using NeyraOptimizer.Diagnostics.Compatibility;
using NeyraOptimizer.Diagnostics.Measurement;
using NeyraOptimizer.Diagnostics.Reporting;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Engines;
using NeyraOptimizer.Infrastructure.CrashRecovery;
using NeyraOptimizer.Infrastructure.Logging;
using NeyraOptimizer.Infrastructure.Persistence;
using NeyraOptimizer.Optimization.Pipeline;
using NeyraOptimizer.Optimization.Restore;
using NeyraOptimizer.Optimization.Safety;
using NeyraOptimizer.Security.Elevation;
using NeyraOptimizer.Windows.Background;
using NeyraOptimizer.Windows.Cleanup;
using NeyraOptimizer.Windows.Packages;
using NeyraOptimizer.Windows.Performance;
using NeyraOptimizer.Windows.Power;
using NeyraOptimizer.Windows.Processes;
using NeyraOptimizer.Windows.RegOps;
using NeyraOptimizer.Windows.Restore;
using NeyraOptimizer.Windows.Security;
using NeyraOptimizer.Windows.Services;
using NeyraOptimizer.Windows.Startup;
using NeyraOptimizer.Windows.SystemInfo;
using NeyraOptimizer.Windows.Tasks;
using NeyraOptimizer.Windows.Visuals;

namespace NeyraOptimizer.App.Infrastructure;

/// <summary>
/// Composition root. Windows-specific implementations are registered here only; every other
/// layer depends on ports, which is what makes the engine unit-testable.
/// </summary>
public static class ServiceConfigurator
{
    public static IServiceProvider Build(SessionState session)
    {
        var services = new ServiceCollection();

        // ── Infrastructure ─────────────────────────────────────────
        services.AddSingleton<INeyraLogger>(_ => new StructuredFileLogger(session.Settings.LoggingLevel));
        services.AddSingleton<ISnapshotRepository, SnapshotRepository>();
        services.AddSingleton<IHistoryRepository, HistoryRepository>();
        services.AddSingleton<IPendingOperationTracker, PendingOperationTracker>();
        services.AddSingleton(_ => new SettingsRepository().Load());

        // ── Security ────────────────────────────────────────────────
        services.AddSingleton<IElevationGateway, ElevationGateway>();

        // ── Windows integration ─────────────────────────────────────
        services.AddSingleton<IRegistryManager, WindowsRegistryManager>();
        services.AddSingleton<ISystemInformationProvider, WmiSystemInformationProvider>();
        services.AddSingleton<IPerformanceMonitor, WindowsPerformanceMonitor>();
        services.AddSingleton<IStartupManager, WindowsStartupManager>();
        services.AddSingleton<IWindowsServiceManager, WindowsServiceManager>();
        services.AddSingleton<ITaskSchedulerManager, WindowsTaskSchedulerManager>();
        services.AddSingleton<IAppPackageManager, AppxPackageManager>();
        services.AddSingleton<IProcessAnalyzer, ProcessAnalyzer>();
        services.AddSingleton<IBackgroundActivityManager, WindowsBackgroundActivityManager>();
        services.AddSingleton<IVisualEffectsManager, WindowsVisualEffectsManager>();
        services.AddSingleton<IPowerManager, WindowsPowerManager>();
        services.AddSingleton<IRestorePointManager, RestorePointManager>();
        services.AddSingleton<ICleanupScanner, CleanupScanner>();

        // ── Diagnostics ─────────────────────────────────────────────
        services.AddSingleton<IBaselineMeasurementService, BaselineMeasurementService>();
        services.AddSingleton<ISystemAnalyzer, SystemAnalyzer>();
        services.AddSingleton<ICompatibilityChecker, CompatibilityChecker>();
        services.AddSingleton<IHealthReportGenerator, HealthReportGenerator>();
        services.AddSingleton<ISupportBundleService, SupportBundleService>();

        // ── Optimization ────────────────────────────────────────────
        services.AddSingleton<ISafetyEngine, SafetyEngine>();
        services.AddSingleton<IOptimizationPipeline, OptimizationPipeline>();
        services.AddSingleton<IRestoreEngine, RestoreEngine>();

        // ── Application use-cases ───────────────────────────────────
        services.AddSingleton<SessionState>(session);
        services.AddSingleton<OperationLock>();
        services.AddSingleton<MeasurementStore>();
        services.AddSingleton<IRecommendationEngine, RecommendationEngine>();
        services.AddSingleton<IAnalysisOrchestrator, AnalysisOrchestrator>();
        services.AddSingleton<IOptimizationCoordinator, OptimizationCoordinator>();
        services.AddSingleton<IModesCoordinator, ModesCoordinator>();
        services.AddSingleton<IRestoreCenterService, RestoreCenterService>();
        services.AddSingleton<ICrashRecoveryService, CrashRecoveryService>();
        services.AddSingleton<IModuleDataService, ModuleDataService>();
        services.AddSingleton<ISingleItemActionService, SingleItemActionService>();
        services.AddSingleton<ICleanupCoordinator, CleanupCoordinator>();

        return services.BuildServiceProvider();
    }
}
