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
    /// Fabric版本信息
    /// </summary>
    public class FabricVersion
    {
        [JsonPropertyName("separator")]
        public string? Separator { get; set; }

        [JsonPropertyName("build")]
        public int Build { get; set; }

        [JsonPropertyName("maven")]
        public string Maven { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("stable")]
        public bool Stable { get; set; }

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName => $"Fabric Loader {Version}";
    }

    /// <summary>
    /// Fabric游戏版本信息
    /// </summary>
    public class FabricGameVersion
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("stable")]
        public bool Stable { get; set; }
    }

    /// <summary>
    /// Fabric安装器版本信息
    /// </summary>
    public class FabricInstallerVersion
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("maven")]
        public string Maven { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("stable")]
        public bool Stable { get; set; }
    }

    /// <summary>
    /// Fabric Meta API响应包装类
    /// </summary>
    public class FabricMetaResponse<T>
    {
        [JsonPropertyName("game")]
        public List<FabricGameVersion>? Game { get; set; }

        [JsonPropertyName("mappings")]
        public List<T>? Mappings { get; set; }

        [JsonPropertyName("loader")]
        public List<FabricVersion>? Loader { get; set; }

        [JsonPropertyName("installer")]
        public List<FabricInstallerVersion>? Installer { get; set; }
    }

    /// <summary>
    /// Fabric服务 - 处理Fabric版本查询和安装
    /// </summary>
    public class FabricService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        // Fabric Meta API
        private const string FABRIC_META_URL = "https://meta.fabricmc.net";
        
        // 使用镜像源获取Fabric元数据
        private const string BMCL_FABRIC_META_URL = "https://bmclapi2.bangbang93.com/fabric-meta";
        private const string BMCL_MAVEN_URL = "https://bmclapi2.bangbang93.com/maven";

        // 官方Maven仓库
        private const string FABRIC_MAVEN_URL = "https://maven.fabricmc.net";

        static FabricService()
        {
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        /// <summary>
        /// 获取Fabric支持的Minecraft版本列表
        /// </summary>
        public static async Task<List<string>> GetSupportedMinecraftVersionsAsync()
        {
            try
            {
                var config = LauncherConfig.Load();
                Debug.WriteLine($"[FabricService] 获取Fabric支持的MC版本列表... (源: {config.DownloadSource})");

                var baseUrl = config.DownloadSource == DownloadSource.BMCLAPI ? BMCL_FABRIC_META_URL : FABRIC_META_URL;
                var url = $"{baseUrl}/v2/versions/game";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var versions = JsonSerializer.Deserialize<List<FabricGameVersion>>(json);

                if (versions != null)
                {
                    var versionList = versions.Select(v => v.Version).ToList();
                    Debug.WriteLine($"[FabricService] 获取到 {versionList.Count} 个支持的MC版本");
                    return versionList;
                }

                return new List<string>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FabricService] 获取Fabric支持版本失败: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// 解析Fabric版本号为可比较的数字（用于排序）
        /// </summary>
        /// <param name="versionString">版本字符串，如 "0.15.11" 或 "0.10.6+build.214"</param>
        /// <returns>可比较的版本号（如 0.015011、0.010006）</returns>
        private static double ParseVersionNumber(string versionString)
        {
            try
            {
                // 移除 +build.xxx 部分
                var mainVersion = versionString.Split('+')[0];
                
                // 分割版本号部分：0.15.11 -> ["0", "15", "11"]
                var parts = mainVersion.Split('.');
                
                // 转换为可比较的数字：0.015011
                // 格式：主版本.次版本(3位).补丁版本(3位)
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
        /// 获取指定Minecraft版本的Fabric Loader版本列表
        /// </summary>
        public static async Task<List<FabricVersion>> GetFabricVersionsAsync(string mcVersion)
        {
            try
            {
                var config = LauncherConfig.Load();
                Debug.WriteLine($"[FabricService] 获取 MC {mcVersion} 的Fabric版本列表... (源: {config.DownloadSource})");

                var baseUrl = config.DownloadSource == DownloadSource.BMCLAPI ? BMCL_FABRIC_META_URL : FABRIC_META_URL;
                var url = $"{baseUrl}/v2/versions/loader";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var fabricVersions = JsonSerializer.Deserialize<List<FabricVersion>>(json);

                if (fabricVersions != null)
                {
                    // 排序规则：
                    // 1. 稳定版（stable=true）优先
                    // 2. 版本号从高到低（按主版本号数字比较）
                    fabricVersions = fabricVersions
                        .OrderByDescending(f => f.Stable) // 稳定版在前
                        .ThenByDescending(f => ParseVersionNumber(f.Version)) // 版本号从高到低
                        .ToList();
                    
                    Debug.WriteLine($"[FabricService] 获取到 {fabricVersions.Count} 个Fabric Loader版本");
                    return fabricVersions;
                }

                return new List<FabricVersion>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FabricService] 获取Fabric版本列表失败: {ex.Message}");
                return new List<FabricVersion>();
            }
        }

        /// <summary>
        /// 下载并安装Fabric
        /// </summary>
        /// <param name="mcVersion">Minecraft版本</param>
        /// <param name="loaderVersion">Fabric Loader版本</param>
        /// <param name="gameDirectory">游戏目录</param>
        /// <param name="customVersionName">自定义版本名称</param>
        /// <param name="progressCallback">进度回调（状态消息, 当前字节数, 速度, 总字节数）</param>
        /// <param name="cancellationToken">取消令牌</param>
        public static async Task<bool> InstallFabricAsync(
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
                Debug.WriteLine($"[FabricService] 开始安装Fabric: MC {mcVersion}, Loader {loaderVersion}");

                progressCallback?.Invoke("正在获取Fabric配置文件...", 0, 0, 100);

                // 1. 获取Fabric profile JSON
                var baseUrl = config.DownloadSource == DownloadSource.BMCLAPI ? BMCL_FABRIC_META_URL : FABRIC_META_URL;
                var profileUrl = $"{baseUrl}/v2/versions/loader/{mcVersion}/{loaderVersion}/profile/json";

                Debug.WriteLine($"[FabricService] 获取Fabric配置: {profileUrl}");

                var profileJson = await _httpClient.GetStringAsync(profileUrl, cancellationToken);
                if (string.IsNullOrEmpty(profileJson))
                {
                    throw new Exception("下载Fabric配置文件失败");
                }

                // 2. 创建临时gameDirectory结构（.temp/versions/ 和 .temp/libraries/）
                var tempGameDir = Path.Combine(gameDirectory, ".temp");
                var tempVersionsDir = Path.Combine(tempGameDir, "versions");
                var tempLibrariesDir = Path.Combine(tempGameDir, "libraries");
                Directory.CreateDirectory(tempVersionsDir);
                Directory.CreateDirectory(tempLibrariesDir);
                var tempVanillaDir = Path.Combine(tempVersionsDir, mcVersion);
                Directory.CreateDirectory(tempVanillaDir);

                // 3. 下载原版文件到临时目录（所有操作都在.temp中）
                progressCallback?.Invoke($"正在下载基础版本 {mcVersion}...", 0, 0, 100);
                Debug.WriteLine($"[FabricService] 开始下载基础MC版本到临时目录: {tempGameDir}");

                // 下载基础MC版本到临时gameDirectory（版本文件到.temp/versions/，库文件到.temp/libraries/）
                var downloadProgressReporter = new System.Progress<ObsMCLauncher.Services.DownloadProgress>(p =>
                {
                    progressCallback?.Invoke(
                        p.Status,
                        p.TotalDownloadedBytes,
                        p.DownloadSpeed,
                        p.TotalBytes
                    );
                });

                // 使用临时gameDirectory下载原版（所有文件都在.temp中）
                var downloadSuccess = await DownloadService.DownloadMinecraftVersion(
                    mcVersion,
                    tempGameDir, // 使用临时gameDirectory，所有操作都在.temp中
                    mcVersion, // 使用原版版本名
                    downloadProgressReporter,
                    cancellationToken
                );

                if (!downloadSuccess)
                {
                    // 清理临时目录
                    if (Directory.Exists(tempGameDir))
                    {
                        try { Directory.Delete(tempGameDir, true); } catch { }
                    }
                    throw new Exception($"下载基础版本 {mcVersion} 失败");
                }

                Debug.WriteLine($"[FabricService] 基础MC版本已下载到临时目录");

                // 将库文件从临时目录移动到真实的libraries目录（如果不存在）
                if (Directory.Exists(tempLibrariesDir))
                {
                    await MoveLibrariesToRealDirectory(tempLibrariesDir, Path.Combine(gameDirectory, "libraries"));
                }

                // 获取临时目录中的原版文件路径
                var tempVanillaJarPath = Path.Combine(tempVanillaDir, $"{mcVersion}.jar");
                var tempVanillaJsonPath = Path.Combine(tempVanillaDir, $"{mcVersion}.json");

                // 获取临时目录路径
                var tempFabricVersionPath = Path.Combine(tempVersionsDir, customVersionName);
                Directory.CreateDirectory(tempFabricVersionPath);
                
                // 从临时原版目录复制文件到Fabric版本目录
                var tempFabricJarPath = Path.Combine(tempFabricVersionPath, $"{customVersionName}.jar");
                var tempFabricJsonPath = Path.Combine(tempFabricVersionPath, $"{customVersionName}.json");
                
                // 复制原版JAR和JSON
                if (File.Exists(tempVanillaJarPath))
                {
                    File.Copy(tempVanillaJarPath, tempFabricJarPath, true);
                    Debug.WriteLine($"[FabricService] ✅ 已复制原版JAR: {tempVanillaJarPath} -> {tempFabricJarPath}");
                }
                else
                {
                    Debug.WriteLine($"[FabricService] ⚠️ 原版JAR不存在: {tempVanillaJarPath}");
                }
                if (File.Exists(tempVanillaJsonPath))
                {
                    File.Copy(tempVanillaJsonPath, tempFabricJsonPath, true);
                    Debug.WriteLine($"[FabricService] ✅ 已复制原版JSON: {tempVanillaJsonPath} -> {tempFabricJsonPath}");
                }
                else
                {
                    Debug.WriteLine($"[FabricService] ⚠️ 原版JSON不存在: {tempVanillaJsonPath}");
                }

                progressCallback?.Invoke("正在安装Fabric配置文件...", 50, 0, 100);

                // 4. 备份原版JSON（用于获取releaseTime）
                var savedVanillaJsonPath = Path.Combine(tempFabricVersionPath, $"{mcVersion}.json");
                if (File.Exists(tempVanillaJsonPath))
                {
                    File.Copy(tempVanillaJsonPath, savedVanillaJsonPath, true);
                    Debug.WriteLine($"[FabricService] 已备份原版JSON到: {savedVanillaJsonPath}");
                }

                // 5. 修改Fabric profile JSON
                var profileObj = JsonSerializer.Deserialize<JsonElement>(profileJson);
                var modifiedProfile = ModifyFabricProfile(profileObj, customVersionName, mcVersion, savedVanillaJsonPath);

                // 6. 保存Fabric版本JSON
                await File.WriteAllTextAsync(tempFabricJsonPath, modifiedProfile, cancellationToken);
                Debug.WriteLine($"[FabricService] Fabric配置文件已保存: {tempFabricJsonPath}");

                // 7. 下载Fabric库文件（使用真实gameDirectory，因为libraries是共享的）
                progressCallback?.Invoke("正在下载Fabric库文件...", 70, 0, 100);
                await DownloadFabricLibrariesAsync(
                    modifiedProfile,
                    gameDirectory, // libraries目录使用真实gameDirectory
                    config.DownloadSource,
                    progressCallback,
                    cancellationToken
                );

                // 8. 原版文件保留在.temp中，不移动到标准位置（所有操作都在.temp中）
                // 如果需要合并父版本信息，将从.temp读取原版JSON
                Debug.WriteLine($"[FabricService] ✅ 原版文件保留在临时目录: {tempVanillaDir}");

                // 9. 将临时目录移动到最终位置
                progressCallback?.Invoke("正在完成安装...", 95, 0, 100);
                var finalFabricVersionPath = Path.Combine(gameDirectory, "versions", customVersionName);
                
                // 如果最终位置已存在，先删除
                if (Directory.Exists(finalFabricVersionPath))
                {
                    try { Directory.Delete(finalFabricVersionPath, true); } catch { }
                }
                
                // 移动临时目录到最终位置
                // 确保jar文件存在
                var finalJarPath = Path.Combine(tempFabricVersionPath, $"{customVersionName}.jar");
                if (!File.Exists(finalJarPath))
                {
                    Debug.WriteLine($"[FabricService] ⚠️ 警告：JAR文件不存在: {finalJarPath}");
                }
                else
                {
                    Debug.WriteLine($"[FabricService] ✅ JAR文件已确认存在: {finalJarPath}");
                }
                
                Directory.Move(tempFabricVersionPath, finalFabricVersionPath);
                Debug.WriteLine($"[FabricService] ✅ 已移动Fabric版本到最终位置: {finalFabricVersionPath}");

                // 10. 清理临时目录
                if (Directory.Exists(tempGameDir))
                {
                    try { Directory.Delete(tempGameDir, true); } catch { }
                }

                progressCallback?.Invoke("Fabric安装完成！", 100, 0, 100);
                Debug.WriteLine($"[FabricService] Fabric安装完成: {customVersionName}");

                return true;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[FabricService] Fabric安装已取消");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FabricService] Fabric安装失败: {ex.Message}");
                Debug.WriteLine($"[FabricService] 堆栈跟踪: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// 将库文件从临时目录移动到真实的libraries目录（如果不存在）
        /// </summary>
        private static Task MoveLibrariesToRealDirectory(string tempLibrariesDir, string realLibrariesDir)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(tempLibrariesDir))
                        return;

                    Directory.CreateDirectory(realLibrariesDir);

                    var tempLibFiles = Directory.GetFiles(tempLibrariesDir, "*.*", SearchOption.AllDirectories);
                    int movedCount = 0;

                    foreach (var tempFile in tempLibFiles)
                    {
                        var relativePath = Path.GetRelativePath(tempLibrariesDir, tempFile);
                        var realFile = Path.Combine(realLibrariesDir, relativePath);

                        // 如果真实位置已存在，跳过
                        if (File.Exists(realFile))
                            continue;

                        // 确保目标目录存在
                        Directory.CreateDirectory(Path.GetDirectoryName(realFile)!);

                        // 移动文件（如果失败则复制）
                        try
                        {
                            File.Move(tempFile, realFile, true);
                        }
                        catch
                        {
                            File.Copy(tempFile, realFile, true);
                        }

                        movedCount++;
                    }

                    Debug.WriteLine($"[FabricService] ✅ 已移动 {movedCount} 个库文件到真实libraries目录");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FabricService] ⚠️ 移动库文件失败: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 修改Fabric profile JSON
        /// </summary>
        private static string ModifyFabricProfile(JsonElement profileObj, string customVersionName, string mcVersion, string mcJsonPath)
        {
            try
            {
                // 使用JsonDocument保持原始JSON结构
                using var doc = JsonDocument.Parse(profileObj.GetRawText());
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    
                    foreach (var property in doc.RootElement.EnumerateObject())
                    {
                        if (property.Name == "id")
                        {
                            // 修改版本ID
                            writer.WriteString("id", customVersionName);
                        }
                        else if (property.Name == "inheritsFrom")
                        {
                            // 修改继承的MC版本
                            writer.WriteString("inheritsFrom", mcVersion);
                        }
                        else if (property.Name == "releaseTime" || property.Name == "time")
                        {
                            // 保持原始时间格式，或使用当前时间的ISO 8601格式
                            try
                            {
                                writer.WritePropertyName(property.Name);
                                property.Value.WriteTo(writer);
                            }
                            catch
                            {
                                // 如果原始格式有问题，使用ISO 8601格式
                                writer.WriteString(property.Name, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00"));
                            }
                        }
                        else if (property.Name == "arguments")
                        {
                            // 特殊处理arguments字段，清理JVM参数中的空格
                            writer.WritePropertyName("arguments");
                            writer.WriteStartObject();
                            
                            foreach (var argProperty in property.Value.EnumerateObject())
                            {
                                writer.WritePropertyName(argProperty.Name);
                                
                                if (argProperty.Name == "jvm" && argProperty.Value.ValueKind == JsonValueKind.Array)
                                {
                                    // 清理JVM参数
                                    writer.WriteStartArray();
                                    foreach (var arg in argProperty.Value.EnumerateArray())
                                    {
                                        if (arg.ValueKind == JsonValueKind.String)
                                        {
                                            var argStr = arg.GetString() ?? "";
                                            // 修复 "-DFabricMcEmu= net.minecraft.client.main.Main " 这种格式
                                            if (argStr.Contains("-DFabricMcEmu="))
                                            {
                                                // 移除 = 后面的空格和末尾的空格
                                                argStr = argStr.Replace("-DFabricMcEmu= ", "-DFabricMcEmu=").Trim();
                                            }
                                            writer.WriteStringValue(argStr);
                                        }
                                        else
                                        {
                                            arg.WriteTo(writer);
                                        }
                                    }
                                    writer.WriteEndArray();
                                }
                                else
                                {
                                    argProperty.Value.WriteTo(writer);
                                }
                            }
                            
                            writer.WriteEndObject();
                        }
                        else
                        {
                            // 保持其他字段不变
                            writer.WritePropertyName(property.Name);
                            property.Value.WriteTo(writer);
                        }
                    }
                    
                    // 如果没有releaseTime、time或assetIndex字段，从基础MC版本JSON中获取
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
                                if (mcDoc.RootElement.TryGetProperty("releaseTime", out var releaseTime))
                                {
                                    writer.WriteString("releaseTime", releaseTime.GetString());
                                }
                                else
                                {
                                    writer.WriteString("releaseTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00"));
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
                                
                                // ⭐ 复制assetIndex（Fabric profile中没有，需要从原版JSON复制）
                                if (!doc.RootElement.TryGetProperty("assetIndex", out _))
                                {
                                    if (mcDoc.RootElement.TryGetProperty("assetIndex", out var assetIndex))
                                    {
                                        writer.WritePropertyName("assetIndex");
                                        assetIndex.WriteTo(writer);
                                        Debug.WriteLine($"[Fabric] 已从原版JSON复制assetIndex: {assetIndex.GetProperty("id").GetString()}");
                                    }
                                }
                            }
                            else
                            {
                                // 如果基础版本JSON不存在，使用当前时间
                                writer.WriteString("releaseTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00"));
                                if (!doc.RootElement.TryGetProperty("time", out _))
                                {
                                    writer.WriteString("time", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00"));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[FabricService] 获取releaseTime失败: {ex.Message}");
                            writer.WriteString("releaseTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00"));
                        }
                    }
                    
                    writer.WriteEndObject();
                }
                
                return System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FabricService] 修改Fabric profile失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 下载Fabric库文件
        /// </summary>
        private static async Task DownloadFabricLibrariesAsync(
            string profileJson,
            string gameDirectory,
            DownloadSource downloadSource,
            Action<string, long, double, long>? progressCallback,
            CancellationToken cancellationToken)
        {
            try
            {
                // 解析profile JSON获取库文件列表
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var profile = JsonSerializer.Deserialize<FabricProfileInfo>(profileJson, options);

                if (profile?.Libraries == null || profile.Libraries.Count == 0)
                {
                    Debug.WriteLine("[FabricService] 没有需要下载的库文件");
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
                        // 获取库文件路径
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
                            
                            // 构造URL
                            var mavenBaseUrl = downloadSource == DownloadSource.BMCLAPI ? BMCL_MAVEN_URL : FABRIC_MAVEN_URL;
                            libUrl = $"{mavenBaseUrl}/{libPath}";
                        }

                        if (string.IsNullOrEmpty(libPath) || string.IsNullOrEmpty(libUrl))
                        {
                            Debug.WriteLine($"[FabricService] 跳过无效库: {library.Name}");
                            continue;
                        }

                        var localPath = Path.Combine(librariesPath, libPath.Replace('/', Path.DirectorySeparatorChar));

                        // 如果文件已存在且大小大于0，跳过
                        if (File.Exists(localPath))
                        {
                            var fileInfo = new FileInfo(localPath);
                            if (fileInfo.Length > 0)
                            {
                                downloadedLibs++;
                                Debug.WriteLine($"[FabricService] 库文件已存在，跳过: {Path.GetFileName(localPath)}");
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

                        try
                        {
                            await DownloadFileAsync(libUrl, localPath, cancellationToken);
                            downloadedLibs++;
                            Debug.WriteLine($"[FabricService] 已下载库: {Path.GetFileName(localPath)} ({downloadedLibs}/{totalLibs})");
                        }
                        catch (HttpRequestException httpEx) when (httpEx.Message.Contains("304"))
                        {
                            // 304 Not Modified - 如果文件已存在，视为成功
                            if (File.Exists(localPath) && new FileInfo(localPath).Length > 0)
                            {
                                downloadedLibs++;
                                Debug.WriteLine($"[FabricService] 库文件已是最新（304）: {Path.GetFileName(localPath)}");
                            }
                            else
                            {
                                Debug.WriteLine($"[FabricService] 收到304但文件不存在，可能需要重新下载: {library.Name}");
                                throw;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[FabricService] 下载库文件失败: {library.Name} - {ex.Message}");
                        // 继续下载其他库，但记录失败
                    }
                }

                Debug.WriteLine($"[FabricService] 库文件下载完成: {downloadedLibs}/{totalLibs}");
                
                // 验证关键库文件是否存在
                var criticalLibraries = new[]
                {
                    "net/fabricmc/fabric-loader",
                    "net/fabricmc/sponge-mixin"
                };
                
                var missingCriticalLibs = new List<string>();
                foreach (var libPrefix in criticalLibraries)
                {
                    var libDir = Path.Combine(librariesPath, libPrefix);
                    if (Directory.Exists(libDir))
                    {
                        var jarFiles = Directory.GetFiles(libDir, "*.jar", SearchOption.AllDirectories);
                        if (jarFiles.Length == 0)
                        {
                            missingCriticalLibs.Add(libPrefix);
                        }
                    }
                    else
                    {
                        missingCriticalLibs.Add(libPrefix);
                    }
                }
                
                if (missingCriticalLibs.Count > 0)
                {
                    throw new Exception($"关键Fabric库文件缺失: {string.Join(", ", missingCriticalLibs)}。请检查网络连接或重试安装。");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FabricService] 下载Fabric库文件失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 下载文件
        /// </summary>
        private static async Task DownloadFileAsync(string url, string savePath, CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            // 304 Not Modified 表示文件已是最新，视为成功
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                // 如果文件已存在，视为成功
                if (File.Exists(savePath) && new FileInfo(savePath).Length > 0)
                {
                    Debug.WriteLine($"[FabricService] 文件已是最新（304）: {Path.GetFileName(savePath)}");
                    return;
                }
                // 如果文件不存在但返回304，抛出异常让上层处理
                throw new HttpRequestException($"收到304状态码但文件不存在: {Path.GetFileName(savePath)}");
            }
            
            // 检查其他状态码
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"下载失败: {response.StatusCode} - {url}");
            }

            // 如果文件已存在且大小匹配，跳过下载
            if (File.Exists(savePath) && response.Content.Headers.ContentLength.HasValue)
            {
                var fileInfo = new FileInfo(savePath);
                if (fileInfo.Length == response.Content.Headers.ContentLength.Value && fileInfo.Length > 0)
                {
                    Debug.WriteLine($"[FabricService] 文件已存在且大小匹配，跳过下载: {Path.GetFileName(savePath)}");
                    return;
                }
            }

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            await contentStream.CopyToAsync(fileStream, 8192, cancellationToken);
        }

        /// <summary>
        /// 将Maven坐标转换为路径
        /// 例如: net.fabricmc:fabric-loader:0.15.0 => net/fabricmc/fabric-loader/0.15.0/fabric-loader-0.15.0.jar
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

        private class FabricProfileInfo
        {
            public List<FabricLibrary>? Libraries { get; set; }
        }

        private class FabricLibrary
        {
            public string? Name { get; set; }
            public string? Url { get; set; }
            public FabricLibraryDownloads? Downloads { get; set; }
        }

        private class FabricLibraryDownloads
        {
            public FabricArtifact? Artifact { get; set; }
        }

        private class FabricArtifact
        {
            public string? Path { get; set; }
            public string? Url { get; set; }
            public string? Sha1 { get; set; }
            public long Size { get; set; }
        }

        #endregion
    }
}

