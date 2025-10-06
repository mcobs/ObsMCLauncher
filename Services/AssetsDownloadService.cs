using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// Assets下载结果
    /// </summary>
    public class AssetsDownloadResult
    {
        public bool Success { get; set; }
        public int TotalAssets { get; set; }
        public int DownloadedAssets { get; set; }
        public int FailedAssets { get; set; }
        public List<string> FailedAssetNames { get; set; } = new List<string>();
    }

    /// <summary>
    /// Assets资源下载服务
    /// </summary>
    public class AssetsDownloadService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// 下载并检查Assets完整性
        /// </summary>
        /// <param name="gameDir">游戏目录</param>
        /// <param name="versionId">版本ID</param>
        /// <param name="onProgress">进度回调 (当前进度, 总进度100, 消息, 下载速度)</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>下载结果</returns>
        public static async Task<AssetsDownloadResult> DownloadAndCheckAssetsAsync(
            string gameDir,
            string versionId,
            Action<int, int, string, double>? onProgress = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                Debug.WriteLine($"========== 开始检查Assets资源 ==========");
                onProgress?.Invoke(0, 100, "正在读取版本信息...", 0);
                cancellationToken.ThrowIfCancellationRequested();

                // 1. 读取版本JSON获取AssetIndex信息
                var versionJsonPath = Path.Combine(gameDir, "versions", versionId, $"{versionId}.json");
                if (!File.Exists(versionJsonPath))
                {
                    Debug.WriteLine($"❌ 版本JSON不存在: {versionJsonPath}");
                    return new AssetsDownloadResult { Success = false };
                }

                var versionJson = await File.ReadAllTextAsync(versionJsonPath);
                var versionInfo = JsonSerializer.Deserialize<VersionInfo>(versionJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (versionInfo?.AssetIndex == null)
                {
                    Debug.WriteLine($"❌ 版本JSON中没有AssetIndex信息");
                    return new AssetsDownloadResult { Success = false };
                }

                var assetIndexId = versionInfo.AssetIndex.Id;
                var assetIndexUrl = versionInfo.AssetIndex.Url;
                Debug.WriteLine($"AssetIndex ID: {assetIndexId}");
                Debug.WriteLine($"AssetIndex URL: {assetIndexUrl}");

                // 2. 下载AssetIndex文件
                var assetsDir = Path.Combine(gameDir, "assets");
                var indexesDir = Path.Combine(assetsDir, "indexes");
                var objectsDir = Path.Combine(assetsDir, "objects");
                Directory.CreateDirectory(indexesDir);
                Directory.CreateDirectory(objectsDir);

                var assetIndexPath = Path.Combine(indexesDir, $"{assetIndexId}.json");
                
                if (!File.Exists(assetIndexPath))
                {
                    onProgress?.Invoke(5, 100, "正在下载资源索引文件...", 0);
                    cancellationToken.ThrowIfCancellationRequested();
                    Debug.WriteLine($"📥 下载AssetIndex: {assetIndexUrl}");

                    var response = await _httpClient.GetAsync(assetIndexUrl, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    var indexContent = await response.Content.ReadAsStringAsync();
                    await File.WriteAllTextAsync(assetIndexPath, indexContent);
                    Debug.WriteLine($"✅ AssetIndex已下载");
                }
                else
                {
                    Debug.WriteLine($"✅ AssetIndex已存在");
                }

                // 3. 解析AssetIndex
                onProgress?.Invoke(10, 100, "正在解析资源索引...", 0);
                cancellationToken.ThrowIfCancellationRequested();
                var assetIndexJson = await File.ReadAllTextAsync(assetIndexPath);
                var assetIndex = JsonSerializer.Deserialize<AssetIndex>(assetIndexJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (assetIndex?.Objects == null)
                {
                    Debug.WriteLine($"❌ AssetIndex解析失败");
                    return new AssetsDownloadResult { Success = false };
                }

                // 4. 检查缺失的Assets
                cancellationToken.ThrowIfCancellationRequested();
                var missingAssets = new List<AssetObject>();
                foreach (var asset in assetIndex.Objects)
                {
                    var hash = asset.Value.Hash;
                    var hashPrefix = hash.Substring(0, 2);
                    var assetPath = Path.Combine(objectsDir, hashPrefix, hash);
                    
                    if (!File.Exists(assetPath))
                    {
                        missingAssets.Add(new AssetObject
                        {
                            Name = asset.Key,
                            Hash = hash,
                            Size = asset.Value.Size
                        });
                    }
                }

                Debug.WriteLine($"总Assets数量: {assetIndex.Objects.Count}");
                Debug.WriteLine($"缺失Assets数量: {missingAssets.Count}");

                if (missingAssets.Count == 0)
                {
                    Debug.WriteLine($"✅ 所有Assets资源完整");
                    onProgress?.Invoke(100, 100, "资源检查完成", 0);
                    return new AssetsDownloadResult 
                    { 
                        Success = true, 
                        TotalAssets = assetIndex.Objects.Count,
                        DownloadedAssets = 0,
                        FailedAssets = 0
                    };
                }

                // 5. 下载缺失的Assets
                Debug.WriteLine($"开始下载 {missingAssets.Count} 个缺失的Assets...");
                var downloaded = 0;
                var failed = 0;
                var total = missingAssets.Count;
                var failedAssets = new List<string>();

                // 使用下载源管理器（支持BMCLAPI镜像）
                var downloadSource = DownloadSourceManager.Instance.CurrentService;
                Debug.WriteLine($"使用下载源: {DownloadSourceManager.Instance.CurrentSource}");

                // 获取多线程设置
                var config = LauncherConfig.Load();
                var maxThreads = Math.Max(1, Math.Min(config.MaxDownloadThreads, 16)); // 限制在1-16之间
                Debug.WriteLine($"使用 {maxThreads} 个并发线程下载Assets");

                // 使用信号量控制并发数
                using var semaphore = new System.Threading.SemaphoreSlim(maxThreads, maxThreads);
                var downloadTasks = new List<Task>();
                var lockObject = new object();
                
                // 速度计算相关
                var startTime = DateTime.Now;
                long totalBytesDownloaded = 0;

                foreach (var asset in missingAssets)
                {
                    var task = Task.Run(async () =>
                    {
                        await semaphore.WaitAsync(cancellationToken);
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var hash = asset.Hash;
                            var hashPrefix = hash.Substring(0, 2);
                            var assetPath = Path.Combine(objectsDir, hashPrefix, hash);
                            var assetDir = Path.GetDirectoryName(assetPath);

                if (!string.IsNullOrEmpty(assetDir))
                {
                    Directory.CreateDirectory(assetDir);
                }

                // 如果文件已存在，跳过
                if (File.Exists(assetPath))
                {
                    var fileInfo = new FileInfo(assetPath);
                    if (fileInfo.Length == asset.Size)
                    {
                        lock (lockObject)
                        {
                            downloaded++;
                            var currentIndex = downloaded + failed;
                            var progress = 10 + (int)((currentIndex / (float)total) * 90);
                            onProgress?.Invoke(progress, 100, $"下载资源文件 ({currentIndex}/{total})", 0);
                        }
                        return;
                    }
                }

                // 使用下载源服务获取URL（支持镜像加速）
                var url = downloadSource.GetAssetUrl(hash);
                
                // 下载文件（带重试机制，最多3次）
                bool downloadSuccess = false;
                Exception? lastException = null;
                
                for (int retry = 0; retry < 3; retry++)
                            {
                                try
                                {
                                    if (retry > 0)
                                    {
                                        Debug.WriteLine($"⚠️ 重试下载 ({retry}/3): {asset.Name}");
                                        await Task.Delay(1000 * retry, cancellationToken); // 递增延迟
                                    }
                                    
                                    var response = await _httpClient.GetAsync(url, cancellationToken);
                                    response.EnsureSuccessStatusCode();
                                    var fileBytes = await response.Content.ReadAsByteArrayAsync();
                                    await File.WriteAllBytesAsync(assetPath, fileBytes);
                                    
                                    downloadSuccess = true;
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    lastException = ex;
                                    if (retry == 2) // 最后一次重试
                                    {
                                        Debug.WriteLine($"❌ 下载失败（3次重试后）: {asset.Name}");
                                        Debug.WriteLine($"   错误: {ex.Message}");
                                    }
                                }
                            }
                            
                            // 线程安全的计数器更新
                            lock (lockObject)
                            {
                                if (downloadSuccess)
                                {
                                    downloaded++;
                                    totalBytesDownloaded += asset.Size;
                                }
                                else
                                {
                                    failed++;
                                    failedAssets.Add($"{asset.Name} ({lastException?.Message})");
                                }

                                // 计算下载速度
                                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                                var speed = elapsed > 0 ? totalBytesDownloaded / elapsed : 0;

                                // 更新进度
                                var currentIndex = downloaded + failed;
                                var progress = 10 + (int)((currentIndex / (float)total) * 90);
                                onProgress?.Invoke(progress, 100, $"下载资源文件 ({currentIndex}/{total})", speed);

                                if (currentIndex % 50 == 0)
                                {
                                    Debug.WriteLine($"📥 进度: {downloaded}成功 / {failed}失败 / {total}总计 - 速度: {FormatSpeed(speed)}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (lockObject)
                            {
                                failed++;
                                failedAssets.Add($"{asset.Name} ({ex.Message})");
                                Debug.WriteLine($"❌ 下载异常: {asset.Name} - {ex.Message}");
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    downloadTasks.Add(task);
                }

                // 等待所有任务完成或取消
                try
                {
                    await Task.WhenAll(downloadTasks);
                }
                catch (OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("Assets下载被取消，等待任务清理...");
                    // 等待所有任务结束（即使有异常）
                    await Task.WhenAll(downloadTasks.Select(t => t.ContinueWith(_ => { })));
                    throw; // 重新抛出取消异常
                }
                
                cancellationToken.ThrowIfCancellationRequested();
                Debug.WriteLine($"========== Assets下载完成 ==========");
                Debug.WriteLine($"成功: {downloaded}/{total}");
                Debug.WriteLine($"失败: {failed}/{total}");
                
                if (failed > 0)
                {
                    Debug.WriteLine($"失败的资源列表（前10个）:");
                    foreach (var failedAsset in failedAssets.Take(10))
                    {
                        Debug.WriteLine($"  - {failedAsset}");
                    }
                    if (failedAssets.Count > 10)
                    {
                        Debug.WriteLine($"  ... 还有 {failedAssets.Count - 10} 个失败项");
                    }
                }
                
                onProgress?.Invoke(100, 100, $"资源下载完成 ({downloaded}成功, {failed}失败)", 0);
                
                // 返回下载结果
                return new AssetsDownloadResult
                {
                    Success = failed == 0,
                    TotalAssets = total,
                    DownloadedAssets = downloaded,
                    FailedAssets = failed,
                    FailedAssetNames = failedAssets
                };
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("❌ Assets下载已取消");
                return new AssetsDownloadResult { Success = false };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Assets下载服务异常: {ex.Message}");
                return new AssetsDownloadResult 
                { 
                    Success = false,
                    FailedAssetNames = new List<string> { $"服务异常: {ex.Message}" }
                };
            }
        }

        /// <summary>
        /// 版本信息模型
        /// </summary>
        private class VersionInfo
        {
            public AssetIndexInfo? AssetIndex { get; set; }
        }

        /// <summary>
        /// AssetIndex信息
        /// </summary>
        private class AssetIndexInfo
        {
            public string? Id { get; set; }
            public string? Url { get; set; }
        }

        /// <summary>
        /// 格式化下载速度
        /// </summary>
        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond == 0) return "0 B/s";
            
            string[] sizes = { "B/s", "KB/s", "MB/s", "GB/s" };
            int order = 0;
            double speed = bytesPerSecond;
            
            while (speed >= 1024 && order < sizes.Length - 1)
            {
                order++;
                speed /= 1024;
            }
            
            return $"{speed:F2} {sizes[order]}";
        }

        /// <summary>
        /// AssetIndex模型
        /// </summary>
        private class AssetIndex
        {
            public Dictionary<string, AssetObjectInfo>? Objects { get; set; }
        }

        /// <summary>
        /// Asset对象信息
        /// </summary>
        private class AssetObjectInfo
        {
            public string Hash { get; set; } = "";
            public long Size { get; set; }
        }

        /// <summary>
        /// Asset对象（用于缺失列表）
        /// </summary>
        private class AssetObject
        {
            public string Name { get; set; } = "";
            public string Hash { get; set; } = "";
            public long Size { get; set; }
        }
    }
}

