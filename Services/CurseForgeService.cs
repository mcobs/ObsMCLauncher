using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// CurseForge API 服务
    /// 文档: https://docs.curseforge.com/
    /// </summary>
    public static class CurseForgeService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string API_BASE_URL = "https://api.curseforge.com";
        
        // CurseForge API Key - 需要从 https://console.curseforge.com/ 获取
        // 以下为本人的API Key，请勿滥用
        private const string API_KEY = "$2a$10$74bDUfowjVtBbxnOLhRJG.06YALKhqHDmALto8HCJGPQGgZkBpGZS";
        
        // Section IDs (分类ID)
        public const int SECTION_MODS = 6;
        public const int SECTION_MODPACKS = 4471;
        public const int SECTION_RESOURCE_PACKS = 12;
        public const int SECTION_WORLDS = 17;
        
        // Minecraft Game ID
        private const int MINECRAFT_GAME_ID = 432;

        static CurseForgeService()
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-KEY", API_KEY);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ObsMCLauncher/1.0");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>
        /// 搜索MOD
        /// </summary>
        /// <param name="searchFilter">搜索关键词</param>
        /// <param name="gameVersion">游戏版本 (如 "1.20.1")</param>
        /// <param name="categoryId">分类ID (0表示全部)</param>
        /// <param name="pageIndex">页码（从0开始）</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="sortField">排序字段 (1=Featured, 2=Popularity, 3=LastUpdated, 4=Name, 5=Author, 6=TotalDownloads, 7=Category, 8=GameVersion)</param>
        /// <param name="sortOrder">排序方向 ("asc" 或 "desc")</param>
        /// <returns></returns>
        public static async Task<CurseForgeResponse<List<CurseForgeMod>>?> SearchModsAsync(
            string searchFilter = "",
            string gameVersion = "",
            int categoryId = 0,
            int pageIndex = 0,
            int pageSize = 20,
            int sortField = 2, // 默认按人气排序
            string sortOrder = "desc",
            int? classId = null)  // 可选的资源类型ID
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"gameId={MINECRAFT_GAME_ID}",
                    $"classId={classId ?? SECTION_MODS}",  // 使用传入的classId或默认MODS
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
                
                Debug.WriteLine($"[CurseForge] 搜索MOD: {url}");
                
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var result = JsonSerializer.Deserialize<CurseForgeResponse<List<CurseForgeMod>>>(json, options);
                
                Debug.WriteLine($"[CurseForge] ✅ 搜索成功，找到 {result?.Data?.Count ?? 0} 个MOD");
                
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CurseForge] ❌ 搜索失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 根据ID获取MOD详情
        /// </summary>
        public static async Task<CurseForgeResponse<CurseForgeMod>?> GetModAsync(int modId)
        {
            try
            {
                var url = $"{API_BASE_URL}/v1/mods/{modId}";
                
                Debug.WriteLine($"[CurseForge] 获取MOD详情: {modId}");
                
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var result = JsonSerializer.Deserialize<CurseForgeResponse<CurseForgeMod>>(json, options);
                
                Debug.WriteLine($"[CurseForge] ✅ 获取MOD详情成功: {result?.Data?.Name}");
                
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CurseForge] ❌ 获取MOD详情失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取MOD的所有文件
        /// </summary>
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
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var result = JsonSerializer.Deserialize<CurseForgeResponse<List<CurseForgeFile>>>(json, options);
                
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CurseForge] ❌ 获取MOD文件列表失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取MOD分类列表
        /// </summary>
        public static async Task<CurseForgeResponse<List<CurseForgeCategory>>?> GetCategoriesAsync(int? classId = null)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"gameId={MINECRAFT_GAME_ID}"
                };

                if (classId.HasValue)
                {
                    queryParams.Add($"classId={classId.Value}");
                }

                var url = $"{API_BASE_URL}/v1/categories?{string.Join("&", queryParams)}";
                
                Debug.WriteLine($"[CurseForge] 获取分类列表");
                
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var result = JsonSerializer.Deserialize<CurseForgeResponse<List<CurseForgeCategory>>>(json, options);
                
                Debug.WriteLine($"[CurseForge] ✅ 获取到 {result?.Data?.Count ?? 0} 个分类");
                
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CurseForge] ❌ 获取分类列表失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 下载MOD文件
        /// </summary>
        public static async Task<bool> DownloadModFileAsync(
            CurseForgeFile file,
            string savePath,
            IProgress<int>? progress = null)
        {
            try
            {
                var downloadUrl = file.GetDownloadUrl();
                Debug.WriteLine($"[CurseForge] 下载文件: {file.FileName}");
                Debug.WriteLine($"[CurseForge] 下载地址: {downloadUrl}");

                var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var canReportProgress = totalBytes != -1 && progress != null;

                using var fileStream = System.IO.File.Create(savePath);
                using var contentStream = await response.Content.ReadAsStreamAsync();

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    if (canReportProgress)
                    {
                        var progressPercentage = (int)((totalRead * 100) / totalBytes);
                        progress!.Report(progressPercentage);
                    }
                }

                Debug.WriteLine($"[CurseForge] ✅ 文件下载完成: {savePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CurseForge] ❌ 文件下载失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 格式化下载次数显示
        /// </summary>
        public static string FormatDownloadCount(int count)
        {
            if (count >= 1000000)
                return $"{count / 1000000.0:F1}M";
            if (count >= 1000)
                return $"{count / 1000.0:F1}K";
            return count.ToString();
        }

        /// <summary>
        /// 获取版本类型文本
        /// </summary>
        public static string GetReleaseTypeText(int releaseType)
        {
            return releaseType switch
            {
                1 => "正式版",
                2 => "测试版",
                3 => "Alpha",
                _ => "未知"
            };
        }

        /// <summary>
        /// 获取版本类型颜色
        /// </summary>
        public static string GetReleaseTypeColor(int releaseType)
        {
            return releaseType switch
            {
                1 => "#4CAF50", // 绿色 - 正式版
                2 => "#FF9800", // 橙色 - 测试版
                3 => "#F44336", // 红色 - Alpha
                _ => "#757575"  // 灰色 - 未知
            };
        }
    }
}

