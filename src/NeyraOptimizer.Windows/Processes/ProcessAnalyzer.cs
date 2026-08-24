using System.ComponentModel;
using System.Diagnostics;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Security.Protection;

namespace NeyraOptimizer.Windows.Processes;

/// <summary>
/// Classifies running processes so the UI can distinguish user applications from system,
/// security, service-hosted and driver-hosted processes. Termination is only ever allowed for
/// non-protected processes in the interactive user session.
/// </summary>
public sealed class ProcessAnalyzer : IProcessAnalyzer
{
    public IReadOnlyList<BackgroundProcessInfo> GetProcessesWithClassification(CancellationToken ct = default)
    {
        var currentSession = Process.GetCurrentProcess().SessionId;
        var result = new List<BackgroundProcessInfo>(128);

        foreach (var p in Process.GetProcesses())
        {
            ct.ThrowIfCancellationRequested();
            using (p)
            {
                try
                {
                    string name = p.ProcessName;
                    bool protectedFlag = ProtectedComponents.ProcessImageNames.Contains(name);
                    double memMb = 0;
                    double cpuSec = 0;
                    DateTime? start = null;

                    try { memMb = p.WorkingSet64 / (1024.0 * 1024.0); } catch (SystemException) { }
                    try { cpuSec = p.TotalProcessorTime.TotalSeconds; } catch (SystemException) { }
                    try { start = SafeStart(p); } catch (SystemException) { }

                    var kind = Classify(name, protectedFlag, p.SessionId == currentSession, p.MainWindowHandle != 0);
                    bool canTerminate = !protectedFlag &&
                                        kind is BackgroundProcessKind.UserApplication or BackgroundProcessKind.UserBackgroundApp &&
                                        p.SessionId == currentSession;

                    result.Add(new BackgroundProcessInfo
                    {
                        ProcessId = p.Id,
                        Name = name,
                        WindowTitle = TryTitle(p),
                        Kind = kind,
                        MemoryMb = Math.Round(memMb, 1),
                        CpuTimeSeconds = Math.Round(cpuSec, 1),
                        StartTimeUtc = start,
                        CanTerminate = canTerminate,
                        TerminationNote = protectedFlag
                            ? "Protected system or security component."
                            : canTerminate ? string.Empty
                            : "Runs outside the current session or is system-managed.",
                    });
                }
                catch (InvalidOperationException)
                {
                    // Exited between enumeration and read: skip.
                }
            }
        }
        return result;
    }

    private static DateTime? SafeStart(Process p)
    {
        try { return p.StartTime.ToUniversalTime(); }
        catch (Win32Exception) { return null; } // access denied for elevated processes — honest null
        catch (InvalidOperationException) { return null; }
    }

    private static string? TryTitle(Process p)
    {
        try { return string.IsNullOrWhiteSpace(p.MainWindowTitle) ? null : p.MainWindowTitle; }
        catch (SystemException) { return null; }
    }

    internal static BackgroundProcessKind Classify(string name, bool protectedFlag, bool sameSession, bool hasWindow)
    {
        if (protectedFlag && IsSecurityRelated(name)) return BackgroundProcessKind.SecurityProcess;
        if (protectedFlag) return BackgroundProcessKind.ProtectedSystem;
        if (name.Equals("svchost", StringComparison.OrdinalIgnoreCase)) return BackgroundProcessKind.ServiceHost;
        if (!sameSession) return BackgroundProcessKind.SystemProcess;
        if (hasWindow) return BackgroundProcessKind.UserApplication;
        return BackgroundProcessKind.UserBackgroundApp;
    }

    private static bool IsSecurityRelated(string name) =>
        name.Contains("msmpeng", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("nissrv", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("securityhealth", StringComparison.OrdinalIgnoreCase);

    public bool TryTerminate(int processId, out string errorText)
    {
        errorText = string.Empty;
        try
        {
            using var p = Process.GetProcessById(processId);
            var name = p.ProcessName;
            if (ProtectedComponents.ProcessImageNames.Contains(name))
            {
                errorText = $"'{name}' is a protected process and will not be terminated.";
                return false;
            }
            var kind = Classify(name, protectedFlag: false, p.SessionId == Process.GetCurrentProcess().SessionId, p.MainWindowHandle != 0);
            if (kind is not (BackgroundProcessKind.UserApplication or BackgroundProcessKind.UserBackgroundApp))
            {
                errorText = "Only user applications in this session can be terminated.";
                return false;
            }

            p.Kill(entireProcessTree: false);
            return true;
        }
        catch (ArgumentException)
        {
            errorText = "The process already exited.";
            return false;
        }
        catch (Win32Exception w32)
        {
            errorText = w32.NativeErrorCode == 5
                ? "Access denied terminating the process."
                : $"Termination failed (error {w32.NativeErrorCode}).";
            return false;
        }
        catch (NotSupportedException nse)
        {
            errorText = "This process cannot be terminated on this platform: " + nse.Message;
            return false;
        }
    }
}
