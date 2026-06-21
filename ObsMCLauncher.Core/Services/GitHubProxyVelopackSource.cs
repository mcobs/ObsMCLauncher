using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Utils;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace ObsMCLauncher.Core.Services
{
    /// <summary>
    /// 带GitHub镜像代理的Velopack更新源
    /// 内部包装GithubSource，在下载时自动为GitHub资源URL添加代理前缀
    /// </summary>
    public class GitHubProxyVelopackSource : IUpdateSource
    {
        private readonly GithubSource _innerSource;

        /// <summary>
        /// 创建带代理的GitHub更新源
        /// </summary>
        /// <param name="repoUrl">GitHub仓库地址，如 https://github.com/user/repo</param>
        /// <param name="accessToken">GitHub访问令牌，可为null</param>
        /// <param name="prerelease">是否包含预发布版本</param>
        public GitHubProxyVelopackSource(string repoUrl, string? accessToken = null, bool prerelease = false)
        {
            // 用代理下载器包装默认的HttpClientFileDownloader
            var proxyDownloader = new ProxyFileDownloader(new HttpClientFileDownloader());
            _innerSource = new GithubSource(repoUrl, accessToken, prerelease, proxyDownloader);
        }

        public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId, VelopackAsset? latestLocalRelease)
        {
            return _innerSource.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease);
        }

        public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken)
        {
            return _innerSource.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
        }

        /// <summary>
        /// 代理文件下载器，在请求前对GitHub URL添加镜像代理
        /// API请求不会被代理（由GitHubProxyHelper.WithProxy处理）
        /// </summary>
        private class ProxyFileDownloader : IFileDownloader
        {
            private readonly IFileDownloader _inner;

            public ProxyFileDownloader(IFileDownloader inner)
            {
                _inner = inner;
            }

            public Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string>? headers, double timeout, CancellationToken cancelToken)
            {
                var proxyUrl = GitHubProxyHelper.WithProxy(url);
                return _inner.DownloadFile(proxyUrl, targetFile, progress, headers, timeout, cancelToken);
            }

            public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers, double timeout)
            {
                var proxyUrl = GitHubProxyHelper.WithProxy(url);
                return _inner.DownloadBytes(proxyUrl, headers, timeout);
            }

            public Task<string> DownloadString(string url, IDictionary<string, string>? headers, double timeout)
            {
                var proxyUrl = GitHubProxyHelper.WithProxy(url);
                return _inner.DownloadString(proxyUrl, headers, timeout);
            }
        }
    }
}
