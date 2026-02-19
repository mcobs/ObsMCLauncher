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
        var handler = new HttpClientHandler
        {
            MaxConnectionsPerServer = 32,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60) // 整体超时 60s
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ObsMCLauncher/1.0");
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
            
            var downloaded = 0;
            var failed = 0;
            var total = missingAssets.Count;
            var failedAssets = new List<string>();
            long totalBytesDownloaded = 0;
            var startTime = DateTime.Now;
            var lastReportTime = DateTime.MinValue;
            var lockObject = new object();

            DebugLogger.Info("Assets", $"准备启动 {maxThreads} 个工作线程进行并行下载...");
            var assetQueue = new System.Collections.Concurrent.ConcurrentQueue<AssetObject>(missingAssets);
            
            var workers = Enumerable.Range(0, maxThreads).Select(async i =>
            {
                DebugLogger.Info("Assets", $"工作线程 #{i} 启动");
                while (assetQueue.TryDequeue(out var asset))
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var hash = asset.Hash;
                    var hashPrefix = hash.Substring(0, 2);
                    var assetPath = Path.Combine(objectsDir, hashPrefix, hash);
                    
                    Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);

                    var url = downloadSource.GetAssetUrl(hash);
                    bool success = false;
                    Exception? lastExc = null;

                    for (int retry = 0; retry < 3; retry++)
                    {
                        try
                        {
                            if (retry > 0) await Task.Delay(500 * retry, cancellationToken).ConfigureAwait(false);
                            
                            // 增加单文件超时控制
                            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            cts.CancelAfter(TimeSpan.FromSeconds(30));

                            var resp = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                            resp.EnsureSuccessStatusCode();
                            
                            using var fs = new FileStream(assetPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                            await resp.Content.CopyToAsync(fs, cts.Token).ConfigureAwait(false);

                            success = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            lastExc = ex;
                            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested) break;
                        }
                    }

                    lock (lockObject)
                    {
                        if (success)
                        {
                            downloaded++;
                            totalBytesDownloaded += asset.Size;
                        }
                        else if (!cancellationToken.IsCancellationRequested)
                        {
                            failed++;
                            failedAssets.Add($"{asset.Name} ({lastExc?.Message})");
                            DebugLogger.Error("Assets", $"下载失败: {asset.Name} - {lastExc?.Message}");
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
