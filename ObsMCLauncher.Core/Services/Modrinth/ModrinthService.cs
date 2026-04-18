using System;
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
        var json = await RequestWithFallbackAsync($"/project/{projectId}", cancellationToken).ConfigureAwait(false);
        if (json == null) return null;

        try
        {
            return JsonSerializer.Deserialize<ModrinthProject>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<ModrinthVersion>?> GetProjectVersionsAsync(
        string projectId,
        string? gameVersion = null,
        string? loader = null,
        CancellationToken cancellationToken = default)
    {
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
            return JsonSerializer.Deserialize<List<ModrinthVersion>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
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
