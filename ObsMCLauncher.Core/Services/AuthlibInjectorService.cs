using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Services;

/// <summary>
/// authlib-injector 文件管理服务
/// </summary>
public class AuthlibInjectorService
{
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    // GitHub 官方源
    private const string GITHUB_RELEASE_URL = "https://github.com/yushijinhun/authlib-injector/releases/latest/download/authlib-injector.jar";
    
    // BMCLAPI 镜像源
    private const string BMCLAPI_URL = "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json";

    /// <summary>
    /// 下载进度回调 (已下载字节数, 总字节数)
    /// </summary>
    public Action<long, long>? OnProgressUpdate { get; set; }

    /// <summary>
    /// 获取 authlib-injector.jar 的存放路径
    /// </summary>
    public static string GetAuthlibInjectorPath()
    {
        var config = LauncherConfig.Load();
        var librariesDir = Path.Combine(config.GetDataDirectory(), "Libraries");
        Directory.CreateDirectory(librariesDir);
        return Path.Combine(librariesDir, "authlib-injector.jar");
    }

    /// <summary>
    /// 检查 authlib-injector.jar 是否存在
    /// </summary>
    public static bool IsAuthlibInjectorExists()
    {
        var path = GetAuthlibInjectorPath();
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    /// <summary>
    /// 下载 authlib-injector.jar
    /// </summary>
    /// <param name="useBMCLAPI">是否使用 BMCLAPI 镜像源</param>
    public async Task<bool> DownloadAuthlibInjectorAsync(bool useBMCLAPI = true)
    {
        try
        {
            var targetPath = GetAuthlibInjectorPath();
            var tempPath = targetPath + ".tmp";

            string downloadUrl = useBMCLAPI ? await GetBMCLAPIDownloadUrlAsync() : GITHUB_RELEASE_URL;

            // 下载文件
            using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        downloadedBytes += bytesRead;

                        // 报告进度
                        OnProgressUpdate?.Invoke(downloadedBytes, totalBytes);
                    }
                }
            }

            // 验证文件大小
            var fileInfo = new FileInfo(tempPath);
            if (fileInfo.Length < 10000) // 至少应该有 10KB
            {
                File.Delete(tempPath);
                throw new Exception("下载的文件大小异常，可能下载失败");
            }

            // 删除旧文件并重命名
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempPath, targetPath);

            return true;
        }
        catch (Exception ex)
        {
            throw new Exception($"下载 authlib-injector 失败: {ex.Message}", ex);
        }
    }

    private async Task<string> GetBMCLAPIDownloadUrlAsync()
    {
        try
        {
            var response = await _httpClient.GetStringAsync(BMCLAPI_URL);
            var json = System.Text.Json.JsonDocument.Parse(response);
            
            if (json.RootElement.TryGetProperty("download_url", out var downloadUrl))
            {
                var url = downloadUrl.GetString();
                if (!string.IsNullOrEmpty(url)) return url;
            }
            throw new Exception("无法从 BMCLAPI 获取下载地址");
        }
        catch
        {
            return GITHUB_RELEASE_URL;
        }
    }
}
