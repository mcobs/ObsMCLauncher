using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services.Minecraft
{
    public class MinecraftVersion
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("releaseTime")]
        public DateTime ReleaseTime { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonIgnore]
        public string IconPath
        {
            get
            {
                if (Type == "snapshot")
                    return "avares://ObsMCLauncher.Desktop/Assets/LoaderIcons/vanilla_snapshot.png";

                if (Type == "release")
                {
                    if (IsLegacyVersion(Id) && IsVersionLessThanOrEqual(Id, "1.12.2"))
                        return "avares://ObsMCLauncher.Desktop/Assets/LoaderIcons/vanilla_old.png";
                    return "avares://ObsMCLauncher.Desktop/Assets/LoaderIcons/vanilla.png";
                }

                return "avares://ObsMCLauncher.Desktop/Assets/LoaderIcons/vanilla_old.png";
            }
        }

        private static bool IsLegacyVersion(string version)
        {
            return version.StartsWith("1.");
        }

        private static bool IsVersionLessThanOrEqual(string version, string targetVersion)
        {
            try
            {
                var vParts = version.Split('.');
                var tParts = targetVersion.Split('.');
                for (int i = 0; i < Math.Min(vParts.Length, tParts.Length); i++)
                {
                    if (int.TryParse(vParts[i], out int v) && int.TryParse(tParts[i], out int t))
                    {
                        if (v < t) return true;
                        if (v > t) return false;
                    }
                    else
                    {
                        int cmp = string.Compare(vParts[i], tParts[i], StringComparison.Ordinal);
                        if (cmp < 0) return true;
                        if (cmp > 0) return false;
                    }
                }
                return vParts.Length <= tParts.Length;
            }
            catch { return false; }
        }
    }

    public class VersionManifest
    {
        [JsonPropertyName("latest")]
        public LatestVersion? Latest { get; set; }

        [JsonPropertyName("versions")]
        public List<MinecraftVersion> Versions { get; set; } = new();
    }

    public class LatestVersion
    {
        [JsonPropertyName("release")]
        public string Release { get; set; } = "";

        [JsonPropertyName("snapshot")]
        public string Snapshot { get; set; } = "";
    }

    public class MinecraftVersionService
    {
        private static readonly HttpClient _httpClient;
        private static readonly JsonSerializerOptions CachedJsonOptions = new() { PropertyNameCaseInsensitive = true };

        private static readonly object _cacheLock = new();
        private static VersionManifest? _cachedManifest;
        private static DateTime _cachedAt;
        private static readonly TimeSpan ManifestCacheTtl = TimeSpan.FromMinutes(10);

        static MinecraftVersionService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            _httpClient.DefaultRequestHeaders.Add("User-Agent", VersionInfo.UserAgent);
        }

        public static async Task<VersionManifest?> GetVersionListAsync()
        {
            lock (_cacheLock)
            {
                if (_cachedManifest != null && (DateTime.UtcNow - _cachedAt) < ManifestCacheTtl)
                {
                    return _cachedManifest;
                }
            }

            try
            {
                var downloadService = DownloadSourceManager.Instance.CurrentService;
                var url = downloadService.GetVersionManifestUrl();

                DebugLogger.Info("MCVersion", $"正在请求版本列表: {url}");

                using var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var error = $"获取版本列表失败: HTTP {response.StatusCode} - {response.ReasonPhrase}";
                    DebugLogger.Error("MCVersion", error);
                    throw new Exception(error);
                }

                var json = await response.Content.ReadAsStringAsync();
                DebugLogger.Info("MCVersion", $"成功获取版本清单，JSON长度: {json.Length} 字符");

                var manifest = JsonSerializer.Deserialize<VersionManifest>(json, CachedJsonOptions);
                
                if (manifest == null)
                {
                    throw new Exception("JSON 反序列化失败，返回 null");
                }

                lock (_cacheLock)
                {
                    _cachedManifest = manifest;
                    _cachedAt = DateTime.UtcNow;
                }

                DebugLogger.Info("MCVersion", $"成功解析版本清单，版本数量: {manifest.Versions.Count}");
                return manifest;
            }
            catch (HttpRequestException ex)
            {
                var error = $"网络请求异常: {ex.Message}";
                DebugLogger.Error("MCVersion", error);
                throw new Exception(error, ex);
            }
            catch (JsonException ex)
            {
                var error = $"JSON 解析异常: {ex.Message}";
                DebugLogger.Error("MCVersion", error);
                throw new Exception(error, ex);
            }
            catch (Exception ex)
            {
                DebugLogger.Error("MCVersion", $"获取版本列表异常: {ex.Message}");
                throw;
            }
        }

        public static async Task<string?> GetVersionJsonAsync(string versionId)
        {
            try
            {
                var downloadService = DownloadSourceManager.Instance.CurrentService;
                var url = downloadService.GetVersionJsonUrl(versionId);

                using var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    DebugLogger.Warn("MCVersion", $"获取版本详情失败: HTTP {response.StatusCode}");
                    return null;
                }

                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Error("MCVersion", $"获取版本详情异常: {ex.Message}");
                return null;
            }
        }

        public static async Task<string?> GetForgeVersionsAsync(string mcVersion)
        {
            try
            {
                var downloadService = DownloadSourceManager.Instance.CurrentService;
                return await downloadService.GetForgeVersionList(mcVersion);
            }
            catch (Exception ex)
            {
                DebugLogger.Error("MCVersion", $"获取Forge版本列表异常: {ex.Message}");
                return null;
            }
        }
    }
}
