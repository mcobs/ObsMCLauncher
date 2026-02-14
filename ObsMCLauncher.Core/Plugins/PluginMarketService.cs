using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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

        [JsonPropertyName("readme")]
        public string? Readme { get; set; }
        
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
        
        [JsonPropertyName("minLauncherVersion")]
        public string MinLauncherVersion { get; set; } = "1.0.0";
        
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
        
        private static readonly HttpClient _httpClient;
        private static PluginMarketIndex? _cachedIndex;
        private static List<PluginCategory>? _cachedCategories;
        private static DateTime _lastFetchTime = DateTime.MinValue;
        private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(10);
        
        static PluginMarketService()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ObsMCLauncher/1.0");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
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
                    Debug.WriteLine($"[PluginMarket] 使用缓存的市场索引");
                    return _cachedIndex;
                }

                Debug.WriteLine($"[PluginMarket] 正在获取插件市场索引...");
                
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
                
                Debug.WriteLine($"[PluginMarket] 成功获取 {index?.Plugins?.Count ?? 0} 个插件");
                return index;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginMarket] 获取插件市场索引失败: {ex.Message}");
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

                Debug.WriteLine($"[PluginMarket] 正在获取插件分类...");
                
                var url = GitHubProxyHelper.WithProxy(CATEGORY_INDEX_URL);
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var json = await response.Content.ReadAsStringAsync();
                var categoryIndex = JsonSerializer.Deserialize<CategoryIndex>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                _cachedCategories = categoryIndex?.Categories;
                
                Debug.WriteLine($"[PluginMarket] 成功获取 {categoryIndex?.Categories?.Count ?? 0} 个分类");
                return _cachedCategories;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginMarket] 获取插件分类失败: {ex.Message}");
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
            Debug.WriteLine($"[PluginMarket] 筛选支持 {currentPlatform} 的插件");

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
        /// 下载并安装插件
        /// </summary>
        public static async Task<bool> DownloadAndInstallPluginAsync(
            MarketPlugin plugin,
            string pluginsDirectory,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Debug.WriteLine($"[PluginMarket] 开始下载插件: {plugin.Name}");
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
                    Debug.WriteLine($"[PluginMarket] 无法获取下载地址: {plugin.Name}");
                    return false;
                }

                downloadUrl = GitHubProxyHelper.WithProxy(downloadUrl);
                Debug.WriteLine($"[PluginMarket] 下载地址: {downloadUrl}");
                
                var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                
                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var canReportProgress = totalBytes != -1;
                
                var tempZipPath = Path.Combine(Path.GetTempPath(), $"{plugin.Id}.zip");
                
                using (var fileStream = File.Create(tempZipPath))
                using (var contentStream = await response.Content.ReadAsStreamAsync())
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
                            var percentage = (totalRead * 50.0) / totalBytes;
                            progress?.Report(percentage);
                        }
                    }
                }
                
                Debug.WriteLine($"[PluginMarket] 下载完成，开始安装: {plugin.Name}");
                progress?.Report(50);
                
                var pluginTargetDir = Path.Combine(pluginsDirectory, plugin.Id);
                
                if (Directory.Exists(pluginTargetDir))
                {
                    Directory.Delete(pluginTargetDir, true);
                }
                
                Directory.CreateDirectory(pluginTargetDir);
                
                ZipFile.ExtractToDirectory(tempZipPath, pluginTargetDir);
                
                File.Delete(tempZipPath);
                
                progress?.Report(100);
                Debug.WriteLine($"[PluginMarket] 插件安装成功: {plugin.Name}");
                
                return true;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[PluginMarket] 插件下载已取消: {plugin.Name}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginMarket] 插件下载/安装失败: {ex.Message}");
                return false;
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
                    Debug.WriteLine($"[PluginMarket] 插件卸载成功: {pluginId}");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginMarket] 插件卸载失败: {ex.Message}");
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
                Debug.WriteLine($"[PluginMarket] 获取最新版本: {url}");

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

                    // 如果指定了资源匹配模式
                    if (!string.IsNullOrEmpty(assetPattern))
                    {
                        if (assetName.Contains(assetPattern.ToLowerInvariant()))
                        {
                            downloadUrl = assetUrl;
                            break;
                        }
                    }
                    // 否则按平台匹配
                    else if (!string.IsNullOrEmpty(pluginId))
                    {
                        // 优先匹配插件ID
                        if (assetName.Contains(pluginId.ToLowerInvariant()))
                        {
                            // 再检查平台
                            var matchesPlatform = currentPlatform switch
                            {
                                "windows" => assetName.Contains("win") || assetName.Contains("windows"),
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
                    // 兜底：取第一个zip文件
                    else if (assetName.EndsWith(".zip") && downloadUrl == null)
                    {
                        downloadUrl = assetUrl;
                    }
                }

                Debug.WriteLine($"[PluginMarket] 最新版本: {tagName}, 下载地址: {downloadUrl}");
                return (tagName, downloadUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginMarket] 获取最新版本失败: {ex.Message}");
                return (null, null);
            }
        }

        /// <summary>
        /// 检查插件更新
        /// </summary>
        public static async Task CheckForUpdatesAsync(IEnumerable<MarketPlugin> plugins, string? installedVersion = null)
        {
            foreach (var plugin in plugins)
            {
                if (string.IsNullOrEmpty(plugin.ReleaseUrl)) continue;

                var (version, downloadUrl) = await GetLatestReleaseInfoAsync(
                    plugin.ReleaseUrl, 
                    plugin.AssetPattern, 
                    plugin.Id);

                if (!string.IsNullOrEmpty(version))
                {
                    plugin.LatestVersion = version;
                    plugin.LatestDownloadUrl = downloadUrl;

                    // 比较版本
                    if (!string.IsNullOrEmpty(installedVersion) || !string.IsNullOrEmpty(plugin.Version))
                    {
                        var currentVer = installedVersion ?? plugin.Version;
                        plugin.HasUpdate = IsNewerVersion(version, currentVer);
                    }
                }
            }
        }

        private static bool IsNewerVersion(string newVersion, string currentVersion)
        {
            try
            {
                newVersion = newVersion.TrimStart('v', 'V');
                currentVersion = currentVersion.TrimStart('v', 'V');

                var newParts = newVersion.Split('.');
                var currentParts = currentVersion.Split('.');

                var maxLen = Math.Max(newParts.Length, currentParts.Length);

                for (int i = 0; i < maxLen; i++)
                {
                    var newPart = i < newParts.Length && int.TryParse(newParts[i], out var n) ? n : 0;
                    var currentPart = i < currentParts.Length && int.TryParse(currentParts[i], out var c) ? c : 0;

                    if (newPart > currentPart) return true;
                    if (newPart < currentPart) return false;
                }

                return false;
            }
            catch
            {
                return false;
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
