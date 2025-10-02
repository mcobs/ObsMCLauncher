using System.Threading.Tasks;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// 下载源服务接口
    /// </summary>
    public interface IDownloadSourceService
    {
        /// <summary>
        /// 获取版本清单URL
        /// </summary>
        string GetVersionManifestUrl();

        /// <summary>
        /// 获取版本JSON URL
        /// </summary>
        string GetVersionJsonUrl(string versionId);

        /// <summary>
        /// 获取资源下载URL
        /// </summary>
        string GetAssetUrl(string hash);

        /// <summary>
        /// 获取库文件URL
        /// </summary>
        string GetLibraryUrl(string path);

        /// <summary>
        /// 获取Forge安装器URL
        /// </summary>
        Task<string?> GetForgeInstallerUrl(string mcVersion, string forgeVersion);

        /// <summary>
        /// 获取Forge版本列表
        /// </summary>
        Task<string?> GetForgeVersionList(string mcVersion);
    }
}

