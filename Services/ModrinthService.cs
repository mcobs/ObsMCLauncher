using ObsMCLauncher.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// Modrinth API 服务
    /// </summary>
    public class ModrinthService
    {
        private readonly DownloadSourceManager _downloadSourceManager;
        private const string BaseUrl = "https://api.modrinth.com/v2";
        
        // 静态HttpClient单例，避免每次请求都创建新实例
        private static readonly HttpClient _httpClient;

        static ModrinthService()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ObsMCLauncher/1.0");
            _httpClient.Timeout = TimeSpan.FromMinutes(30);
        }

        public ModrinthService(DownloadSourceManager downloadSourceManager)
        {
            _downloadSourceManager = downloadSourceManager;
        }

        /// <summary>
        /// 搜索模组
        /// </summary>
        public async Task<ModrinthSearchResponse?> SearchModsAsync(
            string searchQuery = "",
            string gameVersion = "",
            int offset = 0,
            int limit = 20,
            string sortBy = "relevance",
            string? projectType = null,  // 可选的项目类型
            CancellationToken cancellationToken = default)
        {
            try
            {
                // 构建facets（过滤条件）
                var facets = new List<List<string>>
                {
                    new List<string> { $"project_type:{projectType ?? "mod"}" }  // 使用传入的类型或默认mod
                };

                if (!string.IsNullOrEmpty(gameVersion))
                {
                    facets.Add(new List<string> { $"versions:{gameVersion}" });
                }

                var facetsJson = JsonSerializer.Serialize(facets);

                // 构建查询URL
                var queryParams = HttpUtility.ParseQueryString(string.Empty);
                queryParams["query"] = searchQuery;
                queryParams["facets"] = facetsJson;
                queryParams["offset"] = offset.ToString();
                queryParams["limit"] = limit.ToString();
                queryParams["index"] = sortBy;

                var url = $"{BaseUrl}/search?{queryParams}";

                var response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<ModrinthSearchResponse>(json);

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModrinthService] 搜索失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取项目详情
        /// </summary>
        public async Task<ModrinthProject?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
        {
            try
            {
                var url = $"{BaseUrl}/project/{projectId}";
                Debug.WriteLine($"[ModrinthService] 获取项目详情: {url}");

                var response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<ModrinthProject>(json);

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModrinthService] 获取项目详情失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取项目的所有版本
        /// </summary>
        public async Task<List<ModrinthVersion>?> GetProjectVersionsAsync(
            string projectId,
            string? gameVersion = null,
            string? loader = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var queryParams = HttpUtility.ParseQueryString(string.Empty);
                if (!string.IsNullOrEmpty(gameVersion))
                {
                    queryParams["game_versions"] = $"[\"{gameVersion}\"]";
                }
                if (!string.IsNullOrEmpty(loader))
                {
                    queryParams["loaders"] = $"[\"{loader.ToLower()}\"]";
                }

                var queryString = queryParams.Count > 0 ? $"?{queryParams}" : "";
                var url = $"{BaseUrl}/project/{projectId}/version{queryString}";

                var response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<List<ModrinthVersion>>(json);

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModrinthService] 获取版本列表失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 下载模组文件
        /// </summary>
        public async Task<bool> DownloadModFileAsync(
            ModrinthVersionFile file,
            string savePath,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new System.IO.FileStream(savePath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var percent = (int)((double)totalRead / totalBytes * 100);
                        progress?.Report(percent);
                    }
                }

                Debug.WriteLine($"[ModrinthService] 下载完成: {file.Filename}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModrinthService] 下载失败: {ex.Message}");
                return false;
            }
        }
    }
}

