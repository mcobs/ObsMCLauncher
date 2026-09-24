using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

/// <summary>
/// 扫描结果：一个本地模组文件（含解析出的元数据与图标缓存路径）。
/// </summary>
public sealed class ScannedModFile
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public bool IsEnabled { get; set; }
    public long Size { get; set; }
    public ModMetadata? Metadata { get; set; }
    public string? IconCachePath { get; set; }
}

/// <summary>
/// 本地 mods 目录扫描器。
///
/// 目标是把「打开实例 / 刷新列表」的开销压到最低：
/// 1. 每个 jar 最多只解压一次（元数据与图标在同一棵 ZipArchive 上完成）；
/// 2. 元数据按「路径 + 修改时间 + 大小」持久缓存，未变更时完全不解压；
/// 3. 图标缓存文件名带同样的签名，存在即可直接用，不需要开 zip 比对条目长度；
/// 4. 多文件并行扫描（有界并发），结果按文件名排序，顺序稳定。
/// </summary>
public static class LocalModScanner
{
    /// <summary>缓存结构变更时递增，旧缓存会被整份丢弃。</summary>
    private const int CacheVersion = 1;

    private static readonly string CacheFilePath =
        Path.Combine(VersionInfo.GetAppBaseDirectory(), "OMCL", "cache", "mod_scan_cache.json");

