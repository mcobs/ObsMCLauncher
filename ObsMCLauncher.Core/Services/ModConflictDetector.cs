using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

public class ModConflict
{
    public ConflictType Type { get; set; }
    public string ModId1 { get; set; } = "";
    public string ModId2 { get; set; } = "";
    public string Name1 { get; set; } = "";
    public string Name2 { get; set; } = "";
    public string Description { get; set; } = "";
    public string Suggestion { get; set; } = "";
    public ConflictSeverity Severity { get; set; } = ConflictSeverity.Warning;
}

public enum ConflictType
{
    DuplicateId,
    MissingDependency,
    LoaderMismatch,
    VersionIncompatible
}

public enum ConflictSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// 简化的语义化版本号，支持 MAJOR.MINOR.PATCH[-PRERELEASE] 格式
/// </summary>
public class ModVersion : IComparable<ModVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string? PreRelease { get; }
    public string Raw { get; }
    public bool IsValid { get; }

    public ModVersion(string? version)
    {
        Raw = version ?? "";
        IsValid = false;
        if (string.IsNullOrWhiteSpace(version)) return;

        try
        {
            var core = version.Trim();
            // 去掉 build metadata
            var plusIdx = core.IndexOf('+');
            if (plusIdx >= 0) core = core[..plusIdx];

            // 拆出 prerelease
            if (core.Length > 0)
            {
                var hyphenIdx = core.IndexOf('-');
                if (hyphenIdx >= 0)
                {
                    PreRelease = core[(hyphenIdx + 1)..];
                    core = core[..hyphenIdx];
                }
            }

            var parts = core.Split('.', 3);
            if (parts.Length > 0 && int.TryParse(parts[0], out var maj))
            {
                Major = maj;
                Minor = parts.Length > 1 && int.TryParse(parts[1], out var min) ? min : 0;
                Patch = parts.Length > 2 && int.TryParse(parts[2], out var pat) ? pat : 0;
                IsValid = true;
            }
        }
        catch { /* 解析失败保持 IsValid=false */ }
    }

    public int CompareTo(ModVersion? other)
    {
        if (other is null) return 1;

        // 任一无效时退化为字符串比较，保证稳定性
        if (!IsValid || !other.IsValid)
            return string.Compare(Raw, other.Raw, StringComparison.Ordinal);

        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        if (Patch != other.Patch) return Patch.CompareTo(other.Patch);

        // 同号情况下：有 prerelease 的版本低于正式版
        if (PreRelease is null && other.PreRelease is null) return 0;
        if (PreRelease is null) return 1;
        if (other.PreRelease is null) return -1;
        return string.Compare(PreRelease, other.PreRelease, StringComparison.Ordinal);
    }

    /// <summary>
    /// 主版本号差距，用于评估 Severity
    /// </summary>
    public int MajorGapWith(ModVersion other) => Math.Abs(Major - other.Major);
}

/// <summary>
/// 依赖版本范围，支持 Maven 风格语法：
/// [1.0.0,2.0.0)  半开区间
/// [1.0.0]        精确版本
/// [1.0.0,)       无上限
/// 1.0.0          软要求（推荐版本，允许任何版本）
/// </summary>
public class ModVersionRange
{
    private readonly List<Interval> _intervals = new();
    public string Raw { get; }
    public bool IsSoftRequirement { get; }
    public ModVersion? Recommended { get; }

    public ModVersionRange(string? range)
    {
        Raw = range ?? "";
        var trimmed = (range ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            IsSoftRequirement = true;
            return;
        }

        // 不以 [ 或 ( 开头视为软要求
        if (trimmed[0] != '[' && trimmed[0] != '(')
        {
            IsSoftRequirement = true;
            Recommended = new ModVersion(trimmed);
            return;
        }

        IsSoftRequirement = false;
        ParseIntervals(trimmed);
    }

