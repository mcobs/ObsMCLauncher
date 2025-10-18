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
        // 当前文件进度
        public long CurrentFileBytes { get; set; }
        public long CurrentFileTotalBytes { get; set; }
        public double CurrentFilePercentage => CurrentFileTotalBytes > 0 ? (CurrentFileBytes * 100.0 / CurrentFileTotalBytes) : 0;
        
        // 总体进度
        public long TotalDownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public double OverallPercentage => TotalBytes > 0 ? (TotalDownloadedBytes * 100.0 / TotalBytes) : 0;
        
        // 文件计数
        public int CompletedFiles { get; set; }
        public int TotalFiles { get; set; }
        
        // 其他信息
        public string CurrentFile { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public double DownloadSpeed { get; set; } // 字节/秒
        
        // 兼容性属性（保留旧代码兼容）
        [Obsolete("Use CurrentFileBytes instead")]
        public long DownloadedBytes 
        { 
            get => CurrentFileBytes; 
            set => CurrentFileBytes = value; 
        }
        
        [Obsolete("Use CurrentFilePercentage instead")]
        public double ProgressPercentage => CurrentFilePercentage;
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
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                
                // 配置 SSL/TLS 设置
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                
                // 证书验证设置
#if DEBUG
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                {
                    // 调试模式：记录但忽略证书错误
                    if (errors != System.Net.Security.SslPolicyErrors.None)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DownloadService] SSL证书警告: {errors}");
                    }
                    return true;
                },
