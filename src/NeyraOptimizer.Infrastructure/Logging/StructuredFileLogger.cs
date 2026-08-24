using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using NeyraOptimizer.Infrastructure.Json;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Infrastructure.IO;

namespace NeyraOptimizer.Infrastructure.Logging;

public sealed record LogEntry(
    DateTime TimestampUtc,
    LogSeverity Severity,
    string Component,
    string Action,
    string Message,
    string OperationId = "",
    string ErrorCode = "",
    long DurationMs = -1);

public interface INeyraLogger
{
    void Debug(string component, string action, string message);
    void Info(string component, string action, string message, string operationId = "");
    void Warning(string component, string action, string message, string errorCode = "");
    void Error(string component, string action, string message, string errorCode = "");
    void Critical(string component, string action, string message, string errorCode = "");
    /// <summary>Logs an operation with duration measurement.</summary>
    void Operation(LogSeverity severity, string component, string action, string message,
        TimeSpan elapsed, string operationId = "", string errorCode = "");
    IReadOnlyList<LogEntry> SnapshotRecent(int max = 500);
}

/// <summary>
/// Structured logger writing JSON lines per day under LocalApplicationData. Contractually never
/// logs passwords/tokens/private data — callers pass identifiers and error codes only.
/// </summary>
public sealed class StructuredFileLogger : INeyraLogger
{
    private readonly object _lock = new();
    private readonly LogSeverity _minimumLevel;
    private readonly ConcurrentQueue<LogEntry> _recent = new();
    private readonly string _logsDir;

    public StructuredFileLogger(LogSeverity minimumLevel = LogSeverity.Info, string? logsDirOverride = null)
    {
        _minimumLevel = minimumLevel;
        _logsDir = logsDirOverride ?? AppPaths.LogsDir;
        try { Directory.CreateDirectory(_logsDir); } catch (IOException) { }
    }

    public void Debug(string component, string action, string message) =>
        Write(LogSeverity.Debug, component, action, message);

    public void Info(string component, string action, string message, string operationId = "") =>
        Write(LogSeverity.Info, component, action, message, operationId);

    public void Warning(string component, string action, string message, string errorCode = "") =>
        Write(LogSeverity.Warning, component, action, message, errorCode: errorCode);

    public void Error(string component, string action, string message, string errorCode = "") =>
        Write(LogSeverity.Error, component, action, message, errorCode: errorCode);

    public void Critical(string component, string action, string message, string errorCode = "") =>
        Write(LogSeverity.Critical, component, action, message, errorCode: errorCode);

    public void Operation(LogSeverity severity, string component, string action, string message,
        TimeSpan elapsed, string operationId = "", string errorCode = "") =>
        Write(severity, component, action, message, operationId, errorCode, (long)elapsed.TotalMilliseconds);

    public IReadOnlyList<LogEntry> SnapshotRecent(int max = 500) =>
        _recent.Reverse().Take(max).ToList();

    private void Write(LogSeverity severity, string component, string action, string message,
        string operationId = "", string errorCode = "", long durationMs = -1)
    {
        if (severity < _minimumLevel) return;

        // Defensive sanitization: strip control characters so log lines stay parseable.
        message = Sanitize(message);

        var entry = new LogEntry(DateTime.UtcNow, severity, Sanitize(component), Sanitize(action), message, operationId, errorCode, durationMs);
        _recent.Enqueue(entry);
        while (_recent.Count > 1000) _recent.TryDequeue(out _);

        try
        {
            var file = Path.Combine(_logsDir, $"neyra-{DateTime.UtcNow:yyyyMMdd}.log.jsonl");
            lock (_lock)
            {
                File.AppendAllText(file, JsonSerializer.Serialize(entry, JsonOptions.Compact) + Environment.NewLine,
                    Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
        catch (UnauthorizedAccessException) { }
    }

    private static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (var c in input.Take(4000))
        {
            sb.Append(char.IsControl(c) && c != '\t' ? ' ' : c);
        }
        return sb.ToString();
    }
}
