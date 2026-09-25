using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Modrinth;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services.Modpack;

/// <summary>
/// 联网把本地文件对回 Modrinth / CurseForge 上已托管的条目。
///
/// 目标只有一个：<b>能引用就不打包</b>。命中远端的文件只写进清单 <c>files[]</c>、本地副本不进包
/// （既减小包体，也避免二次分发别人托管的资源）；命中不到的照旧打进 <c>overrides/</c>。
/// </summary>
public static class RemoteFileResolver
{
    /// <summary>Modrinth 单次 /v2/version_files 的 hash 数上限（官方未硬性限制，这里保守取 500）。</summary>
    public const int ModrinthBatchSize = 500;

    /// <summary>CurseForge 单次 /v1/fingerprints 的指纹数上限。</summary>
    public const int CurseForgeBatchSize = 200;

    /// <summary>
    /// 按导出格式走对应的查询。
    /// </summary>
    /// <param name="hashesByPath">相对路径 → 已算好的三种哈希。</param>
    /// <param name="format">Modrinth 格式只查 Modrinth；CurseForge 格式只查 CurseForge。</param>
    /// <param name="onProgress">(已完成批次数, 总批次数)。</param>
    /// <returns>相对路径 → 远端信息。查询失败的文件不会出现在结果里（等价于"未命中"）。</returns>
    public static async Task<Dictionary<string, ModpackRemoteFile>> ResolveAsync(
        IReadOnlyDictionary<string, ModpackFileHashes> hashesByPath,
        ModpackExportFormat format,
        Action<int, int>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, ModpackRemoteFile>(StringComparer.OrdinalIgnoreCase);
        if (hashesByPath.Count == 0)
            return result;

        try
        {
            if (format == ModpackExportFormat.Modrinth)
                await ResolveViaModrinthAsync(hashesByPath, result, onProgress, cancellationToken).ConfigureAwait(false);
            else
                await ResolveViaCurseForgeAsync(hashesByPath, result, onProgress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 联网失败不阻断导出：未命中的文件会全部进包
            DebugLogger.Warn("ModpackExport", $"联网匹配失败，剩余文件将直接打包: {ex.Message}");
        }

        return result;
    }

    private static async Task ResolveViaModrinthAsync(
        IReadOnlyDictionary<string, ModpackFileHashes> hashesByPath,
        Dictionary<string, ModpackRemoteFile> result,
        Action<int, int>? onProgress,
        CancellationToken cancellationToken)
    {
        // sha1 → 一组相对路径（不同目录下可能有两个内容完全相同的 jar）
        var pathsByHash = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in hashesByPath)
        {
            if (!pathsByHash.TryGetValue(pair.Value.Sha1, out var list))
            {
                list = new List<string>();
                pathsByHash[pair.Value.Sha1] = list;
            }
            list.Add(pair.Key);
        }

        var batches = Chunk(pathsByHash.Keys.ToList(), ModrinthBatchSize);
        var service = new ModrinthService();

        for (var i = 0; i < batches.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var versions = await service.GetVersionsByHashesAsync(batches[i], cancellationToken).ConfigureAwait(false);
            if (versions != null)
            {
                foreach (var hash in batches[i])
                {
                    if (!versions.TryGetValue(hash, out var version) || !pathsByHash.TryGetValue(hash, out var paths))
                        continue;

                    var url = PickModrinthUrl(version, hash);
                    if (url == null)
                        continue;

                    foreach (var path in paths)
                    {
                        result[path] = new ModpackRemoteFile
                        {
                            DownloadUrl = url,
                            RemoteFileName = Path.GetFileName(path)
                        };
                    }
                }
            }

            onProgress?.Invoke(i + 1, batches.Count);
        }
    }

    /// <summary>优先取 sha1 与本地一致的那个文件，否则退到 primary / 第一个文件。</summary>
    private static string? PickModrinthUrl(ModrinthVersion version, string sha1)
    {
        if (version.Files.Count == 0)
            return null;

        foreach (var file in version.Files)
        {
            if (file.Hashes != null
                && file.Hashes.TryGetValue("sha1", out var remoteSha1)
                && string.Equals(remoteSha1, sha1, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(file.Url))
            {
                return file.Url;
            }
        }

        var primary = version.Files.FirstOrDefault(f => f.Primary && !string.IsNullOrWhiteSpace(f.Url));
        if (primary != null)
            return primary.Url;

        var first = version.Files.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Url));
        return first?.Url;
    }

    private static async Task ResolveViaCurseForgeAsync(
        IReadOnlyDictionary<string, ModpackFileHashes> hashesByPath,
        Dictionary<string, ModpackRemoteFile> result,
        Action<int, int>? onProgress,
        CancellationToken cancellationToken)
    {
        var pathsByFingerprint = new Dictionary<uint, List<string>>();
        foreach (var pair in hashesByPath)
        {
            if (!pathsByFingerprint.TryGetValue(pair.Value.CurseForgeFingerprint, out var list))
            {
                list = new List<string>();
                pathsByFingerprint[pair.Value.CurseForgeFingerprint] = list;
            }
            list.Add(pair.Key);
        }

        var batches = Chunk(pathsByFingerprint.Keys.ToList(), CurseForgeBatchSize);
        var notDownloadable = 0;

        for (var i = 0; i < batches.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var matches = await CurseForgeService.GetFilesByFingerprintsAsync(batches[i]).ConfigureAwait(false);
            if (matches != null)
            {
                foreach (var fingerprint in batches[i])
                {
                    if (!matches.TryGetValue(fingerprint, out var match)
                        || match.File == null
                        || !pathsByFingerprint.TryGetValue(fingerprint, out var paths))
                    {
                        continue;
                    }

                    // 作者禁止第三方分发时 downloadUrl 为 null：导入端同样拿不到，
                    // 与其写进清单让玩家下载失败，不如直接打进包里（PCL 的处理方式）
                    if (string.IsNullOrWhiteSpace(match.File.DownloadUrl))
                    {
                        notDownloadable++;
                        continue;
                    }

                    foreach (var path in paths)
                    {
                        result[path] = new ModpackRemoteFile
                        {
                            DownloadUrl = match.File.DownloadUrl,
                            CurseForgeProjectId = match.File.ModId,
                            CurseForgeFileId = match.File.Id,
                            RemoteFileName = match.File.FileName
                        };
                    }
                }
            }

            onProgress?.Invoke(i + 1, batches.Count);
        }

        if (notDownloadable > 0)
        {
            DebugLogger.Info("ModpackExport",
                $"有 {notDownloadable} 个文件在 CurseForge 上禁止第三方下载，已改为直接打包");
        }
    }

    private static List<List<T>> Chunk<T>(List<T> source, int size)
    {
        var batches = new List<List<T>>();
        for (var i = 0; i < source.Count; i += size)
        {
            var count = Math.Min(size, source.Count - i);
            batches.Add(source.GetRange(i, count));
        }
        return batches;
    }
}
