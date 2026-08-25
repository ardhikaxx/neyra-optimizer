using System.Runtime.InteropServices;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Security.Protection;

namespace NeyraOptimizer.Windows.Tasks;

/// <summary>
/// ITaskSchedulerManager implementation over the COM automation session. Disable/enable never
/// deletes task definitions; protected system tasks are refused before any COM call is made.
/// </summary>
public sealed class WindowsTaskSchedulerManager : ITaskSchedulerManager
{
    public IReadOnlyList<ScheduledTaskInfo> GetTasks(CancellationToken ct = default)
    {
        using var session = TaskSchedulerComSession.TryCreate(out var s)
            ? s
            : throw new ScheduledTaskException("The Task Scheduler service could not be reached.");

        var list = new List<ScheduledTaskInfo>();
        foreach (var t in session.EnumerateTasks(ct))
        {
            var protectedFlag = ProtectedComponents.IsTaskProtected(t.TaskPath);
            if (protectedFlag)
            {
                list.Add(t with
                {
                    IsProtected = true,
                    ProtectionReason = "Required for update integrity, recovery or core system behavior.",
                    RiskLevel = RiskLevel.Critical,
                });
            }
            else
            {
                list.Add(t);
            }
        }
        return list;
    }

    public void SetEnabled(string taskPath, bool enabled)
    {
        // Defense in depth: the elevated validator also checks, but never even open the task here.
        if (!enabled && ProtectedComponents.IsTaskProtected(taskPath))
            throw new ScheduledTaskException($"Scheduled task '{taskPath}' is protected and cannot be disabled.");

        using var session = TaskSchedulerComSession.TryCreate(out var s)
            ? s
            : throw new ScheduledTaskException("The Task Scheduler service could not be reached.");

        try
        {
            session.SetEnabled(taskPath, enabled);
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070005))
        {
            throw new ScheduledTaskException($"Access denied while changing '{taskPath}'. Administrator privileges are required.", ex);
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002))
        {
            throw new ScheduledTaskException($"Scheduled task '{taskPath}' no longer exists.", ex);
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x800704E3))
        {
            throw new ScheduledTaskException("The Task Scheduler service connection was lost. Please try again.", ex);
        }
    }

    public string ExportTaskXml(string taskPath)
    {
        using var session = TaskSchedulerComSession.TryCreate(out var s)
            ? s
            : throw new ScheduledTaskException("The Task Scheduler service could not be reached.");
        return session.ExportXml(taskPath);
    }
}
