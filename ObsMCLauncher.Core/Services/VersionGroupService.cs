using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

/// <summary>
/// 版本分组管理服务
/// 分组数据仅存储在每个版本的 OMCL/init.json 中，不再使用全局索引
/// </summary>
public static class VersionGroupService
{
    /// <summary>
    /// 获取所有分组（系统分组 + 自定义分组）
    /// 自定义分组定义仍保存在全局 config 中
    /// </summary>
    public static List<VersionGroup> GetAllGroups()
    {
        var config = LauncherConfig.Load();
        var groups = VersionGroup.GetSystemGroups();

        foreach (var custom in config.CustomVersionGroups)
        {
            if (!groups.Any(g => g.Id == custom.Id))
            {
                groups.Add(custom);
            }
        }

        return groups;
    }

    /// <summary>
    /// 获取版本的生效分组ID，直接读 init.json
    /// </summary>
    public static string GetEffectiveGroupId(Core.Services.Minecraft.InstalledVersion version)
    {
        var group = VersionInitService.GetGroup(version.Path);
        return string.IsNullOrEmpty(group) ? VersionGroup.AutoGroupId : group;
    }

    /// <summary>
    /// 获取版本的显示分组（自动分组时根据规则判断实际分组）
    /// </summary>
    public static string GetDisplayGroupId(Core.Services.Minecraft.InstalledVersion version)
    {
        var groupId = GetEffectiveGroupId(version);

        if (groupId == VersionGroup.AutoGroupId)
        {
            return ResolveAutoGroup(version);
        }

        return groupId;
    }

    /// <summary>
    /// 根据自动分组规则判断版本应属的分组
    /// 优先级：可装MOD > 常用版本 > 不常用版本
    /// </summary>
    private static string ResolveAutoGroup(Core.Services.Minecraft.InstalledVersion version)
    {
        if (IsModdable(version))
            return VersionGroup.ModdableGroupId;

        if (IsCommon(version))
            return VersionGroup.CommonGroupId;

        if (IsUncommon(version))
            return VersionGroup.UncommonGroupId;

        return VersionGroup.UncommonGroupId;
    }

    private static bool IsModdable(Core.Services.Minecraft.InstalledVersion version)
    {
        var loader = version.LoaderType?.ToLower();
        return loader is "forge" or "fabric" or "quilt" or "neoforge";
    }

    private static bool IsCommon(Core.Services.Minecraft.InstalledVersion version)
    {
        return version.LastPlayed > DateTime.Now.AddDays(-30) && version.LastPlayed > DateTime.MinValue;
    }

    private static bool IsUncommon(Core.Services.Minecraft.InstalledVersion version)
    {
        return version.LastPlayed <= DateTime.Now.AddDays(-30) || version.LastPlayed <= DateTime.MinValue;
    }

    /// <summary>
    /// 设置版本的分组，仅写 init.json
    /// </summary>
    public static void SetVersionGroup(string versionId, string versionPath, string groupId)
    {
        VersionInitService.SetGroup(versionPath, groupId);
    }

    /// <summary>
    /// 移除版本的分组映射（恢复为自动），同步更新 init.json
    /// </summary>
    public static void RemoveVersionGroup(string versionId, string? versionPath = null)
    {
        if (!string.IsNullOrEmpty(versionPath))
        {
            VersionInitService.SetGroup(versionPath, VersionGroup.AutoGroupId);
        }
    }

    /// <summary>
    /// 创建自定义分组
    /// </summary>
    public static VersionGroup CreateGroup(string name)
    {
        var config = LauncherConfig.Load();
        var id = $"custom_{Guid.NewGuid():N}";
        var group = new VersionGroup
        {
            Id = id,
            Name = name,
            IsSystem = false,
            IsDeletable = true
        };

        config.CustomVersionGroups.Add(group);
        config.Save();

        return group;
    }

    /// <summary>
    /// 重命名自定义分组
    /// </summary>
    public static bool RenameGroup(string groupId, string newName)
    {
        var config = LauncherConfig.Load();
        var group = config.CustomVersionGroups.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return false;

        group.Name = newName;
        config.Save();
        return true;
    }

    /// <summary>
    /// 删除自定义分组，组内版本归入"自动"
    /// </summary>
    public static bool DeleteGroup(string groupId, string? gameDirectory = null)
    {
        var config = LauncherConfig.Load();
        var group = config.CustomVersionGroups.FirstOrDefault(g => g.Id == groupId);
        if (group == null || group.IsDeletable == false) return false;

        config.CustomVersionGroups.Remove(group);
        config.Save();

        // 把使用该分组的版本重置为"自动"
        if (!string.IsNullOrEmpty(gameDirectory) && Directory.Exists(gameDirectory))
        {
            var versionsPath = Path.Combine(gameDirectory, "versions");
            if (Directory.Exists(versionsPath))
            {
                foreach (var versionDir in Directory.GetDirectories(versionsPath))
                {
                    try
                    {
                        var data = VersionInitService.Load(versionDir);
                        if (data.Group == groupId)
                        {
                            VersionInitService.SetGroup(versionDir, VersionGroup.AutoGroupId);
                        }
                    }
                    catch { }
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 确保 init.json 存在，并把旧的全局索引数据迁移过来（一次性）
    /// </summary>
    public static void EnsureInitJson(string versionPath)
    {
        if (string.IsNullOrEmpty(versionPath)) return;

        VersionInitService.EnsureExists(versionPath);

        // 一次性迁移：如果 init.json 里分组是默认值，但全局 config 里有旧映射，则迁移过来
        try
        {
            var config = LauncherConfig.Load();
            var versionId = Path.GetFileName(versionPath);
            if (config.VersionGroupMappings.TryGetValue(versionId, out var legacyGroupId))
            {
                var current = VersionInitService.GetGroup(versionPath);
                // init.json 里是默认的"自动"，说明还没迁移过
                if (string.IsNullOrEmpty(current) || current == VersionGroup.AutoGroupId)
                {
                    VersionInitService.SetGroup(versionPath, legacyGroupId);
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// 为所有已安装版本同步 init.json（缺失则创建）
    /// </summary>
    public static void SyncAllInitJsons(string gameDirectory)
    {
        var versionsPath = Path.Combine(gameDirectory, "versions");
        if (!Directory.Exists(versionsPath)) return;

        foreach (var versionDir in Directory.GetDirectories(versionsPath))
        {
            var versionId = Path.GetFileName(versionDir);
            var jsonPath = Path.Combine(versionDir, $"{versionId}.json");
            if (!File.Exists(jsonPath)) continue;

            try
            {
                EnsureInitJson(versionDir);
            }
            catch (Exception ex)
            {
                DebugLogger.Warn("VersionGroup", $"同步 init.json 失败 [{versionId}]: {ex.Message}");
            }
        }
    }
}
