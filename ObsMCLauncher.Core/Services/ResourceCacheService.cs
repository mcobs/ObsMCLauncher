using System;
using System.Collections.Concurrent;
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
    private static readonly TimeSpan DefaultCacheExpiry = TimeSpan.FromDays(30);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();

    private static SemaphoreSlim GetFileLock(string filePath)
    {
        return _fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
    }

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
            DebugLogger.Info("ResourceCache", $"缓存目录已初始化: {CacheBaseDir}");
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
            var fileLock = GetFileLock(cacheFile);

            await fileLock.WaitAsync();
            try
            {
                var cacheItem = new CacheItem<T>
                {
                    Data = data,
                    CachedAt = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(cacheItem, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    PropertyNameCaseInsensitive = true
                });

                await File.WriteAllTextAsync(cacheFile, json);
            }
            finally
            {
                fileLock.Release();
            }

            EnforceCacheSizeLimit(cacheDir);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ResourceCache", $"缓存数据失败: {ex.Message}");
        }
    }

    public static async Task<T?> GetCachedDataAsync<T>(string key, string source, TimeSpan? maxAge = null)
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

            var fileLock = GetFileLock(cacheFile);
            await fileLock.WaitAsync();
            try
            {
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

                var effectiveMaxAge = maxAge ?? DefaultCacheExpiry;
                if (cacheItem.CachedAt.HasValue && DateTime.UtcNow - cacheItem.CachedAt.Value > effectiveMaxAge)
                {
                    DebugLogger.Info("ResourceCache", $"磁盘缓存已过期: key={GetSafeFileName(key)}, source={source}");
                    try { File.Delete(cacheFile); }
                    catch { }
                    return default;
                }

                DebugLogger.Info("ResourceCache", $"磁盘缓存命中: key={GetSafeFileName(key)}, source={source}");
                return cacheItem.Data;
            }
            finally
            {
                fileLock.Release();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ResourceCache", $"读取缓存数据失败: {ex.Message}");
            return default;
        }
    }

    public static long GetCacheSize(string? source = null)
    {
        try
        {
            long total = 0;
            var dirs = source?.ToLower() switch
            {
                "modrinth" => new[] { ModrinthCacheDir },
                "curseforge" => new[] { CurseForgeCacheDir },
                _ => new[] { ModrinthCacheDir, CurseForgeCacheDir }
            };

            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                total += new DirectoryInfo(dir).GetFiles("*.json").Sum(f => f.Length);
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }

    public static void ClearCache(string? source = null)
    {
        try
        {
            var dirs = source?.ToLower() switch
            {
                "modrinth" => new[] { ModrinthCacheDir },
                "curseforge" => new[] { CurseForgeCacheDir },
                _ => new[] { ModrinthCacheDir, CurseForgeCacheDir }
            };

            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in new DirectoryInfo(dir).GetFiles("*.json"))
                {
                    try { file.Delete(); }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ResourceCache", $"清理缓存失败: {ex.Message}");
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
        public T Data { get; set; } = default!;
        public DateTime? CachedAt { get; set; }
    }
}