    private void ParseIntervals(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] != '[' && text[i] != '(') { i++; continue; }

            char lowerCh = text[i];
            int closeIdx = text.IndexOfAny(new[] { ']', ')' }, i + 1);
            if (closeIdx < 0) break;
            char upperCh = text[closeIdx];

            var inside = text.Substring(i + 1, closeIdx - i - 1).Trim();
            var parts = inside.Split(',', 2);

            ModVersion? lower = null, upper = null;
            if (parts.Length >= 1 && !string.IsNullOrWhiteSpace(parts[0]))
                lower = new ModVersion(parts[0].Trim());
            if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
                upper = new ModVersion(parts[1].Trim());

            _intervals.Add(new Interval
            {
                Lower = lower,
                LowerInclusive = lowerCh == '[',
                Upper = upper,
                UpperInclusive = upperCh == ']'
            });

            i = closeIdx + 1;
        }
    }

    public bool Contains(ModVersion version)
    {
        if (IsSoftRequirement) return true; // 软要求允许任何版本
        foreach (var iv in _intervals)
            if (iv.Contains(version)) return true;
        return false;
    }

    /// <summary>
    /// 评估当前已安装版本与要求范围的偏差，用于决定 Severity
    /// </summary>
    public VersionDeviation Assess(ModVersion version)
    {
        var dev = new VersionDeviation();

        // 软要求：不报告冲突，仅在偏离推荐版本时给 Info
        if (IsSoftRequirement)
        {
            dev.InRange = true;
            if (Recommended is not null && version.IsValid && version.CompareTo(Recommended) != 0)
            {
                dev.Severity = ConflictSeverity.Info;
                dev.MajorGap = version.MajorGapWith(Recommended);
            }
            return dev;
        }

        foreach (var iv in _intervals)
        {
            if (iv.Contains(version))
            {
                dev.InRange = true;
                return dev; // 命中任一区间即视为通过
            }
        }

        // 在范围外：根据与最近边界的差距决定 Severity
        dev.InRange = false;
        ModVersion? nearest = null;
        bool below = false;
        foreach (var iv in _intervals)
        {
            if (iv.Lower is not null && version.CompareTo(iv.Lower) < 0)
            {
                if (nearest is null || version.CompareTo(nearest) > 0)
                {
                    nearest = iv.Lower;
                    below = true;
                }
            }
            else if (iv.Upper is not null && version.CompareTo(iv.Upper) > 0)
            {
                if (nearest is null || version.CompareTo(nearest) < 0)
                {
                    nearest = iv.Upper;
                    below = false;
                }
            }
        }

        if (nearest is not null)
        {
            dev.NearestBound = nearest;
            dev.IsBelowBound = below;
            dev.MajorGap = version.IsValid ? version.MajorGapWith(nearest) : 0;
            // 主版本号差距 >=1 视为严重不兼容，否则为警告
            dev.Severity = dev.MajorGap >= 1 ? ConflictSeverity.Error : ConflictSeverity.Warning;
        }
        else
        {
            dev.Severity = ConflictSeverity.Warning;
        }
        return dev;
    }

    private class Interval
    {
        public ModVersion? Lower;
        public bool LowerInclusive;
        public ModVersion? Upper;
        public bool UpperInclusive;

        public bool Contains(ModVersion v)
        {
            if (Lower is not null)
            {
                int c = v.CompareTo(Lower);
                if (LowerInclusive ? c < 0 : c <= 0) return false;
            }
            if (Upper is not null)
            {
                int c = v.CompareTo(Upper);
                if (UpperInclusive ? c > 0 : c >= 0) return false;
            }
            return true;
        }
    }
}

/// <summary>
/// 版本偏差评估结果
/// </summary>
public class VersionDeviation
{
    public bool InRange;
    public ConflictSeverity? Severity;
    public int MajorGap;
    public ModVersion? NearestBound;
    public bool IsBelowBound;
}

public static class ModConflictDetector
{
    public static List<ModConflict> DetectConflicts(string modsDir)
    {
        if (!Directory.Exists(modsDir)) return new List<ModConflict>();

        var modFiles = Directory.GetFiles(modsDir, "*.jar")
            .Concat(Directory.GetFiles(modsDir, "*.jar.disabled"))
            .ToList();

        var metadataList = new List<(string FilePath, ModMetadata Meta, bool Enabled)>();

        foreach (var file in modFiles)
        {
            var meta = ModMetadataParser.ParseFromJar(file);
            if (meta != null)
            {
                var enabled = !file.EndsWith(".disabled");
                metadataList.Add((file, meta, enabled));
            }
        }

        return DetectConflicts(metadataList);
    }

