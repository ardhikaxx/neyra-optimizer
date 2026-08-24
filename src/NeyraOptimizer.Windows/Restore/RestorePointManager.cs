using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NeyraOptimizer.Domain.Abstractions;
using NeyraOptimizer.Windows.Native;

namespace NeyraOptimizer.Windows.Restore;

/// <summary>
/// System Restore integration via srclient.dll. Actual creation runs inside the elevated helper
/// (restore points require admin). This class performs the privileged call; availability probing
/// is best-effort and reported honestly.
/// </summary>
public sealed class RestorePointManager : IRestorePointManager
{
    public bool IsSystemRestoreAvailable()
    {
        try
        {
            // DisableSR: per-volume override. Absent or 0 → SR enabled for that volume.
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
            if (key is null) return true; // key absent on some builds with SR enabled
            if (key.GetValue("DisableSR") is int disabled && disabled == 1)
                return false;
            if (key.GetValue("RPSessionInterval") is int interval)
            {
                _ = interval;
            }
            return true;
        }
        catch (System.Security.SecurityException)
        {
            return true; // cannot determine — let the actual creation attempt decide
        }
    }

    public async Task<string> CreateRestorePointAsync(string description, CancellationToken ct)
    {
        // The SR API can block briefly; run off the caller's context.
        return await Task.Run(() =>
        {
            var info = new NativeMethods.RESTOREPOINTINFOW
            {
                dwEventType = NativeMethods.BEGIN_SYSTEM_CHANGE,
                dwRestorePtType = NativeMethods.MODIFY_SETTINGS,
                llSequenceNumber = 0,
                szDescription = description ?? "Neyra Optimizer snapshot",
            };
            var status = new NativeMethods.STATEMGRSTATUS();

            if (!NativeMethods.SRSetRestorePointW(ref info, ref status))
            {
                var error = status.dwStatus != 0 ? (int)status.dwStatus : Marshal.GetLastWin32Error();
                throw error switch
                {
                    1058 => new InvalidOperationException("System Restore is disabled on this machine."),
                    1054 => new InvalidOperationException("A restore point was already created within the last 24 hours and Windows deferred this one."),
                    5 => new UnauthorizedAccessException("Creating a restore point requires administrator privileges."),
                    _ => new Win32Exception(error, $"SRSetRestorePoint failed (error {error})."),
                };
            }

            return status.llSequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }, ct).ConfigureAwait(false);
    }
}
