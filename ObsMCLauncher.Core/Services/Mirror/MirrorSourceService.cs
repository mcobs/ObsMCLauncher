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

            // CurseForge CDN (mediafilez.forgecdn.net)
            if (originalUrl.StartsWith("https://mediafilez.forgecdn.net", StringComparison.OrdinalIgnoreCase))
            {
                if (!MirrorHealthChecker.IsCurseForgeMirrorAvailable) return originalUrl;
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
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
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
            var modrinthTask = CheckModrinthAsync();
            var curseForgeTask = CheckCurseForgeAsync();
            await Task.WhenAll(modrinthTask, curseForgeTask);
        }

        public static async Task EnsureCheckedAsync()
        {
            bool shouldCheckModrinth, shouldCheckCurseForge;
            lock (_lock)
            {
                shouldCheckModrinth = ShouldRetryCheck(ref _modrinthFailCount, ref _modrinthLastCheck, ref _modrinthAvailable);
                shouldCheckCurseForge = ShouldRetryCheck(ref _curseForgeFailCount, ref _curseForgeLastCheck, ref _curseForgeAvailable);
            }

            var tasks = new List<Task>();
            if (shouldCheckModrinth) tasks.Add(CheckModrinthAsync());
            if (shouldCheckCurseForge) tasks.Add(CheckCurseForgeAsync());
            if (tasks.Count > 0) await Task.WhenAll(tasks);
        }

        public static async Task EnsureModrinthCheckedAsync()
        {
            bool shouldCheck;
            lock (_lock)
            {
                shouldCheck = ShouldRetryCheck(ref _modrinthFailCount, ref _modrinthLastCheck, ref _modrinthAvailable);
            }
            if (shouldCheck) await CheckModrinthAsync();
        }

        public static async Task EnsureCurseForgeCheckedAsync()
        {
            bool shouldCheck;
            lock (_lock)
            {
                shouldCheck = ShouldRetryCheck(ref _curseForgeFailCount, ref _curseForgeLastCheck, ref _curseForgeAvailable);
            }
            if (shouldCheck) await CheckCurseForgeAsync();
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

        private static async Task CheckModrinthAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("https://mod.mcimirror.top/modrinth/v2/tag/category").ConfigureAwait(false);
                lock (_lock)
                {
                    _modrinthAvailable = response.IsSuccessStatusCode;
                    _modrinthLastCheck = DateTime.UtcNow;
                    if (_modrinthAvailable) _modrinthFailCount = 0;
                }
                DebugLogger.Info("Mirror", $"Modrinth镜像源可用性: {(_modrinthAvailable ? "可用" : "不可用")}");
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _modrinthAvailable = false;
                    _modrinthFailCount++;
                    _modrinthLastCheck = DateTime.UtcNow;
                }
                DebugLogger.Warn("Mirror", $"Modrinth镜像源不可用: {ex.Message}");
            }
        }

        private static async Task CheckCurseForgeAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("https://mod.mcimirror.top/curseforge/v1/games").ConfigureAwait(false);
                lock (_lock)
                {
                    _curseForgeAvailable = response.IsSuccessStatusCode;
                    _curseForgeLastCheck = DateTime.UtcNow;
                    if (_curseForgeAvailable) _curseForgeFailCount = 0;
                }
                DebugLogger.Info("Mirror", $"CurseForge镜像源可用性: {(_curseForgeAvailable ? "可用" : "不可用")}");
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _curseForgeAvailable = false;
                    _curseForgeFailCount++;
                    _curseForgeLastCheck = DateTime.UtcNow;
                }
                DebugLogger.Warn("Mirror", $"CurseForge镜像源不可用: {ex.Message}");
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
                if (mirrorUrl.Contains("/modrinth", StringComparison.OrdinalIgnoreCase))
                    await MirrorHealthChecker.EnsureModrinthCheckedAsync();
                else if (mirrorUrl.Contains("/curseforge", StringComparison.OrdinalIgnoreCase))
                    await MirrorHealthChecker.EnsureCurseForgeCheckedAsync();
            }

            var mirrorAvailable = mirrorUrl.Contains("/modrinth", StringComparison.OrdinalIgnoreCase)
                ? MirrorHealthChecker.IsModrinthMirrorAvailable
                : mirrorUrl.Contains("/curseforge", StringComparison.OrdinalIgnoreCase)
                    ? MirrorHealthChecker.IsCurseForgeMirrorAvailable
                    : MirrorHealthChecker.IsMirrorAvailable;

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
                if (mirrorUrl.Contains("/modrinth", StringComparison.OrdinalIgnoreCase))
                    MirrorHealthChecker.MarkModrinthUnavailable();
                else if (mirrorUrl.Contains("/curseforge", StringComparison.OrdinalIgnoreCase))
                    MirrorHealthChecker.MarkCurseForgeUnavailable();
                else
                    MirrorHealthChecker.MarkUnavailable();
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
