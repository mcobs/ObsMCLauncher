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

public static class ModConflictDetector
{
    public static List<ModConflict> DetectConflicts(string modsDir)
    {
        var conflicts = new List<ModConflict>();
        if (!Directory.Exists(modsDir)) return conflicts;

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

        CheckDuplicateIds(metadataList, conflicts);
        CheckMissingDependencies(metadataList, conflicts);
        CheckLoaderMismatch(metadataList, conflicts);

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
}
