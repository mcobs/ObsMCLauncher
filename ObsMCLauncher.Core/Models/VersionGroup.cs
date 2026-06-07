using System.Text.Json.Serialization;

namespace ObsMCLauncher.Core.Models;

/// <summary>
/// 版本分组定义
/// </summary>
public class VersionGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsSystem { get; set; }
    public bool IsDeletable { get; set; }

    // 系统分组ID常量
    public const string AutoGroupId = "auto";
    public const string ModdableGroupId = "moddable";
    public const string CommonGroupId = "common";
    public const string UncommonGroupId = "uncommon";

    /// <summary>
    /// 获取默认的系统分组列表
    /// </summary>
    public static List<VersionGroup> GetSystemGroups() =>
    [
        new() { Id = AutoGroupId, Name = "自动", IsSystem = true, IsDeletable = false },
        new() { Id = ModdableGroupId, Name = "可装MOD", IsSystem = true, IsDeletable = false },
        new() { Id = CommonGroupId, Name = "常用版本", IsSystem = true, IsDeletable = false },
        new() { Id = UncommonGroupId, Name = "不常用版本", IsSystem = true, IsDeletable = false }
    ];
}
