using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// NeoForge版本信息
    /// </summary>
    public class NeoForgeVersion
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        /// <summary>
        /// 完整版本号 (例如: 20.2.88 for MC 1.20.2)
        /// </summary>
        public string FullVersion => Version;

        /// <summary>
        /// 显示名称（只显示版本号，不含"NeoForge"前缀）
        /// </summary>
        public string DisplayName => Version;
        
        /// <summary>
        /// Minecraft版本（从NeoForge版本号推断）
        /// 例如: 20.2.88 -> 1.20.2, 21.1.176 -> 1.21.1
        /// </summary>
        public string MinecraftVersion
        {
            get
            {
                if (string.IsNullOrEmpty(Version)) return "";
                var parts = Version.Split('.');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int major) && int.TryParse(parts[1], out int minor))
                {
                    return $"1.{major}.{minor}";
                }
                return "";
            }
        }
    }

    /// <summary>
    /// NeoForge服务 - 处理NeoForge版本查询和下载
    /// </summary>
    public class NeoForgeService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        
        // BMCLAPI镜像源
        private const string BMCL_NEOFORGE_LIST = "https://bmclapi2.bangbang93.com/neoforge/list/{0}";
        private const string BMCL_NEOFORGE_MAVEN = "https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/{0}/neoforge-{0}-installer.jar";
        
        // 官方源
        private const string OFFICIAL_NEOFORGE_MAVEN_METADATA = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
        private const string OFFICIAL_NEOFORGE_MAVEN = "https://maven.neoforged.net/releases/net/neoforged/neoforge/{0}/neoforge-{0}-installer.jar";

        /// <summary>
        /// 获取NeoForge支持的Minecraft版本列表
        /// </summary>
        public static async Task<List<string>> GetSupportedMinecraftVersionsAsync()
        {
            try
            {
                Debug.WriteLine($"[NeoForgeService] 获取NeoForge支持的MC版本列表...");
                
                // 从Maven元数据解析所有版本，提取MC版本
                var response = await _httpClient.GetAsync(OFFICIAL_NEOFORGE_MAVEN_METADATA);
                response.EnsureSuccessStatusCode();
                
                var xml = await response.Content.ReadAsStringAsync();
                var versions = ParseNeoForgeMavenMetadata(xml);
                
                // 提取所有唯一的MC版本
                var mcVersions = versions
                    .Select(v => v.MinecraftVersion)
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Distinct()
                    .OrderByDescending(v => v)
                    .ToList();
                
                Debug.WriteLine($"[NeoForgeService] 获取到 {mcVersions.Count} 个支持的MC版本");
                return mcVersions;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NeoForgeService] 获取NeoForge支持版本失败: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// 获取指定Minecraft版本的NeoForge版本列表
        /// </summary>
        public static async Task<List<NeoForgeVersion>> GetNeoForgeVersionsAsync(string mcVersion)
        {
            var config = LauncherConfig.Load();
            Debug.WriteLine($"[NeoForgeService] 获取 MC {mcVersion} 的NeoForge版本列表... (源: {config.DownloadSource})");
            
            // 如果首选源是 BMCLAPI，先尝试 BMCLAPI
            if (config.DownloadSource == DownloadSource.BMCLAPI)
            {
                try
                {
                    var url = string.Format(BMCL_NEOFORGE_LIST, mcVersion);
                    Debug.WriteLine($"[NeoForgeService] 尝试从BMCLAPI获取: {url}");
                    
                    var response = await _httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    var neoforgeList = JsonSerializer.Deserialize<List<NeoForgeVersion>>(json);

                    if (neoforgeList != null && neoforgeList.Count > 0)
                    {
                        // 按版本号降序排序（最新的在前）
                        neoforgeList = neoforgeList
                            .OrderByDescending(v => ParseVersionNumber(v.Version))
                            .ToList();
                        Debug.WriteLine($"[NeoForgeService] 从BMCLAPI成功获取到 {neoforgeList.Count} 个NeoForge版本");
                        return neoforgeList;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NeoForgeService] BMCLAPI获取失败: {ex.Message}，将尝试官方源");
                }
            }
            
            // 使用官方源（作为备用或首选）
            try
            {
                Debug.WriteLine($"[NeoForgeService] 尝试从官方源获取: {OFFICIAL_NEOFORGE_MAVEN_METADATA}");
                var response = await _httpClient.GetAsync(OFFICIAL_NEOFORGE_MAVEN_METADATA);
                response.EnsureSuccessStatusCode();
                
                var xml = await response.Content.ReadAsStringAsync();
                var allVersions = ParseNeoForgeMavenMetadata(xml);
                
                // 筛选出匹配的MC版本
                var neoforgeList = allVersions
                    .Where(v => v.MinecraftVersion == mcVersion)
                    .OrderByDescending(v => ParseVersionNumber(v.Version))
                    .ToList();
                
                Debug.WriteLine($"[NeoForgeService] 从官方源成功获取到 {neoforgeList.Count} 个NeoForge版本");
                return neoforgeList;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NeoForgeService] 官方源获取失败: {ex.Message}");
                return new List<NeoForgeVersion>();
            }
        }

        /// <summary>
        /// 解析NeoForge Maven元数据XML
        /// </summary>
        private static List<NeoForgeVersion> ParseNeoForgeMavenMetadata(string xml)
        {
            var versionList = new List<NeoForgeVersion>();
            
            try
            {
                // 简单的XML解析，提取<version>标签
                var lines = xml.Split('\n');
                
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("<version>") && trimmed.EndsWith("</version>"))
                    {
                        var versionText = trimmed.Replace("<version>", "").Replace("</version>", "");
                        
                        // NeoForge版本格式: 20.2.88, 21.1.176, etc.
                        // 跳过beta版本（可选）
                        versionList.Add(new NeoForgeVersion
                        {
                            Version = versionText
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NeoForgeService] 解析Maven元数据失败: {ex.Message}");
            }
            
            return versionList;
        }

        /// <summary>
        /// 解析版本号为可比较的数字
        /// </summary>
        private static double ParseVersionNumber(string versionString)
        {
            try
            {
                // 移除 -beta 等后缀
                var mainVersion = versionString.Split('-')[0];
                
                // 分割版本号部分：21.1.176 -> ["21", "1", "176"]
                var parts = mainVersion.Split('.');
                
                // 转换为可比较的数字：21.001176
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
        /// 下载NeoForge安装器
        /// </summary>
        /// <param name="neoforgeVersion">NeoForge版本 (例如: 21.1.176)</param>
        /// <param name="savePath">保存路径</param>
        /// <param name="progressCallback">进度回调（当前字节数, 速度, 总字节数）</param>
        /// <param name="cancellationToken">取消令牌</param>
        public static async Task<bool> DownloadNeoForgeInstallerWithDetailsAsync(
            string neoforgeVersion,
            string savePath,
            Action<long, double, long>? progressCallback = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                var config = LauncherConfig.Load();
                Debug.WriteLine($"[NeoForgeService] 开始下载NeoForge安装器: {neoforgeVersion} (源: {config.DownloadSource})");
                
                // 准备下载URL
                var urlsToTry = new List<string>();
                
                if (config.DownloadSource == DownloadSource.BMCLAPI)
                {
                    // 使用BMCLAPI镜像源
                    urlsToTry.Add(string.Format(BMCL_NEOFORGE_MAVEN, neoforgeVersion));
                    // 添加官方源作为备用
                    urlsToTry.Add(string.Format(OFFICIAL_NEOFORGE_MAVEN, neoforgeVersion));
                }
                else
                {
                    // 使用官方源
                    urlsToTry.Add(string.Format(OFFICIAL_NEOFORGE_MAVEN, neoforgeVersion));
                    // 添加BMCLAPI作为备用
                    urlsToTry.Add(string.Format(BMCL_NEOFORGE_MAVEN, neoforgeVersion));
                }
                
                // 确保目录存在
                var directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                HttpResponseMessage? response = null;
                
                // 尝试所有可能的URL
                foreach (var url in urlsToTry)
                {
                    try
                    {
                        Debug.WriteLine($"[NeoForgeService] 尝试下载URL: {url}");
                        response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        response.EnsureSuccessStatusCode();
                        Debug.WriteLine($"[NeoForgeService] 成功找到NeoForge安装器: {url}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[NeoForgeService] URL失败: {url} - {ex.Message}");
                        response?.Dispose();
                        response = null;
                    }
                }
                
                if (response == null || !response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[NeoForgeService] 所有URL都无法下载NeoForge安装器");
                    return false;
                }

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;
                
                // 用于计算下载速度
                var startTime = DateTime.Now;
                var lastReportTime = startTime;
                var lastReportedBytes = 0L;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalRead += bytesRead;

                    // 每100ms报告一次进度
                    var now = DateTime.Now;
                    if ((now - lastReportTime).TotalMilliseconds >= 100 || totalRead == totalBytes)
                    {
                        var elapsedSeconds = (now - lastReportTime).TotalSeconds;
                        var bytesInPeriod = totalRead - lastReportedBytes;
                        var speed = elapsedSeconds > 0 ? bytesInPeriod / elapsedSeconds : 0;
                        
                        progressCallback?.Invoke(totalRead, speed, totalBytes);
                        
                        lastReportTime = now;
                        lastReportedBytes = totalRead;
                    }
                }
                
                // 最后再报告一次
                progressCallback?.Invoke(totalRead, 0, totalBytes);

                Debug.WriteLine($"[NeoForgeService] NeoForge安装器下载完成: {savePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NeoForgeService] 下载NeoForge安装器失败: {ex.Message}");
                return false;
            }
        }
    }
}

