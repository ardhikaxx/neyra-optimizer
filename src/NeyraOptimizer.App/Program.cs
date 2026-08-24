using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using NeyraOptimizer.App.Infrastructure;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.Security.Elevation;
using NeyraOptimizer.Windows.Security;

namespace NeyraOptimizer.App;

/// <summary>
/// Process entry point. Handles three startup paths before any WPF initialization:
/// 1. --elevated-op: one-shot privileged child (never shows the UI, never collects credentials)
/// 2. --emergency:   minimal Emergency Restore window independent of main UI state
/// 3. normal launch: single-instance guard then the main shell
/// </summary>
internal static class Program
{
    private const string ElevatedArg = "--elevated-op";
    public const string EmergencyArg = "--emergency";

    [STAThread]
    private static int Main(string[] args)
    {
        // ── Path 1: elevated child process ─────────────────────────────
        if (args.Length >= 2 && args[0].Equals(ElevatedArg, StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(args[1], out var operationId))
                return 3;

            // Defense in depth: refuse to run the child unless actually elevated.
            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                return 4;

            var executor = new ElevatedOperationExecutor();
            return ElevationManager.ExecuteAsChild(operationId, executor);
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Debug.WriteLine("Unhandled: " + e.ExceptionObject);

        bool emergency = args.Any(a => a.Equals(EmergencyArg, StringComparison.OrdinalIgnoreCase));

        // ── Single instance guard ──────────────────────────────────────
        using var guard = new SingleInstanceGuard();
        if (!guard.TryAcquireFirstInstance())
        {
            if (emergency)
            {
                // Emergency mode must always be reachable even while the app runs.
            }
            else
            {
                guard.SignalExistingInstance();
                return 0;
            }
        }

        // ── Normal WPF application ─────────────────────────────────────
        Translator.Initialize();
        var app = new NeyraApplication(emergencyMode: emergency);
        return app.Run();
    }
}
