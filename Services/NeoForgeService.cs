using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
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

        public string DisplayName => Version;
        
        /// <summary>
        /// Minecraft版本（从NeoForge版本号推断）
        /// 格式: {MC主版本}.{MC次版本}.{NeoForge构建号}
        /// </summary>
        public string MinecraftVersion
        {
            get
            {
                if (string.IsNullOrEmpty(Version)) return "";
                
                var versionWithoutSuffix = Version.Split('-')[0];
                var parts = versionWithoutSuffix.Split('.');
                
                if (parts.Length >= 2 && 
                    int.TryParse(parts[0], out int neoMajor) && 
                    int.TryParse(parts[1], out int neoMinor))
                {
                    if (neoMajor > 30) return ""; // 过滤错误数据
                    return $"1.{neoMajor}.{neoMinor}";
                }
                
                return "";
            }
        }
    }

    /// <summary>
    /// install_profile.json 中的处理器定义
    /// </summary>
    public class NeoForgeProcessor
    {
        [JsonPropertyName("jar")]
        public string Jar { get; set; } = "";

        [JsonPropertyName("classpath")]
        public List<string> Classpath { get; set; } = new();

        [JsonPropertyName("args")]
        public List<string> Args { get; set; } = new();

        [JsonPropertyName("outputs")]
        public Dictionary<string, string>? Outputs { get; set; }
        
        [JsonPropertyName("sides")]
        public List<string>? Sides { get; set; }
    }

    /// <summary>
    /// install_profile.json 结构
    /// </summary>
    public class NeoForgeInstallProfile
    {
        [JsonPropertyName("profile")]
        public string Profile { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("json")]
        public string Json { get; set; } = "";

        [JsonPropertyName("minecraft")]
        public string Minecraft { get; set; } = "";

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("data")]
        public Dictionary<string, NeoForgeDataValue> Data { get; set; } = new();

        [JsonPropertyName("processors")]
        public List<NeoForgeProcessor> Processors { get; set; } = new();

        [JsonPropertyName("libraries")]
        public List<JsonElement> Libraries { get; set; } = new();
    }

    public class NeoForgeDataValue
    {
        [JsonPropertyName("client")]
        public string Client { get; set; } = "";

        [JsonPropertyName("server")]
        public string Server { get; set; } = "";
    }

    /// <summary>
    /// NeoForge安装类型
    /// </summary>
    public enum NeoForgeInstallType
    {
        /// <summary>
        /// 旧版 NeoForge (20.2.x, profile="forge")
        /// </summary>
        OldNeoForge,
        
        /// <summary>
        /// 新版 NeoForge (21.x+, profile="neoforge")
        /// </summary>
        NewNeoForge,
        
        /// <summary>
        /// 未知类型
        /// </summary>
        Unknown
    }

    /// <summary>
    /// NeoForge版本管理和安装服务
    /// </summary>
    public class NeoForgeService
    {
        private static readonly HttpClient _httpClient;
        
        private const string BMCL_NEOFORGE_LIST = "https://bmclapi2.bangbang93.com/neoforge/list/{0}";
        private const string BMCL_NEOFORGE_MAVEN = "https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/{0}/neoforge-{0}-installer.jar";
        private const string OFFICIAL_NEOFORGE_MAVEN_METADATA = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
        private const string OFFICIAL_NEOFORGE_MAVEN = "https://maven.neoforged.net/releases/net/neoforged/neoforge/{0}/neoforge-{0}-installer.jar";
        
        static NeoForgeService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ObsMCLauncher/1.0");
        }

        #region 版本列表获取

        /// <summary>
        /// 获取NeoForge版本列表
        /// </summary>
        public static async Task<List<NeoForgeVersion>> GetNeoForgeVersionsAsync(string minecraftVersion)
        {
            try
            {
                var useBMCLAPI = DownloadSourceManager.Instance.CurrentSource == DownloadSource.BMCLAPI;
                
                if (useBMCLAPI)
                {
                    return await GetVersionsFromBMCLAPIAsync(minecraftVersion);
                }
                else
                {
                    return await GetVersionsFromOfficialAsync(minecraftVersion);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NeoForgeService] 获取版本列表失败: {ex.Message}");
                return new List<NeoForgeVersion>();
            }
        }

        private static async Task<List<NeoForgeVersion>> GetVersionsFromBMCLAPIAsync(string minecraftVersion)
        {
            var url = string.Format(BMCL_NEOFORGE_LIST, minecraftVersion);
            Debug.WriteLine($"[NeoForgeService] 从BMCLAPI获取版本列表: {url}");
            
            var response = await _httpClient.GetStringAsync(url);
            var versions = JsonSerializer.Deserialize<List<NeoForgeVersion>>(response) ?? new List<NeoForgeVersion>();
            
            // 按版本号排序（从最新到最旧）
            var sortedVersions = versions
                .Where(v => !string.IsNullOrEmpty(v.MinecraftVersion))
                .OrderByDescending(v => ParseVersionForSorting(v.Version))
                    .ToList();
                
            Debug.WriteLine($"[NeoForgeService] 从BMCLAPI获取到 {sortedVersions.Count} 个版本");
            return sortedVersions;
        }

        private static async Task<List<NeoForgeVersion>> GetVersionsFromOfficialAsync(string minecraftVersion)
        {
            Debug.WriteLine($"[NeoForgeService] 从官方源获取版本列表");
            
            try
            {
                // 尝试从新API获取（NeoForge）
                var neoforgeUrl = "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge";
                var response = await _httpClient.GetStringAsync(neoforgeUrl);
                var apiResult = JsonSerializer.Deserialize<NeoForgeMavenApiResult>(response);
                
                var versions = new List<NeoForgeVersion>();
                
                if (apiResult?.Versions != null)
                {
                    foreach (var version in apiResult.Versions)
                    {
                        var neoVersion = new NeoForgeVersion { Version = version };
                        if (neoVersion.MinecraftVersion == minecraftVersion)
                        {
                            versions.Add(neoVersion);
                        }
                    }
                }
                
                // 对于 1.20.1，还需要从旧API获取
                if (minecraftVersion == "1.20.1")
                {
                    try
                    {
                        var forgeUrl = "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/forge";
                        var forgeResponse = await _httpClient.GetStringAsync(forgeUrl);
                        var forgeResult = JsonSerializer.Deserialize<NeoForgeMavenApiResult>(forgeResponse);
                        
                        if (forgeResult?.Versions != null)
                        {
                            foreach (var version in forgeResult.Versions)
                            {
                                var neoVersion = new NeoForgeVersion 
                                { 
                                    Version = version.StartsWith("1.20.1-") ? version.Substring("1.20.1-".Length) : version
                                };
                                if (neoVersion.MinecraftVersion == minecraftVersion)
                                {
                                    versions.Add(neoVersion);
                                }
                            }
                        }
            }
            catch (Exception ex)
            {
                        Debug.WriteLine($"[NeoForgeService] 获取1.20.1旧版本失败: {ex.Message}");
                    }
                }
                
                // 按版本号排序（从最新到最旧）
                versions = versions
                    .OrderByDescending(v => ParseVersionForSorting(v.Version))
                    .ToList();
                
                Debug.WriteLine($"[NeoForgeService] 获取到 {versions.Count} 个版本");
                return versions;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NeoForgeService] 从官方API获取失败，尝试从Maven元数据获取: {ex.Message}");
                
                // 回退到Maven元数据
                var response = await _httpClient.GetStringAsync(OFFICIAL_NEOFORGE_MAVEN_METADATA);
                var xml = XDocument.Parse(response);
                
                var versions = xml.Descendants("version")
                    .Select(v => new NeoForgeVersion { Version = v.Value })
                    .Where(v => v.MinecraftVersion == minecraftVersion)
                    .OrderByDescending(v => ParseVersionForSorting(v.Version))
                    .ToList();
                    
                return versions;
            }
        }

        /// <summary>
        /// 解析版本号用于排序（例如：20.3.8-beta -> "020.003.008.0"）
        /// </summary>
        private static string ParseVersionForSorting(string version)
        {
            try
            {
                // 移除后缀（如 -beta, -alpha）
                var versionWithoutSuffix = version.Split('-')[0];
                var parts = versionWithoutSuffix.Split('.');
                
                // 将每个部分填充为3位数字，便于字符串比较
                var normalized = string.Join(".", parts.Select(p => 
                {
                    if (int.TryParse(p, out int num))
                    {
                        return num.ToString("D3");
                    }
                    return p;
                }));
                
                return normalized;
            }
            catch
            {
                return version;
            }
        }
        
        private class NeoForgeMavenApiResult
        {
            [JsonPropertyName("versions")]
            public List<string> Versions { get; set; } = new();
        }

        #endregion

        #region 主安装流程

        /// <summary>
        /// 安装NeoForge - 主入口
        /// </summary>
        public static async Task<bool> InstallNeoForgeAsync(
            string neoforgeVersion,
            string gameDirectory,
            Action<string, double, double, long, long>? progressCallback = null,
            CancellationToken cancellationToken = default)
            {
                try
                {
                Debug.WriteLine($"[NeoForgeService] ========== 开始安装 NeoForge {neoforgeVersion} ==========");
                
                // 1. 推断Minecraft版本
                var neoForgeVersionObj = new NeoForgeVersion { Version = neoforgeVersion };
                var mcVersion = neoForgeVersionObj.MinecraftVersion;
                
                if (string.IsNullOrEmpty(mcVersion))
                {
                    throw new Exception($"无法从NeoForge版本 {neoforgeVersion} 推断Minecraft版本");
                }
                
                Debug.WriteLine($"[NeoForgeService] Minecraft版本: {mcVersion}");
                
                // 2. 下载NeoForge安装器
                var tempDir = Path.Combine(Path.GetTempPath(), "ObsMCLauncher", "NeoForge");
                Directory.CreateDirectory(tempDir);
                
                var installerPath = Path.Combine(tempDir, $"neoforge-{neoforgeVersion}-installer.jar");
                
                await DownloadNeoForgeInstallerAsync(neoforgeVersion, installerPath, progressCallback, cancellationToken);
                
                // 3. 清理旧的最终输出文件（但保留中间文件）
                CleanFinalOutputs(gameDirectory, mcVersion, neoforgeVersion);
                
                // 4. 创建版本目录
                var customVersionName = $"Minecraft-{mcVersion}-neoforge-{neoforgeVersion}";
                var versionDir = Path.Combine(gameDirectory, "versions", customVersionName);
                Directory.CreateDirectory(versionDir);
                
                // 5. 下载基础Minecraft版本
                progressCallback?.Invoke("正在下载Minecraft原版...", 0, 0, 0, 0);
                await DownloadVanillaMinecraftAsync(mcVersion, versionDir, customVersionName, progressCallback, cancellationToken);
                
                // 6. 识别安装类型并执行安装
                var installType = DetectInstallType(installerPath);
                Debug.WriteLine($"[NeoForgeService] 检测到安装类型: {installType}");
                
                bool success;
                if (installType == NeoForgeInstallType.OldNeoForge || installType == NeoForgeInstallType.NewNeoForge)
                {
                    success = await ProcessInstallProfileAsync(
                        installerPath, 
                        gameDirectory, 
                        versionDir,
                        customVersionName,
                        mcVersion,
                        neoforgeVersion,
                        progressCallback, 
                        cancellationToken);
                }
                else
                {
                    throw new Exception("不支持的NeoForge安装器类型");
                }
                
                if (success)
                {
                    Debug.WriteLine($"[NeoForgeService] ========== NeoForge {neoforgeVersion} 安装成功 ==========");
                }
                
                return success;
                }
                catch (Exception ex)
                {
                Debug.WriteLine($"[NeoForgeService] 安装失败: {ex.Message}");
                Debug.WriteLine($"[NeoForgeService] 堆栈跟踪: {ex.StackTrace}");
                throw;
            }
        }

        #endregion

        #region 版本类型检测

        /// <summary>
        /// 检测NeoForge安装类型
        /// </summary>
        private static NeoForgeInstallType DetectInstallType(string installerPath)
            {
                try
                {
                using var archive = ZipFile.OpenRead(installerPath);
                var profileEntry = archive.GetEntry("install_profile.json");
                
                if (profileEntry == null)
                    return NeoForgeInstallType.Unknown;
                
                using var stream = profileEntry.Open();
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                
                var profile = JsonSerializer.Deserialize<NeoForgeInstallProfile>(json);
                
                if (profile == null)
                    return NeoForgeInstallType.Unknown;
                
                // 根据profile字段判断版本类型
                // 21.x+: profile字段为 "neoforge" 或 "NeoForge"
                // 20.2.x: profile字段为 "forge" 但包含NeoForge签名
                
                if (profile.Profile.Equals("neoforge", StringComparison.OrdinalIgnoreCase) ||
                    profile.Profile.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
                {
                    return NeoForgeInstallType.NewNeoForge;
                }
                else if (profile.Profile.Equals("forge", StringComparison.OrdinalIgnoreCase))
                {
                    // 检查是否包含NeoForge特征标识
                    var hasNeoForgeSignature = archive.GetEntry("META-INF/NEOFORGE.RSA") != null ||
                                               json.Contains("neoforge", StringComparison.OrdinalIgnoreCase);
                    
                    if (hasNeoForgeSignature)
                    {
                        return NeoForgeInstallType.OldNeoForge;
                    }
                }
                
                return NeoForgeInstallType.Unknown;
                }
                catch (Exception ex)
                {
                Debug.WriteLine($"[NeoForgeService] 检测安装类型失败: {ex.Message}");
                return NeoForgeInstallType.Unknown;
            }
        }

        #endregion

        #region 安装器下载

        private static async Task DownloadNeoForgeInstallerAsync(
            string version,
            string outputPath,
            Action<string, double, double, long, long>? progressCallback,
            CancellationToken cancellationToken)
        {
            var useBMCLAPI = DownloadSourceManager.Instance.CurrentSource == DownloadSource.BMCLAPI;
            var urls = new List<string>();
            
            if (useBMCLAPI)
            {
                urls.Add(string.Format(BMCL_NEOFORGE_MAVEN, version));
                urls.Add(string.Format(OFFICIAL_NEOFORGE_MAVEN, version));
            }
            else
            {
                urls.Add(string.Format(OFFICIAL_NEOFORGE_MAVEN, version));
                urls.Add(string.Format(BMCL_NEOFORGE_MAVEN, version));
            }
            
            Debug.WriteLine($"[NeoForgeService] 开始下载安装器: {version}");
            
            foreach (var url in urls)
            {
                try
                {
                    Debug.WriteLine($"[NeoForgeService] 尝试URL: {url}");
                    
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                
                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                    
                    var buffer = new byte[8192];
                    long downloadedBytes = 0;
                    int bytesRead;
                    
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                        downloadedBytes += bytesRead;
                        
                        progressCallback?.Invoke($"正在下载安装器...", 0, 0, downloadedBytes, totalBytes);
                    }
                    
                    Debug.WriteLine($"[NeoForgeService] 安装器下载完成: {outputPath}");
                    return;
            }
            catch (Exception ex)
            {
                    Debug.WriteLine($"[NeoForgeService] 下载失败: {ex.Message}");
                    if (url == urls.Last())
                        throw new Exception($"无法下载NeoForge安装器: {ex.Message}");
                }
            }
        }

        #endregion

        #region 原版Minecraft下载

        private static async Task DownloadVanillaMinecraftAsync(
            string mcVersion,
            string versionDir,
            string customVersionName,
            Action<string, double, double, long, long>? progressCallback,
            CancellationToken cancellationToken)
        {
            var versionManifest = await MinecraftVersionService.GetVersionListAsync();
            if (versionManifest == null)
            {
                throw new Exception("无法获取Minecraft版本列表");
            }
            
            var vanillaVersion = versionManifest.Versions.FirstOrDefault(v => v.Id == mcVersion);
            
            if (vanillaVersion == null)
            {
                throw new Exception($"未找到Minecraft版本 {mcVersion}");
            }
            
            // 下载version.json和client.jar
            var versionJsonPath = Path.Combine(versionDir, $"{customVersionName}.json");
            var clientJarPath = Path.Combine(versionDir, $"{customVersionName}.jar");
            
            // 下载version.json
            var versionJsonUrl = vanillaVersion.Url;
            string jsonContent;
            using (var response = await _httpClient.GetAsync(versionJsonUrl, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                await File.WriteAllTextAsync(versionJsonPath, jsonContent, cancellationToken);
            }
            
            // 额外保存一份原版 JSON 文件（用于继承 natives 等信息）
            // NeoForge 的 version.json 中 inheritsFrom 指向 mcVersion（如 1.20.4）
            // 启动器在合并父版本时需要找到这个文件来获取 natives 库信息
            var vanillaJsonPath = Path.Combine(versionDir, $"{mcVersion}.json");
            await File.WriteAllTextAsync(vanillaJsonPath, jsonContent, cancellationToken);
            Debug.WriteLine($"[NeoForgeService] 已备份原版JSON到: {vanillaJsonPath}");
            
            // 解析JSON获取client下载信息
            var jsonDoc = JsonDocument.Parse(await File.ReadAllTextAsync(versionJsonPath, cancellationToken));
            var clientUrl = jsonDoc.RootElement.GetProperty("downloads").GetProperty("client").GetProperty("url").GetString();
            
            if (string.IsNullOrEmpty(clientUrl))
            {
                throw new Exception("无法获取客户端下载地址");
            }
            
            // 下载client.jar
            progressCallback?.Invoke("正在下载Minecraft客户端...", 0, 0, 0, 0);
            
            var bmclUrl = clientUrl.Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com");
            var finalUrl = DownloadSourceManager.Instance.CurrentSource == DownloadSource.BMCLAPI ? bmclUrl : clientUrl;
            
            await DownloadFileSimpleAsync(finalUrl, clientJarPath, cancellationToken);
            
            Debug.WriteLine($"[NeoForgeService] 原版Minecraft下载完成");
        }

        #endregion

        #region install_profile.json处理

        /// <summary>
        /// 处理install_profile.json安装流程
        /// </summary>
        private static async Task<bool> ProcessInstallProfileAsync(
            string installerPath,
            string gameDirectory,
            string versionDir,
            string customVersionName,
            string mcVersion,
            string neoforgeVersion,
            Action<string, double, double, long, long>? progressCallback,
            CancellationToken cancellationToken)
        {
            try
            {
                Debug.WriteLine($"[NeoForgeService] ========== 处理 install_profile.json ==========");
                
                // 1. 读取install_profile.json
                NeoForgeInstallProfile? profile;
                using (var archive = ZipFile.OpenRead(installerPath))
                {
                    var profileEntry = archive.GetEntry("install_profile.json");
                    if (profileEntry == null)
                    {
                        throw new Exception("安装器中未找到install_profile.json");
                    }
                    
                    using var stream = profileEntry.Open();
                    using var reader = new StreamReader(stream);
                    var json = reader.ReadToEnd();
                    
                    profile = JsonSerializer.Deserialize<NeoForgeInstallProfile>(json);
                    
                    if (profile == null)
                    {
                        throw new Exception("无法解析install_profile.json");
                    }
                    
                    Debug.WriteLine($"[NeoForgeService] Profile: {profile.Profile}");
                    Debug.WriteLine($"[NeoForgeService] Version: {profile.Version}");
                    Debug.WriteLine($"[NeoForgeService] Minecraft: {profile.Minecraft}");
                    Debug.WriteLine($"[NeoForgeService] 处理器数量: {profile.Processors.Count}");
                }
                
                // 2. 从安装器复制maven库文件（提高安装速度）
                progressCallback?.Invoke("正在复制库文件...", 0, 0, 0, 0);
                await CopyLibrariesFromInstallerAsync(installerPath, gameDirectory, profile);
                
                // 3. 从安装器复制主JAR文件（如果指定了path属性）
                await CopyMainJarFromInstallerAsync(installerPath, gameDirectory, versionDir, customVersionName, profile);
                
                // 4. 下载缺失的库文件
                progressCallback?.Invoke("正在下载缺失的库文件...", 0, 0, 0, 0);
                await DownloadMissingLibrariesAsync(gameDirectory, mcVersion, profile, progressCallback, cancellationToken);
                
                // 5. 提取并修改version.json
                progressCallback?.Invoke("正在配置版本文件...", 0, 0, 0, 0);
                await ExtractAndModifyVersionJsonAsync(installerPath, versionDir, customVersionName, mcVersion, profile);
                
                // 6. 构建变量映射
                var variables = await BuildVariablesAsync(installerPath, gameDirectory, versionDir, customVersionName, mcVersion, profile);
                
                // 7. 执行处理器
                progressCallback?.Invoke("正在执行安装处理器...", 0, 0, 0, 0);
                await ExecuteProcessorsAsync(gameDirectory, mcVersion, profile, variables, progressCallback, cancellationToken);
                
                Debug.WriteLine($"[NeoForgeService] ========== install_profile.json 处理完成 ==========");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NeoForgeService] 处理install_profile.json失败: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region 库文件处理

        /// <summary>
        /// 从安装器内置的maven仓库中提取库文件到本地
        /// </summary>
        private static async Task CopyLibrariesFromInstallerAsync(
            string installerPath,
            string gameDirectory,
            NeoForgeInstallProfile profile)
        {
            Debug.WriteLine($"[NeoForgeService] 开始从安装器复制库文件...");
            
            var librariesDir = Path.Combine(gameDirectory, "libraries");
            int copiedCount = 0;
            
            using var archive = ZipFile.OpenRead(installerPath);
            
            // 遍历安装配置中定义的所有库文件
            foreach (var libElement in profile.Libraries)
        {
            try
            {
                    var nameProperty = libElement.GetProperty("name");
                    var mavenCoordinate = nameProperty.GetString();
                    
                    if (string.IsNullOrEmpty(mavenCoordinate))
                        continue;
                    
                    var relativePath = MavenCoordinateToPath(mavenCoordinate);
                    var destPath = Path.Combine(librariesDir, relativePath);
                    
                    // 尝试从安装器内部的maven目录提取文件
                    var entryPath = $"maven/{relativePath.Replace('\\', '/')}";
                    var entry = archive.GetEntry(entryPath);
                    
                    if (entry != null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                        entry.ExtractToFile(destPath, overwrite: true);
                        copiedCount++;
                        
                        Debug.WriteLine($"[NeoForgeService] 已复制: {Path.GetFileName(destPath)}");
                }
            }
            catch (Exception ex)
            {
                    Debug.WriteLine($"[NeoForgeService] 复制库文件失败: {ex.Message}");
                }
            }
            
            Debug.WriteLine($"[NeoForgeService] 从安装器复制了 {copiedCount} 个库文件");
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// 从安装器复制主JAR文件（如果指定了path属性）
        /// </summary>
        private static async Task CopyMainJarFromInstallerAsync(
            string installerPath,
            string gameDirectory,
            string versionDir,
            string customVersionName,
            NeoForgeInstallProfile profile)
        {
            if (string.IsNullOrEmpty(profile.Path))
            {
                Debug.WriteLine($"[NeoForgeService] install_profile.json 中未指定path属性，跳过主JAR复制");
                return;
            }

            try
            {
                using var archive = new ZipArchive(File.OpenRead(installerPath), ZipArchiveMode.Read);
                
                // 从安装器中获取主JAR
                var mavenPath = profile.Path.Replace("\\", "/");
                if (!mavenPath.StartsWith("maven/"))
                {
                    mavenPath = "maven/" + mavenPath;
                }
                
                var entry = archive.GetEntry(mavenPath);
                if (entry == null)
                {
                    Debug.WriteLine($"[NeoForgeService] 未在安装器中找到主JAR: {mavenPath}");
                    return;
                }

                // 确定目标路径
                // path 通常是 Maven 坐标，如 "net/neoforged/neoforge/21.1.211/neoforge-21.1.211.jar"
                var librariesDir = Path.Combine(gameDirectory, "libraries");
                var destPath = Path.Combine(librariesDir, profile.Path.Replace("/", Path.DirectorySeparatorChar.ToString()));
                
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                
                using var sourceStream = entry.Open();
                using var destStream = File.Create(destPath);
                await sourceStream.CopyToAsync(destStream);
                
                Debug.WriteLine($"[NeoForgeService] ✅ 主JAR复制成功: {Path.GetFileName(destPath)}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NeoForgeService] ⚠️ 复制主JAR失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查并下载安装器中未包含的库文件
        /// </summary>
        private static async Task DownloadMissingLibrariesAsync(
            string gameDirectory,
            string mcVersion,
            NeoForgeInstallProfile profile,
            Action<string, double, double, long, long>? progressCallback,
            CancellationToken cancellationToken)
        {
            Debug.WriteLine($"[NeoForgeService] 检查缺失的库文件...");
            
            var librariesDir = Path.Combine(gameDirectory, "libraries");
            var missingLibraries = new List<string>();
            
            foreach (var libElement in profile.Libraries)
        {
            try
            {
                    var nameProperty = libElement.GetProperty("name");
                    var mavenCoordinate = nameProperty.GetString();
                    
                    if (string.IsNullOrEmpty(mavenCoordinate))
                        continue;
                    
                    var relativePath = MavenCoordinateToPath(mavenCoordinate);
                    var destPath = Path.Combine(librariesDir, relativePath);
                    
                    if (!File.Exists(destPath))
                    {
                        missingLibraries.Add(mavenCoordinate);
                        Debug.WriteLine($"[NeoForgeService] 缺失: {mavenCoordinate}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NeoForgeService] 检查库文件失败: {ex.Message}");
                }
            }
            
            Debug.WriteLine($"[NeoForgeService] 发现 {missingLibraries.Count} 个缺失的库文件");
            
            if (missingLibraries.Count == 0)
                return;
            
            var downloadService = new DownloadService();
            var downloadedCount = 0;
            
            foreach (var maven in missingLibraries)
                {
                    try
                    {
                    var relativePath = MavenCoordinateToPath(maven);
                    var destPath = Path.Combine(librariesDir, relativePath);
                    
                    var url = BuildMavenDownloadUrl(maven);
                    
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    await DownloadFileSimpleAsync(url, destPath, cancellationToken);
                    
                    downloadedCount++;
                    progressCallback?.Invoke($"正在下载库文件 ({downloadedCount}/{missingLibraries.Count})...", 
                        downloadedCount, missingLibraries.Count, 0, 0);
                    }
                    catch (Exception ex)
                    {
                    Debug.WriteLine($"[NeoForgeService] 下载库文件失败 {maven}: {ex.Message}");
                }
            }
            
            Debug.WriteLine($"[NeoForgeService] 成功下载 {downloadedCount}/{missingLibraries.Count} 个库文件");
        }

        /// <summary>
        /// 将Maven坐标转换为本地文件系统路径
        /// </summary>
        private static string MavenCoordinateToPath(string mavenCoordinate)
        {
            // 支持格式: group:artifact:version[:classifier][@extension]
            var parts = mavenCoordinate.Split('@');
            var coordinate = parts[0];
            var extension = parts.Length > 1 ? parts[1] : "jar";
            
            var segments = coordinate.Split(':');
            if (segments.Length < 3)
                throw new ArgumentException($"无效的Maven坐标: {mavenCoordinate}");
            
            var group = segments[0].Replace('.', '/');
            var artifact = segments[1];
            var version = segments[2];
            var classifier = segments.Length > 3 ? segments[3] : null;
            
            var fileName = classifier != null 
                ? $"{artifact}-{version}-{classifier}.{extension}"
                : $"{artifact}-{version}.{extension}";
            
            return Path.Combine(group, artifact, version, fileName);
        }

        /// <summary>
        /// 根据Maven坐标生成远程仓库下载地址
        /// </summary>
        private static string BuildMavenDownloadUrl(string mavenCoordinate)
        {
            var useBMCLAPI = DownloadSourceManager.Instance.CurrentSource == DownloadSource.BMCLAPI;
            var baseUrl = useBMCLAPI 
                ? "https://bmclapi2.bangbang93.com/maven/"
                : "https://maven.neoforged.net/releases/";
            
            var relativePath = MavenCoordinateToPath(mavenCoordinate).Replace('\\', '/');
            return baseUrl + relativePath;
        }

        #endregion

        #region version.json处理

        private static async Task ExtractAndModifyVersionJsonAsync(
            string installerPath,
            string versionDir,
            string customVersionName,
            string mcVersion,
            NeoForgeInstallProfile profile)
        {
            Debug.WriteLine($"[NeoForgeService] 提取version.json...");
            
            using var archive = ZipFile.OpenRead(installerPath);
            
            // 处理可能的路径格式：/version.json 或 version.json
            var jsonPath = profile.Json.TrimStart('/');
            var versionJsonEntry = archive.GetEntry(jsonPath);
            
            if (versionJsonEntry == null)
            {
                throw new Exception($"安装器中未找到 {jsonPath}");
            }
            
            using var stream = versionJsonEntry.Open();
            using var reader = new StreamReader(stream);
            var versionJson = reader.ReadToEnd();
            
            // 修改version.json
            var jsonDoc = JsonDocument.Parse(versionJson);
            var options = new JsonSerializerOptions { WriteIndented = true };
            var modifiedJson = ModifyVersionJson(jsonDoc, customVersionName, mcVersion);
            
            var outputPath = Path.Combine(versionDir, $"{customVersionName}.json");
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(modifiedJson, options));
            
            Debug.WriteLine($"[NeoForgeService] version.json已保存: {outputPath}");
        }

        private static JsonElement ModifyVersionJson(JsonDocument original, string customVersionName, string mcVersion)
        {
            var root = original.RootElement;
            var modified = new Dictionary<string, object>();
            
            // 复制所有属性
            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "id":
                        modified["id"] = customVersionName;
                        break;
                    case "inheritsFrom":
                        modified["inheritsFrom"] = mcVersion;
                        break;
                    default:
                        // 其他所有属性，包括 "arguments"，都直接复制，不做任何修改
                        modified[property.Name] = property.Value.Clone();
                        break;
                }
            }
            
            // 确保 inheritsFrom 存在
            if (!modified.ContainsKey("inheritsFrom"))
            {
                modified["inheritsFrom"] = mcVersion;
            }

            // 调试：记录原始 launchTarget
            if (root.TryGetProperty("arguments", out var args) && args.TryGetProperty("game", out var gameArgs))
            {
                var gameArgsList = gameArgs.EnumerateArray().ToList();
                for (int i = 0; i < gameArgsList.Count - 1; i++)
                {
                    if (gameArgsList[i].ValueKind == JsonValueKind.String && gameArgsList[i].GetString() == "--launchTarget")
                    {
                        var target = gameArgsList[i + 1].GetString();
                        Debug.WriteLine($"[NeoForgeService] 原始 launchTarget: {target}");
                        break;
                    }
                }
            }
            
            return JsonSerializer.SerializeToElement(modified);
        }

        #endregion

        #region 变量构建

        private static async Task<Dictionary<string, string>> BuildVariablesAsync(
            string installerPath,
            string gameDirectory,
            string versionDir,
            string customVersionName,
            string mcVersion,
            NeoForgeInstallProfile profile)
        {
            Debug.WriteLine($"[NeoForgeService] 构建变量映射...");
            
            var variables = new Dictionary<string, string>();
            var tempDir = Path.Combine(Path.GetTempPath(), "ObsMCLauncher", "NeoForge", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            
            // 处理data字段（从安装器提取文件）
            using (var archive = ZipFile.OpenRead(installerPath))
            {
                foreach (var kvp in profile.Data)
                {
                    var key = kvp.Key;
                    var value = kvp.Value.Client; // 使用client端的值
                    
                    if (string.IsNullOrEmpty(value))
                        continue;
                    
                    // 解析变量值
                    var parsedValue = await ParseDataValueAsync(value, archive, tempDir, gameDirectory, mcVersion);
                    variables[key] = parsedValue;
                    
                    Debug.WriteLine($"[NeoForgeService] 变量: {{{key}}} = {parsedValue}");
                }
            }
            
            // 添加内置变量
            var librariesDir = Path.Combine(gameDirectory, "libraries");
            var minecraftJar = Path.Combine(versionDir, $"{customVersionName}.jar");
            
            variables["SIDE"] = "client";
            variables["MINECRAFT_JAR"] = minecraftJar;
            variables["MINECRAFT_VERSION"] = minecraftJar; // 指向JAR文件路径
            variables["ROOT"] = gameDirectory;
            variables["INSTALLER"] = installerPath;
            variables["LIBRARY_DIR"] = librariesDir;
            
            Debug.WriteLine($"[NeoForgeService] 变量映射构建完成，共 {variables.Count} 个变量");
            
            return variables;
        }

        private static async Task<string> ParseDataValueAsync(
            string value,
            ZipArchive archive,
            string tempDir,
            string gameDirectory,
            string mcVersion)
        {
            // 解析三种不同格式的变量值：
            // 1. 'literal' - 单引号包裹的字面字符串
            // 2. [maven:coordinate] - 方括号包裹的Maven坐标（指向处理器输出文件，不需要下载）
            // 3. /path/in/installer - 安装器内部文件路径（需要提取到临时目录）
            
            if (value.StartsWith("'") && value.EndsWith("'"))
            {
                // 字面字符串
                return value.Trim('\'');
            }
            else if (value.StartsWith("[") && value.EndsWith("]"))
            {
                // Maven坐标 - 这些文件是处理器的输出，会在处理器执行时生成
                // 只需要返回它们的本地路径，不需要预先下载
                var maven = value.Trim('[', ']');
                var relativePath = MavenCoordinateToPath(maven);
                return Path.Combine(gameDirectory, "libraries", relativePath);
            }
            else if (value.StartsWith("/"))
            {
                // 从安装器提取文件到临时目录
                var entryPath = value.TrimStart('/');
                var entry = archive.GetEntry(entryPath);
                
                if (entry != null)
                {
                    var fileName = Path.GetFileName(entryPath);
                    var destPath = Path.Combine(tempDir, fileName);
                    entry.ExtractToFile(destPath, overwrite: true);
                    return destPath;
                }
                
                throw new Exception($"安装器中未找到文件: {entryPath}");
            }
            
            return await Task.FromResult(value);
        }

        #endregion

        #region 处理器执行

        private static async Task ExecuteProcessorsAsync(
            string gameDirectory,
            string mcVersion,
            NeoForgeInstallProfile profile,
            Dictionary<string, string> variables,
            Action<string, double, double, long, long>? progressCallback,
            CancellationToken cancellationToken)
        {
            Debug.WriteLine($"[NeoForgeService] ========== 开始执行处理器 ==========");
            Debug.WriteLine($"[NeoForgeService] 总处理器数: {profile.Processors.Count}");
            
            int completedCount = 0;
            
            foreach (var processor in profile.Processors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // 检查sides
                if (processor.Sides != null && !processor.Sides.Contains("client"))
                {
                    Debug.WriteLine($"[NeoForgeService] ⏭️ 跳过服务端处理器: {processor.Jar}");
                    continue;
                }
                
                Debug.WriteLine($"[NeoForgeService] ---------- 处理器 {completedCount + 1}/{profile.Processors.Count} ----------");
                Debug.WriteLine($"[NeoForgeService] JAR: {processor.Jar}");
                
                // 检查输出文件是否已存在，避免重复执行
                if (await AreOutputsValidAsync(processor, variables))
                {
                    Debug.WriteLine($"[NeoForgeService] ✅ 输出已存在且有效，跳过处理器");
                    completedCount++;
                    progressCallback?.Invoke($"处理器 {completedCount}/{profile.Processors.Count}", completedCount, profile.Processors.Count, 0, 0);
                    continue;
                }
                
                // 对DOWNLOAD_MOJMAPS任务使用直接下载方式
                if (await TryDirectDownloadMojangMappingsAsync(processor, variables, cancellationToken))
                {
                    Debug.WriteLine($"[NeoForgeService] ✅ 已通过直接下载完成DOWNLOAD_MOJMAPS");
                    completedCount++;
                    progressCallback?.Invoke($"处理器 {completedCount}/{profile.Processors.Count}", completedCount, profile.Processors.Count, 0, 0);
                    continue;
                }
                
                // 执行处理器
                await ExecuteSingleProcessorAsync(processor, variables, gameDirectory, mcVersion);
                
                // 验证输出
                await ValidateOutputsAsync(processor, variables);
                
                completedCount++;
                progressCallback?.Invoke($"处理器 {completedCount}/{profile.Processors.Count}", completedCount, profile.Processors.Count, 0, 0);
                
                Debug.WriteLine($"[NeoForgeService] ✅ 处理器执行成功");
            }
            
            Debug.WriteLine($"[NeoForgeService] ========== 所有处理器执行完成 ==========");
        }

        /// <summary>
        /// 检查处理器输出文件是否已存在且校验通过
        /// </summary>
        private static async Task<bool> AreOutputsValidAsync(
            NeoForgeProcessor processor,
            Dictionary<string, string> variables)
        {
            if (processor.Outputs == null || processor.Outputs.Count == 0)
                return false;
            
            foreach (var output in processor.Outputs)
            {
                var outputPath = ReplaceVariables(output.Key, variables);
                var expectedSha1 = output.Value;
                
                if (!File.Exists(outputPath))
                {
                    return false;
                }

                // 计算SHA-1
                var actualSha1 = await CalculateSHA1Async(outputPath);
                
                if (!string.Equals(actualSha1, expectedSha1, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[NeoForgeService] 输出文件校验失败: {Path.GetFileName(outputPath)}");
                    Debug.WriteLine($"[NeoForgeService]   期望: {expectedSha1}");
                    Debug.WriteLine($"[NeoForgeService]   实际: {actualSha1}");
                    
                    // 删除无效文件
                    try { File.Delete(outputPath); } catch { }
                    return false;
                }
            }
            
            return true;
        }

        /// <summary>
        /// 尝试直接下载Mojang映射文件，跳过Java处理器
        /// </summary>
        private static async Task<bool> TryDirectDownloadMojangMappingsAsync(
            NeoForgeProcessor processor,
            Dictionary<string, string> variables,
            CancellationToken cancellationToken)
        {
            var args = processor.Args.Select(a => ReplaceVariables(a, variables)).ToList();
            
            // 检测是否为DOWNLOAD_MOJMAPS任务
            var taskIndex = args.IndexOf("--task");
            if (taskIndex < 0 || taskIndex + 1 >= args.Count || args[taskIndex + 1] != "DOWNLOAD_MOJMAPS")
                return false;
            
            var sideIndex = args.IndexOf("--side");
            if (sideIndex < 0 || sideIndex + 1 >= args.Count || args[sideIndex + 1] != "client")
                return false;
            
            var versionIndex = args.IndexOf("--version");
            var outputIndex = args.IndexOf("--output");
            
            if (versionIndex < 0 || outputIndex < 0 || 
                versionIndex + 1 >= args.Count || outputIndex + 1 >= args.Count)
                return false;
            
            var mcVersion = args[versionIndex + 1];
            var outputPath = args[outputIndex + 1];
            
            Debug.WriteLine($"[NeoForgeService] 检测到DOWNLOAD_MOJMAPS任务，直接下载映射文件");
            Debug.WriteLine($"[NeoForgeService]   Minecraft版本: {mcVersion}");
            Debug.WriteLine($"[NeoForgeService]   输出路径: {outputPath}");
            
            try
            {
                // 获取version manifest
                var versionManifest = await MinecraftVersionService.GetVersionListAsync();
                if (versionManifest == null)
                {
                    Debug.WriteLine($"[NeoForgeService] 无法获取版本列表");
                    return false;
                }
                
                var targetVersion = versionManifest.Versions.FirstOrDefault(v => v.Id == mcVersion);
                
                if (targetVersion == null)
                {
                    Debug.WriteLine($"[NeoForgeService] 未找到版本 {mcVersion}");
                    return false;
                }
                
                // 下载version.json
                var versionJsonResponse = await _httpClient.GetStringAsync(targetVersion.Url, cancellationToken);
                var versionJson = JsonDocument.Parse(versionJsonResponse);
                
                // 获取client_mappings URL
                if (!versionJson.RootElement.TryGetProperty("downloads", out var downloads) ||
                    !downloads.TryGetProperty("client_mappings", out var clientMappings) ||
                    !clientMappings.TryGetProperty("url", out var urlElement))
                {
                    Debug.WriteLine($"[NeoForgeService] 未找到client_mappings下载信息");
                    return false;
                }
                
                var mappingsUrl = urlElement.GetString();
                if (string.IsNullOrEmpty(mappingsUrl))
                    return false;
                
                // 下载mappings
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                
                await DownloadFileSimpleAsync(mappingsUrl, outputPath, cancellationToken);
                
                Debug.WriteLine($"[NeoForgeService] ✅ 映射文件下载完成: {Path.GetFileName(outputPath)}");
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NeoForgeService] 直接下载映射文件失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 执行单个处理器
        /// </summary>
        private static async Task ExecuteSingleProcessorAsync(
            NeoForgeProcessor processor,
            Dictionary<string, string> variables,
            string gameDirectory,
            string mcVersion)
        {
            var librariesDir = Path.Combine(gameDirectory, "libraries");
            
            // 获取处理器JAR路径
            var processorJarPath = Path.Combine(librariesDir, MavenCoordinateToPath(processor.Jar));
            
            if (!File.Exists(processorJarPath))
            {
                throw new Exception($"处理器JAR不存在: {processorJarPath}");
            }
            
            // 读取主类
            var mainClass = GetMainClassFromJar(processorJarPath);
            
            if (string.IsNullOrEmpty(mainClass))
            {
                throw new Exception($"无法从处理器JAR获取主类: {processorJarPath}");
            }
            
            Debug.WriteLine($"[NeoForgeService] 主类: {mainClass}");
            Debug.WriteLine($"[NeoForgeService] 处理器执行开始...");
            Debug.WriteLine($"[NeoForgeService] 处理器JAR: {processorJarPath}");
            Debug.WriteLine($"[NeoForgeService] 处理器输出验证:");
            
            // 构建classpath
            var classpathParts = new List<string>();
            
            foreach (var cpMaven in processor.Classpath)
            {
                var cpPath = Path.Combine(librariesDir, MavenCoordinateToPath(cpMaven));
                if (File.Exists(cpPath))
                {
                    classpathParts.Add(cpPath);
                }
            }
            
            classpathParts.Add(processorJarPath);
            
            var classpath = string.Join(Path.PathSeparator, classpathParts);
            
            // 解析参数
            var args = processor.Args.Select(a => ReplaceVariables(a, variables)).ToList();
            
            Debug.WriteLine($"[NeoForgeService] 参数: {string.Join(" ", args.Select(a => $"\"{a}\""))}");
            
            // 获取Java路径 - 优先使用配置中的Java
            string javaPath;
            try
            {
                var config = LauncherConfig.Load();
                
                // 优先使用配置中的Java路径（如果存在且有效）
                if (!string.IsNullOrEmpty(config.JavaPath) && File.Exists(config.JavaPath))
                {
                    javaPath = config.JavaPath;
                    Debug.WriteLine($"[NeoForgeService] 使用配置的Java: {javaPath}");
                }
                else
                {
                    // 配置的Java不可用，进行全局检测
                    Debug.WriteLine($"[NeoForgeService] 配置的Java不可用，开始自动检测...");
                    var javaInfo = JavaDetector.SelectBestJava();
                    javaPath = javaInfo?.Path ?? "java";
                    Debug.WriteLine($"[NeoForgeService] 检测到Java: {javaPath}");
                }
            }
            catch
            {
                // 加载配置失败，使用自动检测
                var javaInfo = JavaDetector.SelectBestJava();
                javaPath = javaInfo?.Path ?? "java";
            }
            
            // 构建命令
            var processInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = $"-cp \"{classpath}\" {mainClass} {string.Join(" ", args.Select(a => $"\"{a}\""))}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            Debug.WriteLine($"[NeoForgeService] 执行: {processInfo.FileName} {processInfo.Arguments}");
            
            // 执行进程
            using var process = Process.Start(processInfo);
            
            if (process == null)
            {
                throw new Exception("无法启动处理器进程");
            }
            
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();
            
            // 过滤并输出日志（避免UI堵塞）
            if (!string.IsNullOrWhiteSpace(output))
            {
                // 过滤掉大量的.class文件输出，只保留关键信息
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var importantLines = lines.Where(line => 
                    !line.Trim().EndsWith(".class", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("failed", StringComparison.OrdinalIgnoreCase)
                ).ToList();
                
                if (importantLines.Count > 0)
                {
                    // 如果有重要信息，输出
                    Debug.WriteLine($"[Processor] {string.Join("\n", importantLines)}");
                }
                else if (lines.Length > 0)
                {
                    // 如果全是.class文件，只输出统计信息
                    var classCount = lines.Count(l => l.Trim().EndsWith(".class", StringComparison.OrdinalIgnoreCase));
                    if (classCount > 0)
                    {
                        Debug.WriteLine($"[Processor] 处理了 {classCount} 个类文件");
                    }
                }
            }
            
            if (!string.IsNullOrWhiteSpace(error))
            {
                // 错误信息始终输出
                Debug.WriteLine($"[Processor Error] {error}");
            }
            
            if (process.ExitCode != 0)
            {
                throw new Exception($"处理器执行失败，退出码: {process.ExitCode}");
            }
        }

        /// <summary>
        /// 验证处理器输出
        /// </summary>
        private static async Task ValidateOutputsAsync(
            NeoForgeProcessor processor,
            Dictionary<string, string> variables)
        {
            if (processor.Outputs == null || processor.Outputs.Count == 0)
                return;
            
            foreach (var output in processor.Outputs)
            {
                var outputPath = ReplaceVariables(output.Key, variables);
                var expectedSha1 = output.Value;
                
                if (!File.Exists(outputPath))
                {
                    throw new Exception($"处理器输出文件缺失: {outputPath}");
                }
                
                var actualSha1 = await CalculateSHA1Async(outputPath);
                
                if (!string.Equals(actualSha1, expectedSha1, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(outputPath); } catch { }
                    throw new Exception($"处理器输出校验失败: {Path.GetFileName(outputPath)}\n期望: {expectedSha1}\n实际: {actualSha1}");
                }
            }
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 替换变量
        /// </summary>
        private static string ReplaceVariables(string input, Dictionary<string, string> variables)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            
            // 处理 {VAR_NAME} 格式
            if (input.StartsWith("{") && input.EndsWith("}"))
            {
                var varName = input.Trim('{', '}');
                return variables.TryGetValue(varName, out var value) ? value : input;
            }
            
            // 处理 'literal' 格式
            if (input.StartsWith("'") && input.EndsWith("'"))
            {
                return input.Trim('\'');
            }
            
            // 处理 [maven:coordinate] 格式 - 转换为本地库文件路径
            if (input.StartsWith("[") && input.EndsWith("]"))
            {
                var maven = input.Trim('[', ']');
                
                // 从变量中获取游戏目录
                if (variables.TryGetValue("ROOT", out var gameDirectory))
                {
                    try
                    {
                        var relativePath = MavenCoordinateToPath(maven);
                        var librariesDir = Path.Combine(gameDirectory, "libraries");
                        return Path.Combine(librariesDir, relativePath);
                    }
                    catch
                    {
                        // 如果转换失败，返回原值
                        return input;
                    }
                }
                
                return input;
            }
            
            // 处理混合格式（包含变量的字符串）
            var result = input;
            foreach (var kvp in variables)
            {
                result = result.Replace($"{{{kvp.Key}}}", kvp.Value);
            }
            
            return result;
        }

        /// <summary>
        /// 从JAR获取主类
        /// </summary>
        private static string GetMainClassFromJar(string jarPath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(jarPath);
                var manifestEntry = archive.GetEntry("META-INF/MANIFEST.MF");
                
                if (manifestEntry == null)
                    return "";
                
                using var stream = manifestEntry.Open();
                using var reader = new StreamReader(stream);
                
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (line?.StartsWith("Main-Class:") == true)
                    {
                        return line.Substring("Main-Class:".Length).Trim();
                    }
                }
                
                return "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 计算文件SHA-1
        /// </summary>
        private static async Task<string> CalculateSHA1Async(string filePath)
        {
            using var sha1 = SHA1.Create();
            using var stream = File.OpenRead(filePath);
            var hash = await Task.Run(() => sha1.ComputeHash(stream));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// 清理上一次安装生成的最终文件，保留中间处理结果
        /// </summary>
        private static void CleanFinalOutputs(string gameDirectory, string mcVersion, string neoforgeVersion)
        {
            Debug.WriteLine($"[NeoForgeService] 清理最终输出文件...");
            
            try
            {
                var librariesDir = Path.Combine(gameDirectory, "libraries");
                
                // 删除最终生成的NeoForge客户端JAR
                var neoforgeDir = Path.Combine(librariesDir, "net", "neoforged", "neoforge", neoforgeVersion);
                var clientJar = Path.Combine(neoforgeDir, $"neoforge-{neoforgeVersion}-client.jar");
                
                if (File.Exists(clientJar))
                {
                    File.Delete(clientJar);
                    Debug.WriteLine($"[NeoForgeService] 已删除: {Path.GetFileName(clientJar)}");
                }
                
                // 保留处理器生成的中间文件，支持后续的增量安装
                Debug.WriteLine($"[NeoForgeService] 保留中间文件以支持增量安装");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NeoForgeService] 清理失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 内部文件下载辅助方法
        /// </summary>
        private static async Task DownloadFileSimpleAsync(string url, string destPath, CancellationToken cancellationToken)
        {
            var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            
            await stream.CopyToAsync(fileStream, cancellationToken);
        }

        #endregion
    }
}

