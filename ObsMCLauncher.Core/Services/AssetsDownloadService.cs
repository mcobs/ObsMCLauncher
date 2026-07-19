using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

public class AssetsDownloadResult
{
    public bool Success { get; set; }
    public int TotalAssets { get; set; }
    public int DownloadedAssets { get; set; }
    public int FailedAssets { get; set; }
    public List<string> FailedAssetNames { get; set; } = new();
}

public class AssetsDownloadService
{
    private static readonly HttpClient _httpClient;

    static AssetsDownloadService()
    {
        var handler = new SocketsHttpHandler
        {
            // 提高每服务器最大连接数，支持更高并发
            // 默认 worker 数 × 8 连接，确保下载流水线不阻塞
            MaxConnectionsPerServer = 64,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            // 启用 HTTP/2 多路复用，单 TCP 连接可承载多个并发请求
            // 对 BMCLAPI 等同一源的大量小文件下载有明显性能提升
            EnableMultipleHttp2Connections = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30)
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60) // 整体超时 60s
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", Utils.VersionInfo.UserAgent);
        // 尝试协商 HTTP/2（服务器不支持时自动降级到 HTTP/1.1）
        _httpClient.DefaultRequestVersion = System.Net.HttpVersion.Version20;
        _httpClient.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
    }

    public static async Task<AssetsDownloadResult> DownloadAndCheckAssetsAsync(
        string gameDir,
        string versionId,
        Action<int, int, string, double>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            DebugLogger.Info("Assets", $"========== 开始检查Assets资源: {versionId} ==========");
            onProgress?.Invoke(0, 100, "正在读取版本信息...", 0);
            cancellationToken.ThrowIfCancellationRequested();

            AssetIndexInfo? assetIndexInfo = null;
            string currentVerId = versionId;
            var visitedVersions = new HashSet<string>();

            // 递归查找 assetIndex (处理 inheritsFrom)
            while (!string.IsNullOrEmpty(currentVerId) && visitedVersions.Add(currentVerId))
            {
                DebugLogger.Info("Assets", $"正在尝试从版本 {currentVerId} 获取 AssetIndex...");
                var jsonPath = Path.Combine(gameDir, "versions", currentVerId, $"{currentVerId}.json");
                
                if (!File.Exists(jsonPath) && currentVerId != versionId)
                {
                    var fallbackPath = Path.Combine(gameDir, "versions", versionId, $"{currentVerId}.json");
                    if (File.Exists(fallbackPath))
                    {
                        jsonPath = fallbackPath;
                        DebugLogger.Info("Assets", $"在整合包目录中找到父版本 JSON: {currentVerId}.json");
                    }
                }

                if (!File.Exists(jsonPath))
                {
                    DebugLogger.Warn("Assets", $"找不到 JSON: {jsonPath}");
                    break;
                }

                var json = await File.ReadAllTextAsync(jsonPath, cancellationToken).ConfigureAwait(false);
                var versionData = JsonSerializer.Deserialize<VersionInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                if (versionData?.AssetIndex != null && !string.IsNullOrEmpty(versionData.AssetIndex.Id))
                {
                    assetIndexInfo = versionData.AssetIndex;
                    DebugLogger.Info("Assets", $"在版本 {currentVerId} 中找到 AssetIndex: {assetIndexInfo.Id}");
                    break;
                }

                if (!string.IsNullOrEmpty(versionData?.InheritsFrom))
                {
                    DebugLogger.Info("Assets", $"版本 {currentVerId} 继承自 {versionData.InheritsFrom}，继续向上查找...");
                    currentVerId = versionData.InheritsFrom;
                }
                else
                {
                    break;
                }
            }

            if (assetIndexInfo == null)
            {
                DebugLogger.Error("Assets", $"无法找到版本 {versionId} 的AssetIndex信息");
                return new AssetsDownloadResult { Success = false };
            }

            var assetIndexId = assetIndexInfo.Id;
            var assetIndexUrl = assetIndexInfo.Url;
            
            var assetsDir = Path.Combine(gameDir, "assets");
            var indexesDir = Path.Combine(assetsDir, "indexes");
            var objectsDir = Path.Combine(assetsDir, "objects");
            Directory.CreateDirectory(indexesDir);
            Directory.CreateDirectory(objectsDir);

            var assetIndexPath = Path.Combine(indexesDir, $"{assetIndexId}.json");

            if (!File.Exists(assetIndexPath))
            {
                onProgress?.Invoke(5, 100, "正在下载资源索引文件...", 0);
                DebugLogger.Info("Assets", $"正在下载 AssetIndex: {assetIndexUrl}");

                var response = await _httpClient.GetAsync(assetIndexUrl, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var indexContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(assetIndexPath, indexContent, cancellationToken).ConfigureAwait(false);
                DebugLogger.Info("Assets", "AssetIndex 下载成功");
            }

            var assetIndexJson = await File.ReadAllTextAsync(assetIndexPath, cancellationToken).ConfigureAwait(false);
            var assetIndex = JsonSerializer.Deserialize<AssetIndex>(assetIndexJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (assetIndex?.Objects == null)
            {
                DebugLogger.Error("Assets", "AssetIndex 解析失败");
                return new AssetsDownloadResult { Success = false };
            }

            var missingAssets = new List<AssetObject>();
            foreach (var asset in assetIndex.Objects)
            {
                var hash = asset.Value.Hash;
                var hashPrefix = hash.Substring(0, 2);
                var assetPath = Path.Combine(objectsDir, hashPrefix, hash);

                if (!File.Exists(assetPath))
                {
                    missingAssets.Add(new AssetObject { Name = asset.Key, Hash = hash, Size = asset.Value.Size });
                }
            }

            DebugLogger.Info("Assets", $"总数: {assetIndex.Objects.Count}, 缺失: {missingAssets.Count}");

            if (missingAssets.Count == 0)
            {
                if (assetIndexId == "legacy" || assetIndexId == "pre-1.6")
                    await CreateLegacyVirtualAssetsAsync(gameDir, assetIndex, objectsDir, cancellationToken).ConfigureAwait(false);

                onProgress?.Invoke(100, 100, "资源检查完成", 0);
                return new AssetsDownloadResult { Success = true, TotalAssets = assetIndex.Objects.Count };
            }

            var config = LauncherConfig.Load();
            var downloadSource = ObsMCLauncher.Core.Services.Download.DownloadSourceManager.Instance.CurrentService;
            var maxThreads = Math.Max(1, config.MaxDownloadThreads);
            // 每个 worker 同时处理的并发下载数，提升单线程 await 期间的 CPU 利用率
            // 总并发量 = maxThreads × concurrencyPerWorker
            // 对 BMCLAPI 等大量小文件场景，适当提高并发可显著提升总吞吐
            const int concurrencyPerWorker = 4;

            var downloaded = 0;
            var failed = 0;
            var total = missingAssets.Count;
            var failedAssets = new List<string>();
            long totalBytesDownloaded = 0;
            var startTime = DateTime.Now;
            var lastReportTime = DateTime.MinValue;
            var lockObject = new object();

            DebugLogger.Info("Assets", $"准备启动 {maxThreads} 个 worker × {concurrencyPerWorker} 并发 = {maxThreads * concurrencyPerWorker} 总并发下载...");
            var assetQueue = new System.Collections.Concurrent.ConcurrentQueue<AssetObject>(missingAssets);

            // 每个 worker 内部使用 SemaphoreSlim 控制并发数，同时从队列中取任务
            var workers = Enumerable.Range(0, maxThreads).Select(async i =>
            {
                DebugLogger.Info("Assets", $"工作线程 #{i} 启动 (并发数: {concurrencyPerWorker})");
                var pendingTasks = new List<Task>(concurrencyPerWorker);
                var sem = new SemaphoreSlim(concurrencyPerWorker);

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!assetQueue.TryDequeue(out var asset))
                    {
                        // 队列空，等待所有进行中的任务完成
                        if (pendingTasks.Count == 0) break;
                        await Task.WhenAll(pendingTasks).ConfigureAwait(false);
                        pendingTasks.Clear();
                        continue;
                    }

                    await sem.WaitAsync(cancellationToken).ConfigureAwait(false);

                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            await DownloadOneAssetAsync(asset, objectsDir, downloadSource, cancellationToken).ConfigureAwait(false);
                            OnAssetDone(asset, true, null, lockObject, ref downloaded, ref failed, ref totalBytesDownloaded,
                                failedAssets, total, startTime, ref lastReportTime, onProgress);
                        }
                        catch (Exception ex)
                        {
                            OnAssetDone(asset, false, ex, lockObject, ref downloaded, ref failed, ref totalBytesDownloaded,
                                failedAssets, total, startTime, ref lastReportTime, onProgress);
                        }
                        finally
                        {
                            sem.Release();
                        }
                    }, cancellationToken);

                    pendingTasks.Add(task);
                    // 清理已完成的任务，避免列表无限增长
                    if (pendingTasks.Count >= concurrencyPerWorker * 2)
                    {
                        var completed = pendingTasks.Where(t => t.IsCompleted).ToList();
                        foreach (var t in completed) pendingTasks.Remove(t);
                        if (pendingTasks.Count >= concurrencyPerWorker * 2)
                        {
                            await Task.WhenAny(pendingTasks).ConfigureAwait(false);
                        }
                    }
                }

                // 等待本 worker 剩余任务完成
                await Task.WhenAll(pendingTasks).ConfigureAwait(false);
                DebugLogger.Info("Assets", $"工作线程 #{i} 退出");
            }).ToList();

            await Task.WhenAll(workers).ConfigureAwait(false);

            if (assetIndexId == "legacy" || assetIndexId == "pre-1.6")
                await CreateLegacyVirtualAssetsAsync(gameDir, assetIndex, objectsDir, cancellationToken).ConfigureAwait(false);

            return new AssetsDownloadResult
            {
                Success = failed == 0,
                TotalAssets = total,
                DownloadedAssets = downloaded,
                FailedAssets = failed,
                FailedAssetNames = failedAssets
            };
        }
        catch (Exception ex)
        {
            DebugLogger.Error("Assets", $"严重异常: {ex.Message}");
            return new AssetsDownloadResult { Success = false };
        }
    }

    /// <summary>
    /// 下载单个资源文件，包含重试与文件占用恢复机制
    /// </summary>
    private static async Task DownloadOneAssetAsync(
        AssetObject asset,
        string objectsDir,
        ObsMCLauncher.Core.Services.Download.IDownloadSourceService downloadSource,
        CancellationToken cancellationToken)
    {
        var hash = asset.Hash;
        var hashPrefix = hash.Substring(0, 2);
        var assetPath = Path.Combine(objectsDir, hashPrefix, hash);

        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);

        var url = downloadSource.GetAssetUrl(hash);
        Exception? lastExc = null;

        for (int retry = 0; retry < 3; retry++)
        {
            try
            {
                if (retry > 0) await Task.Delay(500 * retry, cancellationToken).ConfigureAwait(false);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(30));

                var resp = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();

                // 使用 FileShare.Read 允许其他进程（如杀毒软件）读取文件，
                // 避免杀毒软件扫描新建文件时短暂锁定导致写入失败。
                // 文件访问冲突时进行短重试，避免直接失败。
                // 缓冲区从 8KB 提升到 64KB，减少小文件 I/O 系统调用次数
                bool fileWritten = false;
                for (int fileRetry = 0; fileRetry < 5; fileRetry++)
                {
                    try
                    {
                        using var fs = new FileStream(assetPath, FileMode.Create, FileAccess.Write, FileShare.Read, 65536, true);
                        await resp.Content.CopyToAsync(fs, cts.Token).ConfigureAwait(false);
                        fileWritten = true;
                        break;
                    }
                    catch (IOException ioex) when (ioex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase))
                    {
                        if (fileRetry < 4)
                        {
                            await Task.Delay(200 * (fileRetry + 1), cts.Token).ConfigureAwait(false);
                            continue;
                        }
                        throw;
                    }
                }

                if (!fileWritten)
                {
                    throw new IOException("无法写入文件：多次重试后仍被占用");
                }

                if (FileHashVerifier.IsEnabled)
                {
                    if (!FileHashVerifier.VerifyFileHash(assetPath, hash, HashType.Sha1))
                    {
                        File.Delete(assetPath);
                        throw new Exception("SHA-1校验失败");
                    }
                }

                return; // 成功
            }
            catch (Exception ex)
            {
                lastExc = ex;
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested) break;
            }
        }

        throw lastExc ?? new Exception($"下载失败: {asset.Name}");
    }

    /// <summary>
    /// 单个资源下载完成后的统计与进度上报（线程安全）
    /// </summary>
    private static void OnAssetDone(
        AssetObject asset,
        bool success,
        Exception? error,
        object lockObject,
        ref int downloaded,
        ref int failed,
        ref long totalBytesDownloaded,
        List<string> failedAssets,
        int total,
        DateTime startTime,
        ref DateTime lastReportTime,
        Action<int, int, string, double>? onProgress)
    {
        lock (lockObject)
        {
            if (success)
            {
                downloaded++;
                totalBytesDownloaded += asset.Size;
            }
            else
            {
                failed++;
                failedAssets.Add($"{asset.Name} ({error?.Message})");
                DebugLogger.Error("Assets", $"下载失败: {asset.Name} - {error?.Message}");
            }

            var now = DateTime.Now;
            if ((now - lastReportTime).TotalMilliseconds >= 250 || (downloaded + failed) == total)
            {
                var elapsed = (now - startTime).TotalSeconds;
                var speed = elapsed > 0 ? totalBytesDownloaded / elapsed : 0;
                var currentIndex = downloaded + failed;
                var progressPercent = 10 + (int)((currentIndex / (float)total) * 90);

                onProgress?.Invoke(progressPercent, 100, $"下载资源文件 ({currentIndex}/{total})", speed);
                lastReportTime = now;
            }
        }
    }

    private class VersionInfo
    {
        public AssetIndexInfo? AssetIndex { get; set; }
        public string? InheritsFrom { get; set; }
    }

    private class AssetIndexInfo
    {
        public string? Id { get; set; }
        public string? Url { get; set; }
    }

    private static Task CreateLegacyVirtualAssetsAsync(string gameDir, AssetIndex assetIndex, string objectsDir, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                if (assetIndex.Objects == null) return;
                var virtualDir = Path.Combine(gameDir, "assets", "virtual", "legacy");
                Directory.CreateDirectory(virtualDir);

                foreach (var asset in assetIndex.Objects)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var targetFile = Path.Combine(virtualDir, asset.Key);
                    var sourceFile = Path.Combine(objectsDir, asset.Value.Hash.Substring(0, 2), asset.Value.Hash);

                    if (File.Exists(sourceFile) && !File.Exists(targetFile))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                        File.Copy(sourceFile, targetFile, true);
                    }
                }
            }
            catch { }
        }, cancellationToken);
    }

    private class AssetIndex { public Dictionary<string, AssetObjectInfo>? Objects { get; set; } }
    private class AssetObjectInfo { public string Hash { get; set; } = ""; public long Size { get; set; } }
    private class AssetObject { public string Name { get; set; } = ""; public string Hash { get; set; } = ""; public long Size { get; set; } }
}
