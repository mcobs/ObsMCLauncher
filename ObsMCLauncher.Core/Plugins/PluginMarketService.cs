using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Plugins
{
    /// <summary>
    /// 插件市场索引
    /// </summary>
    public class PluginMarketIndex
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;
        
        [JsonPropertyName("lastUpdate")]
        public string LastUpdate { get; set; } = string.Empty;
        
        [JsonPropertyName("plugins")]
        public List<MarketPlugin> Plugins { get; set; } = new();
    }
    
    /// <summary>
    /// 插件分类信息
    /// </summary>
    public class PluginCategory
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
        
        [JsonPropertyName("icon")]
        public string Icon { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// 分类索引
    /// </summary>
    public class CategoryIndex
    {
        [JsonPropertyName("categories")]
        public List<PluginCategory> Categories { get; set; } = new();
    }
    
    /// <summary>
    /// 市场插件信息
    /// </summary>
    public class MarketPlugin
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;
        
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;
        
        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;
        
        [JsonPropertyName("icon")]
        public string? Icon { get; set; }
        
        [JsonPropertyName("repository")]
        public string? Repository { get; set; }
        
        [JsonPropertyName("releaseUrl")]
        public string? ReleaseUrl { get; set; }
        
        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("assetPattern")]
        public string? AssetPattern { get; set; }
        
        /// <summary>支持的最低插件 API 版本（留空 = 不设下限）</summary>
        [JsonPropertyName("minPluginApiVersion")]
        public string? MinPluginApiVersion { get; set; }

        /// <summary>支持的最高插件 API 版本（留空 = 支持到最新版）</summary>
        [JsonPropertyName("maxPluginApiVersion")]
        public string? MaxPluginApiVersion { get; set; }
        
        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();
        
        [JsonPropertyName("downloads")]
        public int Downloads { get; set; }
        
        [JsonPropertyName("rating")]
        public double Rating { get; set; }

        [JsonIgnore]
        public string? LatestVersion { get; set; }
        
        [JsonIgnore]
        public string? LatestDownloadUrl { get; set; }
        
        [JsonIgnore]
        public bool HasUpdate { get; set; }

        /// <summary>
        /// 支持的平台列表
        /// </summary>
        public List<string> Platforms
        {
            get
            {
                var platforms = new List<string>();
                foreach (var tag in Tags)
                {
                    var lowerTag = tag.ToLowerInvariant();
                    if (lowerTag == "windows" || lowerTag == "win")
                        platforms.Add("Windows");
                    else if (lowerTag == "linux")
                        platforms.Add("Linux");
                    else if (lowerTag == "macos" || lowerTag == "mac" || lowerTag == "osx")
                        platforms.Add("macOS");
                    else if (lowerTag == "android")
                        platforms.Add("Android");
                }
                return platforms;
            }
        }

        /// <summary>
        /// 检查是否支持当前平台
        /// </summary>
        public bool SupportsCurrentPlatform
        {
            get
            {
                var platforms = Platforms;
                if (platforms.Count == 0) return true; // 没有平台标签则默认支持所有平台

                var currentPlatform = GetCurrentPlatform();
                return platforms.Contains(currentPlatform);
            }
        }

        /// <summary>
        /// 当前启动器的插件 API 版本是否落在声明的 [最小, 最大] 区间内。
        /// 两个字段都留空 = 不做限制；最大留空 = 支持到最新版。
        /// </summary>
        public bool IsApiVersionCompatible
        {
            get
            {
                var current = PluginApi.Version;

                if (!string.IsNullOrWhiteSpace(MinPluginApiVersion) &&
                    VersionCompare.Compare(current, MinPluginApiVersion) < 0)
                    return false;

                if (!string.IsNullOrWhiteSpace(MaxPluginApiVersion) &&
                    VersionCompare.Compare(current, MaxPluginApiVersion) > 0)
                    return false;

                return true;
            }
        }

        /// <summary>不兼容原因（兼容时为 null），用于界面提示</summary>
        public string? IncompatibleReason
        {
            get
            {
                if (IsApiVersionCompatible) return null;

                var hasMin = !string.IsNullOrWhiteSpace(MinPluginApiVersion);
                var hasMax = !string.IsNullOrWhiteSpace(MaxPluginApiVersion);

                var range = (hasMin, hasMax) switch
                {
                    (true, true) => $"{MinPluginApiVersion} ~ {MaxPluginApiVersion}",
                    (true, false) => $"≥ {MinPluginApiVersion}",
                    (false, true) => $"≤ {MaxPluginApiVersion}",
                    _ => "*"
                };

                return $"需要插件 API {range}，当前启动器为 {PluginApi.Version}";
            }
        }

        private static string GetCurrentPlatform()
        {
            if (OperatingSystem.IsWindows()) return "Windows";
            if (OperatingSystem.IsLinux()) return "Linux";
            if (OperatingSystem.IsMacOS()) return "macOS";
            if (OperatingSystem.IsAndroid()) return "Android";
            return "Unknown";
        }
    }
    
    /// <summary>
    /// 插件市场服务
    /// </summary>
    public class PluginMarketService
    {
        private const string MARKET_INDEX_URL = "https://raw.githubusercontent.com/mcobs/ObsMCLauncher-PluginMarket/main/plugins.json";
        private const string CATEGORY_INDEX_URL = "https://raw.githubusercontent.com/mcobs/ObsMCLauncher-PluginMarket/main/categories.json";

        /// <summary>plugin.json 允许的最大体积（防止畸形包把内存打满）</summary>
        private const long MaxManifestBytes = 1024 * 1024;
        
        private static readonly HttpClient _httpClient;
        private static PluginMarketIndex? _cachedIndex;
        private static List<PluginCategory>? _cachedCategories;
        private static DateTime _lastFetchTime = DateTime.MinValue;
        private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(10);
        
        static PluginMarketService()
        {
            _httpClient = HttpClientFactory.CreateClient(timeout: TimeSpan.FromSeconds(30));
            _httpClient.DefaultRequestHeaders.Add("User-Agent", VersionInfo.UserAgent);
        }
        
        /// <summary>
        /// 获取插件市场索引
        /// </summary>
        public static async Task<PluginMarketIndex?> GetMarketIndexAsync(bool forceRefresh = false)
        {
            try
            {
                if (!forceRefresh && _cachedIndex != null && DateTime.Now - _lastFetchTime < _cacheDuration)
                {
                    DebugLogger.Info("PluginMarket", "使用缓存的市场索引");
                    return _cachedIndex;
                }

                DebugLogger.Info("PluginMarket", "正在获取插件市场索引...");
                
                var url = GitHubProxyHelper.WithProxy(MARKET_INDEX_URL);
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var json = await response.Content.ReadAsStringAsync();
                var index = JsonSerializer.Deserialize<PluginMarketIndex>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                _cachedIndex = index;
                _lastFetchTime = DateTime.Now;
                
                DebugLogger.Info("PluginMarket", $"成功获取 {index?.Plugins?.Count ?? 0} 个插件");
                return index;
            }
            catch (Exception ex)
            {
                DebugLogger.Error("PluginMarket", $"获取插件市场索引失败: {ex.Message}");
                return _cachedIndex;
            }
        }
        
        /// <summary>
        /// 获取插件分类列表
        /// </summary>
        public static async Task<List<PluginCategory>?> GetCategoriesAsync(bool forceRefresh = false)
        {
            try
            {
                if (!forceRefresh && _cachedCategories != null && DateTime.Now - _lastFetchTime < _cacheDuration)
                {
                    return _cachedCategories;
                }

                DebugLogger.Info("PluginMarket", "正在获取插件分类...");
                
                var url = GitHubProxyHelper.WithProxy(CATEGORY_INDEX_URL);
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var json = await response.Content.ReadAsStringAsync();
                var categoryIndex = JsonSerializer.Deserialize<CategoryIndex>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                _cachedCategories = categoryIndex?.Categories;
                
                DebugLogger.Info("PluginMarket", $"成功获取 {categoryIndex?.Categories?.Count ?? 0} 个分类");
                return _cachedCategories;
            }
            catch (Exception ex)
            {
                DebugLogger.Error("PluginMarket", $"获取插件分类失败: {ex.Message}");
                return _cachedCategories;
            }
        }

        /// <summary>
        /// 获取支持当前平台的插件列表
        /// </summary>
        public static async Task<List<MarketPlugin>?> GetPluginsForCurrentPlatformAsync()
        {
            var index = await GetMarketIndexAsync();
            if (index?.Plugins == null) return null;

            var currentPlatform = GetCurrentPlatform();
            DebugLogger.Info("PluginMarket", $"筛选支持 {currentPlatform} 的插件");

            return index.Plugins.FindAll(p => p.SupportsCurrentPlatform);
        }

        private static string GetCurrentPlatform()
        {
            if (OperatingSystem.IsWindows()) return "Windows";
            if (OperatingSystem.IsLinux()) return "Linux";
            if (OperatingSystem.IsMacOS()) return "macOS";
            if (OperatingSystem.IsAndroid()) return "Android";
            return "Unknown";
        }
        
        /// <summary>
        /// 下载并安装插件（全新安装或覆盖安装）。
        /// <b>覆盖安装会先删除插件目录</b>，因此更新已安装插件请走
        /// <see cref="PluginUpdateService"/>——它带备份、插件数据保留与失败回滚。
        /// </summary>
        public static async Task<bool> DownloadAndInstallPluginAsync(
            MarketPlugin plugin,
            string pluginsDirectory,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            string? tempZipPath = null;
            try
            {
                DebugLogger.Info("PluginMarket", $"开始下载插件: {plugin.Name}");
                progress?.Report(0);

                // 如果有releaseUrl，先获取最新版本信息
                if (!string.IsNullOrEmpty(plugin.ReleaseUrl) && string.IsNullOrEmpty(plugin.LatestDownloadUrl))
                {
                    var (latestVer, latestUrl) = await GetLatestReleaseInfoAsync(
                        plugin.ReleaseUrl,
                        plugin.AssetPattern,
                        plugin.Id);

                    if (!string.IsNullOrEmpty(latestUrl))
                    {
                        plugin.LatestVersion = latestVer;
                        plugin.LatestDownloadUrl = latestUrl;
                    }
                }

                var downloadUrl = GetDownloadUrl(plugin);
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    DebugLogger.Warn("PluginMarket", $"无法获取下载地址: {plugin.Name}");
                    return false;
                }

                tempZipPath = Path.Combine(
                    Path.GetTempPath(),
                    $"{SafeFileComponent(plugin.Id)}-{Guid.NewGuid():N}.zip");

                if (!await DownloadToFileAsync(downloadUrl, tempZipPath, progress, 0, 50, cancellationToken))
                    return false;

                DebugLogger.Info("PluginMarket", $"下载完成，开始安装: {plugin.Name}");
                progress?.Report(50);

                var validationError = ValidatePluginPackage(tempZipPath, plugin.Id);
                if (validationError != null)
                {
                    DebugLogger.Error("PluginMarket", $"插件包校验失败 [{plugin.Name}]: {validationError}");
                    return false;
                }

                var pluginTargetDir = Path.Combine(pluginsDirectory, plugin.Id);

                if (Directory.Exists(pluginTargetDir))
                {
                    Directory.Delete(pluginTargetDir, true);
                }

                Directory.CreateDirectory(pluginTargetDir);

                SafeZipExtractor.ExtractToDirectory(tempZipPath, pluginTargetDir);

                progress?.Report(100);
                DebugLogger.Info("PluginMarket", $"插件安装成功: {plugin.Name}");

                return true;
            }
            catch (OperationCanceledException)
            {
                DebugLogger.Info("PluginMarket", $"插件下载已取消: {plugin.Name}");
                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.Error("PluginMarket", $"插件下载/安装失败: {ex.Message}");
                return false;
            }
            finally
            {
                TryDeleteFile(tempZipPath);
            }
        }

        /// <summary>
        /// 把 URL 下载到本地文件，进度映射到 [progressFrom, progressTo] 区间。
        /// 下载地址自动套 GitHub 镜像；取消会向上抛 <see cref="OperationCanceledException"/>。
        /// </summary>
        public static async Task<bool> DownloadToFileAsync(
            string url,
            string destinationPath,
            IProgress<double>? progress = null,
            double progressFrom = 0,
            double progressTo = 100,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var requestUrl = GitHubProxyHelper.WithProxy(url);
                DebugLogger.Info("PluginMarket", $"下载地址: {requestUrl}");

                var response = await _httpClient.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var canReportProgress = totalBytes > 0;
                var span = progressTo - progressFrom;

                var dir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (var fileStream = File.Create(destinationPath))
                using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) != 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                        totalRead += bytesRead;

                        if (canReportProgress)
                        {
                            progress?.Report(progressFrom + (totalRead * span) / totalBytes);
                        }
                    }
                }

                progress?.Report(progressTo);
                return true;
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(destinationPath);
                throw;
            }
            catch (Exception ex)
            {
                DebugLogger.Error("PluginMarket", $"下载失败: {ex.Message}");
                TryDeleteFile(destinationPath);
                return false;
            }
        }

        /// <summary>
        /// 校验插件包：必须在压缩包<b>根目录</b>能解析出 plugin.json，且 id 与预期一致。
        /// 这是安装/更新的最后一道闸门——防止结构不对的包把插件目录写坏。
        /// </summary>
        /// <param name="zipPath">插件包路径</param>
        /// <param name="expectedPluginId">市场登记的插件 id；为空则只校验清单自身完整性</param>
        /// <returns>校验通过返回 null，否则返回失败原因</returns>
        public static string? ValidatePluginPackage(string zipPath, string? expectedPluginId = null)
        {
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);

                var manifestEntry = archive.Entries.FirstOrDefault(e =>
                    string.Equals(e.FullName.Replace('\\', '/').TrimStart('/'), "plugin.json", StringComparison.OrdinalIgnoreCase));

                if (manifestEntry == null)
                    return "压缩包根目录缺少 plugin.json";

                if (manifestEntry.Length > MaxManifestBytes)
                    return "plugin.json 体积异常";

                string json;
                using (var stream = manifestEntry.Open())
                using (var reader = new StreamReader(stream))
                {
                    json = reader.ReadToEnd();
                }

                // 与 PluginLoader.LoadPlugin 用同一套反序列化方式，避免"校验通过但装不上"
                var metadata = JsonSerializer.Deserialize<PluginMetadata>(json);

                if (metadata == null || string.IsNullOrWhiteSpace(metadata.Id))
                    return "plugin.json 缺少 id 字段";

                if (!string.IsNullOrWhiteSpace(expectedPluginId) &&
                    !string.Equals(metadata.Id, expectedPluginId, StringComparison.OrdinalIgnoreCase))
                {
                    return $"plugin.json 的 id ({metadata.Id}) 与市场登记 ({expectedPluginId}) 不一致";
                }

                if (string.IsNullOrWhiteSpace(metadata.Version))
                    return "plugin.json 缺少 version 字段";

                return null;
            }
            catch (Exception ex)
            {
                return $"无法解析插件包: {ex.Message}";
            }
        }

        /// <summary>把任意字符串压成安全的文件名片段（插件 id 理论上已受正则约束，这里只做兜底）</summary>
        private static string SafeFileComponent(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Where(c => !invalid.Contains(c)).ToArray();
            return chars.Length == 0 ? "plugin" : new string(chars);
        }

        private static void TryDeleteFile(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 卸载插件
        /// </summary>
        public static bool UninstallPlugin(string pluginId, string pluginsDirectory)
        {
            try
            {
                var pluginDir = Path.Combine(pluginsDirectory, pluginId);
                
                if (Directory.Exists(pluginDir))
                {
                    Directory.Delete(pluginDir, true);
                    DebugLogger.Info("PluginMarket", $"插件卸载成功: {pluginId}");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.Error("PluginMarket", $"插件卸载失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从GitHub Release获取最新版本信息
        /// </summary>
        public static async Task<(string? version, string? downloadUrl)> GetLatestReleaseInfoAsync(
            string releaseUrl, 
            string? assetPattern = null,
            string? pluginId = null)
        {
            try
            {
                if (string.IsNullOrEmpty(releaseUrl))
                    return (null, null);

                var url = GitHubProxyHelper.WithProxy(releaseUrl);
                DebugLogger.Info("PluginMarket", $"获取最新版本: {url}");

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tagName = root.TryGetProperty("tag_name", out var tagEl) 
                    ? tagEl.GetString()?.TrimStart('v', 'V') 
                    : null;

                if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                    return (tagName, null);

                string? downloadUrl = null;
                var currentPlatform = GetCurrentPlatform().ToLowerInvariant();

                foreach (var asset in assets.EnumerateArray())
                {
                    var assetName = asset.TryGetProperty("name", out var nameEl) 
                        ? nameEl.GetString()?.ToLowerInvariant() ?? "" 
                        : "";

                    var assetUrl = asset.TryGetProperty("browser_download_url", out var urlEl) 
                        ? urlEl.GetString() 
                        : null;

                    if (string.IsNullOrEmpty(assetUrl)) continue;

                    if (!assetName.EndsWith(".zip")) continue;

                    if (!string.IsNullOrEmpty(assetPattern))
                    {
                        if (assetName.Contains(assetPattern.ToLowerInvariant()))
                        {
                            downloadUrl = assetUrl;
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(pluginId))
                    {
                        var normalizedPluginId = pluginId.ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "");
                        var normalizedAssetName = assetName.Replace("-", "").Replace("_", "").Replace(" ", "");
                        
                        if (normalizedAssetName.Contains(normalizedPluginId))
                        {
                            var matchesPlatform = currentPlatform switch
                            {
                                "windows" => assetName.Contains("win") || assetName.Contains("windows") || (!assetName.Contains("linux") && !assetName.Contains("osx") && !assetName.Contains("macos") && !assetName.Contains("mac")),
                                "linux" => assetName.Contains("linux"),
                                "macos" => assetName.Contains("osx") || assetName.Contains("macos") || assetName.Contains("mac"),
                                _ => true
                            };
                            
                            if (matchesPlatform)
                            {
                                downloadUrl = assetUrl;
                                break;
                            }
                        }
                    }

                    if (downloadUrl == null)
                    {
                        downloadUrl = assetUrl;
                    }
                }

                DebugLogger.Info("PluginMarket", $"最新版本: {tagName}, 下载地址: {downloadUrl}");
                return (tagName, downloadUrl);
            }
            catch (Exception ex)
            {
                DebugLogger.Error("PluginMarket", $"获取最新版本失败: {ex.Message}");
                return (null, null);
            }
        }

        /// <summary>
        /// 获取插件的下载URL（优先使用最新版本）
        /// </summary>
        public static string GetDownloadUrl(MarketPlugin plugin)
        {
            if (!string.IsNullOrEmpty(plugin.LatestDownloadUrl))
                return plugin.LatestDownloadUrl;
            
            if (!string.IsNullOrEmpty(plugin.DownloadUrl))
                return plugin.DownloadUrl;

            return string.Empty;
        }
    }
}
