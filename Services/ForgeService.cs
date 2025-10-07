using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// Forge版本信息
    /// </summary>
    public class ForgeVersion
    {
        [JsonPropertyName("build")]
        public int Build { get; set; }

        [JsonPropertyName("mcversion")]
        public string McVersion { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("modified")]
        public string Modified { get; set; } = "";

        /// <summary>
        /// 完整版本号 (例如: 1.20.1-47.2.0)
        /// </summary>
        public string FullVersion => $"{McVersion}-{Version}";

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName => $"Forge {Version}";
    }

    /// <summary>
    /// Forge版本列表响应
    /// </summary>
    public class ForgeListResponse
    {
        [JsonPropertyName("mcversion")]
        public string McVersion { get; set; } = "";

        [JsonPropertyName("builds")]
        public List<ForgeVersion> Builds { get; set; } = new();
    }

    /// <summary>
    /// Forge支持的Minecraft版本
    /// </summary>
    public class ForgeSupportedMinecraft
    {
        public List<string> Versions { get; set; } = new();
    }

    /// <summary>
    /// Forge install_profile.json 结构
    /// </summary>
    public class ForgeInstallProfile
    {
        [JsonPropertyName("spec")]
        public int Spec { get; set; }

        [JsonPropertyName("profile")]
        public string Profile { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("minecraft")]
        public string Minecraft { get; set; } = "";

        [JsonPropertyName("libraries")]
        public List<ForgeLibrary> Libraries { get; set; } = new();

        [JsonPropertyName("data")]
        public Dictionary<string, ForgeData>? Data { get; set; }

        [JsonPropertyName("processors")]
        public List<ForgeProcessor>? Processors { get; set; }
    }

    public class ForgeLibrary
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("downloads")]
        public ForgeLibraryDownloads? Downloads { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    public class ForgeLibraryDownloads
    {
        [JsonPropertyName("artifact")]
        public ForgeArtifact? Artifact { get; set; }
    }

    public class ForgeArtifact
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("sha1")]
        public string Sha1 { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public class ForgeData
    {
        [JsonPropertyName("client")]
        public string Client { get; set; } = "";

        [JsonPropertyName("server")]
        public string Server { get; set; } = "";
    }

    public class ForgeProcessor
    {
        [JsonPropertyName("jar")]
        public string Jar { get; set; } = "";

        [JsonPropertyName("classpath")]
        public List<string> Classpath { get; set; } = new();

        [JsonPropertyName("args")]
        public List<string> Args { get; set; } = new();

        [JsonPropertyName("outputs")]
        public Dictionary<string, string>? Outputs { get; set; }
    }

    /// <summary>
    /// Forge服务 - 处理Forge版本查询和下载
    /// </summary>
    public class ForgeService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        
        // BMCLAPI镜像源
        private const string BMCL_FORGE_SUPPORT = "https://bmclapi2.bangbang93.com/forge/minecraft";
        private const string BMCL_FORGE_LIST = "https://bmclapi2.bangbang93.com/forge/minecraft/{0}";
        // BMCLAPI的Forge下载使用Maven格式
        private const string BMCL_FORGE_DOWNLOAD = "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/{0}/forge-{0}-installer.jar";
        
        // 官方源（Forge官方文件服务器）
        private const string OFFICIAL_FORGE_MAVEN = "https://maven.minecraftforge.net/net/minecraftforge/forge/";
        private const string OFFICIAL_FORGE_PROMO = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";

        /// <summary>
        /// 获取Forge支持的Minecraft版本列表
        /// </summary>
        public static async Task<List<string>> GetSupportedMinecraftVersionsAsync()
        {
            try
            {
                var config = LauncherConfig.Load();
                Debug.WriteLine($"[ForgeService] 获取Forge支持的MC版本列表... (源: {config.DownloadSource})");
                
                if (config.DownloadSource == DownloadSource.BMCLAPI)
                {
                    // 使用BMCLAPI镜像源
                    var response = await _httpClient.GetAsync(BMCL_FORGE_SUPPORT);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    var versions = JsonSerializer.Deserialize<List<string>>(json);

                    Debug.WriteLine($"[ForgeService] 从BMCLAPI获取到 {versions?.Count ?? 0} 个支持的MC版本");
                    return versions ?? new List<string>();
                }
                else
                {
                    // 使用官方源 - 通过解析promotions文件获取支持的版本
                    var response = await _httpClient.GetAsync(OFFICIAL_FORGE_PROMO);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    var promoData = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                    
                    if (promoData != null && promoData.ContainsKey("promos"))
                    {
                        var promosJson = promoData["promos"].ToString();
                        var promos = JsonSerializer.Deserialize<Dictionary<string, string>>(promosJson ?? "{}");
                        
                        // 从promos中提取MC版本号
                        var versions = promos?.Keys
                            .Select(k => k.Split('-')[0])
                            .Distinct()
                            .OrderByDescending(v => v)
                            .ToList() ?? new List<string>();
                        
                        Debug.WriteLine($"[ForgeService] 从官方源获取到 {versions.Count} 个支持的MC版本");
                        return versions;
                    }
                    
                    return new List<string>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeService] 获取Forge支持版本失败: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// 获取指定Minecraft版本的Forge版本列表
        /// </summary>
        public static async Task<List<ForgeVersion>> GetForgeVersionsAsync(string mcVersion)
        {
            try
            {
                var config = LauncherConfig.Load();
                Debug.WriteLine($"[ForgeService] 获取 MC {mcVersion} 的Forge版本列表... (源: {config.DownloadSource})");
                
                if (config.DownloadSource == DownloadSource.BMCLAPI)
                {
                    // 使用BMCLAPI镜像源
                    var url = string.Format(BMCL_FORGE_LIST, mcVersion);
                    var response = await _httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    var forgeList = JsonSerializer.Deserialize<List<ForgeVersion>>(json);

                    if (forgeList != null)
                    {
                        // 按build号降序排序（最新的在前）
                        forgeList = forgeList.OrderByDescending(f => f.Build).ToList();
                        Debug.WriteLine($"[ForgeService] 从BMCLAPI获取到 {forgeList.Count} 个Forge版本");
                    }

                    return forgeList ?? new List<ForgeVersion>();
                }
                else
                {
                    // 使用官方源 - 从Maven仓库获取版本列表
                    var response = await _httpClient.GetAsync(OFFICIAL_FORGE_MAVEN + "maven-metadata.xml");
                    response.EnsureSuccessStatusCode();
                    
                    var xml = await response.Content.ReadAsStringAsync();
                    var forgeList = ParseForgeMavenMetadata(xml, mcVersion);
                    
                    Debug.WriteLine($"[ForgeService] 从官方源获取到 {forgeList.Count} 个Forge版本");
                    return forgeList;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeService] 获取Forge版本列表失败: {ex.Message}");
                return new List<ForgeVersion>();
            }
        }

        /// <summary>
        /// 解析Forge Maven元数据XML
        /// </summary>
        private static List<ForgeVersion> ParseForgeMavenMetadata(string xml, string mcVersion)
        {
            var forgeList = new List<ForgeVersion>();
            
            try
            {
                // 简单的XML解析，提取<version>标签
                var lines = xml.Split('\n');
                int build = 1000; // 从高数字开始，确保排序正确
                
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("<version>") && trimmed.EndsWith("</version>"))
                    {
                        var versionText = trimmed.Replace("<version>", "").Replace("</version>", "");
                        
                        // 格式: 1.20.1-47.2.0 或 1.20.1-47.2.0-1.20.1
                        if (versionText.StartsWith(mcVersion + "-"))
                        {
                            // 移除MC版本号前缀，获取Forge版本号
                            var forgeVer = versionText.Substring(mcVersion.Length + 1);
                            
                            // 如果有额外的后缀（如 47.2.0-1.20.1），只取前面部分
                            var firstDashIndex = forgeVer.IndexOf('-');
                            if (firstDashIndex > 0)
                            {
                                // 检查是否是类似 "47.2.0-1.20.1" 的格式
                                var afterDash = forgeVer.Substring(firstDashIndex + 1);
                                if (afterDash.StartsWith(mcVersion))
                                {
                                    forgeVer = forgeVer.Substring(0, firstDashIndex);
                                }
                            }
                            
                            forgeList.Add(new ForgeVersion
                            {
                                Build = build--,
                                McVersion = mcVersion,
                                Version = forgeVer,
                                Modified = DateTime.Now.ToString("yyyy-MM-dd")
                            });
                        }
                    }
                }
                
                // 按build号降序排序
                forgeList = forgeList.OrderByDescending(f => f.Build).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeService] 解析Maven元数据失败: {ex.Message}");
            }
            
            return forgeList;
        }

        /// <summary>
        /// 下载Forge安装器
        /// </summary>
        /// <param name="forgeVersion">Forge版本 (格式: mcVersion-forgeVersion, 例如: 1.20.1-47.2.0)</param>
        /// <param name="savePath">保存路径</param>
        /// <param name="progress">进度回调</param>
        public static async Task<bool> DownloadForgeInstallerAsync(
            string forgeVersion,
            string savePath,
            IProgress<double>? progress = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                var config = LauncherConfig.Load();
                Debug.WriteLine($"[ForgeService] 开始下载Forge安装器: {forgeVersion} (源: {config.DownloadSource})");
                
                // 准备多个可能的URL格式
                var urlsToTry = new List<string>();
                
                // 提取MC版本号，用于判断是否是旧版本
                string mcVersion = "";
                if (forgeVersion.Contains("-"))
                {
                    mcVersion = forgeVersion.Split('-')[0];
                }
                
                // 判断是否是旧版本Forge（1.12.2及之前）
                bool isOldVersion = IsOldForgeVersion(mcVersion);
                
                if (config.DownloadSource == DownloadSource.BMCLAPI)
                {
                    // 使用BMCLAPI镜像源 - Maven格式
                    if (isOldVersion)
                    {
                        // 旧版本格式：1.8.9-11.15.1.2318-1.8.9
                        string oldFormatVersion = $"{forgeVersion}-{mcVersion}";
                        urlsToTry.Add(string.Format(BMCL_FORGE_DOWNLOAD, oldFormatVersion));
                        // 也尝试不带后缀的格式
                        urlsToTry.Add(string.Format(BMCL_FORGE_DOWNLOAD, forgeVersion));
                    }
                    else
                    {
                        urlsToTry.Add(string.Format(BMCL_FORGE_DOWNLOAD, forgeVersion));
                    }
                    
                    // 添加官方源作为备用
                    if (isOldVersion)
                    {
                        string oldFormatVersion = $"{forgeVersion}-{mcVersion}";
                        urlsToTry.Add($"{OFFICIAL_FORGE_MAVEN}{oldFormatVersion}/forge-{oldFormatVersion}-installer.jar");
                        urlsToTry.Add($"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{oldFormatVersion}/forge-{oldFormatVersion}-installer.jar");
                    }
                    urlsToTry.Add($"{OFFICIAL_FORGE_MAVEN}{forgeVersion}/forge-{forgeVersion}-installer.jar");
                    urlsToTry.Add($"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{forgeVersion}/forge-{forgeVersion}-installer.jar");
                }
                else
                {
                    // 使用官方源
                    if (isOldVersion)
                    {
                        // 旧版本格式：1.8.9-11.15.1.2318-1.8.9
                        string oldFormatVersion = $"{forgeVersion}-{mcVersion}";
                        // 格式1: Maven 仓库（旧版本格式）
                        urlsToTry.Add($"{OFFICIAL_FORGE_MAVEN}{oldFormatVersion}/forge-{oldFormatVersion}-installer.jar");
                        // 格式2: files.minecraftforge.net（旧版本格式）
                        urlsToTry.Add($"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{oldFormatVersion}/forge-{oldFormatVersion}-installer.jar");
                    }
                    
                    // 格式3: Maven 仓库 (标准格式)
                    urlsToTry.Add($"{OFFICIAL_FORGE_MAVEN}{forgeVersion}/forge-{forgeVersion}-installer.jar");
                    // 格式4: files.minecraftforge.net (标准格式)
                    urlsToTry.Add($"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{forgeVersion}/forge-{forgeVersion}-installer.jar");
                    // 格式5: BMCLAPI 作为最终备用
                    if (isOldVersion)
                    {
                        string oldFormatVersion = $"{forgeVersion}-{mcVersion}";
                        urlsToTry.Add(string.Format(BMCL_FORGE_DOWNLOAD, oldFormatVersion));
                    }
                    urlsToTry.Add(string.Format(BMCL_FORGE_DOWNLOAD, forgeVersion));
                }

                // 确保目录存在
                var directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                HttpResponseMessage? response = null;
                string successUrl = "";
                Exception? lastException = null;
                
                // 尝试所有可能的URL
                foreach (var url in urlsToTry)
                {
                    try
                    {
                        Debug.WriteLine($"[ForgeService] 尝试下载URL: {url}");
                        response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        response.EnsureSuccessStatusCode();
                        successUrl = url;
                        Debug.WriteLine($"[ForgeService] 成功找到Forge安装器: {url}");
                        break; // 成功，跳出循环
                    }
                    catch (HttpRequestException ex)
                    {
                        Debug.WriteLine($"[ForgeService] URL失败 ({ex.StatusCode}): {url}");
                        lastException = ex;
                        response?.Dispose();
                        response = null;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ForgeService] URL错误: {url} - {ex.Message}");
                        lastException = ex;
                        response?.Dispose();
                        response = null;
                    }
                }
                
                // 如果所有URL都失败了
                if (response == null || !response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[ForgeService] 所有URL都无法下载Forge安装器");
                    if (lastException != null)
                    {
                        throw new Exception($"无法下载Forge安装器，已尝试 {urlsToTry.Count} 个URL", lastException);
                    }
                    return false;
                }

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var percentage = (double)totalRead / totalBytes * 100;
                        progress?.Report(percentage);
                    }
                }

                Debug.WriteLine($"[ForgeService] Forge安装器下载完成: {savePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeService] 下载Forge安装器失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从Forge安装器中提取install_profile.json
        /// </summary>
        public static async Task<ForgeInstallProfile?> ExtractInstallProfileAsync(string installerPath)
        {
            try
            {
                Debug.WriteLine($"[ForgeService] 解析Forge安装器: {installerPath}");

                using var zip = ZipFile.OpenRead(installerPath);
                var profileEntry = zip.GetEntry("install_profile.json");

                if (profileEntry == null)
                {
                    Debug.WriteLine($"[ForgeService] 未找到install_profile.json");
                    return null;
                }

                using var stream = profileEntry.Open();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();

                var profile = JsonSerializer.Deserialize<ForgeInstallProfile>(json);
                Debug.WriteLine($"[ForgeService] 成功解析install_profile.json");

                return profile;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeService] 解析install_profile失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从Forge安装器中提取version.json
        /// </summary>
        public static async Task<string?> ExtractVersionJsonAsync(string installerPath, string versionId)
        {
            try
            {
                Debug.WriteLine($"[ForgeService] 从安装器提取version.json: {versionId}");

                using var zip = ZipFile.OpenRead(installerPath);
                
                // 尝试多种可能的路径
                var possiblePaths = new[]
                {
                    $"version.json",
                    $"{versionId}.json",
                    $"versions/{versionId}/{versionId}.json"
                };

                ZipArchiveEntry? versionEntry = null;
                foreach (var path in possiblePaths)
                {
                    versionEntry = zip.GetEntry(path);
                    if (versionEntry != null)
                    {
                        Debug.WriteLine($"[ForgeService] 找到version.json: {path}");
                        break;
                    }
                }

                if (versionEntry == null)
                {
                    Debug.WriteLine($"[ForgeService] 未找到version.json");
                    return null;
                }

                using var stream = versionEntry.Open();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();

                Debug.WriteLine($"[ForgeService] 成功提取version.json");
                return json;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeService] 提取version.json失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 判断是否是旧版本Forge（1.12.2及之前）
        /// 旧版本使用 1.8.9-11.15.1.2318-1.8.9 格式
        /// 新版本使用 1.16.5-36.2.34 格式
        /// </summary>
        private static bool IsOldForgeVersion(string mcVersion)
        {
            try
            {
                if (string.IsNullOrEmpty(mcVersion)) return false;
                
                // 解析版本号
                var versionParts = mcVersion.Split('.');
                if (versionParts.Length < 2) return false;
                
                if (!int.TryParse(versionParts[0], out int major)) return false;
                if (!int.TryParse(versionParts[1], out int minor)) return false;
                
                // 1.12.2 及之前的版本
                if (major == 1 && minor <= 12)
                {
                    return true;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 解析Maven坐标为路径
        /// 例如: net.minecraftforge:forge:1.20.1-47.2.0 => net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0.jar
        /// </summary>
        public static string MavenToPath(string maven)
        {
            var parts = maven.Split(':');
            if (parts.Length < 3) return "";

            var group = parts[0].Replace('.', '/');
            var artifact = parts[1];
            var version = parts[2];
            var classifier = parts.Length > 3 ? $"-{parts[3]}" : "";

            return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.jar";
        }
    }
}

