using System.Diagnostics;
using System.Runtime.InteropServices;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Windows.Native;

namespace NeyraOptimizer.Windows.Performance;

/// <summary>
/// Cheap near-real-time sampling. Counters are created lazily, cached, and disposed — the
/// dashboard must not leak instances when refreshed every few seconds. GPU usage is only
/// reported when GPU engine counters exist; otherwise callers receive null (never a guess).
/// </summary>
public sealed class WindowsPerformanceMonitor : IPerformanceMonitor
{
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _diskIdleCounter;
    private readonly object _lock = new();
    private bool _gpuCountersMissing;
    private DateTime _lastGpuSample = DateTime.MinValue;
    private double _lastGpuValue = double.NaN;

    public async Task<double?> SampleCpuLoadAsync(int sampleSeconds, CancellationToken ct)
    {
        sampleSeconds = Math.Clamp(sampleSeconds, 1, 30);
        lock (_lock)
        {
            if (_cpuCounter is null)
            {
                try { _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true); }
                catch (InvalidOperationException) { return null; }
                catch (UnauthorizedAccessException) { return null; }
            }
            _cpuCounter.NextValue(); // warm-up per docs
        }

        var delay = Math.Min(1000, sampleSeconds * 1000);
        await Task.Delay(delay, ct).ConfigureAwait(false);
        try
        {
            var v = _cpuCounter?.NextValue();
            return v is null || double.IsNaN(v.Value) ? null : Math.Clamp(v.Value, 0, 100);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public (long TotalMb, long AvailableMb, long CommitLimitMb, long CommitUsedMb) SampleMemory()
    {
        var status = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (!NativeMethods.GlobalMemoryStatusEx(ref status))
            return (0, 0, 0, 0);

        return (
            (long)(status.ullTotalPhys / (1024 * 1024)),
            (long)(status.ullAvailPhys / (1024 * 1024)),
            (long)(status.ullTotalPageFile / (1024 * 1024)),
            (long)((status.ullTotalPageFile - status.ullAvailPageFile) / (1024 * 1024)));
    }

    public double? SampleDiskActivePercent()
    {
        lock (_lock)
        {
            try
            {
                if (_diskIdleCounter is null)
                    _diskIdleCounter = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total", readOnly: true);
                var idle = _diskIdleCounter.NextValue();
                if (double.IsNaN(idle)) return null;
                return Math.Clamp(100 - idle, 0, 100);
            }
            catch (InvalidOperationException)
            {
                return null; // counter category missing on some systems/VMs
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public double? SampleGpuUsagePercent()
    {
        // Rate-limit: GPU engine enumeration is comparatively expensive.
        if ((DateTime.UtcNow - _lastGpuSample).TotalMilliseconds < 1500 && !double.IsNaN(_lastGpuValue))
            return double.IsNaN(_lastGpuValue) ? null : _lastGpuValue;
        _lastGpuSample = DateTime.UtcNow;

        if (_gpuCountersMissing) return null;

        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            double sum = 0;
            bool any = false;
            foreach (var name in category.GetInstanceNames())
            {
                // Only utilization counters, skip VRAM-sharing counters.
                if (!name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("engtype_CopyEngine", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("engtype_VideoDecode", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var counter in category.GetCounters(name))
                {
                    if (!counter.CounterName.Equals("Utilization Percentage", StringComparison.OrdinalIgnoreCase))
                    {
                        counter.Dispose();
                        continue;
                    }
                    counter.NextValue(); // warm-up
                    sum += counter.NextValue();
                    any = true;
                    counter.Dispose();
                }
            }
            if (!any)
            {
                _gpuCountersMissing = true;
                return _lastGpuValue = double.NaN;
            }
            _lastGpuValue = Math.Clamp(sum, 0, 100);
            return _lastGpuValue;
        }
        catch (InvalidOperationException)
        {
            _gpuCountersMissing = true;
            return _lastGpuValue = double.NaN;
        }
        catch (UnauthorizedAccessException)
        {
            _gpuCountersMissing = true;
            return _lastGpuValue = double.NaN;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _cpuCounter?.Dispose();
            _cpuCounter = null;
            _diskIdleCounter?.Dispose();
            _diskIdleCounter = null;
        }
    }
}
