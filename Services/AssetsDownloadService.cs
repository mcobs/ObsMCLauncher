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
        /// <returns>是否成功</returns>
        public static async Task<bool> DownloadAndCheckAssetsAsync(
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
                    return false;
                }

                var versionJson = await File.ReadAllTextAsync(versionJsonPath);
                var versionInfo = JsonSerializer.Deserialize<VersionInfo>(versionJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (versionInfo?.AssetIndex == null)
                {
                    Debug.WriteLine($"❌ 版本JSON中没有AssetIndex信息");
                    return false;
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
                    return false;
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
                    return true;
                }

                // 5. 下载缺失的Assets
                Debug.WriteLine($"开始下载 {missingAssets.Count} 个缺失的Assets...");
                var downloaded = 0;
                var total = missingAssets.Count;

                // 使用BMCLAPI镜像源（更快）
                var config = LauncherConfig.Load();
                var baseUrl = config.DownloadSource == DownloadSource.BMCLAPI
                    ? "https://bmclapi2.bangbang93.com/assets"
                    : "https://resources.download.minecraft.net";

                foreach (var asset in missingAssets)
                {
                    downloaded++;
                    var progress = 10 + (int)((downloaded / (float)total) * 90);
                    onProgress?.Invoke(progress, 100, $"下载资源文件 ({downloaded}/{total})");

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

                        // 构建下载URL
                        var url = $"{baseUrl}/{hashPrefix}/{hash}";
                        
                        // 下载文件
                        var response = await _httpClient.GetAsync(url);
                        response.EnsureSuccessStatusCode();
                        var fileBytes = await response.Content.ReadAsByteArrayAsync();
                        await File.WriteAllBytesAsync(assetPath, fileBytes);

                        if (downloaded % 50 == 0)
                        {
                            Debug.WriteLine($"📥 已下载: {downloaded}/{total}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"❌ 下载失败: {asset.Name} - {ex.Message}");
                        // 继续下载其他文件
                    }
                }

                Debug.WriteLine($"✅ Assets下载完成！共 {downloaded}/{total}");
                onProgress?.Invoke(100, 100, $"资源下载完成 ({downloaded}/{total})");
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Assets下载服务异常: {ex.Message}");
                return false;
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

