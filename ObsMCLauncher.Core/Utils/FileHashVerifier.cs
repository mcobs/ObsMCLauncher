using System.IO;
using System.Security.Cryptography;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Utils;

public static class FileHashVerifier
{
    public static bool IsEnabled => LauncherConfig.Load().EnableFileHashVerification;

    public static bool VerifyFileHash(string filePath, string expectedHash, HashType hashType = HashType.Sha1)
    {
        if (!IsEnabled) return true;

        if (!File.Exists(filePath)) return false;

        var actualHash = hashType switch
        {
            HashType.Sha1 => ComputeSha1(filePath),
            HashType.Sha256 => ComputeSha256(filePath),
            _ => ComputeSha1(filePath)
        };

        return string.Equals(actualHash, expectedHash, System.StringComparison.OrdinalIgnoreCase);
    }

    public static string ComputeSha1(string filePath)
    {
        using var sha1 = SHA1.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha1.ComputeHash(stream);
        return System.BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    public static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return System.BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}

public enum HashType
{
    Sha1,
    Sha256
}
