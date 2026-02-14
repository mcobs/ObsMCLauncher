using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Services;

/// <summary>
/// CurseForge API 服务
/// </summary>
public static class CurseForgeService
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private const string API_BASE_URL = "https://api.curseforge.com";
    
    // CurseForge API Key
    private const string API_KEY = "$2a$10$74bDUfowjVtBbxnOLhRJG.06YALKhqHDmALto8HCJGPQGgZkBpGZS";
    
    // Section IDs
    public const int SECTION_MODS = 6;
    public const int SECTION_MODPACKS = 4471;
    public const int SECTION_RESOURCE_PACKS = 12;
    public const int SECTION_WORLDS = 17;
    
    private const int MINECRAFT_GAME_ID = 432;

    static CurseForgeService()
    {
        if (!_httpClient.DefaultRequestHeaders.Contains("X-API-KEY"))
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-KEY", API_KEY);
        }
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ObsMCLauncher/1.0");
        }
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

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
        try
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

            var url = $"{API_BASE_URL}/v1/mods/search?{string.Join("&", queryParams)}";
            
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<CurseForgeResponse<List<CurseForgeMod>>>(json, options);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CurseForge] 搜索失败: {ex.Message}");
            return null;
        }
    }

    public static async Task<CurseForgeResponse<CurseForgeMod>?> GetModAsync(int modId)
    {
        try
        {
            var url = $"{API_BASE_URL}/v1/mods/{modId}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<CurseForgeResponse<CurseForgeMod>>(json, options);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CurseForge] 获取MOD详情失败: {ex.Message}");
            return null;
        }
    }

    public static async Task<CurseForgeFile?> GetModFileInfoAsync(int projectId, int fileId)
    {
        try
        {
            var url = $"{API_BASE_URL}/v1/mods/{projectId}/files/{fileId}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<CurseForgeResponse<CurseForgeFile>>(json, options);
            return result?.Data;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CurseForge] 获取文件信息失败: {ex.Message}");
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
        try
        {
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

            var url = $"{API_BASE_URL}/v1/mods/{modId}/files?{string.Join("&", queryParams)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<CurseForgeResponse<List<CurseForgeFile>>>(json, options);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CurseForge] 获取MOD文件列表失败: {ex.Message}");
            return null;
        }
    }

    public static async Task<CurseForgeResponse<List<CurseForgeCategory>>?> GetCategoriesAsync(int? classId = null)
    {
        try
        {
            var queryParams = new List<string> { $"gameId={MINECRAFT_GAME_ID}" };
            if (classId.HasValue)
            {
                queryParams.Add($"classId={classId.Value}");
            }

            var url = $"{API_BASE_URL}/v1/categories?{string.Join("&", queryParams)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<CurseForgeResponse<List<CurseForgeCategory>>>(json, options);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CurseForge] 获取分类列表失败: {ex.Message}");
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

            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CurseForge] ❌ 文件下载失败: {ex.Message}");
            return false;
        }
    }

    public static string FormatDownloadCount(int count)
    {
        if (count >= 1000000) return $"{count / 1000000.0:F1}M";
        if (count >= 1000) return $"{count / 1000.0:F1}K";
        return count.ToString();
    }
}