#endif
                
                // 其他网络优化
                MaxConnectionsPerServer = 10,
                UseProxy = true,
                UseCookies = false,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(10)
            };

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ObsMCLauncher/1.0");
            _httpClient.DefaultRequestHeaders.ConnectionClose = false; // 保持连接以提高性能
            
            System.Diagnostics.Debug.WriteLine("[DownloadService] ✅ HttpClient 已配置 (TLS 1.2/1.3)");
        }

        /// <summary>
        /// 下载Minecraft版本
        /// </summary>
        /// <param name="versionId">版本ID（如 1.20.4）</param>
        /// <param name="gameDirectory">游戏目录</param>
        /// <param name="customVersionName">自定义版本名称（文件夹名），如果为空则使用versionId</param>
        /// <param name="progress">进度报告</param>
        /// <param name="cancellationToken">取消令牌</param>
        public static async Task<bool> DownloadMinecraftVersion(
            string versionId,
            string gameDirectory,
            string? customVersionName = null,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var downloadSource = DownloadSourceManager.Instance.CurrentService;
                
                // 使用自定义名称或默认版本ID
                var installName = string.IsNullOrEmpty(customVersionName) ? versionId : customVersionName;
                
                progress?.Report(new DownloadProgress 
                { 
                    Status = "正在获取版本信息...",
                    CurrentFile = versionId 
                });

                // 1. 下载版本JSON
                string versionJsonUrl;
                
                // 如果是Mojang源，需要先从version_manifest获取真实URL
                if (downloadSource is MojangAPIService)
                {
                    System.Diagnostics.Debug.WriteLine("使用Mojang官方源，先获取version_manifest...");
                    var manifest = await MinecraftVersionService.GetVersionListAsync();
                    if (manifest == null)
                    {
                        throw new Exception("获取版本清单失败");
                    }
                    
                    var manifestVersion = manifest.Versions.FirstOrDefault(v => v.Id == versionId);
                    if (manifestVersion == null || string.IsNullOrEmpty(manifestVersion.Url))
                    {
                        throw new Exception($"在版本清单中未找到版本 {versionId} 或URL为空");
                    }
                    
                    versionJsonUrl = manifestVersion.Url;
                    System.Diagnostics.Debug.WriteLine($"从version_manifest获取到真实URL: {versionJsonUrl}");
                }
                else
                {
                    // BMCLAPI等其他源直接使用固定模式
                    versionJsonUrl = downloadSource.GetVersionJsonUrl(versionId);
                }
                
                var versionJsonPath = Path.Combine(gameDirectory, "versions", installName, $"{installName}.json");
                
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

                // 计算总文件数和总大小
                var totalFiles = 1; // 客户端JAR
                var totalBytes = versionInfo.Downloads?.Client?.Size ?? 0;
                
                if (versionInfo.Libraries != null)
                {
                    foreach (var lib in versionInfo.Libraries)
                    {
                        if (IsLibraryAllowed(lib) && lib.Downloads?.Artifact != null)
                        {
                            totalFiles++;
                            totalBytes += lib.Downloads.Artifact.Size;
                        }
                    }
                }
                totalFiles++; // 资源索引

                var downloadState = new DownloadState
                {
                    CompletedFiles = 0,
                    TotalDownloadedBytes = 0
                };

                // 3. 下载客户端JAR
                var clientJarPath = Path.Combine(gameDirectory, "versions", installName, $"{installName}.jar");
                if (versionInfo.Downloads?.Client?.Url != null)
                {
                    // 根据下载源替换客户端JAR的URL
                    var clientJarUrl = versionInfo.Downloads.Client.Url;
                    if (downloadSource is BMCLAPIService)
                    {
                        // BMCLAPI的客户端JAR下载格式: https://bmclapi2.bangbang93.com/version/{version}/client
                        clientJarUrl = $"https://bmclapi2.bangbang93.com/version/{versionId}/client";
                        System.Diagnostics.Debug.WriteLine($"使用BMCLAPI镜像源下载客户端JAR");
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"开始下载客户端JAR: {clientJarUrl}");
                    System.Diagnostics.Debug.WriteLine($"保存路径: {clientJarPath}");

                    var clientSize = versionInfo.Downloads.Client.Size;
                    
                    await DownloadFileWithProgressAsync(
                        clientJarUrl,
                        clientJarPath,
                        (currentBytes, speed, actualTotalBytes) =>
                        {
                            // 如果预先知道的大小为0，使用实际下载时获取的大小
                            var effectiveClientSize = clientSize > 0 ? clientSize : actualTotalBytes;
                            var effectiveTotalBytes = totalBytes > 0 ? totalBytes : actualTotalBytes;
                            
                            progress?.Report(new DownloadProgress
                            {
                                Status = "正在下载客户端JAR...",
                                CurrentFile = $"{installName}.jar",
                                CurrentFileTotalBytes = effectiveClientSize,
                                CurrentFileBytes = currentBytes,
                                TotalBytes = effectiveTotalBytes,
                                TotalDownloadedBytes = downloadState.TotalDownloadedBytes + currentBytes,
                                CompletedFiles = downloadState.CompletedFiles,
                                TotalFiles = totalFiles,
                                DownloadSpeed = speed
                            });
                        },
                        cancellationToken);
                    
                    downloadState.CompletedFiles++;
                    downloadState.TotalDownloadedBytes += clientSize;
                    System.Diagnostics.Debug.WriteLine($"✅ 客户端JAR已下载: {clientJarPath}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 警告：客户端下载URL为空！");
                }

                // 4. 下载库文件
                if (versionInfo.Libraries != null && versionInfo.Libraries.Count > 0)
                {
                    await DownloadLibrariesWithProgressAsync(
                        versionInfo.Libraries,
                        gameDirectory,
                        downloadSource,
                        totalFiles,
                        totalBytes,
                        downloadState,
                        progress,
                        cancellationToken);
                }

                // 5. 下载资源文件（如果需要）
                if (versionInfo.AssetIndex != null)
                {
                    progress?.Report(new DownloadProgress
                    {
                        Status = "正在下载资源索引...",
                        CurrentFile = "assets",
                        CompletedFiles = downloadState.CompletedFiles,
                        TotalFiles = totalFiles,
                        TotalBytes = totalBytes,
                        TotalDownloadedBytes = downloadState.TotalDownloadedBytes
                    });

                    await DownloadAssetsAsync(
                        versionInfo.AssetIndex,
                        gameDirectory,
                        downloadSource,
                        null,
                        cancellationToken);
                    
                    downloadState.CompletedFiles++;
                }

                progress?.Report(new DownloadProgress
                {
                    Status = "下载完成！",
                    TotalBytes = totalBytes,
                    TotalDownloadedBytes = totalBytes,
                    CompletedFiles = totalFiles,
                    TotalFiles = totalFiles,
                    CurrentFileTotalBytes = 100,
                    CurrentFileBytes = 100
                });

                return true;
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 下载已取消");
                progress?.Report(new DownloadProgress
                {
                    Status = "下载已取消"
                });
                throw; // 重新抛出，让上层处理
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
                        CurrentFileTotalBytes = 100,
                        CurrentFileBytes = (long)libraryProgress,
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
                    CurrentFileTotalBytes = 100,
                    CurrentFileBytes = 100
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
        /// 下载文件并报告详细进度（包括速度）
        /// </summary>
        private static async Task DownloadFileWithProgressAsync(
            string url,
            string savePath,
            Action<long, double, long>? progressCallback,
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
                
                var startTime = DateTime.Now;
                var lastReportTime = startTime;
                var lastReportedBytes = 0L;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    downloadedBytes += bytesRead;

                    // 每100ms报告一次进度
                    var now = DateTime.Now;
                    if ((now - lastReportTime).TotalMilliseconds >= 100)
                    {
                        var elapsedSeconds = (now - lastReportTime).TotalSeconds;
                        var bytesInPeriod = downloadedBytes - lastReportedBytes;
                        var speed = elapsedSeconds > 0 ? bytesInPeriod / elapsedSeconds : 0;
                        
                        progressCallback?.Invoke(downloadedBytes, speed, totalBytes);
                        
                        lastReportTime = now;
                        lastReportedBytes = downloadedBytes;
                    }
                }

                // 最后再报告一次，确保100%
                progressCallback?.Invoke(downloadedBytes, 0, totalBytes);

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
        /// 下载文件（旧版本，保持兼容性）
        /// </summary>
        private static async Task DownloadFileAsync(
            string url,
            string savePath,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            await DownloadFileWithProgressAsync(url, savePath, 
                (bytes, speed, totalBytes) =>
                {
                    progress?.Report(new DownloadProgress
                    {
                        CurrentFileBytes = bytes,
                        CurrentFileTotalBytes = totalBytes,
                        CurrentFile = Path.GetFileName(savePath),
                        Status = $"正在下载 {Path.GetFileName(savePath)}...",
                        DownloadSpeed = speed
                    });
                },
                cancellationToken);
        }

        /// <summary>
        /// 下载进度状态
        /// </summary>
        private class DownloadState
        {
            public int CompletedFiles { get; set; }
            public long TotalDownloadedBytes { get; set; }
        }

        /// <summary>
        /// 下载库文件（带详细进度）
        /// </summary>
        private static async Task DownloadLibrariesWithProgressAsync(
            List<Library> libraries,
            string gameDirectory,
            IDownloadSourceService downloadSource,
            int totalFiles,
            long totalBytes,
            DownloadState state,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            var librariesPath = Path.Combine(gameDirectory, "libraries");
            Directory.CreateDirectory(librariesPath);

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
                        state.CompletedFiles++;
                        state.TotalDownloadedBytes += artifact.Size;
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

                    var libSize = artifact.Size;
                    var baseDownloadedBytes = state.TotalDownloadedBytes;

                    await DownloadFileWithProgressAsync(
                        url, 
                        libraryPath,
                        (currentBytes, speed, actualTotalBytes) =>
                        {
                            // 如果预先知道的大小为0，使用实际下载时获取的大小
                            var effectiveLibSize = libSize > 0 ? libSize : actualTotalBytes;
                            
                            progress?.Report(new DownloadProgress
                            {
                                Status = $"正在下载库文件 ({state.CompletedFiles + 1}/{totalFiles})...",
                                CurrentFile = Path.GetFileName(libraryPath),
                                CurrentFileTotalBytes = effectiveLibSize,
                                CurrentFileBytes = currentBytes,
                                TotalBytes = totalBytes,
                                TotalDownloadedBytes = baseDownloadedBytes + currentBytes,
                                CompletedFiles = state.CompletedFiles,
                                TotalFiles = totalFiles,
                                DownloadSpeed = speed
                            });
                        },
                        cancellationToken);

                    state.CompletedFiles++;
                    state.TotalDownloadedBytes += libSize;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"下载库文件失败 {artifact.Path}: {ex.Message}");
                }
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