    /// <summary>
    /// 复用调用方已经解析好的元数据做冲突检测，避免为了检测再把整个目录的 jar 解压一遍。
    /// </summary>
    public static List<ModConflict> DetectConflicts(
        IReadOnlyList<(string FilePath, ModMetadata Meta, bool Enabled)> mods)
    {
        var conflicts = new List<ModConflict>();
        if (mods.Count == 0) return conflicts;

        var metadataList = mods.Where(m => m.Meta != null).ToList();

        CheckDuplicateIds(metadataList, conflicts);
        CheckMissingDependencies(metadataList, conflicts);
        CheckLoaderMismatch(metadataList, conflicts);
        CheckVersionIncompatibility(metadataList, conflicts);

        return conflicts;
    }

    private static void CheckDuplicateIds(
        List<(string FilePath, ModMetadata Meta, bool Enabled)> mods,
        List<ModConflict> conflicts)
    {
        var enabledMods = mods.Where(m => m.Enabled).ToList();
        var idGroups = enabledMods
            .Where(m => !string.IsNullOrEmpty(m.Meta.ModId))
            .GroupBy(m => m.Meta.ModId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in idGroups)
        {
            var modList = group.ToList();
            for (int i = 0; i < modList.Count; i++)
            {
                for (int j = i + 1; j < modList.Count; j++)
                {
                    conflicts.Add(new ModConflict
                    {
                        Type = ConflictType.DuplicateId,
                        ModId1 = group.Key,
                        ModId2 = group.Key,
                        Name1 = modList[i].Meta.Name ?? Path.GetFileName(modList[i].FilePath),
                        Name2 = modList[j].Meta.Name ?? Path.GetFileName(modList[j].FilePath),
                        Description = $"存在重复的模组ID \"{group.Key}\"，可能导致游戏崩溃",
                        Severity = ConflictSeverity.Error
                    });
                }
            }
        }
    }

    private static void CheckMissingDependencies(
        List<(string FilePath, ModMetadata Meta, bool Enabled)> mods,
        List<ModConflict> conflicts)
    {
        var enabledMods = mods.Where(m => m.Enabled).ToList();
        var enabledIds = new HashSet<string>(
            enabledMods.Select(m => m.Meta.ModId),
            StringComparer.OrdinalIgnoreCase);

        // 忽略 Minecraft/Java/Fabric Loader 等系统级依赖
        var systemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "minecraft", "java", "fabricloader", "fabric-api", "fabric",
            "quilt_loader", "quilted_fabric_api", "qsl", "quilt_base",
            "forge", "neoforge", "fml"
        };

