using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Mirror;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

public static class CurseForgeService
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private const string OfficialApiBase = "https://api.curseforge.com";
    private const string MirrorApiBase = "https://mod.mcimirror.top/curseforge";
    private const string API_KEY = "$2a$10$74bDUfowjVtBbxnOLhRJG.06YALKhqHDmALto8HCJGPQGgZkBpGZS";

    public const int SECTION_MODS = 6;
    public const int SECTION_MODPACKS = 4471;
    public const int SECTION_RESOURCE_PACKS = 12;
    public const int SECTION_WORLDS = 17;

    private const int MINECRAFT_GAME_ID = 432;

    private static readonly ConcurrentDictionary<int, (CurseForgeMod data, DateTime expiry)> _modCache = new();
    private static readonly ConcurrentDictionary<string, (List<CurseForgeFile> data, DateTime expiry)> _filesCache = new();
    private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

    static CurseForgeService()
    {
        if (!_httpClient.DefaultRequestHeaders.Contains("X-API-KEY"))
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-KEY", API_KEY);
        }
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", VersionInfo.UserAgent);
        }
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    private static bool ShouldUseMirror =>
        LauncherConfig.Load().MirrorSourceMode == MirrorSourceMode.PreferMirror && MirrorHealthChecker.IsCurseForgeMirrorAvailable;

    private static string ApiBase => ShouldUseMirror ? MirrorApiBase : OfficialApiBase;

    public static async Task<CurseForgeResponse<List<CurseForgeMod>>?> SearchModsAsync(
        string searchFilter = "",
        string gameVersion = "",
        int categoryId = 0,
        int pageIndex = 0,
        int pageSize = 20,
        int sortField = 2,
        string sortOrder = "desc",
        int? classId = null)
    {
        var queryParams = new List<string>
        {
            $"gameId={MINECRAFT_GAME_ID}",
            $"classId={classId ?? SECTION_MODS}",
            $"index={pageIndex * pageSize}",
            $"pageSize={pageSize}",
            $"sortField={sortField}",
            $"sortOrder={sortOrder}"
        };

        if (!string.IsNullOrEmpty(searchFilter))
        {
            queryParams.Add($"searchFilter={Uri.EscapeDataString(searchFilter)}");
        }

        if (!string.IsNullOrEmpty(gameVersion))
        {
            queryParams.Add($"gameVersion={Uri.EscapeDataString(gameVersion)}");
        }

        if (categoryId > 0)
        {
            queryParams.Add($"categoryId={categoryId}");
        }

        var path = $"/v1/mods/search?{string.Join("&", queryParams)}";
        var json = await RequestWithFallbackAsync(path).ConfigureAwait(false);
        if (json == null) return null;

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<CurseForgeResponse<List<CurseForgeMod>>>(json, options);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("CurseForge", $"搜索结果解析失败: {ex.Message}");
            return null;
        }
    }

    public static async Task<CurseForgeResponse<CurseForgeMod>?> GetModAsync(int modId)
    {
        // 1. 检查内存缓存
        if (_modCache.TryGetValue(modId, out var cached) && cached.expiry > DateTime.Now)
        {
            return new CurseForgeResponse<CurseForgeMod> { Data = cached.data };
        }

        // 2. 检查磁盘缓存
        var cachedFromDisk = await ResourceCacheService.GetCachedDataAsync<CurseForgeMod>(modId.ToString(), "curseforge");
        if (cachedFromDisk != null)
        {
            // 更新内存缓存
            _modCache[modId] = (cachedFromDisk, DateTime.Now + _cacheDuration);
            return new CurseForgeResponse<CurseForgeMod> { Data = cachedFromDisk };
        }

        // 3. 从API获取
        var json = await RequestWithFallbackAsync($"/v1/mods/{modId}").ConfigureAwait(false);
        if (json == null) return null;

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<CurseForgeResponse<CurseForgeMod>>(json, options);
            if (result?.Data != null)
            {
                // 更新内存缓存
                _modCache[modId] = (result.Data, DateTime.Now + _cacheDuration);
                // 写入磁盘缓存
                await ResourceCacheService.CacheDataAsync(modId.ToString(), result.Data, "curseforge");
            }
            return result;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("CurseForge", $"获取MOD详情解析失败: {ex.Message}");
            return null;
        }
    }

    public static async Task<Dictionary<int, CurseForgeMod>?> GetModsAsync(IEnumerable<int> modIds)
    {
        var idList = modIds.Distinct().ToList();
        if (idList.Count == 0) return new Dictionary<int, CurseForgeMod>();

        var result = new Dictionary<int, CurseForgeMod>();
        var uncachedIds = new List<int>();

        foreach (var id in idList)
        {
            // 1. 检查内存缓存
            if (_modCache.TryGetValue(id, out var cached) && cached.expiry > DateTime.Now)
            {
                result[id] = cached.data;
                continue;
            }

            // 2. 检查磁盘缓存
            var diskCached = await ResourceCacheService.GetCachedDataAsync<CurseForgeMod>(id.ToString(), "curseforge");
            if (diskCached != null)
            {
                result[id] = diskCached;
                _modCache[id] = (diskCached, DateTime.Now + _cacheDuration);
            }
            else
            {
                uncachedIds.Add(id);
            }
        }

        if (uncachedIds.Count == 0) return result;

        var json = await RequestWithFallbackAsync($"/v1/mods?modIds={Uri.EscapeDataString(JsonSerializer.Serialize(uncachedIds))}").ConfigureAwait(false);
        if (json == null) return result.Count > 0 ? result : null;

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var response = JsonSerializer.Deserialize<CurseForgeResponse<List<CurseForgeMod>>>(json, options);
            if (response?.Data == null) return result.Count > 0 ? result : null;
            foreach (var mod in response.Data)
            {
                result[mod.Id] = mod;
                _modCache[mod.Id] = (mod, DateTime.Now + _cacheDuration);
                await ResourceCacheService.CacheDataAsync(mod.Id.ToString(), mod, "curseforge");
            }
            return result;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("CurseForge", $"批量获取MOD信息解析失败: {ex.Message}");
            return result.Count > 0 ? result : null;
        }
    }

    public static async Task<CurseForgeFile?> GetModFileInfoAsync(int projectId, int fileId)
    {
        var json = await RequestWithFallbackAsync($"/v1/mods/{projectId}/files/{fileId}").ConfigureAwait(false);
        if (json == null) return null;

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<CurseForgeResponse<CurseForgeFile>>(json, options);
            return result?.Data;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("CurseForge", $"获取文件信息解析失败: {ex.Message}");
            return null;
        }
    }

    public static async Task<CurseForgeResponse<List<CurseForgeFile>>?> GetModFilesAsync(
        int modId,
        string? gameVersion = null,
        int? modLoaderType = null,
        int pageIndex = 0,
        int pageSize = 50)
    {
        var cacheKey = $"files_{modId}_v{gameVersion}_l{modLoaderType}_p{pageIndex}";

        if (_filesCache.TryGetValue(cacheKey, out var cached) && cached.expiry > DateTime.Now)
        {
            return new CurseForgeResponse<List<CurseForgeFile>> { Data = cached.data };
        }

        var diskCached = await ResourceCacheService.GetCachedDataAsync<List<CurseForgeFile>>(cacheKey, "curseforge");
        if (diskCached != null)
        {
            _filesCache[cacheKey] = (diskCached, DateTime.Now + _cacheDuration);
            return new CurseForgeResponse<List<CurseForgeFile>> { Data = diskCached };
        }

        var queryParams = new List<string>
        {
            $"index={pageIndex * pageSize}",
            $"pageSize={pageSize}"
        };

        if (!string.IsNullOrEmpty(gameVersion))
        {
            queryParams.Add($"gameVersion={Uri.EscapeDataString(gameVersion)}");
        }

        if (modLoaderType.HasValue)
        {
            queryParams.Add($"modLoaderType={modLoaderType.Value}");
        }

        var path = $"/v1/mods/{modId}/files?{string.Join("&", queryParams)}";
        var json = await RequestWithFallbackAsync(path).ConfigureAwait(false);
        if (json == null) return null;

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<CurseForgeResponse<List<CurseForgeFile>>>(json, options);
            if (result?.Data != null)
            {
                _filesCache[cacheKey] = (result.Data, DateTime.Now + _cacheDuration);
                await ResourceCacheService.CacheDataAsync(cacheKey, result.Data, "curseforge");
            }
            return result;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("CurseForge", $"获取MOD文件列表解析失败: {ex.Message}");
            return null;
        }
    }

    public static async Task<CurseForgeResponse<List<CurseForgeCategory>>?> GetCategoriesAsync(int? classId = null)
    {
        var queryParams = new List<string> { $"gameId={MINECRAFT_GAME_ID}" };
        if (classId.HasValue)
        {
            queryParams.Add($"classId={classId.Value}");
        }

        var path = $"/v1/categories?{string.Join("&", queryParams)}";
        var json = await RequestWithFallbackAsync(path).ConfigureAwait(false);
        if (json == null) return null;

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<CurseForgeResponse<List<CurseForgeCategory>>>(json, options);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("CurseForge", $"获取分类列表解析失败: {ex.Message}");
            return null;
        }
    }

    public static async Task<bool> DownloadModFileAsync(
        CurseForgeFile file,
        string savePath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var downloadUrl = file.GetDownloadUrl();
            var mirrorUrl = MirrorUrlHelper.RewriteUrl(downloadUrl);
            var usedMirror = mirrorUrl != downloadUrl;

            if (usedMirror && ShouldUseMirror)
            {
                try
                {
                    var success = await DownloadFileInternalAsync(mirrorUrl, savePath, progress, cancellationToken).ConfigureAwait(false);
                    if (success) return true;

                    DebugLogger.Warn("CurseForge", "镜像源下载失败, 回退到官方源");
                }
                catch (Exception ex)
                {
                    DebugLogger.Warn("CurseForge", $"镜像源下载异常: {ex.Message}, 回退到官方源");
                }
            }

            return await DownloadFileInternalAsync(downloadUrl, savePath, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("CurseForge", $"文件下载失败: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> DownloadFileInternalAsync(
        string url,
        string savePath,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var canReport = totalBytes > 0 && progress != null;

        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

        await using var fileStream = File.Create(savePath);
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;

            if (canReport)
            {
                var percent = (int)(totalRead * 100 / totalBytes);
                progress!.Report(percent);
            }
        }

        return true;
    }

    public static string FormatDownloadCount(int count)
    {
        if (count >= 1000000) return $"{count / 1000000.0:F1}M";
        if (count >= 1000) return $"{count / 1000.0:F1}K";
        return count.ToString();
    }

    private static async Task<string?> RequestWithFallbackAsync(string path)
    {
        if (ShouldUseMirror)
        {
            await MirrorHealthChecker.EnsureCurseForgeCheckedAsync().ConfigureAwait(false);
        }

        if (ShouldUseMirror)
        {
            try
            {
                var mirrorUrl = MirrorApiBase + path;
                var response = await _httpClient.GetAsync(mirrorUrl).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }

                DebugLogger.Warn("CurseForge", $"镜像源请求失败 ({(int)response.StatusCode}), 回退到官方源");
            }
            catch (Exception ex)
            {
                DebugLogger.Warn("CurseForge", $"镜像源请求异常: {ex.Message}, 回退到官方源");
            }

            MirrorHealthChecker.MarkCurseForgeUnavailable();
        }

        try
        {
            var officialUrl = OfficialApiBase + path;
            var response = await _httpClient.GetAsync(officialUrl).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("CurseForge", $"官方源请求失败: {ex.Message}");
            return null;
        }
    }
}
