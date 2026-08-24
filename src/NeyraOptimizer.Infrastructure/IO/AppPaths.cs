namespace NeyraOptimizer.Infrastructure.IO;

/// <summary>
/// Centralizes every filesystem location the app uses. Never hardcodes usernames or drives;
/// derives everything from Environment.SpecialFolder and Known Folder conventions.
/// </summary>
public static class AppPaths
{
    public static string ProgramDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NeyraOptimizer");

    /// <summary>Snapshots intentionally live OUTSIDE any database so Emergency Restore can enumerate
    /// and validate them even when other state is corrupt.</summary>
    public static string Snapshots { get; } = Path.Combine(ProgramDataRoot, "Snapshots");

    /// <summary>Baseline before/after measurement files.</summary>
    public static string Measurements { get; } = Path.Combine(ProgramDataRoot, "Measurements");

    public static string History { get; } = Path.Combine(ProgramDataRoot, "History");

    /// <summary>Exported reports and support bundles staged here until the user moves them.</summary>
    public static string Exports { get; } = Path.Combine(ProgramDataRoot, "Exports");

    public static string SettingsDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NeyraOptimizer");

    public static string SettingsFile { get; } = Path.Combine(SettingsDir, "settings.json");

    public static string PendingOperationFile { get; } = Path.Combine(SettingsDir, "pending-operation.json");

    public static string LogsDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NeyraOptimizer", "Logs");

    public static void EnsureDirectories()
    {
        foreach (var dir in new[] { ProgramDataRoot, Snapshots, Measurements, History, Exports, SettingsDir, LogsDir })
        {
            Directory.CreateDirectory(dir);
        }
    }
}
