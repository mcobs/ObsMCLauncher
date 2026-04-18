using System;
using System.Net.Http;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services.Mirror
{
    public static class MirrorUrlHelper
    {
        private const string McimBase = "https://mod.mcimirror.top";

        public static string RewriteUrl(string originalUrl)
        {
            if (string.IsNullOrEmpty(originalUrl)) return originalUrl;

            var config = LauncherConfig.Load();
            if (config.MirrorSourceMode != MirrorSourceMode.PreferMirror) return originalUrl;

            if (!MirrorHealthChecker.IsMirrorAvailable) return originalUrl;

            // Modrinth API
            if (originalUrl.StartsWith("https://api.modrinth.com", StringComparison.OrdinalIgnoreCase))
            {
                return $"{McimBase}/modrinth{originalUrl.Substring("https://api.modrinth.com".Length)}";
            }

            // Modrinth CDN
            if (originalUrl.StartsWith("https://cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
            {
                return $"{McimBase}{originalUrl.Substring("https://cdn.modrinth.com".Length)}";
            }

            // CurseForge API
            if (originalUrl.StartsWith("https://api.curseforge.com", StringComparison.OrdinalIgnoreCase))
            {
                return $"{McimBase}/curseforge{originalUrl.Substring("https://api.curseforge.com".Length)}";
            }

            // CurseForge CDN (edge.forgecdn.net)
            if (originalUrl.StartsWith("https://edge.forgecdn.net", StringComparison.OrdinalIgnoreCase))
            {
                return $"{McimBase}{originalUrl.Substring("https://edge.forgecdn.net".Length)}";
            }

            // CurseForge CDN (mediafilez.forgecdn.net)
            if (originalUrl.StartsWith("https://mediafilez.forgecdn.net", StringComparison.OrdinalIgnoreCase))
            {
                return $"{McimBase}{originalUrl.Substring("https://mediafilez.forgecdn.net".Length)}";
            }

            return originalUrl;
        }

        public static string GetOriginalUrl(string mirrorUrl)
        {
            if (string.IsNullOrEmpty(mirrorUrl)) return mirrorUrl;

            // Modrinth API
            if (mirrorUrl.StartsWith($"{McimBase}/modrinth", StringComparison.OrdinalIgnoreCase))
            {
                return $"https://api.modrinth.com{mirrorUrl.Substring($"{McimBase}/modrinth".Length)}";
            }

            // Modrinth CDN (注意: /modrinth 前缀匹配必须在通用匹配之前)
            if (mirrorUrl.StartsWith(McimBase + "/modrinth", StringComparison.OrdinalIgnoreCase))
            {
                return $"https://api.modrinth.com{mirrorUrl.Substring(McimBase.Length + "/modrinth".Length)}";
            }

            // CurseForge API
            if (mirrorUrl.StartsWith($"{McimBase}/curseforge", StringComparison.OrdinalIgnoreCase))
            {
                return $"https://api.curseforge.com{mirrorUrl.Substring($"{McimBase}/curseforge".Length)}";
            }

            // CDN 通用回退 - mcimirror.top 但不是 /modrinth 也不是 /curseforge
            if (mirrorUrl.StartsWith(McimBase, StringComparison.OrdinalIgnoreCase))
            {
                var path = mirrorUrl.Substring(McimBase.Length);
                if (!path.StartsWith("/modrinth", StringComparison.OrdinalIgnoreCase) &&
                    !path.StartsWith("/curseforge", StringComparison.OrdinalIgnoreCase))
                {
                    return $"https://cdn.modrinth.com{path}";
                }
            }

            return mirrorUrl;
        }
    }

    public static class MirrorHealthChecker
    {
        private static readonly HttpClient _httpClient;
        private static bool _isAvailable = true;
        private static DateTime _lastCheckTime = DateTime.MinValue;
        private static readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);
        private static readonly object _lock = new();

        static MirrorHealthChecker()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", VersionInfo.UserAgent);
        }

        public static bool IsMirrorAvailable
        {
            get
            {
                lock (_lock)
                {
                    return _isAvailable;
                }
            }
        }

        public static async Task CheckAvailabilityAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("https://mod.mcimirror.top/modrinth/v2/tag/category").ConfigureAwait(false);
                lock (_lock)
                {
                    _isAvailable = response.IsSuccessStatusCode;
                    _lastCheckTime = DateTime.UtcNow;
                }

                DebugLogger.Info("Mirror", $"MCIM镜像源可用性检测: {(_isAvailable ? "可用" : "不可用")}");
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _isAvailable = false;
                    _lastCheckTime = DateTime.UtcNow;
                }

                DebugLogger.Warn("Mirror", $"MCIM镜像源不可用: {ex.Message}");
            }
        }

        public static async Task EnsureCheckedAsync()
        {
            bool shouldCheck;
            lock (_lock)
            {
                shouldCheck = DateTime.UtcNow - _lastCheckTime > _checkInterval;
            }

            if (shouldCheck)
            {
                await CheckAvailabilityAsync().ConfigureAwait(false);
            }
        }

        public static void MarkUnavailable()
        {
            lock (_lock)
            {
                _isAvailable = false;
                _lastCheckTime = DateTime.UtcNow;
            }
        }
    }

    public static class MirrorDownloadHelper
    {
        public static async Task<string> DownloadStringWithFallbackAsync(
            string url,
            HttpClient httpClient,
            Action? onMirrorFailed = null)
        {
            var config = LauncherConfig.Load();
            var mirrorUrl = MirrorUrlHelper.RewriteUrl(url);
            var usedMirror = mirrorUrl != url;

            if (usedMirror)
            {
                await MirrorHealthChecker.EnsureCheckedAsync();
            }

            if (usedMirror && MirrorHealthChecker.IsMirrorAvailable)
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
                MirrorHealthChecker.MarkUnavailable();
            }

            // 回退到官方源
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
