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

namespace ObsMCLauncher.Plugins
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
        
        [JsonPropertyName("minLauncherVersion")]
        public string MinLauncherVersion { get; set; } = "1.0.0";
        
        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();
        
        [JsonPropertyName("downloads")]
        public int Downloads { get; set; }
        
        [JsonPropertyName("rating")]
        public double Rating { get; set; }
    }
    
    /// <summary>
    /// 插件市场服务
    /// </summary>
    public class PluginMarketService
    {
        private const string MARKET_INDEX_URL = "https://raw.githubusercontent.com/mcobs/ObsMCLauncher-PluginMarket/main/plugins.json";
        private const string CATEGORY_INDEX_URL = "https://raw.githubusercontent.com/mcobs/ObsMCLauncher-PluginMarket/main/categories.json";
        private static readonly HttpClient _httpClient;
        
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
        public static async Task<PluginMarketIndex?> GetMarketIndexAsync()
        {
            try
            {
                Debug.WriteLine($"[PluginMarket] 正在获取插件市场索引...");
                
                var response = await _httpClient.GetAsync(MARKET_INDEX_URL);
                response.EnsureSuccessStatusCode();
                
                var json = await response.Content.ReadAsStringAsync();
                var index = JsonSerializer.Deserialize<PluginMarketIndex>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                
                Debug.WriteLine($"[PluginMarket] 成功获取 {index?.Plugins?.Count ?? 0} 个插件");
                return index;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginMarket] 获取插件市场索引失败: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 获取插件分类列表
        /// </summary>
        public static async Task<List<PluginCategory>?> GetCategoriesAsync()
        {
            try
            {
                Debug.WriteLine($"[PluginMarket] 正在获取插件分类...");
                
                var response = await _httpClient.GetAsync(CATEGORY_INDEX_URL);
                response.EnsureSuccessStatusCode();
                
                var json = await response.Content.ReadAsStringAsync();
                var categoryIndex = JsonSerializer.Deserialize<CategoryIndex>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                
                Debug.WriteLine($"[PluginMarket] 成功获取 {categoryIndex?.Categories?.Count ?? 0} 个分类");
                return categoryIndex?.Categories;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginMarket] 获取插件分类失败: {ex.Message}");
                return null;
            }
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
                
                // 下载插件ZIP文件
                var response = await _httpClient.GetAsync(plugin.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                
                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var canReportProgress = totalBytes != -1;
                
                // 创建临时文件
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
                            var percentage = (totalRead * 50.0) / totalBytes; // 下载占50%
                            progress?.Report(percentage);
                        }
                    }
                }
                
                Debug.WriteLine($"[PluginMarket] 下载完成，开始安装: {plugin.Name}");
                progress?.Report(50);
                
                // 解压到插件目录
                var pluginTargetDir = Path.Combine(pluginsDirectory, plugin.Id);
                
                // 如果目录已存在，先删除
                if (Directory.Exists(pluginTargetDir))
                {
                    Directory.Delete(pluginTargetDir, true);
                }
                
                Directory.CreateDirectory(pluginTargetDir);
                
                // 解压ZIP文件
                ZipFile.ExtractToDirectory(tempZipPath, pluginTargetDir);
                
                // 删除临时文件
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
    }
}

