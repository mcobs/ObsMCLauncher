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
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    static ModrinthService()
    {
        // 只用于元数据接口，超时对齐其他服务；之前是 2 分钟，镜像卡住时要干等满才回退官方
        _httpClient = HttpClientFactory.CreateClient(timeout: TimeSpan.FromSeconds(30));
        _httpClient.DefaultRequestHeaders.Add("User-Agent", VersionInfo.UserAgent);
    }

    private static bool ShouldUseMirror =>
        LauncherConfig.Load().MirrorSourceMode == MirrorSourceMode.PreferMirror && MirrorHealthChecker.IsModrinthMirrorAvailable;

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
            // 1. 检查内存缓存
            if (_projectCache.TryGetValue(id, out var cached) && cached.expiry > DateTime.Now)
            {
                result[id] = cached.data;
                continue;
            }

            // 2. 检查磁盘缓存
            var diskCached = await ResourceCacheService.GetCachedDataAsync<ModrinthProject>(id, "modrinth");
            if (diskCached != null)
            {
                result[id] = diskCached;
                _projectCache[id] = (diskCached, DateTime.Now + _cacheDuration);
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
                await ResourceCacheService.CacheDataAsync(p.Id, p, "modrinth");
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

    /// <summary>
    /// 按本地文件 SHA1 批量反查 Modrinth 版本信息（导出整合包用）。
    /// 单次调用建议不超过 500 个 hash（调用方负责分批）。
    /// </summary>
    /// <returns>sha1 → 版本；请求失败返回 null（调用方按"全部未命中"降级）。</returns>
    public async Task<Dictionary<string, ModrinthVersion>?> GetVersionsByHashesAsync(
        IReadOnlyCollection<string> sha1Hashes,
        CancellationToken cancellationToken = default)
    {
        if (sha1Hashes.Count == 0)
            return new Dictionary<string, ModrinthVersion>();

        var body = JsonSerializer.Serialize(new ModrinthHashLookupRequest
        {
            Hashes = new List<string>(sha1Hashes),
            Algorithm = "sha1"
        });

        var json = await SendPostWithFallbackAsync("/version_files", body, cancellationToken).ConfigureAwait(false);
        if (json == null)
            return null;

        try
        {
            var result = JsonSerializer.Deserialize<Dictionary<string, ModrinthVersion>>(json, JsonOptions);
            if (result == null)
                return null;

            foreach (var hash in sha1Hashes)
            {
                if (result.TryGetValue(hash, out var version))
                    _versionCache[version.Id] = (new List<ModrinthVersion> { version }, DateTime.Now + _cacheDuration);
            }

            DebugLogger.Info("Modrinth", $"SHA1 反查命中 {result.Count}/{sha1Hashes.Count} 个文件");
            return result;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("Modrinth", $"SHA1 反查解析失败: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> SendPostWithFallbackAsync(string path, string jsonBody, CancellationToken cancellationToken)
    {
        if (ShouldUseMirror)
        {
            await MirrorHealthChecker.EnsureModrinthCheckedAsync().ConfigureAwait(false);
        }

        if (ShouldUseMirror)
        {
            try
            {
                using var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
                using var mirrorResponse = await _httpClient.PostAsync(MirrorBaseUrl + path, content, cancellationToken).ConfigureAwait(false);
                if (mirrorResponse.IsSuccessStatusCode)
                {
                    return await mirrorResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }

                DebugLogger.Warn("Modrinth", $"镜像源 POST 失败 ({(int)mirrorResponse.StatusCode}), 回退到官方源");
            }
            catch (Exception ex)
            {
                DebugLogger.Warn("Modrinth", $"镜像源 POST 异常: {ex.Message}, 回退到官方源");
            }

            MirrorHealthChecker.MarkModrinthUnavailable();
        }

        try
        {
            using var officialContent = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(OfficialBaseUrl + path, officialContent, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("Modrinth", $"官方源 POST 失败: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> RequestWithFallbackAsync(string path, CancellationToken cancellationToken)
    {
        if (ShouldUseMirror)
        {
            await MirrorHealthChecker.EnsureModrinthCheckedAsync().ConfigureAwait(false);
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

            MirrorHealthChecker.MarkModrinthUnavailable();
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
