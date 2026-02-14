using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ObsMCLauncher.Core.Services.Modrinth;

public class ModrinthService
{
    private const string BaseUrl = "https://api.modrinth.com/v2";

    private static readonly HttpClient _httpClient;

    static ModrinthService()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ObsMCLauncher/1.0");
        _httpClient.Timeout = TimeSpan.FromMinutes(2);
    }

    public async Task<ModrinthSearchResponse?> SearchModsAsync(
        string searchQuery,
        string? gameVersion = null,
        string projectType = "mod",
        int offset = 0,
        int limit = 20,
        string sortBy = "relevance",
        CancellationToken cancellationToken = default)
    {
        try
        {
            // facets = [["project_type:mod"],["versions:1.20.1"]]
            var facets = new List<List<string>>
            {
                new() { $"project_type:{projectType}" }
            };

            if (!string.IsNullOrEmpty(gameVersion))
            {
                facets.Add(new List<string> { $"versions:{gameVersion}" });
            }

            var facetsJson = JsonSerializer.Serialize(facets);

            // 不用 System.Web，直接拼接 query
            var url =
                $"{BaseUrl}/search" +
                $"?query={Uri.EscapeDataString(searchQuery ?? string.Empty)}" +
                $"&facets={Uri.EscapeDataString(facetsJson)}" +
                $"&offset={offset}" +
                $"&limit={limit}" +
                $"&index={Uri.EscapeDataString(sortBy)}";

            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
        try
        {
            var url = $"{BaseUrl}/project/{projectId}";
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
        try
        {
            var url = $"{BaseUrl}/project/{projectId}/version";

            var hasQuery = false;
            if (!string.IsNullOrEmpty(gameVersion))
            {
                url += (hasQuery ? "&" : "?") + $"game_versions={Uri.EscapeDataString($"[\"{gameVersion}\"]")}";
                hasQuery = true;
            }

            if (!string.IsNullOrEmpty(loader))
            {
                url += (hasQuery ? "&" : "?") + $"loaders={Uri.EscapeDataString($"[\"{loader.ToLowerInvariant()}\"]")}";
            }

            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
}
