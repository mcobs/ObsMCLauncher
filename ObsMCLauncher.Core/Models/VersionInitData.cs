using System.Collections.Generic;

namespace ObsMCLauncher.Core.Models;

/// <summary>
/// 版本目录下 OMCL/init.json 的数据模型，可扩展
/// </summary>
public class VersionInitData
{
    /// <summary>
    /// 分组ID
    /// </summary>
    public string Group { get; set; } = VersionGroup.AutoGroupId;

    /// <summary>
    /// 用户自定义标签
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// 备注
    /// </summary>
    public string Notes { get; set; } = "";

    /// <summary>
    /// 版本隔离模式: "global"=跟随全局, "enabled"=开启, "disabled"=关闭
    /// </summary>
    public string IsolationMode { get; set; } = "global";

    /// <summary>
    /// 自定义最大内存(MB)，null 表示使用全局设置
    /// </summary>
    public int? MaxMemory { get; set; } = null;

    /// <summary>
    /// 自定义最小内存(MB)，null 表示使用全局设置
    /// </summary>
    public int? MinMemory { get; set; } = null;

    /// <summary>
    /// 版本描述
    /// </summary>
    public string Description { get; set; } = "";
}
