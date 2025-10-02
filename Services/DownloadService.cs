using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// 下载进度信息
    /// </summary>
    public class DownloadProgress
    {
        public long TotalBytes { get; set; }
        public long DownloadedBytes { get; set; }
        public double ProgressPercentage => TotalBytes > 0 ? (DownloadedBytes * 100.0 / TotalBytes) : 0;
        public string CurrentFile { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// Minecraft版本下载服务
    /// </summary>
    public class DownloadService
    {
        private static readonly HttpClient _httpClient;

        static DownloadService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(10)
            };

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ObsMCLauncher/1.0");
        }

        /// <summary>
        /// 下载Minecraft版本
        /// </summary>
        public static async Task<bool> DownloadMinecraftVersion(
            string versionId,
            string gameDirectory,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var downloadSource = DownloadSourceManager.Instance.CurrentService;
                
                progress?.Report(new DownloadProgress 
                { 
                    Status = "正在获取版本信息...",
                    CurrentFile = versionId 
                });

                // 1. 下载版本JSON
                var versionJsonUrl = downloadSource.GetVersionJsonUrl(versionId);
                var versionJsonPath = Path.Combine(gameDirectory, "versions", versionId, $"{versionId}.json");
                
                Directory.CreateDirectory(Path.GetDirectoryName(versionJsonPath)!);
                
                var versionJson = await DownloadStringAsync(versionJsonUrl, cancellationToken);
                if (string.IsNullOrEmpty(versionJson))
                {
                    throw new Exception("下载版本JSON失败");
                }

                await File.WriteAllTextAsync(versionJsonPath, versionJson, cancellationToken);
                System.Diagnostics.Debug.WriteLine($"✅ 版本JSON已保存: {versionJsonPath}");

                // 2. 解析版本JSON
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var versionInfo = JsonSerializer.Deserialize<VersionInfo>(versionJson, options);
                if (versionInfo == null)
                {
                    throw new Exception("解析版本JSON失败");
                }

                System.Diagnostics.Debug.WriteLine($"✅ 解析版本信息成功");
                System.Diagnostics.Debug.WriteLine($"   客户端URL: {versionInfo.Downloads?.Client?.Url}");
                System.Diagnostics.Debug.WriteLine($"   库文件数量: {versionInfo.Libraries?.Count ?? 0}");

                // 3. 下载客户端JAR
                var clientJarPath = Path.Combine(gameDirectory, "versions", versionId, $"{versionId}.jar");
                if (versionInfo.Downloads?.Client?.Url != null)
                {
                    progress?.Report(new DownloadProgress
                    {
                        Status = "正在下载客户端JAR...",
                        CurrentFile = $"{versionId}.jar",
                        TotalBytes = versionInfo.Downloads.Client.Size,
                        DownloadedBytes = 0
                    });

                    System.Diagnostics.Debug.WriteLine($"开始下载客户端JAR: {versionInfo.Downloads.Client.Url}");
                    System.Diagnostics.Debug.WriteLine($"保存路径: {clientJarPath}");

                    await DownloadFileAsync(
                        versionInfo.Downloads.Client.Url,
                        clientJarPath,
                        progress,
                        cancellationToken);
                    
                    System.Diagnostics.Debug.WriteLine($"✅ 客户端JAR已下载: {clientJarPath}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 警告：客户端下载URL为空！");
                }

                // 4. 下载库文件
                if (versionInfo.Libraries != null && versionInfo.Libraries.Count > 0)
                {
                    progress?.Report(new DownloadProgress
                    {
                        Status = $"正在下载库文件 (共{versionInfo.Libraries.Count}个)...",
                        CurrentFile = "libraries"
                    });

                    await DownloadLibrariesAsync(
                        versionInfo.Libraries,
                        gameDirectory,
                        downloadSource,
                        progress,
                        cancellationToken);
                }

                // 5. 下载资源文件（如果需要）
                if (versionInfo.AssetIndex != null)
                {
                    progress?.Report(new DownloadProgress
                    {
                        Status = "正在下载资源索引...",
                        CurrentFile = "assets"
                    });

                    await DownloadAssetsAsync(
                        versionInfo.AssetIndex,
                        gameDirectory,
                        downloadSource,
                        progress,
                        cancellationToken);
                }

                progress?.Report(new DownloadProgress
                {
                    Status = "下载完成！",
                    TotalBytes = 100,
                    DownloadedBytes = 100
                });

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 下载版本失败: {ex.Message}");
                progress?.Report(new DownloadProgress
                {
                    Status = $"下载失败: {ex.Message}"
                });
                return false;
            }
        }

        /// <summary>
        /// 下载库文件
        /// </summary>
        private static async Task DownloadLibrariesAsync(
            List<Library> libraries,
            string gameDirectory,
            IDownloadSourceService downloadSource,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            var librariesPath = Path.Combine(gameDirectory, "libraries");
            Directory.CreateDirectory(librariesPath);

            int completed = 0;
            int total = libraries.Count;

            foreach (var library in libraries)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // 检查是否允许下载该库
                if (!IsLibraryAllowed(library)) continue;

                var artifact = library.Downloads?.Artifact;
                if (artifact == null || string.IsNullOrEmpty(artifact.Path)) continue;

                var libraryPath = Path.Combine(librariesPath, artifact.Path.Replace('/', Path.DirectorySeparatorChar));
                
                // 如果文件已存在且大小正确，跳过
                if (File.Exists(libraryPath))
                {
                    var fileInfo = new FileInfo(libraryPath);
                    if (fileInfo.Length == artifact.Size)
                    {
                        completed++;
                        continue;
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);

                try
                {
                    var url = artifact.Url;
                    if (string.IsNullOrEmpty(url))
                    {
                        url = downloadSource.GetLibraryUrl(artifact.Path);
                    }

                    await DownloadFileAsync(url, libraryPath, null, cancellationToken);
                    completed++;

                    // 计算库文件下载进度（基于文件数量）
                    var libraryProgress = total > 0 ? (completed * 100.0 / total) : 0;
                    progress?.Report(new DownloadProgress
                    {
                        Status = $"正在下载库文件 ({completed}/{total})...",
                        TotalBytes = 100,
                        DownloadedBytes = (long)libraryProgress,
                        CurrentFile = Path.GetFileName(libraryPath)
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"下载库文件失败 {artifact.Path}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 下载资源文件
        /// </summary>
        private static async Task DownloadAssetsAsync(
            AssetIndex assetIndex,
            string gameDirectory,
            IDownloadSourceService downloadSource,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            try
            {
                // 下载资源索引JSON
                var assetsIndexPath = Path.Combine(gameDirectory, "assets", "indexes", $"{assetIndex.Id}.json");
                Directory.CreateDirectory(Path.GetDirectoryName(assetsIndexPath)!);

                if (string.IsNullOrEmpty(assetIndex.Url))
                {
                    System.Diagnostics.Debug.WriteLine("资源索引URL为空，跳过下载");
                    return;
                }

                var assetIndexJson = await DownloadStringAsync(assetIndex.Url, cancellationToken);
                if (string.IsNullOrEmpty(assetIndexJson))
                {
                    System.Diagnostics.Debug.WriteLine("下载资源索引失败");
                    return;
                }

                await File.WriteAllTextAsync(assetsIndexPath, assetIndexJson, cancellationToken);
                System.Diagnostics.Debug.WriteLine($"✅ 资源索引已保存: {assetsIndexPath}");

                // 注意：这里不下载所有资源文件，因为文件太多
                // 游戏启动时会自动下载缺失的资源
                progress?.Report(new DownloadProgress
                {
                    Status = "资源索引已下载（资源文件将在首次启动时自动下载）",
                    TotalBytes = 100,
                    DownloadedBytes = 100
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"下载资源索引失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查库是否允许下载（根据规则）
        /// </summary>
        private static bool IsLibraryAllowed(Library library)
        {
            if (library.Rules == null || library.Rules.Count == 0)
                return true;

            foreach (var rule in library.Rules)
            {
                if (rule.Action == "allow")
                {
                    if (rule.Os?.Name == "windows" || rule.Os == null)
                        return true;
                }
                else if (rule.Action == "disallow")
                {
                    if (rule.Os?.Name == "windows")
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 下载文件
        /// </summary>
        private static async Task DownloadFileAsync(
            string url,
            string savePath,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"📥 开始下载: {url}");
                
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;

                System.Diagnostics.Debug.WriteLine($"   文件大小: {totalBytes / 1024.0 / 1024.0:F2} MB");

                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    downloadedBytes += bytesRead;

                    if (progress != null)
                    {
                        progress.Report(new DownloadProgress
                        {
                            TotalBytes = totalBytes,
                            DownloadedBytes = downloadedBytes,
                            CurrentFile = Path.GetFileName(savePath),
                            Status = $"正在下载 {Path.GetFileName(savePath)}..."
                        });
                    }
                }

                System.Diagnostics.Debug.WriteLine($"✅ 下载完成: {Path.GetFileName(savePath)} ({downloadedBytes / 1024.0 / 1024.0:F2} MB)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 下载失败: {url}");
                System.Diagnostics.Debug.WriteLine($"   错误: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 下载字符串内容
        /// </summary>
        private static async Task<string> DownloadStringAsync(string url, CancellationToken cancellationToken)
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        #region 数据模型

        private class VersionInfo
        {
            public DownloadsInfo? Downloads { get; set; }
            public List<Library>? Libraries { get; set; }
            public AssetIndex? AssetIndex { get; set; }
        }

        private class DownloadsInfo
        {
            public DownloadItem? Client { get; set; }
        }

        private class DownloadItem
        {
            public string? Url { get; set; }
            public long Size { get; set; }
        }

        private class Library
        {
            public LibraryDownloads? Downloads { get; set; }
            public List<Rule>? Rules { get; set; }
        }

        private class LibraryDownloads
        {
            public Artifact? Artifact { get; set; }
        }

        private class Artifact
        {
            public string? Path { get; set; }
            public string? Url { get; set; }
            public long Size { get; set; }
        }

        private class Rule
        {
            public string? Action { get; set; }
            public OsInfo? Os { get; set; }
        }

        private class OsInfo
        {
            public string? Name { get; set; }
        }

        private class AssetIndex
        {
            public string? Id { get; set; }
            public string? Url { get; set; }
        }

        #endregion
    }
}

