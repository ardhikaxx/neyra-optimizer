using System.Collections;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.Windows.Tasks;

/// <summary>
/// Task Scheduler access through the official COM automation API (ProgID "Schedule.Service")
/// using IDispatch late binding. This is a structured API — no localized command output parsing —
/// and it is stable across Windows 10 and 11 because all member resolution happens at runtime.
/// </summary>
internal sealed class TaskSchedulerComSession : IDisposable
{
    private const int TaskEnumHidden = 0x1;      // TASK_ENUM_HIDDEN
    private const int TaskStateDisabled = 1;

    private dynamic? _service;

    public static bool TryCreate(out TaskSchedulerComSession session)
    {
        session = new TaskSchedulerComSession();
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null) return false;
            session._service = Activator.CreateInstance(type);
            return session._service is not null;
        }
        catch (SystemException)
        {
            return false;
        }
    }

    public IEnumerable<ScheduledTaskInfo> EnumerateTasks(CancellationToken ct)
    {
        if (_service is null) yield break;
        var root = _service.GetFolder(@"\");
        foreach (var task in WalkFolder(root, ct))
        {
            yield return task;
        }
    }

    public string ExportXml(string taskPath)
    {
        if (_service is null) throw new InvalidOperationException("COM service not connected.");
        var folderPath = System.IO.Path.GetDirectoryName(taskPath) ?? @"\";
        if (string.IsNullOrEmpty(folderPath)) folderPath = @"\";
        var name = System.IO.Path.GetFileName(taskPath);
        var folder = _service.GetFolder(folderPath);
        var task = folder.GetTask(name);
        return task.Xml as string ?? string.Empty;
    }

    public void SetEnabled(string taskPath, bool enabled)
    {
        if (_service is null) throw new InvalidOperationException("COM service not connected.");
        var folderPath = System.IO.Path.GetDirectoryName(taskPath);
        if (string.IsNullOrEmpty(folderPath)) folderPath = @"\";
        var name = System.IO.Path.GetFileName(taskPath);
        var folder = _service.GetFolder(folderPath);
        var task = folder.GetTask(name);
        task.Enabled = enabled; // throws COMException with HRESULT on ACL denial — surfaced to pipeline
    }

    public (bool Enabled, bool Running, DateTime? LastRun, DateTime? NextRun, string Xml) ReadState(string taskPath)
    {
        if (_service is null) throw new InvalidOperationException("COM service not connected.");
        var folderPath = System.IO.Path.GetDirectoryName(taskPath);
        if (string.IsNullOrEmpty(folderPath)) folderPath = @"\";
        var name = System.IO.Path.GetFileName(taskPath);
        var folder = _service.GetFolder(folderPath);
        var task = folder.GetTask(name);
        DateTime? last = ToDateTime(task.LastRunTime);
        DateTime? next = ToDateTime(task.NextRunTime);
        var xml = task.Xml as string ?? string.Empty;
        return ((bool)task.Enabled, (int)task.State == TaskStateDisabled ? false : IsRunningState((int)task.State), last, next, xml);

        static bool IsRunningState(int state) => state is >= 3 and <= 5 || state == 8 || state == 9; // RUNNING-ish states
    }

    private IEnumerable<ScheduledTaskInfo> WalkFolder(dynamic folder, CancellationToken ct, int depth = 0)
    {
        if (depth > 4) yield break; // safety cap against pathological trees

        var tasks = folder.GetTasks(TaskEnumHidden);
        foreach (var t in EnumerateCollection(tasks!))
        {
            ct.ThrowIfCancellationRequested();
            ScheduledTaskInfo? info = MapTask(t);
            if (info is not null) yield return info;
        }

        if (depth < 4)
        {
            var folders = folder.GetFolders(0);
            foreach (var f in EnumerateCollection(folders!))
            {
                foreach (var child in WalkFolder(f, ct, depth + 1))
                {
                    yield return child;
                }
            }
        }
    }

    private static IEnumerable<dynamic> EnumerateCollection(dynamic collection)
    {
        var enumerator = ((IEnumerable)collection).GetEnumerator();
        while (enumerator.MoveNext())
        {
            yield return enumerator.Current!;
        }
    }

    private static ScheduledTaskInfo? MapTask(dynamic task)
    {
        try
        {
            var path = (task.Path as string) ?? string.Empty;
            if (path.Length == 0) return null;

            var def = task.Definition;
            string author = SafeGet(() => def.RegistrationInfo?.Author as string ?? string.Empty);
            string description = SafeGet(() => def.RegistrationInfo?.Description as string ?? string.Empty);

            var triggersSummary = BuildTriggersSummary(def);
            var actionsSummary = BuildActionsSummary(def);

            DateTime? lastRun = ToDateTime(task.LastRunTime);
            DateTime? nextRun = ToDateTime(task.NextRunTime);
            bool enabled = (bool)task.Enabled;
            int state = (int)task.State;
            bool running = state is >= 3 and <= 5 or 8 or 9;

            return new ScheduledTaskInfo
            {
                TaskPath = path,
                Name = path[(path.LastIndexOf('\\') + 1)..],
                Author = author,
                Description = description,
                IsEnabled = enabled,
                IsRunning = running,
                LastRunTimeUtc = lastRun,
                NextRunTimeUtc = nextRun,
                TriggersSummary = triggersSummary,
                ActionsSummary = actionsSummary,
            };
        }
        catch (SystemException)
        {
            return null; // unreadable task: skip rather than fabricate data
        }
    }

    private static string BuildTriggersSummary(dynamic definition)
    {
        try
        {
            var triggers = definition.Triggers;
            var count = (int)triggers.Count;
            if (count == 0) return "No triggers";
            var parts = new List<string>(Math.Min(count, 6));
            for (var i = 1; i <= count && i <= 6; i++)
            {
                var triggerType = (int)((dynamic)triggers.Item(i)).Type;
                parts.Add(triggerType switch
                {
                    1 => "Time", 2 => "Daily", 3 => "Weekly", 4 => "Monthly",
                    5 => "Idle", 6 => "Boot", 7 => "Logon",
                    8 => "PowerEvent", 9 => "Event", _ => $"Type{triggerType}",
                });
            }
            var summary = string.Join(", ", parts);
            return count > 6 ? $"{summary}, … ({count} total)" : summary;
        }
        catch (SystemException)
        {
            return "Unknown";
        }
    }

    private static string BuildActionsSummary(dynamic definition)
    {
        try
        {
            var actions = definition.Actions;
            var count = (int)actions.Count;
            if (count == 0) return "No actions";
            var parts = new List<string>();
            for (var i = 1; i <= Math.Min(count, 3); i++)
            {
                var action = actions.Item(i);
                var actionType = (int)((dynamic)action).Type;
                if (actionType == 0) // TASK_ACTION_EXEC
                {
                    var path = action.Path as string ?? string.Empty;
                    var args = action.Arguments as string ?? string.Empty;
                    parts.Add($"{path} {args}".Trim());
                }
                else
                {
                    parts.Add($"Action type {actionType}");
                }
            }
            var summary = string.Join(" | ", parts);
            return count > 3 ? $"{summary} | … ({count} total)" : summary;
        }
        catch (SystemException)
        {
            return "Unknown";
        }
    }

    private static T SafeGet<T>(Func<T> getter)
    {
        try { return getter(); } catch (SystemException) { return default!; }
    }

    internal static DateTime? ToDateTime(object? oleDate)
    {
        try
        {
            if (oleDate is null) return null;
            if (oleDate is DateTime dt)
                return dt == DateTime.MinValue ? null : dt.ToUniversalTime();
            if (oleDate is double d && d > 0)
                return DateTime.FromOADate(d).ToUniversalTime();
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_service is not null)
        {
            try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(_service); }
            catch (ArgumentException) { }
            _service = null;
        }
    }
}
