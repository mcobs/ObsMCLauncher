using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace ObsMCLauncher.Core.Services.Minecraft
{
    /// <summary>
    /// BMCLAPI 下载源服务
    /// 文档: https://bmclapidoc.bangbang93.com/
    /// </summary>
    public class BMCLAPIService : IDownloadSourceService
    {
        private const string BaseUrl = "https://bmclapi2.bangbang93.com";
        private static readonly HttpClient httpClient;

        static BMCLAPIService()
        {
            // 创建支持自动解压缩的 HttpClient
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };

            httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            httpClient.DefaultRequestHeaders.Add("User-Agent", "ObsMCLauncher/1.0");
        }

        /// <summary>
        /// 获取版本清单URL
        /// </summary>
        public string GetVersionManifestUrl()
        {
            return $"{BaseUrl}/mc/game/version_manifest.json";
        }

        /// <summary>
        /// 获取版本JSON URL
        /// </summary>
        public string GetVersionJsonUrl(string versionId)
        {
            return $"{BaseUrl}/version/{versionId}/json";
        }

        /// <summary>
        /// 获取资源下载URL
        /// </summary>
        public string GetAssetUrl(string hash)
        {
            var prefix = hash.Substring(0, 2);
            return $"{BaseUrl}/assets/{prefix}/{hash}";
        }

        /// <summary>
        /// 获取库文件URL
        /// </summary>
        public string GetLibraryUrl(string path)
        {
            return $"{BaseUrl}/maven/{path}";
        }

        /// <summary>
        /// 获取Forge版本列表
        /// </summary>
        public async Task<string?> GetForgeVersionList(string mcVersion)
        {
            try
            {
                var url = $"{BaseUrl}/forge/minecraft/{mcVersion}";
                var response = await httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取Forge版本列表失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取Forge安装器URL
        /// </summary>
        public Task<string?> GetForgeInstallerUrl(string mcVersion, string forgeVersion)
        {
            try
            {
                // BMCLAPI Forge下载地址格式
                var url = $"{BaseUrl}/forge/download/{forgeVersion}";
                return Task.FromResult<string?>(url);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取Forge安装器URL失败: {ex.Message}");
                return Task.FromResult<string?>(null);
            }
        }

        /// <summary>
        /// 获取Fabric Meta API URL
        /// </summary>
        public string GetFabricMetaUrl()
        {
            return $"{BaseUrl}/fabric-meta";
        }

        /// <summary>
        /// 获取Fabric Loader版本列表URL
        /// </summary>
        public string GetFabricLoaderVersionsUrl()
        {
            return $"{BaseUrl}/fabric-meta/v2/versions/loader";
        }

        /// <summary>
        /// 获取Fabric支持的游戏版本列表URL
        /// </summary>
        public string GetFabricGameVersionsUrl()
        {
            return $"{BaseUrl}/fabric-meta/v2/versions/game";
        }

        /// <summary>
        /// 获取Fabric Profile URL
        /// </summary>
        public string GetFabricProfileUrl(string mcVersion, string loaderVersion)
        {
            return $"{BaseUrl}/fabric-meta/v2/versions/loader/{mcVersion}/{loaderVersion}/profile/json";
        }

        /// <summary>
        /// 获取Fabric Maven库文件URL
        /// </summary>
        public string GetFabricMavenUrl(string path)
        {
            return $"{BaseUrl}/maven/{path}";
        }

        /// <summary>
        /// 获取 BMCLAPI 基础 URL（用于 OptiFine 等扩展功能）
        /// </summary>
        public string GetBMCLApiUrl()
        {
            return BaseUrl;
        }
    }
}