    public static List<ScannedModFile> Scan(string modsDir, string iconCacheDir)
    {
        var results = new List<ScannedModFile>();
        if (!Directory.Exists(modsDir)) return results;

        var files = Directory.GetFiles(modsDir, "*.jar")
            .Concat(Directory.GetFiles(modsDir, "*.jar.disabled"))
            .ToArray();
        if (files.Length == 0) return results;

        var cache = LoadCache();
        // 从旧缓存起步，这样其它实例的记录不会被这次扫描覆盖掉
        var merged = new ConcurrentDictionary<string, ModScanCacheEntry>(
            cache.Entries, StringComparer.OrdinalIgnoreCase);
        var scanned = new ConcurrentBag<ScannedModFile>();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8)
        };

        Parallel.ForEach(files, options, file =>
        {
            try
            {
                var item = ScanFile(file, iconCacheDir, cache.Entries, out var entry);
                if (entry != null) merged[file] = entry;
                if (item != null) scanned.Add(item);
            }
            catch
            {
                // 单个文件失败不影响整批
            }
        });

        results.AddRange(scanned.OrderBy(m => m.FileName, StringComparer.OrdinalIgnoreCase));
        SaveCache(merged);
        return results;
    }

    private static ScannedModFile? ScanFile(
        string file,
        string iconCacheDir,
        IReadOnlyDictionary<string, ModScanCacheEntry> cache,
        out ModScanCacheEntry? entry)
    {
        entry = null;

        var info = new FileInfo(file);
        if (!info.Exists) return null;

        var ticks = info.LastWriteTimeUtc.Ticks;
        var length = info.Length;
        var fileName = info.Name;
        var enabled = !fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

        var iconPath = BuildIconCachePath(iconCacheDir, file, ticks, length);

        var cacheHit = cache.TryGetValue(file, out var cached)
                       && cached != null
                       && cached.Ticks == ticks
                       && cached.Length == length;

        // 上次已经确认过「这个包里没有图标」的话，这次也不用再开一遍
        var cachedIconChecked = cacheHit && cached!.IconChecked;
        var iconReady = File.Exists(iconPath);
        var iconChecked = iconReady || cachedIconChecked;

        var meta = cacheHit ? cached!.Meta : null;

        // 只有元数据或图标真的缺一个，才需要解压，而且只解压一次
        if (!cacheHit || (!iconReady && !cachedIconChecked))
        {
            try
            {
                using var archive = ZipFile.OpenRead(file);
                if (!cacheHit) meta = ModMetadataParser.ParseFromArchive(archive);
                if (!iconReady)
                {
                    iconReady = ExtractIcon(archive, meta, iconCacheDir, iconPath, file);
                    iconChecked = true;
                }
            }
            catch
            {
                // 不是合法 zip（或读取失败）时保留已有信息
            }
        }

        entry = new ModScanCacheEntry
        {
            Ticks = ticks,
            Length = length,
            Meta = meta,
            IconChecked = iconChecked
        };

        return new ScannedModFile
        {
            FilePath = file,
            FileName = fileName,
            IsEnabled = enabled,
            Size = length,
            Metadata = meta,
            IconCachePath = iconReady ? iconPath : null
        };
    }

    /// <summary>
    /// 图标缓存路径：路径哈希 + 源文件签名。签名变了就是新文件，不用开 zip 判断是否过期。
    /// </summary>
    private static string BuildIconCachePath(string iconCacheDir, string file, long ticks, long length)
    {
        var pathHash = StableHash(file);
        var signature = StableHash($"{file}|{ticks}|{length}");
        return Path.Combine(iconCacheDir, $"{pathHash}_{signature}.png");
    }

    private static bool ExtractIcon(
        ZipArchive archive, ModMetadata? meta, string iconCacheDir, string targetPath, string sourcePath)
    {
        var iconEntry = FindIconEntry(archive, meta);
        if (iconEntry == null) return false;

        try
        {
            Directory.CreateDirectory(iconCacheDir);
            iconEntry.ExtractToFile(targetPath, true);
            PruneStaleIcons(iconCacheDir, sourcePath, targetPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ZipArchiveEntry? FindIconEntry(ZipArchive archive, ModMetadata? meta)
    {
        // 元数据声明的图标路径优先
        if (!string.IsNullOrEmpty(meta?.IconPath))
        {
            var declared = archive.GetEntry(meta.IconPath!);
            if (declared != null) return declared;
        }

        foreach (var candidate in new[] { "pack.png", "logo.png", "icon.png" })
        {
            var entry = archive.GetEntry(candidate);
            if (entry != null) return entry;
        }

        // assets/<modid>/.../icon.png
        var modId = meta?.ModId;
        if (!string.IsNullOrEmpty(modId))
        {
            var prefix = $"assets/{modId}/";
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    entry.FullName.EndsWith("/icon.png", StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
        }

        // 兜底：任意 assets 下的 icon.png
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith("/icon.png", StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        return null;
    }

    /// <summary>
    /// 同一个模组路径只会保留当前签名的那张图标，避免源文件每次更新都在缓存目录里留一份。
    /// </summary>
    private static void PruneStaleIcons(string iconCacheDir, string sourcePath, string keepPath)
    {
        try
        {
            var prefix = StableHash(sourcePath) + "_";
            foreach (var stale in Directory.GetFiles(iconCacheDir, prefix + "*.png"))
            {
                if (string.Equals(stale, keepPath, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(stale); } catch { }
            }
        }
        catch
        {
        }
    }

    private static string StableHash(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static ModScanCache LoadCache()
    {
        try
        {
            if (!File.Exists(CacheFilePath)) return new ModScanCache();

            var cache = JsonSerializer.Deserialize<ModScanCache>(File.ReadAllText(CacheFilePath));
            if (cache == null || cache.Version != CacheVersion) return new ModScanCache();

            // JSON 反序列化出来的字典用的是默认比较器，统一成忽略大小写（Windows 路径）
            cache.Entries = new Dictionary<string, ModScanCacheEntry>(
                cache.Entries, StringComparer.OrdinalIgnoreCase);
            return cache;
        }
        catch
        {
            return new ModScanCache();
        }
    }

    private static void SaveCache(IReadOnlyDictionary<string, ModScanCacheEntry> entries)
    {
        try
        {
            var dir = Path.GetDirectoryName(CacheFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var cache = new ModScanCache
            {
                Version = CacheVersion,
                // 顺手清掉已经不存在的文件，缓存不会无限增长
                Entries = entries
                    .Where(kv => File.Exists(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            };

            var tmpPath = CacheFilePath + ".tmp";
            File.WriteAllText(tmpPath, JsonSerializer.Serialize(cache));
            File.Move(tmpPath, CacheFilePath, true);
        }
        catch
        {
        }
    }
}

internal sealed class ModScanCache
{
    public int Version { get; set; }
    public Dictionary<string, ModScanCacheEntry> Entries { get; set; } = new();
}

internal sealed class ModScanCacheEntry
{
    public long Ticks { get; set; }
    public long Length { get; set; }
    public ModMetadata? Meta { get; set; }

    /// <summary>是否已经确认过图标的有无（包括「确认没有」），避免每次都为了找图标再开一遍 zip。</summary>
    public bool IconChecked { get; set; }
}
