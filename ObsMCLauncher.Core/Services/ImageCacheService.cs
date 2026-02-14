using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ObsMCLauncher.Core.Services;

/// <summary>
/// 图片缓存服务 - 仅负责下载和磁盘缓存逻辑，不依赖 UI 框架
/// </summary>
public class ImageCacheService
{
    private static readonly HttpClient _httpClient = new();
    private static readonly string CacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OMCL", "cache", "icons");

    static ImageCacheService()
    {
        if (!Directory.Exists(CacheDir))
        {
            Directory.CreateDirectory(CacheDir);
        }
    }

    /// <summary>
    /// 获取图片的本地缓存路径，如果不存在则下载
    /// </summary>
    /// <param name="url">图片 URL</param>
    /// <returns>本地文件路径，下载失败返回 null</returns>
    public static async Task<string?> GetImagePathAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            // 处理 URL 带有参数的情况并生成 MD5 文件名
            string fileName = GetMd5Hash(url) + Path.GetExtension(url.Split('?')[0]);
            if (string.IsNullOrEmpty(Path.GetExtension(fileName))) fileName += ".png";
            
            string filePath = Path.Combine(CacheDir, fileName);

            if (File.Exists(filePath))
            {
                return filePath;
            }

            var bytes = await _httpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(filePath, bytes);

            return filePath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCache] 下载图片失败: {url}, {ex.Message}");
            return null;
        }
    }

    private static string GetMd5Hash(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
