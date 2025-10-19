using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// Quilt版本信息
    /// </summary>
    public class QuiltVersion
    {
        [JsonPropertyName("separator")]
        public string? Separator { get; set; }

        [JsonPropertyName("build")]
        public int Build { get; set; }

        [JsonPropertyName("maven")]
        public string Maven { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName => $"Quilt Loader {Version}";
    }

    /// <summary>
    /// Quilt游戏版本信息
    /// </summary>
    public class QuiltGameVersion
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("stable")]
        public bool Stable { get; set; }
    }

    /// <summary>
    /// Quilt服务 - 处理Quilt版本查询和安装
    /// </summary>
    public class QuiltService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        // Quilt Meta API（官方）
        private const string QUILT_META_URL = "https://meta.quiltmc.org";
        
        // BMCLAPI镜像源
        private const string BMCL_QUILT_META_URL = "https://bmclapi2.bangbang93.com/quilt-meta";
        private const string BMCL_MAVEN_URL = "https://bmclapi2.bangbang93.com/maven";

        // 官方Maven仓库
        private const string QUILT_MAVEN_URL = "https://maven.quiltmc.org/repository/release";

        static QuiltService()
        {
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        /// <summary>
        /// 获取Quilt支持的Minecraft版本列表
        /// </summary>
        public static async Task<List<string>> GetSupportedMinecraftVersionsAsync()
        {
            try
            {
                var config = LauncherConfig.Load();
                Debug.WriteLine($"[QuiltService] 获取Quilt支持的MC版本列表... (源: {config.DownloadSource})");

                // 优先使用BMCLAPI，如果失败则使用官方源
                var urlsToTry = new List<string>();
                
                if (config.DownloadSource == DownloadSource.BMCLAPI)
                {
                    urlsToTry.Add($"{BMCL_QUILT_META_URL}/v3/versions/game");
                    urlsToTry.Add($"{QUILT_META_URL}/v3/versions/game"); // 备用
                }
                else
                {
                    urlsToTry.Add($"{QUILT_META_URL}/v3/versions/game");
                    urlsToTry.Add($"{BMCL_QUILT_META_URL}/v3/versions/game"); // 备用
                }

                Exception? lastException = null;
                foreach (var url in urlsToTry)
                {
                    try
                    {
                        Debug.WriteLine($"[QuiltService] 尝试从 {url} 获取版本列表");
                        var response = await _httpClient.GetAsync(url);
                        response.EnsureSuccessStatusCode();

                        var json = await response.Content.ReadAsStringAsync();
                        Debug.WriteLine($"[QuiltService] 收到的JSON数据长度: {json.Length}");
                        Debug.WriteLine($"[QuiltService] JSON前200字符: {(json.Length > 200 ? json.Substring(0, 200) : json)}");
                        
                        var versions = JsonSerializer.Deserialize<List<QuiltGameVersion>>(json);

                        if (versions != null)
                        {
                            var versionList = versions.Select(v => v.Version).ToList();
                            Debug.WriteLine($"[QuiltService] ✅ 获取到 {versionList.Count} 个支持的MC版本");
                            if (versionList.Count > 0)
                            {
                                Debug.WriteLine($"[QuiltService] 前5个版本: {string.Join(", ", versionList.Take(5))}");
                            }
                            return versionList;
                        }
                        else
                        {
                            Debug.WriteLine($"[QuiltService] ❌ 反序列化返回null");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[QuiltService] ❌ 从 {url} 获取失败: {ex.Message}");
                        lastException = ex;
                    }
                }

                // 所有URL都失败了
                if (lastException != null)
                {
                    Debug.WriteLine($"[QuiltService] ⚠️ 所有下载源都失败了，最后的错误: {lastException.Message}");
                }

                return new List<string>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[QuiltService] 获取Quilt支持版本失败: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// 解析Quilt版本号为可比较的数字（用于排序）
        /// </summary>
        /// <param name="versionString">版本字符串，如 "0.25.0" 或 "0.18.1-beta.25"</param>
        /// <returns>可比较的版本号</returns>
        private static double ParseVersionNumber(string versionString)
        {
            try
            {
                // 移除 -beta.xxx 或其他后缀部分
                var mainVersion = versionString.Split('-')[0];
                
                // 分割版本号部分：0.25.0 -> ["0", "25", "0"]
                var parts = mainVersion.Split('.');
                
                // 转换为可比较的数字
                double versionNumber = 0;
                if (parts.Length > 0 && int.TryParse(parts[0], out int major))
                    versionNumber += major;
                if (parts.Length > 1 && int.TryParse(parts[1], out int minor))
                    versionNumber += minor / 1000.0;
                if (parts.Length > 2 && int.TryParse(parts[2], out int patch))
                    versionNumber += patch / 1000000.0;
                
                return versionNumber;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 获取指定Minecraft版本的Quilt Loader版本列表
        /// </summary>
        public static async Task<List<QuiltVersion>> GetQuiltVersionsAsync(string mcVersion)
        {
            try
            {
                var config = LauncherConfig.Load();
                Debug.WriteLine($"[QuiltService] 获取 MC {mcVersion} 的Quilt版本列表... (源: {config.DownloadSource})");

                // 优先使用BMCLAPI，如果失败则使用官方源
                var urlsToTry = new List<string>();
                
                if (config.DownloadSource == DownloadSource.BMCLAPI)
                {
                    urlsToTry.Add($"{BMCL_QUILT_META_URL}/v3/versions/loader");
                    urlsToTry.Add($"{QUILT_META_URL}/v3/versions/loader"); // 备用
                }
                else
                {
                    urlsToTry.Add($"{QUILT_META_URL}/v3/versions/loader");
                    urlsToTry.Add($"{BMCL_QUILT_META_URL}/v3/versions/loader"); // 备用
                }

                Exception? lastException = null;
                foreach (var url in urlsToTry)
                {
                    try
                    {
                        Debug.WriteLine($"[QuiltService] 尝试从 {url} 获取Loader版本");
                        var response = await _httpClient.GetAsync(url);
                        response.EnsureSuccessStatusCode();

                        var json = await response.Content.ReadAsStringAsync();
                        Debug.WriteLine($"[QuiltService] 收到的JSON数据长度: {json.Length}");
                        Debug.WriteLine($"[QuiltService] JSON前200字符: {(json.Length > 200 ? json.Substring(0, 200) : json)}");
                        
                        var quiltVersions = JsonSerializer.Deserialize<List<QuiltVersion>>(json);

                        if (quiltVersions != null)
                        {
                            // 排序规则：版本号从高到低
                            quiltVersions = quiltVersions
                                .OrderByDescending(f => ParseVersionNumber(f.Version))
                                .ToList();
                            
                            Debug.WriteLine($"[QuiltService] ✅ 获取到 {quiltVersions.Count} 个Quilt Loader版本");
                            if (quiltVersions.Count > 0)
                            {
                                Debug.WriteLine($"[QuiltService] 前3个版本: {string.Join(", ", quiltVersions.Take(3).Select(v => v.Version))}");
                            }
                            return quiltVersions;
                        }
                        else
                        {
                            Debug.WriteLine($"[QuiltService] ❌ 反序列化返回null");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[QuiltService] ❌ 从 {url} 获取失败: {ex.Message}");
                        lastException = ex;
                    }
                }

                // 所有URL都失败了
                if (lastException != null)
                {
                    Debug.WriteLine($"[QuiltService] ⚠️ 所有下载源都失败了，最后的错误: {lastException.Message}");
                }

                return new List<QuiltVersion>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[QuiltService] 获取Quilt版本列表失败: {ex.Message}");
                return new List<QuiltVersion>();
            }
        }

        /// <summary>
        /// 下载并安装Quilt
        /// </summary>
        /// <param name="mcVersion">Minecraft版本</param>
        /// <param name="loaderVersion">Quilt Loader版本</param>
        /// <param name="gameDirectory">游戏目录</param>
        /// <param name="customVersionName">自定义版本名称</param>
        /// <param name="progressCallback">进度回调（状态消息, 当前字节数, 速度, 总字节数）</param>
        /// <param name="cancellationToken">取消令牌</param>
        public static async Task<bool> InstallQuiltAsync(
            string mcVersion,
            string loaderVersion,
            string gameDirectory,
            string customVersionName,
            Action<string, long, double, long>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var config = LauncherConfig.Load();
                Debug.WriteLine($"[QuiltService] 开始安装Quilt: MC {mcVersion}, Loader {loaderVersion}");

                progressCallback?.Invoke("正在获取Quilt配置文件...", 0, 0, 100);

                // 1. 获取Quilt profile JSON（尝试多个URL）
                var urlsToTry = new List<string>();
                
                if (config.DownloadSource == DownloadSource.BMCLAPI)
                {
                    urlsToTry.Add($"{BMCL_QUILT_META_URL}/v3/versions/loader/{mcVersion}/{loaderVersion}/profile/json");
                    urlsToTry.Add($"{QUILT_META_URL}/v3/versions/loader/{mcVersion}/{loaderVersion}/profile/json"); // 备用
                }
                else
                {
                    urlsToTry.Add($"{QUILT_META_URL}/v3/versions/loader/{mcVersion}/{loaderVersion}/profile/json");
                    urlsToTry.Add($"{BMCL_QUILT_META_URL}/v3/versions/loader/{mcVersion}/{loaderVersion}/profile/json"); // 备用
                }

                string? profileJson = null;
                Exception? lastException = null;

                foreach (var profileUrl in urlsToTry)
                {
                    try
                    {
                        Debug.WriteLine($"[QuiltService] 尝试获取Quilt配置: {profileUrl}");
                        profileJson = await _httpClient.GetStringAsync(profileUrl, cancellationToken);
                        
                        if (!string.IsNullOrEmpty(profileJson))
                        {
                            Debug.WriteLine($"[QuiltService] ✅ 成功获取Quilt配置");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[QuiltService] ❌ 获取配置失败: {ex.Message}");
                        lastException = ex;
                    }
                }

                if (string.IsNullOrEmpty(profileJson))
                {
                    throw new Exception($"下载Quilt配置文件失败: {lastException?.Message ?? "未知错误"}");
                }

                // 2. 创建Quilt版本目录
                var quiltVersionPath = Path.Combine(gameDirectory, "versions", customVersionName);
                Directory.CreateDirectory(quiltVersionPath);

                // 3. 下载原版文件到Quilt目录
                var quiltJarPath = Path.Combine(quiltVersionPath, $"{customVersionName}.jar");
                var quiltJsonPath = Path.Combine(quiltVersionPath, $"{customVersionName}.json");
                var vanillaJsonPath = Path.Combine(quiltVersionPath, $"{mcVersion}.json");

                progressCallback?.Invoke($"正在下载基础版本 {mcVersion}...", 0, 0, 100);
                Debug.WriteLine($"[QuiltService] 开始下载基础MC版本到Quilt目录");

                // 下载基础MC版本到Quilt目录
                var downloadProgressReporter = new System.Progress<ObsMCLauncher.Services.DownloadProgress>(p =>
                {
                    progressCallback?.Invoke(
                        p.Status,
                        p.TotalDownloadedBytes,
                        p.DownloadSpeed,
                        p.TotalBytes
                    );
                });

                var downloadSuccess = await DownloadService.DownloadMinecraftVersion(
                    mcVersion,
                    gameDirectory,
                    customVersionName, // 直接下载到Quilt版本目录
                    downloadProgressReporter,
                    cancellationToken
                );

                if (!downloadSuccess)
                {
                    throw new Exception($"下载基础版本 {mcVersion} 失败");
                }

                Debug.WriteLine($"[QuiltService] 基础MC版本已下载到Quilt目录");

                progressCallback?.Invoke("正在安装Quilt配置文件...", 50, 0, 100);

                // 4. 备份原版JSON
                if (File.Exists(quiltJsonPath))
                {
                    File.Copy(quiltJsonPath, vanillaJsonPath, true);
                    Debug.WriteLine($"[QuiltService] 已备份原版JSON到: {vanillaJsonPath}");
                }

                // 5. 修改Quilt profile JSON
                var profileObj = JsonSerializer.Deserialize<JsonElement>(profileJson);
                var modifiedProfile = ModifyQuiltProfile(profileObj, customVersionName, mcVersion, vanillaJsonPath);

                // 6. 保存Quilt版本JSON
                await File.WriteAllTextAsync(quiltJsonPath, modifiedProfile, cancellationToken);
                Debug.WriteLine($"[QuiltService] Quilt配置文件已保存: {quiltJsonPath}");

                // 7. 下载Quilt库文件
                progressCallback?.Invoke("正在下载Quilt库文件...", 70, 0, 100);
                await DownloadQuiltLibrariesAsync(
                    modifiedProfile,
                    gameDirectory,
                    config.DownloadSource,
                    progressCallback,
                    cancellationToken
                );

                progressCallback?.Invoke("Quilt安装完成！", 100, 0, 100);
                Debug.WriteLine($"[QuiltService] Quilt安装完成: {customVersionName}");

                return true;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[QuiltService] Quilt安装已取消");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[QuiltService] Quilt安装失败: {ex.Message}");
                Debug.WriteLine($"[QuiltService] 堆栈跟踪: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// 修改Quilt profile JSON
        /// </summary>
        private static string ModifyQuiltProfile(JsonElement profileObj, string customVersionName, string mcVersion, string mcJsonPath)
        {
            try
            {
                using var doc = JsonDocument.Parse(profileObj.GetRawText());
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    
                    foreach (var property in doc.RootElement.EnumerateObject())
                    {
                        if (property.Name == "id")
                        {
                            writer.WriteString("id", customVersionName);
                        }
                        else if (property.Name == "inheritsFrom")
                        {
                            writer.WriteString("inheritsFrom", mcVersion);
                        }
                        else if (property.Name == "releaseTime" || property.Name == "time")
                        {
                            try
                            {
                                writer.WritePropertyName(property.Name);
                                property.Value.WriteTo(writer);
                            }
                            catch
                            {
                                writer.WriteString(property.Name, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00"));
                            }
                        }
                        else
                        {
                            writer.WritePropertyName(property.Name);
                            property.Value.WriteTo(writer);
                        }
                    }
                    
                    // 如果没有必要的字段，从基础MC版本JSON中获取
                    if (!doc.RootElement.TryGetProperty("releaseTime", out _) || 
                        !doc.RootElement.TryGetProperty("time", out _) ||
                        !doc.RootElement.TryGetProperty("assetIndex", out _))
                    {
                        try
                        {
                            if (File.Exists(mcJsonPath))
                            {
                                var mcJson = File.ReadAllText(mcJsonPath);
                                using var mcDoc = JsonDocument.Parse(mcJson);
                                
                                if (!doc.RootElement.TryGetProperty("releaseTime", out _))
                                {
                                    if (mcDoc.RootElement.TryGetProperty("releaseTime", out var releaseTime))
                                    {
                                        writer.WriteString("releaseTime", releaseTime.GetString());
                                    }
                                    else
                                    {
                                        writer.WriteString("releaseTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00"));
                                    }
                                }
                                
                                if (!doc.RootElement.TryGetProperty("time", out _))
                                {
                                    if (mcDoc.RootElement.TryGetProperty("time", out var time))
                                    {
                                        writer.WriteString("time", time.GetString());
                                    }
                                    else
                                    {
                                        writer.WriteString("time", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00"));
                                    }
                                }
                                
                                // 复制assetIndex
                                if (!doc.RootElement.TryGetProperty("assetIndex", out _))
                                {
                                    if (mcDoc.RootElement.TryGetProperty("assetIndex", out var assetIndex))
                                    {
                                        writer.WritePropertyName("assetIndex");
                                        assetIndex.WriteTo(writer);
                                        Debug.WriteLine($"[QuiltService] ✅ 已从原版JSON复制assetIndex");
                                    }
                                }
                            }
                            else
                            {
                                if (!doc.RootElement.TryGetProperty("releaseTime", out _))
                                {
                                    writer.WriteString("releaseTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00"));
                                }
                                if (!doc.RootElement.TryGetProperty("time", out _))
                                {
                                    writer.WriteString("time", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00"));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[QuiltService] 获取时间信息失败: {ex.Message}");
                            writer.WriteString("releaseTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00"));
                        }
                    }
                    
                    writer.WriteEndObject();
                }
                
                return System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[QuiltService] 修改Quilt profile失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 下载Quilt库文件
        /// </summary>
        private static async Task DownloadQuiltLibrariesAsync(
            string profileJson,
            string gameDirectory,
            DownloadSource downloadSource,
            Action<string, long, double, long>? progressCallback,
            CancellationToken cancellationToken)
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var profile = JsonSerializer.Deserialize<QuiltProfileInfo>(profileJson, options);

                if (profile?.Libraries == null || profile.Libraries.Count == 0)
                {
                    Debug.WriteLine("[QuiltService] 没有需要下载的库文件");
                    return;
                }

                var librariesPath = Path.Combine(gameDirectory, "libraries");
                Directory.CreateDirectory(librariesPath);

                int totalLibs = profile.Libraries.Count;
                int downloadedLibs = 0;

                foreach (var library in profile.Libraries)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        string? libPath = null;
                        string? libUrl = null;

                        if (library.Downloads?.Artifact != null)
                        {
                            libPath = library.Downloads.Artifact.Path;
                            libUrl = library.Downloads.Artifact.Url;
                        }
                        else if (!string.IsNullOrEmpty(library.Name))
                        {
                            // 从Maven坐标生成路径
                            libPath = MavenToPath(library.Name);
                            
                            // 构造URL - 优先使用BMCLAPI
                            var mavenBaseUrl = downloadSource == DownloadSource.BMCLAPI ? BMCL_MAVEN_URL : QUILT_MAVEN_URL;
                            libUrl = $"{mavenBaseUrl}/{libPath}";
                        }

                        if (string.IsNullOrEmpty(libPath) || string.IsNullOrEmpty(libUrl))
                        {
                            Debug.WriteLine($"[QuiltService] 跳过无效库: {library.Name}");
                            continue;
                        }

                        var localPath = Path.Combine(librariesPath, libPath.Replace('/', Path.DirectorySeparatorChar));

                        // 如果文件已存在且有效，跳过
                        if (File.Exists(localPath))
                        {
                            var fileInfo = new FileInfo(localPath);
                            if (fileInfo.Length > 0)
                            {
                                downloadedLibs++;
                                continue;
                            }
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

                        // 下载库文件
                        progressCallback?.Invoke(
                            $"正在下载库文件 ({downloadedLibs + 1}/{totalLibs})...",
                            downloadedLibs,
                            0,
                            totalLibs
                        );

                        // 尝试多个可能的URL
                        var urlsToTry = new List<string> { libUrl };
                        
                        // 如果是BMCLAPI源，添加官方源作为备用
                        if (downloadSource == DownloadSource.BMCLAPI)
                        {
                            urlsToTry.Add($"{QUILT_MAVEN_URL}/{libPath}");
                        }
                        else
                        {
                            // 如果是官方源，添加BMCLAPI作为备用
                            urlsToTry.Add($"{BMCL_MAVEN_URL}/{libPath}");
                        }

                        bool downloaded = false;
                        foreach (var url in urlsToTry)
                        {
                            try
                            {
                                await DownloadFileAsync(url, localPath, cancellationToken);
                                downloaded = true;
                                break;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[QuiltService] 从 {url} 下载失败: {ex.Message}");
                            }
                        }

                        if (downloaded)
                        {
                            downloadedLibs++;
                            Debug.WriteLine($"[QuiltService] 已下载库: {Path.GetFileName(localPath)} ({downloadedLibs}/{totalLibs})");
                        }
                        else
                        {
                            Debug.WriteLine($"[QuiltService] ⚠️ 所有源都无法下载: {library.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[QuiltService] 下载库文件失败: {library.Name} - {ex.Message}");
                    }
                }

                Debug.WriteLine($"[QuiltService] 库文件下载完成: {downloadedLibs}/{totalLibs}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[QuiltService] 下载Quilt库文件失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 下载文件
        /// </summary>
        private static async Task DownloadFileAsync(string url, string savePath, CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            await contentStream.CopyToAsync(fileStream, 8192, cancellationToken);
        }

        /// <summary>
        /// 将Maven坐标转换为路径
        /// 例如: org.quiltmc:quilt-loader:0.25.0 => org/quiltmc/quilt-loader/0.25.0/quilt-loader-0.25.0.jar
        /// </summary>
        private static string MavenToPath(string maven)
        {
            var parts = maven.Split(':');
            if (parts.Length < 3) return "";

            var group = parts[0].Replace('.', '/');
            var artifact = parts[1];
            var version = parts[2];
            var classifier = parts.Length > 3 ? $"-{parts[3]}" : "";
            var extension = parts.Length > 4 ? parts[4] : "jar";

            return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.{extension}";
        }

        #region 数据模型

        private class QuiltProfileInfo
        {
            public List<QuiltLibrary>? Libraries { get; set; }
        }

        private class QuiltLibrary
        {
            public string? Name { get; set; }
            public string? Url { get; set; }
            public QuiltLibraryDownloads? Downloads { get; set; }
        }

        private class QuiltLibraryDownloads
        {
            public QuiltArtifact? Artifact { get; set; }
        }

        private class QuiltArtifact
        {
            public string? Path { get; set; }
            public string? Url { get; set; }
            public string? Sha1 { get; set; }
            public long Size { get; set; }
        }

        #endregion
    }
}

