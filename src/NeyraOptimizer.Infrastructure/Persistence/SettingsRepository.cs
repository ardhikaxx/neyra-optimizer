using System.Text.Json;
using NeyraOptimizer.Domain.Settings;
using NeyraOptimizer.Infrastructure.IO;

namespace NeyraOptimizer.Infrastructure.Persistence;

public sealed class SettingsRepository : JsonFileRepositoryBase
{
    private readonly string _path;

    public SettingsRepository(string? settingsFileOverride = null)
    {
        _path = settingsFileOverride ?? AppPaths.SettingsFile;
    }

    public AppSettings Load()
    {
        var settings = Read<AppSettings>(_path);
        if (settings is null) return new AppSettings();
        return Migrate(settings);
    }

    public void Save(AppSettings settings)
    {
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        WriteAtomic(_path, JsonSerializer.Serialize(settings, Serializer), withIntegrityManifest: false);
    }

    /// <summary>Forward-migrates older persisted settings. Never throws on unknown fields.</summary>
    internal static AppSettings Migrate(AppSettings settings)
    {
        // v1 → current: nothing to transform yet; clamp values defensively.
        settings.DashboardRefreshSeconds = Math.Clamp(settings.DashboardRefreshSeconds, 2, 60);
        return settings;
    }
}
