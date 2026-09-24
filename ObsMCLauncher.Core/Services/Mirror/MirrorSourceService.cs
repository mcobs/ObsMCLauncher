using System;
using System.Net.Http;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services.Mirror
{
    /// <summary>地址来源的平台</summary>
    public enum MirrorPlatform
    {
        None,
        Modrinth,
        CurseForge
    }

    public static class MirrorUrlHelper
    {
        internal const string McimBase = "https://mod.mcimirror.top";

        /// <summary>
        /// 判断一个地址属于哪个平台，官方地址和镜像地址都支持。
        /// CDN 地址也能区分来源：/data 来自 cdn.modrinth.com，/files 和 /avatars 来自 ForgeCDN。
        /// </summary>
        public static MirrorPlatform GetPlatform(string url)
        {
            if (string.IsNullOrEmpty(url)) return MirrorPlatform.None;

            if (url.StartsWith("https://api.modrinth.com", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
                return MirrorPlatform.Modrinth;

            if (url.StartsWith("https://api.curseforge.com", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://edge.forgecdn.net", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://media.forgecdn.net", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://mediafilez.forgecdn.net", StringComparison.OrdinalIgnoreCase))
                return MirrorPlatform.CurseForge;

            if (!url.StartsWith(McimBase, StringComparison.OrdinalIgnoreCase)) return MirrorPlatform.None;

            var path = url.Substring(McimBase.Length);
            if (path.StartsWith("/modrinth", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/data/", StringComparison.OrdinalIgnoreCase))
                return MirrorPlatform.Modrinth;

            if (path.StartsWith("/curseforge", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/files/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/avatars/", StringComparison.OrdinalIgnoreCase))
                return MirrorPlatform.CurseForge;

            return MirrorPlatform.None;
        }

        public static string RewriteUrl(string originalUrl)
        {
            if (string.IsNullOrEmpty(originalUrl)) return originalUrl;

            var config = LauncherConfig.Load();
            if (config.MirrorSourceMode != MirrorSourceMode.PreferMirror) return originalUrl;

            // Modrinth API
            if (originalUrl.StartsWith("https://api.modrinth.com", StringComparison.OrdinalIgnoreCase))
            {
                if (!MirrorHealthChecker.IsModrinthMirrorAvailable) return originalUrl;
                return $"{McimBase}/modrinth{originalUrl.Substring("https://api.modrinth.com".Length)}";
            }

            // Modrinth CDN
            if (originalUrl.StartsWith("https://cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
            {
                if (!MirrorHealthChecker.IsModrinthMirrorAvailable) return originalUrl;
                return $"{McimBase}{originalUrl.Substring("https://cdn.modrinth.com".Length)}";
            }

            // CurseForge API
            if (originalUrl.StartsWith("https://api.curseforge.com", StringComparison.OrdinalIgnoreCase))
            {
                if (!MirrorHealthChecker.IsCurseForgeMirrorAvailable) return originalUrl;
                return $"{McimBase}/curseforge{originalUrl.Substring("https://api.curseforge.com".Length)}";
            }

            // CurseForge CDN (edge.forgecdn.net)
            if (originalUrl.StartsWith("https://edge.forgecdn.net", StringComparison.OrdinalIgnoreCase))
            {
                if (!MirrorHealthChecker.IsCurseForgeMirrorAvailable) return originalUrl;
                return $"{McimBase}{originalUrl.Substring("https://edge.forgecdn.net".Length)}";
            }

            // CurseForge CDN (media.forgecdn.net)：Mod 图标、截图都挂在这个域名下
            if (originalUrl.StartsWith("https://media.forgecdn.net", StringComparison.OrdinalIgnoreCase))
            {
                if (!MirrorHealthChecker.IsCurseForgeMirrorAvailable) return originalUrl;
                return $"{McimBase}{originalUrl.Substring("https://media.forgecdn.net".Length)}";
            }

            // mediafilez.forgecdn.net 不在这里处理：MCIM 明确说明不要把它转发到镜像

            return originalUrl;
        }

        public static string GetOriginalUrl(string mirrorUrl)
        {
            if (string.IsNullOrEmpty(mirrorUrl)) return mirrorUrl;
            if (!mirrorUrl.StartsWith(McimBase, StringComparison.OrdinalIgnoreCase)) return mirrorUrl;

            var path = mirrorUrl.Substring(McimBase.Length);

            // API
            if (path.StartsWith("/modrinth", StringComparison.OrdinalIgnoreCase))
                return $"https://api.modrinth.com{path.Substring("/modrinth".Length)}";
            if (path.StartsWith("/curseforge", StringComparison.OrdinalIgnoreCase))
                return $"https://api.curseforge.com{path.Substring("/curseforge".Length)}";

            // CDN：路径本身就区分了来源，/data 来自 cdn.modrinth.com，/files 和 /avatars 来自 forgecdn
            if (path.StartsWith("/data/", StringComparison.OrdinalIgnoreCase))
                return $"https://cdn.modrinth.com{path}";
            if (path.StartsWith("/files/", StringComparison.OrdinalIgnoreCase))
                return $"https://edge.forgecdn.net{path}";
            if (path.StartsWith("/avatars/", StringComparison.OrdinalIgnoreCase))
                return $"https://media.forgecdn.net{path}";

            return mirrorUrl;
        }
    }

    public static class MirrorHealthChecker
    {
        private static readonly HttpClient _httpClient;
        private static bool _modrinthAvailable = true;
        private static bool _curseForgeAvailable = true;
        private static DateTime _modrinthLastCheck = DateTime.MinValue;
        private static DateTime _curseForgeLastCheck = DateTime.MinValue;
        private static readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan _shortRetryInterval = TimeSpan.FromSeconds(30);
        private static int _modrinthFailCount;
        private static int _curseForgeFailCount;
        private static readonly object _lock = new();

        static MirrorHealthChecker()
        {
            _httpClient = HttpClientFactory.CreateClient(
                timeout: TimeSpan.FromSeconds(8),
                automaticDecompression: System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", VersionInfo.UserAgent);
        }

        public static bool IsMirrorAvailable
        {
            get
            {
                lock (_lock)
                {
                    return _modrinthAvailable || _curseForgeAvailable;
                }
            }
        }

        public static bool IsModrinthMirrorAvailable
        {
            get
            {
                lock (_lock)
                {
                    return _modrinthAvailable;
                }
            }
        }

        public static bool IsCurseForgeMirrorAvailable
        {
            get
            {
                lock (_lock)
                {
                    return _curseForgeAvailable;
                }
            }
        }

        public static string GetStatusSummary()
        {
            lock (_lock)
            {
                var parts = new System.Collections.Generic.List<string>();
                if (_modrinthAvailable) parts.Add("Modrinth: 可用");
                else parts.Add($"Modrinth: 不可用 (失败{_modrinthFailCount}次)");
                if (_curseForgeAvailable) parts.Add("CurseForge: 可用");
                else parts.Add($"CurseForge: 不可用 (失败{_curseForgeFailCount}次)");
                return string.Join(" | ", parts);
            }
        }

        public static async Task CheckAvailabilityAsync()
        {
            await CheckAsync().ConfigureAwait(false);
        }

        public static async Task EnsureCheckedAsync()
        {
            bool shouldCheck;
            lock (_lock)
            {
                shouldCheck = ShouldRetryCheck(ref _modrinthFailCount, ref _modrinthLastCheck, ref _modrinthAvailable)
                              || ShouldRetryCheck(ref _curseForgeFailCount, ref _curseForgeLastCheck, ref _curseForgeAvailable);
            }

            if (shouldCheck) await CheckAsync().ConfigureAwait(false);
        }

        public static async Task EnsureModrinthCheckedAsync()
        {
            bool shouldCheck;
            lock (_lock)
            {
                shouldCheck = ShouldRetryCheck(ref _modrinthFailCount, ref _modrinthLastCheck, ref _modrinthAvailable);
            }
            if (shouldCheck) await CheckAsync().ConfigureAwait(false);
        }

        public static async Task EnsureCurseForgeCheckedAsync()
        {
            bool shouldCheck;
            lock (_lock)
            {
                shouldCheck = ShouldRetryCheck(ref _curseForgeFailCount, ref _curseForgeLastCheck, ref _curseForgeAvailable);
            }
            if (shouldCheck) await CheckAsync().ConfigureAwait(false);
        }

        private static bool ShouldRetryCheck(ref int failCount, ref DateTime lastCheck, ref bool available)
        {
            if (available) return DateTime.UtcNow - lastCheck > _checkInterval;
            return DateTime.UtcNow - lastCheck > _shortRetryInterval;
        }

        public static void MarkUnavailable()
        {
            lock (_lock)
            {
                _modrinthAvailable = false;
                _curseForgeAvailable = false;
                _modrinthFailCount++;
                _curseForgeFailCount++;
                _modrinthLastCheck = DateTime.UtcNow;
                _curseForgeLastCheck = DateTime.UtcNow;
            }
        }

        public static void MarkModrinthUnavailable()
        {
            lock (_lock)
            {
                _modrinthAvailable = false;
                _modrinthFailCount++;
                _modrinthLastCheck = DateTime.UtcNow;
            }
        }

        public static void MarkCurseForgeUnavailable()
        {
            lock (_lock)
            {
                _curseForgeAvailable = false;
                _curseForgeFailCount++;
                _curseForgeLastCheck = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// 按地址判断该标记哪个平台不可用。CDN 地址也能区分来源：
        /// /data 来自 Modrinth，/files 和 /avatars 来自 ForgeCDN。
        /// </summary>
        public static void MarkUnavailableFor(string url)
        {
            switch (MirrorUrlHelper.GetPlatform(url))
            {
                case MirrorPlatform.Modrinth:
                    MarkModrinthUnavailable();
                    break;
                case MirrorPlatform.CurseForge:
                    MarkCurseForgeUnavailable();
                    break;
                default:
                    MarkUnavailable();
                    break;
            }
        }

        /// <summary>
        /// 探测镜像服务的存活。用专门的 /healthz，而不是某个数据接口——
        /// 数据接口在缓存冷启动、回源超时时会返回 502，拿它当健康检查会误判成镜像挂了。
        /// </summary>
        private static async Task<bool> ProbeAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{MirrorUrlHelper.McimBase}/healthz").ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                DebugLogger.Warn("Mirror", $"镜像源健康检查失败: {ex.Message}");
                return false;
            }
        }

        private static async Task CheckAsync()
        {
            var available = await ProbeAsync().ConfigureAwait(false);
            lock (_lock)
            {
                _modrinthAvailable = available;
                _curseForgeAvailable = available;
                _modrinthLastCheck = DateTime.UtcNow;
                _curseForgeLastCheck = DateTime.UtcNow;
                if (available)
                {
                    _modrinthFailCount = 0;
                    _curseForgeFailCount = 0;
                }
            }
            DebugLogger.Info("Mirror", $"镜像源可用性: {(available ? "可用" : "不可用")}");
        }
    }

    public static class MirrorDownloadHelper
    {
        public static async Task<string> DownloadStringWithFallbackAsync(
            string url,
            HttpClient httpClient,
            Action? onMirrorFailed = null)
        {
            var mirrorUrl = MirrorUrlHelper.RewriteUrl(url);
            var usedMirror = mirrorUrl != url;
            var platform = MirrorUrlHelper.GetPlatform(url);

            if (usedMirror)
            {
                if (platform == MirrorPlatform.Modrinth)
                    await MirrorHealthChecker.EnsureModrinthCheckedAsync().ConfigureAwait(false);
                else if (platform == MirrorPlatform.CurseForge)
                    await MirrorHealthChecker.EnsureCurseForgeCheckedAsync().ConfigureAwait(false);
            }

            var mirrorAvailable = platform switch
            {
                MirrorPlatform.Modrinth => MirrorHealthChecker.IsModrinthMirrorAvailable,
                MirrorPlatform.CurseForge => MirrorHealthChecker.IsCurseForgeMirrorAvailable,
                _ => MirrorHealthChecker.IsMirrorAvailable
            };

            if (usedMirror && mirrorAvailable)
            {
                try
                {
                    var response = await httpClient.GetAsync(mirrorUrl).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }

                    DebugLogger.Warn("Mirror", $"镜像源请求失败 ({(int)response.StatusCode}): {mirrorUrl}");
                }
                catch (Exception ex)
                {
                    DebugLogger.Warn("Mirror", $"镜像源请求异常: {mirrorUrl} - {ex.Message}");
                }

                onMirrorFailed?.Invoke();
                MirrorHealthChecker.MarkUnavailableFor(mirrorUrl);
            }

            try
            {
                var response = await httpClient.GetAsync(url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Mirror", $"官方源请求也失败: {url} - {ex.Message}");
                throw;
            }
        }

        public static string RewriteDownloadUrl(string url)
        {
            return MirrorUrlHelper.RewriteUrl(url);
        }

        public static string GetFallbackUrl(string mirrorUrl)
        {
            var original = MirrorUrlHelper.GetOriginalUrl(mirrorUrl);
            return original != mirrorUrl ? original : mirrorUrl;
        }
    }
}
