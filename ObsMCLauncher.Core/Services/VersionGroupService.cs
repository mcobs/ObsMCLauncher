using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

/// <summary>
/// 版本分组管理服务
/// </summary>
public static class VersionGroupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 获取所有分组（系统分组 + 自定义分组）
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
    /// 获取版本的生效分组ID
    /// 优先读 config 索引，缺失时 fallback 读 init.json 并回填索引
    /// </summary>
    public static string GetEffectiveGroupId(Core.Services.Minecraft.InstalledVersion version)
    {
        var config = LauncherConfig.Load();

        // 优先读 config 索引（快，无需文件IO）
        if (config.VersionGroupMappings.TryGetValue(version.Id, out var groupId))
        {
            return groupId;
        }

        // fallback：读 init.json（版本可能从其他地方迁移来）
        var initGroup = ReadGroupFromInitJson(version.Path);
        if (initGroup != null)
        {
            // 回填 config 索引，下次无需再读文件
            config.VersionGroupMappings[version.Id] = initGroup;
            config.Save();
            return initGroup;
        }

        // 都没有则归入"自动"
        return VersionGroup.AutoGroupId;
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

        // 不满足任何条件的归入不常用版本
        return VersionGroup.UncommonGroupId;
    }

    private static bool IsModdable(Core.Services.Minecraft.InstalledVersion version)
    {
        var loader = version.LoaderType?.ToLower();
        return loader is "forge" or "fabric" or "quilt" or "neoforge";
    }

    private static bool IsCommon(Core.Services.Minecraft.InstalledVersion version)
    {
        // 30天内游玩过且不是从未游玩
        return version.LastPlayed > DateTime.Now.AddDays(-30) && version.LastPlayed > DateTime.MinValue;
    }

    private static bool IsUncommon(Core.Services.Minecraft.InstalledVersion version)
    {
        // 超过30天未游玩，或从未游玩过
        return version.LastPlayed <= DateTime.Now.AddDays(-30) || version.LastPlayed <= DateTime.MinValue;
    }

    /// <summary>
    /// 设置版本的分组，init.json 先写再同步 config 索引
    /// </summary>
    public static void SetVersionGroup(string versionId, string versionPath, string groupId)
    {
        // 先写 init.json（权威源）
        UpdateInitJson(versionPath, groupId);

        // 再同步 config 索引
        var config = LauncherConfig.Load();
        config.VersionGroupMappings[versionId] = groupId;
        config.Save();
    }

    /// <summary>
    /// 移除版本的分组映射（恢复为自动），同步更新 init.json
    /// </summary>
    public static void RemoveVersionGroup(string versionId, string? versionPath = null)
    {
        var config = LauncherConfig.Load();
        config.VersionGroupMappings.Remove(versionId);
        config.Save();

        // 同步 init.json 为"自动"
        if (!string.IsNullOrEmpty(versionPath))
        {
            UpdateInitJson(versionPath, VersionGroup.AutoGroupId);
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
    public static bool DeleteGroup(string groupId)
    {
        var config = LauncherConfig.Load();
        var group = config.CustomVersionGroups.FirstOrDefault(g => g.Id == groupId);
        if (group == null || group.IsDeletable == false) return false;

        config.CustomVersionGroups.Remove(group);

        // 组内版本归入"自动"
        var affectedVersions = config.VersionGroupMappings
            .Where(kv => kv.Value == groupId)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var versionId in affectedVersions)
        {
            config.VersionGroupMappings[versionId] = VersionGroup.AutoGroupId;
        }

        config.Save();
        return true;
    }

    /// <summary>
    /// 确保版本目录下 OMCL/init.json 与 config 索引保持同步
    /// </summary>
    public static void EnsureInitJson(string versionPath)
    {
        if (string.IsNullOrEmpty(versionPath)) return;

        var versionId = Path.GetFileName(versionPath);
        var config = LauncherConfig.Load();
        var groupId = config.VersionGroupMappings.TryGetValue(versionId, out var gid)
            ? gid
            : VersionGroup.AutoGroupId;

        // 强制同步，确保 init.json 始终反映最新状态
        UpdateInitJson(versionPath, groupId);
    }

    /// <summary>
    /// 更新版本目录下 OMCL/init.json 的分组信息
    /// </summary>
    private static void UpdateInitJson(string versionPath, string groupId)
    {
        if (string.IsNullOrEmpty(versionPath)) return;

        var omclDir = Path.Combine(versionPath, "OMCL");
        var initPath = Path.Combine(omclDir, "init.json");

        if (!Directory.Exists(omclDir))
        {
            Directory.CreateDirectory(omclDir);
        }

        VersionInitData initData;

        if (File.Exists(initPath))
        {
            try
            {
                var existingJson = File.ReadAllText(initPath);
                initData = JsonSerializer.Deserialize<VersionInitData>(existingJson, JsonOptions)
                           ?? new VersionInitData();
            }
            catch
            {
                initData = new VersionInitData();
            }
        }
        else
        {
            initData = new VersionInitData();
        }

        initData.Group = groupId;
        var json = JsonSerializer.Serialize(initData, JsonOptions);
        File.WriteAllText(initPath, json);
    }

    /// <summary>
    /// 从 init.json 读取分组信息
    /// </summary>
    public static string? ReadGroupFromInitJson(string versionPath)
    {
        if (string.IsNullOrEmpty(versionPath)) return null;

        var initPath = Path.Combine(versionPath, "OMCL", "init.json");
        if (!File.Exists(initPath)) return null;

        try
        {
            var json = File.ReadAllText(initPath);
            var data = JsonSerializer.Deserialize<VersionInitData>(json, JsonOptions);
            return data?.Group;
        }
        catch
        {
            return null;
        }
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
