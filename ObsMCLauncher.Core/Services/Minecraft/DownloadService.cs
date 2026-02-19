using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services.Minecraft
{
    public class DownloadProgress
    {
        public long CurrentFileBytes { get; set; }
        public long CurrentFileTotalBytes { get; set; }
        public double CurrentFilePercentage => CurrentFileTotalBytes > 0 ? (CurrentFileBytes * 100.0 / CurrentFileTotalBytes) : 0;
        
        public long TotalDownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public double OverallPercentage => TotalBytes > 0 ? (TotalDownloadedBytes * 100.0 / TotalBytes) : 0;
        
        public int CompletedFiles { get; set; }
        public int TotalFiles { get; set; }
        
        public string CurrentFile { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public double DownloadSpeed { get; set; }
        
        [Obsolete("Use CurrentFileBytes instead")]
        public long DownloadedBytes 
        { 
            get => CurrentFileBytes; 
            set => CurrentFileBytes = value; 
        }
        
        [Obsolete("Use CurrentFilePercentage instead")]
        public double ProgressPercentage => CurrentFilePercentage;
    }

    public class DownloadService
    {
        private static readonly HttpClient _httpClient;

        static DownloadService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                
#if DEBUG
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                {
                    if (errors != System.Net.Security.SslPolicyErrors.None)
                    {
                        DebugLogger.Warn("Download", $"SSL证书警告: {errors}");
                    }
                    return true;
                },
#endif
                
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
            _httpClient.DefaultRequestHeaders.ConnectionClose = false;
            
            DebugLogger.Info("Download", "HttpClient 已配置 (TLS 1.2/1.3)");
        }

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
                
                var installName = string.IsNullOrEmpty(customVersionName) ? versionId : customVersionName;
                
                progress?.Report(new DownloadProgress 
                { 
                    Status = "正在获取版本信息...",
                    CurrentFile = versionId 
                });

                string versionJsonUrl;
                
                if (downloadSource is MojangAPIService)
                {
                    DebugLogger.Info("Download", "使用Mojang官方源，先获取version_manifest...");
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
                    DebugLogger.Info("Download", $"从version_manifest获取到真实URL: {versionJsonUrl}");
                }
                else
                {
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
                DebugLogger.Info("Download", $"版本JSON已保存: {versionJsonPath}");

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var versionInfo = JsonSerializer.Deserialize<VersionInfo>(versionJson, options);
                if (versionInfo == null)
                {
                    throw new Exception("解析版本JSON失败");
                }

                DebugLogger.Info("Download", $"解析版本信息成功");
                DebugLogger.Info("Download", $"客户端URL: {versionInfo.Downloads?.Client?.Url}");
                DebugLogger.Info("Download", $"库文件数量: {versionInfo.Libraries?.Count ?? 0}");

                var totalFiles = 1;
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
                totalFiles++;

                var downloadState = new DownloadState
                {
                    CompletedFiles = 0,
                    TotalDownloadedBytes = 0
                };

                var clientJarPath = Path.Combine(gameDirectory, "versions", installName, $"{installName}.jar");
                if (versionInfo.Downloads?.Client?.Url != null)
                {
                    var clientJarUrl = versionInfo.Downloads.Client.Url;
                    if (downloadSource is BMCLAPIService)
                    {
                        clientJarUrl = $"https://bmclapi2.bangbang93.com/version/{versionId}/client";
                        DebugLogger.Info("Download", "使用BMCLAPI镜像源下载客户端JAR");
                    }
                    
                    DebugLogger.Info("Download", $"开始下载客户端JAR: {clientJarUrl}");
                    DebugLogger.Info("Download", $"保存路径: {clientJarPath}");

                    var clientSize = versionInfo.Downloads.Client.Size;
                    
                    await DownloadFileWithProgressAsync(
                        clientJarUrl,
                        clientJarPath,
                        (currentBytes, speed, actualTotalBytes) =>
                        {
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
                    DebugLogger.Info("Download", $"客户端JAR已下载: {clientJarPath}");
                }
                else
                {
                    DebugLogger.Warn("Download", "客户端下载URL为空！");
                }

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
                DebugLogger.Warn("Download", "下载已取消");
                progress?.Report(new DownloadProgress
                {
                    Status = "下载已取消"
                });
                throw;
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Download", $"下载版本失败: {ex.Message}");
                progress?.Report(new DownloadProgress
                {
                    Status = $"下载失败: {ex.Message}"
                });
                return false;
            }
        }

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

                if (!IsLibraryAllowed(library)) continue;

                var artifact = library.Downloads?.Artifact;
                if (artifact == null || string.IsNullOrEmpty(artifact.Path)) continue;

                var libraryPath = Path.Combine(librariesPath, artifact.Path.Replace('/', Path.DirectorySeparatorChar));
                
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
                    DebugLogger.Error("Download", $"下载库文件失败 {artifact.Path}: {ex.Message}");
                }
            }
        }

        private static async Task DownloadAssetsAsync(
            AssetIndex assetIndex,
            string gameDirectory,
            IDownloadSourceService downloadSource,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            try
            {
                var assetsIndexPath = Path.Combine(gameDirectory, "assets", "indexes", $"{assetIndex.Id}.json");
                Directory.CreateDirectory(Path.GetDirectoryName(assetsIndexPath)!);

                if (string.IsNullOrEmpty(assetIndex.Url))
                {
                    DebugLogger.Warn("Download", "资源索引URL为空，跳过下载");
                    return;
                }

                var assetIndexJson = await DownloadStringAsync(assetIndex.Url, cancellationToken);
                if (string.IsNullOrEmpty(assetIndexJson))
                {
                    DebugLogger.Warn("Download", "下载资源索引失败");
                    return;
                }

                await File.WriteAllTextAsync(assetsIndexPath, assetIndexJson, cancellationToken);
                DebugLogger.Info("Download", $"资源索引已保存: {assetsIndexPath}");

                progress?.Report(new DownloadProgress
                {
                    Status = "资源索引已下载（资源文件将在首次启动时自动下载）",
                    CurrentFileTotalBytes = 100,
                    CurrentFileBytes = 100
                });
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Download", $"下载资源索引失败: {ex.Message}");
            }
        }

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

        private static async Task DownloadFileWithProgressAsync(
            string url,
            string savePath,
            Action<long, double, long>? progressCallback,
            CancellationToken cancellationToken)
        {
            try
            {
                DebugLogger.Info("Download", $"开始下载: {url}");
                
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;

                DebugLogger.Info("Download", $"文件大小: {totalBytes / 1024.0 / 1024.0:F2} MB");

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

                progressCallback?.Invoke(downloadedBytes, 0, totalBytes);

                DebugLogger.Info("Download", $"下载完成: {Path.GetFileName(savePath)} ({downloadedBytes / 1024.0 / 1024.0:F2} MB)");
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Download", $"下载失败: {url}");
                DebugLogger.Error("Download", $"错误: {ex.Message}");
                throw;
            }
        }

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

        private class DownloadState
        {
            public int CompletedFiles { get; set; }
            public long TotalDownloadedBytes { get; set; }
        }

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

            var config = LauncherConfig.Load();
            var maxConcurrent = Math.Max(1, config.MaxDownloadThreads);
            DebugLogger.Info("Download", $"使用 {maxConcurrent} 个并发线程下载库文件");

            var downloadTasks = new List<Task>();
            var semaphore = new System.Threading.SemaphoreSlim(maxConcurrent, maxConcurrent);
            var lockObject = new object();

            foreach (var library in libraries)
            {
                if (cancellationToken.IsCancellationRequested) break;

                if (!IsLibraryAllowed(library)) continue;

                var artifact = library.Downloads?.Artifact;
                if (artifact == null || string.IsNullOrEmpty(artifact.Path)) continue;

                var libraryPath = Path.Combine(librariesPath, artifact.Path.Replace('/', Path.DirectorySeparatorChar));
                
                if (File.Exists(libraryPath))
                {
                    var fileInfo = new FileInfo(libraryPath);
                    if (fileInfo.Length == artifact.Size)
                    {
                        lock (lockObject)
                        {
                            state.CompletedFiles++;
                            state.TotalDownloadedBytes += artifact.Size;
                        }
                        continue;
                    }
                }

                var downloadTask = Task.Run(async () =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);

                        var url = artifact.Url;
                        if (string.IsNullOrEmpty(url))
                        {
                            url = downloadSource.GetLibraryUrl(artifact.Path);
                        }

                        var libSize = artifact.Size;

                        await DownloadFileWithProgressAsync(
                            url, 
                            libraryPath,
                            (currentBytes, speed, actualTotalBytes) =>
                            {
                                var effectiveLibSize = libSize > 0 ? libSize : actualTotalBytes;
                                
                                long currentTotalDownloaded;
                                int currentCompletedFiles;
                                lock (lockObject)
                                {
                                    currentTotalDownloaded = state.TotalDownloadedBytes;
                                    currentCompletedFiles = state.CompletedFiles;
                                }
                                
                                progress?.Report(new DownloadProgress
                                {
                                    Status = $"正在下载库文件 ({currentCompletedFiles + 1}/{totalFiles})...",
                                    CurrentFile = Path.GetFileName(libraryPath),
                                    CurrentFileTotalBytes = effectiveLibSize,
                                    CurrentFileBytes = currentBytes,
                                    TotalBytes = totalBytes,
                                    TotalDownloadedBytes = currentTotalDownloaded + currentBytes,
                                    CompletedFiles = currentCompletedFiles,
                                    TotalFiles = totalFiles,
                                    DownloadSpeed = speed
                                });
                            },
                            cancellationToken);

                        lock (lockObject)
                        {
                            state.CompletedFiles++;
                            state.TotalDownloadedBytes += libSize;
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Error("Download", $"下载库文件失败 {artifact.Path}: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);

                downloadTasks.Add(downloadTask);
            }

            await Task.WhenAll(downloadTasks);
        }

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