        foreach (var (filePath, meta, _) in enabledMods)
        {
            foreach (var dep in meta.Dependencies)
            {
                if (!dep.IsRequired) continue;
                if (systemIds.Contains(dep.ModId)) continue;
                if (enabledIds.Contains(dep.ModId)) continue;

                conflicts.Add(new ModConflict
                {
                    Type = ConflictType.MissingDependency,
                    ModId1 = meta.ModId,
                    ModId2 = dep.ModId,
                    Name1 = meta.Name ?? Path.GetFileName(filePath),
                    Name2 = dep.ModId,
                    Description = $"\"{meta.Name ?? meta.ModId}\" 需要 \"{dep.ModId}\"，但未安装" +
                                  (!string.IsNullOrEmpty(dep.VersionRange) ? $" (版本要求: {dep.VersionRange})" : ""),
                    Severity = ConflictSeverity.Error
                });
            }
        }
    }

    private static void CheckLoaderMismatch(
        List<(string FilePath, ModMetadata Meta, bool Enabled)> mods,
        List<ModConflict> conflicts)
    {
        var enabledMods = mods.Where(m => m.Enabled).ToList();
        var loaderGroups = enabledMods
            .Where(m => !string.IsNullOrEmpty(m.Meta.Loader))
            .GroupBy(m => m.Meta.Loader, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        // 检查是否存在 Fabric 和 Forge/NeoForge 模组混用
        var loaders = enabledMods
            .Where(m => !string.IsNullOrEmpty(m.Meta.Loader))
            .Select(m => m.Meta.Loader.ToLowerInvariant())
            .Distinct()
            .ToList();

        bool hasFabric = loaders.Contains("fabric") || loaders.Contains("quilt");
        bool hasForge = loaders.Contains("forge") || loaders.Contains("neoforge");

        if (hasFabric && hasForge)
        {
            var fabricMods = enabledMods.Where(m => m.Meta.Loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase) ||
                                                     m.Meta.Loader.Equals("Quilt", StringComparison.OrdinalIgnoreCase)).ToList();
            var forgeMods = enabledMods.Where(m => m.Meta.Loader.Equals("Forge", StringComparison.OrdinalIgnoreCase) ||
                                                    m.Meta.Loader.Equals("NeoForge", StringComparison.OrdinalIgnoreCase)).ToList();

            conflicts.Add(new ModConflict
            {
                Type = ConflictType.LoaderMismatch,
                ModId1 = fabricMods.FirstOrDefault().Meta.ModId ?? "",
                ModId2 = forgeMods.FirstOrDefault().Meta.ModId ?? "",
                Name1 = "Fabric/Quilt 模组",
                Name2 = "Forge/NeoForge 模组",
                Description = $"Fabric 和 Forge 模组不兼容，不能同时使用（{fabricMods.Count} 个Fabric vs {forgeMods.Count} 个Forge）",
                Severity = ConflictSeverity.Error
            });
        }
    }

    /// <summary>
    /// 检查已安装的依赖模组版本是否满足要求版本范围
    /// </summary>
    /// <remarks>
    /// Severity 分级标准：
    /// - Error:   已安装版本在硬性范围外，且主版本号与最近边界差距 >=1
    /// - Warning: 已安装版本在硬性范围外，但仅 minor/patch 差距
    /// - Info:    软要求情况下偏离推荐版本（不报告，仅作记录）
    /// </remarks>
    private static void CheckVersionIncompatibility(
        List<(string FilePath, ModMetadata Meta, bool Enabled)> mods,
        List<ModConflict> conflicts)
    {
        var enabledMods = mods.Where(m => m.Enabled).ToList();

        // 构建已启用模组的 ModId -> ModVersion 映射
        var installedVersions = new Dictionary<string, ModVersion>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in enabledMods)
        {
            if (string.IsNullOrEmpty(m.Meta.ModId) || string.IsNullOrEmpty(m.Meta.Version))
                continue;
            var v = new ModVersion(m.Meta.Version);
            if (v.IsValid)
                installedVersions[m.Meta.ModId] = v;
        }

        // 忽略 Minecraft/Java/Loader 等系统级依赖
        var systemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "minecraft", "java", "fabricloader", "fabric-api", "fabric",
            "quilt_loader", "quilted_fabric_api", "qsl", "quilt_base",
            "forge", "neoforge", "fml"
        };

        foreach (var (filePath, meta, _) in enabledMods)
        {
            foreach (var dep in meta.Dependencies)
            {
                if (!dep.IsRequired) continue;
                if (systemIds.Contains(dep.ModId)) continue;
                if (string.IsNullOrEmpty(dep.VersionRange)) continue;

                // 缺失依赖由 CheckMissingDependencies 处理，这里只校验已安装的
                if (!installedVersions.TryGetValue(dep.ModId, out var installed))
                    continue;

                var range = new ModVersionRange(dep.VersionRange);
                var dev = range.Assess(installed);

                // 软要求下的 Info 提示过于嘈杂，暂不报告
                if (dev.Severity is null or ConflictSeverity.Info) continue;
                if (dev.InRange) continue;

                var severity = dev.Severity.Value;
                string direction = dev.IsBelowBound ? "过低" : "过高";
                string description = $"\"{meta.Name ?? meta.ModId}\" 要求 \"{dep.ModId}\" 版本 {dep.VersionRange}，" +
                                     $"当前安装版本 {installed.Raw} {direction}";

                string suggestion = severity == ConflictSeverity.Error
                    ? $"请将 {dep.ModId} {(dev.IsBelowBound ? "升级到" : "降级到")} 满足 {dep.VersionRange} 的版本，否则可能导致游戏崩溃"
                    : $"建议调整 {dep.ModId} 版本以满足兼容性要求 {dep.VersionRange}，当前版本可能存在运行时异常";

                conflicts.Add(new ModConflict
                {
                    Type = ConflictType.VersionIncompatible,
                    ModId1 = meta.ModId,
                    ModId2 = dep.ModId,
                    Name1 = meta.Name ?? Path.GetFileName(filePath),
                    Name2 = dep.ModId,
                    Description = description,
                    Suggestion = suggestion,
                    Severity = severity
                });
            }
        }
    }
}
