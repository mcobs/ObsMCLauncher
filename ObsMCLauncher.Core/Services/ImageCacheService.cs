using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Mirror;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

public class ImageCacheService
{
    private static readonly HttpClient _httpClient = new();
    private static readonly string CacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OMCL", "cache", "icons");
    private const long MaxCacheSizeBytes = 100 * 1024 * 1024;

    static ImageCacheService()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", VersionInfo.UserAgent);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        if (!Directory.Exists(CacheDir))
        {
            Directory.CreateDirectory(CacheDir);
        }
        DebugLogger.Info("ImageCache", $"图标缓存目录已初始化: {CacheDir}");
    }

    public static async Task<string?> GetImagePathAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            string fileName = GetMd5Hash(url) + Path.GetExtension(url.Split('?')[0]);
            if (string.IsNullOrEmpty(Path.GetExtension(fileName))) fileName += ".png";

            string filePath = Path.Combine(CacheDir, fileName);

            if (File.Exists(filePath))
            {
                try { File.SetLastAccessTimeUtc(filePath, DateTime.UtcNow); } catch { }
                return filePath;
            }

            var mirrorUrl = MirrorUrlHelper.RewriteUrl(url);
            var config = LauncherConfig.Load();
            var shouldTryMirror = config.MirrorSourceMode == MirrorSourceMode.PreferMirror
                                  && MirrorHealthChecker.IsMirrorAvailable
                                  && mirrorUrl != url;

            if (shouldTryMirror)
            {
                try
                {
                    var bytes = await _httpClient.GetByteArrayAsync(mirrorUrl);
                    await File.WriteAllBytesAsync(filePath, bytes);
                    EnforceCacheSizeLimit();
                    return filePath;
                }
                catch (Exception ex)
                {
                    DebugLogger.Warn("ImageCache", $"镜像源下载图片失败: {mirrorUrl}, {ex.Message}, 回退到官方源");
                }
            }

            try
            {
                var bytes = await _httpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(filePath, bytes);
                EnforceCacheSizeLimit();
                return filePath;
            }
            catch (Exception ex)
            {
                DebugLogger.Error("ImageCache", $"下载图片失败: {url}, {ex.Message}");
                return null;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ImageCache", $"处理图片缓存失败: {url}, {ex.Message}");
            return null;
        }
    }

    public static long GetCacheSize()
    {
        try
        {
            if (!Directory.Exists(CacheDir)) return 0;
            return new DirectoryInfo(CacheDir).GetFiles().Sum(f => f.Length);
        }
        catch { return 0; }
    }

    public static void ClearCache()
    {
        try
        {
            if (!Directory.Exists(CacheDir)) return;
            foreach (var file in new DirectoryInfo(CacheDir).GetFiles())
            {
                try { file.Delete(); }
                catch { }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ImageCache", $"清理图标缓存失败: {ex.Message}");
        }
    }

    private static void EnforceCacheSizeLimit()
    {
        try
        {
            if (!Directory.Exists(CacheDir)) return;

            var dirInfo = new DirectoryInfo(CacheDir);
            var totalSize = dirInfo.GetFiles().Sum(f => f.Length);

            if (totalSize <= MaxCacheSizeBytes) return;

            var files = dirInfo.GetFiles()
                .OrderBy(f => f.LastAccessTimeUtc)
                .ToList();

            foreach (var file in files)
            {
                if (totalSize <= MaxCacheSizeBytes * 0.7) break;
                totalSize -= file.Length;
                try { file.Delete(); }
                catch { }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ImageCache", $"图标缓存大小限制检查失败: {ex.Message}");
        }
    }

    private static string GetMd5Hash(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
