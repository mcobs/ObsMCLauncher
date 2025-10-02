using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// Mojang 官方 API 服务
    /// </summary>
    public class MojangAPIService : IDownloadSourceService
    {
        private const string LauncherMetaUrl = "https://launchermeta.mojang.com";
        private const string ResourcesUrl = "https://resources.download.minecraft.net";
        private const string LibrariesUrl = "https://libraries.minecraft.net";
        private const string ForgeMavenUrl = "https://maven.minecraftforge.net";
        
        private static readonly HttpClient httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static MojangAPIService()
        {
            httpClient.DefaultRequestHeaders.Add("User-Agent", "ObsMCLauncher/1.0");
        }

        /// <summary>
        /// 获取版本清单URL
        /// </summary>
        public string GetVersionManifestUrl()
        {
            return $"{LauncherMetaUrl}/mc/game/version_manifest.json";
        }

        /// <summary>
        /// 获取版本JSON URL
        /// </summary>
        public string GetVersionJsonUrl(string versionId)
        {
            // Mojang的版本JSON URL需要从version_manifest.json中获取
            // 这里返回一个占位符，实际使用时需要先解析version_manifest
            return $"{LauncherMetaUrl}/v1/packages/[package_id]/{versionId}.json";
        }

        /// <summary>
        /// 获取资源下载URL
        /// </summary>
        public string GetAssetUrl(string hash)
        {
            var prefix = hash.Substring(0, 2);
            return $"{ResourcesUrl}/{prefix}/{hash}";
        }

        /// <summary>
        /// 获取库文件URL
        /// </summary>
        public string GetLibraryUrl(string path)
        {
            return $"{LibrariesUrl}/{path}";
        }

        /// <summary>
        /// 获取Forge版本列表
        /// </summary>
        public async Task<string?> GetForgeVersionList(string mcVersion)
        {
            try
            {
                // Forge官方Maven仓库
                var url = $"{ForgeMavenUrl}/net/minecraftforge/forge/maven-metadata.xml";
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
                // Forge官方下载地址
                var url = $"{ForgeMavenUrl}/net/minecraftforge/forge/{mcVersion}-{forgeVersion}/forge-{mcVersion}-{forgeVersion}-installer.jar";
                return Task.FromResult<string?>(url);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取Forge安装器URL失败: {ex.Message}");
                return Task.FromResult<string?>(null);
            }
        }
    }
}

