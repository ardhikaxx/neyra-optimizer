using System.Runtime.InteropServices;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;

namespace NeyraOptimizer.Windows.Native;

/// <summary>GlobalMemoryStatusEx wrapper used for cheap, reliable RAM totals.</summary>
internal static class GlobalMemoryReader
{
    public static long TotalPhysicalMb()
    {
        var status = new NativeMethods.MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>(),
        };
        return NativeMethods.GlobalMemoryStatusEx(ref status)
            ? (long)(status.ullTotalPhys / (1024 * 1024))
            : 0;
    }
}
