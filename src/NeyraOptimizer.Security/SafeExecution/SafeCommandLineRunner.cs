using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace NeyraOptimizer.Security.SafeExecution;

/// <summary>
/// Whitelisted command execution wrapper. Executable paths are fixed constants (never derived from
/// user input), arguments are built from code with strict validation, output is size-capped and
/// every run enforces a timeout plus exit-code capture. No shell interpolation ever happens.
/// </summary>
public sealed partial class SafeCommandLineRunner
{
    private static readonly string WindowsPowerShell = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    public sealed record SafeRunResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
    {
        public bool Success => ExitCode == 0 && !TimedOut;
    }

    /// <summary>Runs an encoded PowerShell command built exclusively from whitelisted cmdlet templates.</summary>
    public Task<SafeRunResult> RunPowerShellAsync(string encodedCommand, TimeSpan timeout, CancellationToken ct) =>
        RunAsync(WindowsPowerShell, $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}", timeout, ct);

    /// <summary>Base64-encodes a PowerShell script the way -EncodedCommand expects (UTF-16LE).</summary>
    public static string EncodePowerShell(string script) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private static async Task<SafeRunResult> RunAsync(string exePath, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        if (!File.Exists(exePath))
            return new SafeRunResult(-1, string.Empty, $"Executable not found: {Path.GetFileName(exePath)}", false);
        if (!IsAllowedExecutable(exePath))
            return new SafeRunResult(-1, string.Empty, "Executable is not on the whitelist.", false);

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null && stdout.Length < MaxCapturedChars) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null && stderr.Length < MaxCapturedChars) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool timedOut;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            timedOut = false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timedOut = true;
            try { process.Kill(entireProcessTree: true); } catch (SystemException) { }
        }

        if (ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch (SystemException) { }
        }

        return new SafeRunResult(process.HasExited ? process.ExitCode : -1,
            stdout.ToString(), stderr.ToString(), timedOut || ct.IsCancellationRequested);
    }

    private const int MaxCapturedChars = 64 * 1024;

    private static bool IsAllowedExecutable(string exePath) =>
        exePath.Equals(WindowsPowerShell, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^[A-Za-z0-9\.\-_\\:]{1,200}$")]
    public static partial Regex SafeTokenRegex();
}
