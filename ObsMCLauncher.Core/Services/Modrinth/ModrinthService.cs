using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Mirror;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services.Modrinth;

public class ModrinthService
{
    private const string OfficialBaseUrl = "https://api.modrinth.com/v2";
    private const string MirrorBaseUrl = "https://mod.mcimirror.top/modrinth/v2";

    private static readonly HttpClient _httpClient;
    private static readonly ConcurrentDictionary<string, (ModrinthProject data, DateTime expiry)> _projectCache = new();
    private static readonly ConcurrentDictionary<string, (List<ModrinthVersion> data, DateTime expiry)> _versionCache = new();
    private static readonly ConcurrentDictionary<string, (Dictionary<string, ModrinthProject> data, DateTime expiry)> _projectsBatchCache = new();
    private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

    static ModrinthService()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", VersionInfo.UserAgent);
        _httpClient.Timeout = TimeSpan.FromMinutes(2);
    }

    private static bool ShouldUseMirror =>
        LauncherConfig.Load().MirrorSourceMode == MirrorSourceMode.PreferMirror && MirrorHealthChecker.IsMirrorAvailable;

    public async Task<ModrinthSearchResponse?> SearchModsAsync(
        string searchQuery,
        string? gameVersion = null,
        string projectType = "mod",
        int offset = 0,
        int limit = 20,
        string sortBy = "relevance",
        CancellationToken cancellationToken = default)
    {
        var facets = new List<List<string>>
        {
            new() { $"project_type:{projectType}" }
        };

        if (!string.IsNullOrEmpty(gameVersion))
        {
            facets.Add(new List<string> { $"versions:{gameVersion}" });
        }

        var facetsJson = JsonSerializer.Serialize(facets);

        var path =
            $"/search" +
            $"?query={Uri.EscapeDataString(searchQuery ?? string.Empty)}" +
            $"&facets={Uri.EscapeDataString(facetsJson)}" +
            $"&offset={offset}" +
            $"&limit={limit}" +
            $"&index={Uri.EscapeDataString(sortBy)}";

        var json = await RequestWithFallbackAsync(path, cancellationToken).ConfigureAwait(false);
        if (json == null) return null;

        try
        {
            return JsonSerializer.Deserialize<ModrinthSearchResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    public async Task<ModrinthProject?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        // 1. 检查内存缓存
        if (_projectCache.TryGetValue(projectId, out var cached) && cached.expiry > DateTime.Now)
        {
            return cached.data;
        }

        // 2. 检查磁盘缓存
        var cachedFromDisk = await ResourceCacheService.GetCachedDataAsync<ModrinthProject>(projectId, "modrinth");
        if (cachedFromDisk != null)
        {
            // 更新内存缓存
            _projectCache[projectId] = (cachedFromDisk, DateTime.Now + _cacheDuration);
            return cachedFromDisk;
        }

        // 3. 从API获取
        var json = await RequestWithFallbackAsync($"/project/{projectId}", cancellationToken).ConfigureAwait(false);
        if (json == null) return null;

        try
        {
            var project = JsonSerializer.Deserialize<ModrinthProject>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (project != null)
            {
                // 更新内存缓存
                _projectCache[projectId] = (project, DateTime.Now + _cacheDuration);
                // 写入磁盘缓存
                await ResourceCacheService.CacheDataAsync(projectId, project, "modrinth");
            }
            return project;
        }
        catch
        {
            return null;
        }
    }

    public async Task<Dictionary<string, ModrinthProject>?> GetProjectsAsync(IEnumerable<string> projectIds, CancellationToken cancellationToken = default)
    {
        var idList = projectIds.Distinct().ToList();
        if (idList.Count == 0) return new Dictionary<string, ModrinthProject>();

        var result = new Dictionary<string, ModrinthProject>();
        var uncachedIds = new List<string>();

        foreach (var id in idList)
        {
            if (_projectCache.TryGetValue(id, out var cached) && cached.expiry > DateTime.Now)
            {
                result[id] = cached.data;
            }
            else
            {
                uncachedIds.Add(id);
            }
        }

        if (uncachedIds.Count == 0) return result;

        var idsParam = Uri.EscapeDataString(JsonSerializer.Serialize(uncachedIds));
        var json = await RequestWithFallbackAsync($"/projects?ids={idsParam}", cancellationToken).ConfigureAwait(false);
        if (json == null) return result.Count > 0 ? result : null;

        try
        {
            var projects = JsonSerializer.Deserialize<List<ModrinthProject>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (projects == null) return result.Count > 0 ? result : null;
            foreach (var p in projects)
            {
                result[p.Id] = p;
                _projectCache[p.Id] = (p, DateTime.Now + _cacheDuration);
            }
            return result;
        }
        catch
        {
            return result.Count > 0 ? result : null;
        }
    }

    public async Task<List<ModrinthVersion>?> GetProjectVersionsAsync(
        string projectId,
        string? gameVersion = null,
        string? loader = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{projectId}_v{gameVersion}_l{loader}";
        
        // 1. 检查内存缓存
        if (_versionCache.TryGetValue(cacheKey, out var cached) && cached.expiry > DateTime.Now)
        {
            return cached.data;
        }

        // 2. 检查磁盘缓存
        var cachedFromDisk = await ResourceCacheService.GetCachedDataAsync<List<ModrinthVersion>>(cacheKey, "modrinth");
        if (cachedFromDisk != null)
        {
            // 更新内存缓存
            _versionCache[cacheKey] = (cachedFromDisk, DateTime.Now + _cacheDuration);
            return cachedFromDisk;
        }

        // 3. 从API获取
        var path = $"/project/{projectId}/version";

        var hasQuery = false;
        if (!string.IsNullOrEmpty(gameVersion))
        {
            path += (hasQuery ? "&" : "?") + $"game_versions={Uri.EscapeDataString($"[\"{gameVersion}\"]")}";
            hasQuery = true;
        }

        if (!string.IsNullOrEmpty(loader))
        {
            path += (hasQuery ? "&" : "?") + $"loaders={Uri.EscapeDataString($"[\"{loader.ToLowerInvariant()}\"]")}";
        }

        var json = await RequestWithFallbackAsync(path, cancellationToken).ConfigureAwait(false);
        if (json == null) return null;

        try
        {
            var versions = JsonSerializer.Deserialize<List<ModrinthVersion>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (versions != null)
            {
                // 更新内存缓存
                _versionCache[cacheKey] = (versions, DateTime.Now + _cacheDuration);
                var allKey = $"{projectId}_v_l";
                if (!_versionCache.ContainsKey(allKey) || _versionCache[allKey].expiry <= DateTime.Now)
                {
                    if (string.IsNullOrEmpty(gameVersion) && string.IsNullOrEmpty(loader))
                    {
                        _versionCache[allKey] = (versions, DateTime.Now + _cacheDuration);
                        // 写入全量版本的磁盘缓存
                        await ResourceCacheService.CacheDataAsync(allKey, versions, "modrinth");
                    }
                }
                // 写入当前查询的磁盘缓存
                await ResourceCacheService.CacheDataAsync(cacheKey, versions, "modrinth");
            }
            return versions;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> RequestWithFallbackAsync(string path, CancellationToken cancellationToken)
    {
        if (ShouldUseMirror)
        {
            await MirrorHealthChecker.EnsureCheckedAsync().ConfigureAwait(false);
        }

        if (ShouldUseMirror)
        {
            try
            {
                var mirrorUrl = MirrorBaseUrl + path;
                using var mirrorResponse = await _httpClient.GetAsync(mirrorUrl, cancellationToken).ConfigureAwait(false);
                if (mirrorResponse.IsSuccessStatusCode)
                {
                    return await mirrorResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }

                DebugLogger.Warn("Modrinth", $"镜像源请求失败 ({(int)mirrorResponse.StatusCode}), 回退到官方源");
            }
            catch (Exception ex)
            {
                DebugLogger.Warn("Modrinth", $"镜像源请求异常: {ex.Message}, 回退到官方源");
            }

            MirrorHealthChecker.MarkUnavailable();
        }

        try
        {
            var officialUrl = OfficialBaseUrl + path;
            using var response = await _httpClient.GetAsync(officialUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("Modrinth", $"官方源请求失败: {ex.Message}");
            return null;
        }
    }
}
