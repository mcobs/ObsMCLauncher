using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

/// <summary>
/// 资源信息磁盘缓存服务
/// </summary>
public class ResourceCacheService
{
    private static readonly string CacheBaseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OMCL", "cache");
    private static readonly string ModrinthCacheDir = Path.Combine(CacheBaseDir, "modrinth");
    private static readonly string CurseForgeCacheDir = Path.Combine(CacheBaseDir, "curseforge");

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

    /// <summary>
    /// 缓存数据到磁盘
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="data">要缓存的数据</param>
    /// <param name="source">缓存源 (modrinth/curseforge)</param>
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
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ResourceCache", $"缓存数据失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 从磁盘读取缓存数据
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="source">缓存源 (modrinth/curseforge)</param>
    /// <returns>缓存的数据，如果不存在或已过期返回null</returns>
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
                // 缓存损坏，删除文件
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



    /// <summary>
    /// 获取安全的文件名（移除无效字符）
    /// </summary>
    private static string GetSafeFileName(string input)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = new string(input.Where(c => !invalidChars.Contains(c)).ToArray());
        // 限制文件名长度
        return safeName.Length > 100 ? safeName.Substring(0, 100) : safeName;
    }

    /// <summary>
    /// 缓存项结构
    /// </summary>
    private class CacheItem<T>
    {
        public T Data { get; set; }
    }
}