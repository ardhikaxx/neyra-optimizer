using System.Threading;

namespace NeyraOptimizer.App.Infrastructure;

/// <summary>
/// Ensures only one Neyra Optimizer instance can run system modifications at a time.
/// A second launch signals the primary instance (which brings its window to the front) and exits.
/// The emergency restore path is intentionally exempt so rollback stays reachable.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "Global\\NeyraOptimizer.SingleInstance.Mutex";
    private const string EventName = "Local\\NeyraOptimizer.Activate.Event";

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;

    public bool IsPrimaryInstance { get; private set; }

    public bool TryAcquireFirstInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // Not the owner: release our handle and report.
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        IsPrimaryInstance = true;

        try
        {
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        }
        catch (System.Threading.ThreadInterruptedException) { }

        return true;
    }

    public void SignalExistingInstance()
    {
        try
        {
            using var evt = EventWaitHandle.OpenExisting(EventName);
            evt.Set();
        }
        catch (System.Threading.WaitHandleCannotBeOpenedException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Watches for second-instance activation signals. Returns a wait handle or null.</summary>
    public WaitHandle? ActivateSignal => _activateEvent;

    public void Dispose()
    {
        if (_mutex is not null && IsPrimaryInstance)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex?.Dispose();
        _activateEvent?.Dispose();
    }
}
