using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace ObsMCLauncher.Core.Services.Modpack;

/// <summary>一个文件的三种哈希（去重查询与清单所需）。</summary>
public readonly record struct ModpackFileHashes(string Sha1, string Sha512, uint CurseForgeFingerprint);

/// <summary>
/// 一次读取同时算出 SHA1 + SHA512 + CurseForge 指纹。
///
/// 第一遍只统计"过滤掉空白字节后的长度"（MurmurHash2 的初值需要它），
/// 第二遍在同一个读缓冲上并行喂给 SHA1 / SHA512 / MurmurHash2 —— 也就是
/// <b>2 次顺序读换来 3 个哈希</b>，而不是 3 次读。
/// 只有会被拿去 Modrinth / CurseForge 查询的文件才需要走这里（<see cref="ModpackFileFilter.IsPotentiallyRemoteResource"/>）。
/// </summary>
public static class ModpackFileHasher
{
    private const int BufferSize = 128 * 1024;

    public static ModpackFileHashes ComputeForLookup(string filePath, CancellationToken cancellationToken = default)
    {
        var filteredLength = CurseForgeFingerprintAccumulator.CountFilteredBytes(filePath, cancellationToken);
        return Compute(filePath, filteredLength, cancellationToken);
    }

    public static ModpackFileHashes Compute(string filePath, long filteredLength, CancellationToken cancellationToken = default)
    {
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        var fingerprint = new CurseForgeFingerprintAccumulator(filteredLength);

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.SequentialScan);
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var span = buffer.AsSpan(0, read);
                sha1.AppendData(span);
                sha512.AppendData(span);
                fingerprint.Append(span);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return new ModpackFileHashes(
            ToHex(sha1.GetHashAndReset()),
            ToHex(sha512.GetHashAndReset()),
            fingerprint.Finish());
    }

    private static string ToHex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();
}
