using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeyraOptimizer.Infrastructure.Json;

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = Create(writeIndented: true);

    /// <summary>Single-line JSON for log files and streaming formats.</summary>
    public static readonly JsonSerializerOptions Compact = Create(writeIndented: false);

    public static JsonSerializerOptions Create(bool writeIndented) => new()
    {
        WriteIndented = writeIndented,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };
}
