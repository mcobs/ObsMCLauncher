using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// 库文件自动下载服务
    /// </summary>
    public static class LibraryDownloader
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        /// <summary>
        /// 自动下载缺失的库文件
        /// </summary>
        public static async Task<(int successCount, int failedCount)> DownloadMissingLibrariesAsync(
            string gameDirectory,
            string versionId,
            List<string> missingLibraryNames,
            Action<string, double, double>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            if (missingLibraryNames == null || missingLibraryNames.Count == 0)
            {
                return (0, 0);
            }

            Debug.WriteLine($"[LibraryDownloader] 开始下载 {missingLibraryNames.Count} 个缺失的库文件...");

            // 1. 读取 version.json 获取下载信息
            var versionJsonPath = Path.Combine(gameDirectory, "versions", versionId, $"{versionId}.json");
            if (!File.Exists(versionJsonPath))
            {
                Debug.WriteLine($"[LibraryDownloader] ❌ 版本JSON不存在: {versionJsonPath}");
                return (0, missingLibraryNames.Count);
            }

            var versionJson = await File.ReadAllTextAsync(versionJsonPath, cancellationToken);
            var versionDoc = JsonDocument.Parse(versionJson);
            var root = versionDoc.RootElement;

            // 2. 构建库名称到下载信息的映射
            var libraryDownloadMap = new Dictionary<string, LibraryDownloadInfo>();
            
            if (root.TryGetProperty("libraries", out var librariesElement))
            {
                foreach (var lib in librariesElement.EnumerateArray())
                {
                    if (!lib.TryGetProperty("name", out var nameElement))
                        continue;

                    var libName = nameElement.GetString();
                    if (string.IsNullOrEmpty(libName))
                        continue;

                    // 检查是否是缺失的库
                    if (!missingLibraryNames.Contains(libName))
                        continue;

                    // 提取下载信息
                    if (lib.TryGetProperty("downloads", out var downloads) &&
                        downloads.TryGetProperty("artifact", out var artifact))
                    {
                        if (artifact.TryGetProperty("url", out var urlElement) &&
                            artifact.TryGetProperty("path", out var pathElement))
                        {
                            var url = urlElement.GetString();
                            var path = pathElement.GetString();

                            if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(path))
                            {
                                long size = 0;
                                if (artifact.TryGetProperty("size", out var sizeElement))
                                {
                                    size = sizeElement.GetInt64();
                                }

                                libraryDownloadMap[libName] = new LibraryDownloadInfo
                                {
                                    Name = libName,
                                    Url = url,
                                    Path = path,
                                    Size = size
                                };

                                Debug.WriteLine($"[LibraryDownloader] 找到下载信息: {libName}");
                                Debug.WriteLine($"[LibraryDownloader]   URL: {url}");
                            }
                        }
                    }
                }
            }

            // 检查是否需要从父版本继承
            if (root.TryGetProperty("inheritsFrom", out var inheritsFromElement))
            {
                var parentVersion = inheritsFromElement.GetString();
                if (!string.IsNullOrEmpty(parentVersion))
                {
                    Debug.WriteLine($"[LibraryDownloader] 检查父版本 {parentVersion} 的库信息...");
                    
                    var parentVersionJsonPath = Path.Combine(gameDirectory, "versions", versionId, $"{parentVersion}.json");
                    if (File.Exists(parentVersionJsonPath))
                    {
                        var parentVersionJson = await File.ReadAllTextAsync(parentVersionJsonPath, cancellationToken);
                        var parentDoc = JsonDocument.Parse(parentVersionJson);
                        var parentRoot = parentDoc.RootElement;

                        if (parentRoot.TryGetProperty("libraries", out var parentLibrariesElement))
                        {
                            foreach (var lib in parentLibrariesElement.EnumerateArray())
                            {
                                if (!lib.TryGetProperty("name", out var nameElement))
                                    continue;

                                var libName = nameElement.GetString();
                                if (string.IsNullOrEmpty(libName))
                                    continue;

                                // 如果子版本已有，跳过
                                if (libraryDownloadMap.ContainsKey(libName))
                                    continue;

                                // 检查是否是缺失的库
                                if (!missingLibraryNames.Contains(libName))
                                    continue;

                                // 提取下载信息
                                if (lib.TryGetProperty("downloads", out var downloads) &&
                                    downloads.TryGetProperty("artifact", out var artifact))
                                {
                                    if (artifact.TryGetProperty("url", out var urlElement) &&
                                        artifact.TryGetProperty("path", out var pathElement))
                                    {
                                        var url = urlElement.GetString();
                                        var path = pathElement.GetString();

                                        if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(path))
                                        {
                                            long size = 0;
                                            if (artifact.TryGetProperty("size", out var sizeElement))
                                            {
                                                size = sizeElement.GetInt64();
                                            }

                                            libraryDownloadMap[libName] = new LibraryDownloadInfo
                                            {
                                                Name = libName,
                                                Url = url,
                                                Path = path,
                                                Size = size
                                            };

                                            Debug.WriteLine($"[LibraryDownloader] 从父版本找到下载信息: {libName}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 3. 下载缺失的库
            int successCount = 0;
            int failedCount = 0;
            int current = 0;
            int total = libraryDownloadMap.Count;

            var librariesDir = Path.Combine(gameDirectory, "libraries");

            foreach (var kvp in libraryDownloadMap)
            {
                current++;
                var libInfo = kvp.Value;

                try
                {
                    var destPath = Path.Combine(librariesDir, libInfo.Path.Replace("/", Path.DirectorySeparatorChar.ToString()));
                    var destDir = Path.GetDirectoryName(destPath);

                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    progressCallback?.Invoke($"正在下载 {libInfo.Name} ({current}/{total})...", current, total);
                    Debug.WriteLine($"[LibraryDownloader] 下载 [{current}/{total}]: {libInfo.Name}");
                    Debug.WriteLine($"[LibraryDownloader]   从: {libInfo.Url}");
                    Debug.WriteLine($"[LibraryDownloader]   到: {destPath}");

                    var response = await _httpClient.GetAsync(libInfo.Url, cancellationToken);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                        await File.WriteAllBytesAsync(destPath, content, cancellationToken);

                        // 验证文件大小
                        if (libInfo.Size > 0)
                        {
                            var fileInfo = new FileInfo(destPath);
                            if (fileInfo.Length == libInfo.Size)
                            {
                                Debug.WriteLine($"[LibraryDownloader] ✅ 下载成功: {libInfo.Name} ({fileInfo.Length} 字节)");
                                successCount++;
                            }
                            else
                            {
                                Debug.WriteLine($"[LibraryDownloader] ⚠️ 文件大小不匹配: {libInfo.Name}");
                                Debug.WriteLine($"[LibraryDownloader]   期望: {libInfo.Size}, 实际: {fileInfo.Length}");
                                failedCount++;
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"[LibraryDownloader] ✅ 下载成功: {libInfo.Name}");
                            successCount++;
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[LibraryDownloader] ❌ 下载失败: {libInfo.Name}");
                        Debug.WriteLine($"[LibraryDownloader]   HTTP状态: {response.StatusCode}");
                        failedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LibraryDownloader] ❌ 下载异常: {libInfo.Name}");
                    Debug.WriteLine($"[LibraryDownloader]   错误: {ex.Message}");
                    failedCount++;
                }
            }

            // 4. 对于没有找到下载信息的库，尝试从Maven镜像下载
            var missingDownloadInfo = missingLibraryNames.Where(name => !libraryDownloadMap.ContainsKey(name)).ToList();
            
            if (missingDownloadInfo.Count > 0)
            {
                Debug.WriteLine($"[LibraryDownloader] {missingDownloadInfo.Count} 个库没有下载信息，尝试从Maven镜像下载...");
                
                foreach (var libName in missingDownloadInfo)
                {
                    // 尝试多个Maven源
                    var mavenSources = new[]
                    {
                        "https://libraries.minecraft.net/",
                        "https://bmclapi2.bangbang93.com/maven/",
                        "https://maven.neoforged.net/releases/"
                    };

                    bool downloaded = false;
                    
                    foreach (var baseUrl in mavenSources)
                    {
                        try
                        {
                            var relativePath = MavenCoordinateToPath(libName);
                            var url = baseUrl + relativePath.Replace('\\', '/');
                            var destPath = Path.Combine(librariesDir, relativePath);
                            var destDir = Path.GetDirectoryName(destPath);

                            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                            {
                                Directory.CreateDirectory(destDir);
                            }

                            Debug.WriteLine($"[LibraryDownloader] 尝试从 {baseUrl} 下载: {libName}");

                            var response = await _httpClient.GetAsync(url, cancellationToken);
                            
                            if (response.IsSuccessStatusCode)
                            {
                                var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                                await File.WriteAllBytesAsync(destPath, content, cancellationToken);
                                
                                Debug.WriteLine($"[LibraryDownloader] ✅ 从Maven下载成功: {libName}");
                                successCount++;
                                downloaded = true;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[LibraryDownloader] 从 {baseUrl} 下载失败: {ex.Message}");
                        }
                    }

                    if (!downloaded)
                    {
                        Debug.WriteLine($"[LibraryDownloader] ❌ 所有源都下载失败: {libName}");
                        failedCount++;
                    }
                }
            }

            Debug.WriteLine($"[LibraryDownloader] ========== 库文件下载结果 ==========");
            Debug.WriteLine($"[LibraryDownloader] 总计: {missingLibraryNames.Count} 个");
            Debug.WriteLine($"[LibraryDownloader] 成功: {successCount} 个");
            Debug.WriteLine($"[LibraryDownloader] 失败: {failedCount} 个");

            return (successCount, failedCount);
        }

        /// <summary>
        /// Maven坐标转换为相对路径
        /// </summary>
        private static string MavenCoordinateToPath(string coordinate)
        {
            // 格式: group:artifact:version[:classifier][@extension]
            var parts = coordinate.Split(':');
            if (parts.Length < 3)
                return coordinate;

            var group = parts[0].Replace('.', '/');
            var artifact = parts[1];
            var version = parts[2];
            var classifier = parts.Length > 3 ? parts[3] : "";
            var extension = "jar";

            // 处理 @extension
            if (version.Contains('@'))
            {
                var versionParts = version.Split('@');
                version = versionParts[0];
                extension = versionParts[1];
            }
            else if (classifier.Contains('@'))
            {
                var classifierParts = classifier.Split('@');
                classifier = classifierParts[0];
                extension = classifierParts[1];
            }

            var fileName = string.IsNullOrEmpty(classifier)
                ? $"{artifact}-{version}.{extension}"
                : $"{artifact}-{version}-{classifier}.{extension}";

            return $"{group}/{artifact}/{version}/{fileName}";
        }

        private class LibraryDownloadInfo
        {
            public string Name { get; set; } = "";
            public string Url { get; set; } = "";
            public string Path { get; set; } = "";
            public long Size { get; set; }
        }
    }
}

