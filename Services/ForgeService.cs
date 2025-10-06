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
        private const string BMCL_FORGE_SUPPORT = "https://bmclapi2.bangbang93.com/forge/minecraft";
        private const string BMCL_FORGE_LIST = "https://bmclapi2.bangbang93.com/forge/minecraft/{0}";
        private const string BMCL_FORGE_DOWNLOAD = "https://bmclapi2.bangbang93.com/forge/download/{0}";

        /// <summary>
        /// 获取Forge支持的Minecraft版本列表
        /// </summary>
        public static async Task<List<string>> GetSupportedMinecraftVersionsAsync()
        {
            try
            {
                Debug.WriteLine($"[ForgeService] 获取Forge支持的MC版本列表...");
                var response = await _httpClient.GetAsync(BMCL_FORGE_SUPPORT);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var versions = JsonSerializer.Deserialize<List<string>>(json);

                Debug.WriteLine($"[ForgeService] 获取到 {versions?.Count ?? 0} 个支持的MC版本");
                return versions ?? new List<string>();
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
                Debug.WriteLine($"[ForgeService] 获取 MC {mcVersion} 的Forge版本列表...");
                var url = string.Format(BMCL_FORGE_LIST, mcVersion);
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var forgeList = JsonSerializer.Deserialize<List<ForgeVersion>>(json);

                if (forgeList != null)
                {
                    // 按build号降序排序（最新的在前）
                    forgeList = forgeList.OrderByDescending(f => f.Build).ToList();
                    Debug.WriteLine($"[ForgeService] 获取到 {forgeList.Count} 个Forge版本");
                }

                return forgeList ?? new List<ForgeVersion>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeService] 获取Forge版本列表失败: {ex.Message}");
                return new List<ForgeVersion>();
            }
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
            IProgress<double>? progress = null)
        {
            try
            {
                Debug.WriteLine($"[ForgeService] 开始下载Forge安装器: {forgeVersion}");
                var url = string.Format(BMCL_FORGE_DOWNLOAD, forgeVersion);

                // 确保目录存在
                var directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
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

