using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

public class ResourceCacheService
{
    private static readonly string CacheBaseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OMCL", "cache");
    private static readonly string ModrinthCacheDir = Path.Combine(CacheBaseDir, "modrinth");
    private static readonly string CurseForgeCacheDir = Path.Combine(CacheBaseDir, "curseforge");
    private const long MaxCacheSizeBytes = 200 * 1024 * 1024;

    static ResourceCacheService()
    {
        try
        {
            if (!Directory.Exists(ModrinthCacheDir))
            {
                Directory.CreateDirectory(ModrinthCacheDir);
            }
            if (!Directory.Exists(CurseForgeCacheDir))
            {
                Directory.CreateDirectory(CurseForgeCacheDir);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ResourceCache", $"创建缓存目录失败: {ex.Message}");
        }
    }

    public static async Task CacheDataAsync<T>(string key, T data, string source)
    {
        try
        {
            var cacheDir = source.ToLower() switch
            {
                "modrinth" => ModrinthCacheDir,
                "curseforge" => CurseForgeCacheDir,
                _ => throw new ArgumentException("Invalid source", nameof(source))
            };

            var cacheFile = Path.Combine(cacheDir, $"{GetSafeFileName(key)}.json");

            var cacheItem = new CacheItem<T>
            {
                Data = data
            };

            var json = JsonSerializer.Serialize(cacheItem, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNameCaseInsensitive = true
            });

            await File.WriteAllTextAsync(cacheFile, json);

            EnforceCacheSizeLimit(cacheDir);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ResourceCache", $"缓存数据失败: {ex.Message}");
        }
    }

    public static async Task<T?> GetCachedDataAsync<T>(string key, string source)
    {
        try
        {
            var cacheDir = source.ToLower() switch
            {
                "modrinth" => ModrinthCacheDir,
                "curseforge" => CurseForgeCacheDir,
                _ => throw new ArgumentException("Invalid source", nameof(source))
            };

            var cacheFile = Path.Combine(cacheDir, $"{GetSafeFileName(key)}.json");

            if (!File.Exists(cacheFile))
            {
                return default;
            }

            var json = await File.ReadAllTextAsync(cacheFile);
            var cacheItem = JsonSerializer.Deserialize<CacheItem<T>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (cacheItem == null)
            {
                try { File.Delete(cacheFile); }
                catch { }
                return default;
            }

            return cacheItem.Data;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ResourceCache", $"读取缓存数据失败: {ex.Message}");
            return default;
        }
    }

    private static void EnforceCacheSizeLimit(string cacheDir)
    {
        try
        {
            if (!Directory.Exists(cacheDir)) return;

            var dirInfo = new DirectoryInfo(cacheDir);
            var totalSize = dirInfo.GetFiles("*.json").Sum(f => f.Length);

            if (totalSize <= MaxCacheSizeBytes) return;

            var files = dirInfo.GetFiles("*.json")
                .OrderBy(f => f.LastAccessTime)
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
            DebugLogger.Error("ResourceCache", $"缓存大小限制检查失败: {ex.Message}");
        }
    }

    private static string GetSafeFileName(string input)
    {
        if (string.IsNullOrEmpty(input)) return "empty";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private class CacheItem<T>
    {
        public T Data { get; set; }
    }
}
