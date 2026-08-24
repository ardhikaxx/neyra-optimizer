using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using NeyraOptimizer.Security.Integrity;

namespace NeyraOptimizer.Security.Elevation;

/// <summary>
/// Executes ONE privileged operation through a UAC-elevated child process of this same executable.
/// The parent stays unelevated; the child validates the request independently, performs exactly one
/// operation, writes a signed result file and exits. No credentials are ever collected or stored.
/// </summary>
public sealed class ElevationManager
{
    private readonly string _opsRoot;
    private readonly string _exePath;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.General);

    public ElevationManager(string? opsRoot = null, string? exePath = null)
    {
        _opsRoot = opsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "NeyraOptimizer", "ops");
        _exePath = exePath ?? Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
    }

    public bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Runs an elevated operation, showing UAC when needed. Returns the operation result.</summary>
    public async Task<ElevatedOperationResult> RunElevatedAsync(ElevatedOperationRequest request, CancellationToken ct)
    {
        var (valid, error) = ElevatedOperationValidator.Validate(request);
        if (!valid)
            return new ElevatedOperationResult { OperationId = request.OperationId, Success = false, ErrorText = $"Validation failed: {error}" };

        var opDir = PrepareOpDirectory(request.OperationId);
        try
        {
            var requestJson = JsonSerializer.Serialize(request, JsonOpts);
            IntegrityUtil.WriteWithManifest(Path.Combine(opDir, "request.json"), requestJson);

            var psi = new ProcessStartInfo
            {
                FileName = _exePath,
                Arguments = $"--elevated-op {request.OperationId:N}",
                UseShellExecute = true,
                Verb = "runas", // triggers UAC
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return Fail(request.OperationId, "Elevated process could not be started.");

            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            var resultPath = Path.Combine(opDir, "result.json");
            if (process.ExitCode != 0 || !File.Exists(resultPath))
                return Fail(request.OperationId,
                    $"Elevated operation did not complete (exit code {process.ExitCode}). It may have been cancelled at the UAC prompt.");

            var resultJson = File.ReadAllText(resultPath);
            var result = JsonSerializer.Deserialize<ElevatedOperationResult>(resultJson, JsonOpts);
            return result ?? Fail(request.OperationId, "Elevated result could not be parsed.");
        }
        catch (System.ComponentModel.Win32Exception w32) when (w32.NativeErrorCode == 1223)
        {
            return new ElevatedOperationResult
            {
                OperationId = request.OperationId,
                Success = false,
                ErrorText = "Administrator approval was cancelled.",
            };
        }
        catch (OperationCanceledException)
        {
            return new ElevatedOperationResult { OperationId = request.OperationId, Success = false, ErrorText = "Cancelled." };
        }
        finally
        {
            TryDeleteDir(opDir);
        }
    }

    /// <summary>Called by the elevated child entry point. Returns process exit code.</summary>
    public static int ExecuteAsChild(Guid operationId, IElevatedExecutor executor, string? opsRootOverride = null)
    {
        try
        {
            var opsRoot = opsRootOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "NeyraOptimizer", "ops");
            var opDir = Path.Combine(opsRoot, operationId.ToString("N"));
            var requestPath = Path.Combine(opDir, "request.json");

            var requestJson = File.ReadAllText(requestPath);
            var request = JsonSerializer.Deserialize<ElevatedOperationRequest>(requestJson, JsonOpts);
            if (request is null) throw new InvalidOperationException("Request unreadable.");

            // Independent validation inside the elevated process.
            var (valid, error) = ElevatedOperationValidator.Validate(request);
            ElevatedOperationResult result;
            if (!valid)
            {
                result = new ElevatedOperationResult { OperationId = operationId, Success = false, ErrorText = $"Validation failed: {error}" };
            }
            else
            {
                result = executor.Execute(request);
            }

            result = result with { OperationId = operationId };
            IntegrityUtil.WriteWithManifest(Path.Combine(opDir, "result.json"),
                JsonSerializer.Serialize(result, JsonOpts));
            // Do NOT delete here: the parent reads result.json after process exit and performs cleanup.
            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            // Last-resort: write an unvalidated failure marker so the parent gets a reason.
            try
            {
                var opsRoot2 = opsRootOverrideFallback();
                var opDir2 = Path.Combine(opsRoot2, operationId.ToString("N"));
                Directory.CreateDirectory(opDir2);
                File.WriteAllText(Path.Combine(opDir2, "result.json"),
                    JsonSerializer.Serialize(new ElevatedOperationResult
                    {
                        OperationId = operationId,
                        Success = false,
                        ErrorText = "Child execution failed: " + ex.GetType().Name,
                    }, JsonOpts));
            }
            catch { /* nothing more we can do */ }
            return 2;
        }

        string opsRootOverrideFallback() => opsRootOverride ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NeyraOptimizer", "ops");
    }

    private string PrepareOpDirectory(Guid id)
    {
        var dir = Path.Combine(_opsRoot, id.ToString("N"));
        Directory.CreateDirectory(dir);

        // Restrict ACLs: current user + Administrators only, inheritance disabled.
        try
        {
            var dirInfo = new DirectoryInfo(dir);
            var security = dirInfo.GetAccessControl();
            security.SetAccessRuleProtection(true, false);
            var self = WindowsIdentity.GetCurrent().User!;
            security.ResetAccessRule(new FileSystemAccessRule(self, FileSystemRights.FullControl, AccessControlType.Allow));
            security.ResetAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl, AccessControlType.Allow));
            security.ResetAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl, AccessControlType.Allow));
            dirInfo.SetAccessControl(security);
        }
        catch (SystemException)
        {
            // On systems where ACL manipulation fails we continue; contents contain no secrets.
        }
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (IOException) { /* retried by parent cleanup later */ }
        catch (UnauthorizedAccessException) { }
    }

    private static ElevatedOperationResult Fail(Guid id, string message) =>
        new() { OperationId = id, Success = false, ErrorText = message };
}

/// <summary>Implemented in the Windows integration layer; performs the privileged work.</summary>
public interface IElevatedExecutor
{
    ElevatedOperationResult Execute(ElevatedOperationRequest request);
}
