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
        /// <param name="onProgress">进度回调 (当前, 总数, 消息)</param>
        /// <returns>下载结果</returns>
        public static async Task<AssetsDownloadResult> DownloadAndCheckAssetsAsync(
            string gameDir,
            string versionId,
            Action<int, int, string>? onProgress = null)
        {
            try
            {
                Debug.WriteLine($"========== 开始检查Assets资源 ==========");
                onProgress?.Invoke(0, 100, "正在读取版本信息...");

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
                    onProgress?.Invoke(5, 100, "正在下载资源索引文件...");
                    Debug.WriteLine($"📥 下载AssetIndex: {assetIndexUrl}");

                    var response = await _httpClient.GetAsync(assetIndexUrl);
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
                onProgress?.Invoke(10, 100, "正在解析资源索引...");
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
                    onProgress?.Invoke(100, 100, "资源检查完成");
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

                foreach (var asset in missingAssets)
                {
                    var currentIndex = downloaded + failed + 1;
                    var progress = 10 + (int)((currentIndex / (float)total) * 90);
                    onProgress?.Invoke(progress, 100, $"下载资源文件 ({currentIndex}/{total})");

                    try
                    {
                        var hash = asset.Hash;
                        var hashPrefix = hash.Substring(0, 2);
                        var assetPath = Path.Combine(objectsDir, hashPrefix, hash);
                        var assetDir = Path.GetDirectoryName(assetPath);

                        if (!string.IsNullOrEmpty(assetDir))
                        {
                            Directory.CreateDirectory(assetDir);
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
                                    await Task.Delay(1000 * retry); // 递增延迟
                                }
                                
                                var response = await _httpClient.GetAsync(url);
                                response.EnsureSuccessStatusCode();
                                var fileBytes = await response.Content.ReadAsByteArrayAsync();
                                await File.WriteAllBytesAsync(assetPath, fileBytes);
                                
                                downloadSuccess = true;
                                downloaded++;
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
                        
                        if (!downloadSuccess)
                        {
                            failed++;
                            failedAssets.Add($"{asset.Name} ({lastException?.Message})");
                        }

                        if ((downloaded + failed) % 50 == 0)
                        {
                            Debug.WriteLine($"📥 进度: {downloaded}成功 / {failed}失败 / {total}总计");
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        failedAssets.Add($"{asset.Name} ({ex.Message})");
                        Debug.WriteLine($"❌ 下载异常: {asset.Name} - {ex.Message}");
                    }
                }

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
                
                onProgress?.Invoke(100, 100, $"资源下载完成 ({downloaded}成功, {failed}失败)");
                
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

