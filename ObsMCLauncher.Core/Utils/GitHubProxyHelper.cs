using System;
using System.Diagnostics;

namespace ObsMCLauncher.Core.Utils
{
    /// <summary>
    /// GitHub代理帮助类 - 用于优化中国大陆访问GitHub资源
    /// </summary>
    public static class GitHubProxyHelper
    {
        /// <summary>
        /// GitHub镜像代理地址
        /// </summary>
        public const string GITHUB_PROXY = "https://gh-proxy.com/";

        /// <summary>
        /// 检查URL是否为GitHub相关URL
        /// </summary>
        public static bool IsGitHubUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            
            return url.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("github.io", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("githubassets.com", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 为GitHub URL添加镜像代理
        /// </summary>
        /// <param name="url">原始URL</param>
        /// <param name="useProxy">是否使用代理，默认true</param>
        /// <returns>代理后的URL或原始URL</returns>
        public static string WithProxy(string url, bool useProxy = true)
        {
            if (!useProxy || string.IsNullOrEmpty(url)) return url;

            if (!IsGitHubUrl(url)) return url;

            if (url.StartsWith(GITHUB_PROXY, StringComparison.OrdinalIgnoreCase)) return url;

            var proxyUrl = GITHUB_PROXY + url;
            Debug.WriteLine($"[GitHubProxy] 使用镜像: {url} -> {proxyUrl}");
            return proxyUrl;
        }

        /// <summary>
        /// 移除URL中的镜像代理
        /// </summary>
        public static string RemoveProxy(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            if (url.StartsWith(GITHUB_PROXY, StringComparison.OrdinalIgnoreCase))
            {
                return url.Substring(GITHUB_PROXY.Length);
            }

            return url;
        }

        /// <summary>
        /// 转换GitHub API URL为镜像URL
        /// </summary>
        public static string ConvertApiUrl(string apiUrl)
        {
            if (string.IsNullOrEmpty(apiUrl)) return apiUrl;

            if (apiUrl.Contains("api.github.com", StringComparison.OrdinalIgnoreCase))
            {
                return WithProxy(apiUrl);
            }

            return apiUrl;
        }

        /// <summary>
        /// 转换GitHub Release下载URL为镜像URL
        /// </summary>
        public static string ConvertReleaseUrl(string releaseUrl)
        {
            return WithProxy(releaseUrl);
        }

        /// <summary>
        /// 转换GitHub Raw文件URL为镜像URL
        /// </summary>
        public static string ConvertRawUrl(string rawUrl)
        {
            return WithProxy(rawUrl);
        }
    }
}
