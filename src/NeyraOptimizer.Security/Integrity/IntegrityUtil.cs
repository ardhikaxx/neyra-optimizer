using System.Security.Cryptography;

namespace NeyraOptimizer.Security.Integrity;

public static class IntegrityUtil
{
    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static string ComputeSha256Utf8(string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>Writes a JSON file plus a sidecar ".sha256" manifest used by integrity checks.</summary>
    public static void WriteWithManifest(string filePath, string jsonContent)
    {
        File.WriteAllText(filePath, jsonContent);
        File.WriteAllText(filePath + ".sha256", ComputeSha256Utf8(jsonContent));
    }

    /// <summary>Reads a JSON file only when its sidecar hash matches. Returns null when missing or corrupt.</summary>
    public static string? ReadVerified(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            var manifestPath = filePath + ".sha256";
            if (!File.Exists(manifestPath)) return null;
            var expected = File.ReadAllText(manifestPath).Trim();
            var actual = ComputeSha256(File.ReadAllText(filePath)); // hash of on-disk bytes
            // Compare against both raw-bytes and canonical UTF8 forms to tolerate encoding round-trips.
            var content = File.ReadAllText(filePath);
            if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(expected, ComputeSha256Utf8(content), StringComparison.OrdinalIgnoreCase))
            {
                return content;
            }
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
