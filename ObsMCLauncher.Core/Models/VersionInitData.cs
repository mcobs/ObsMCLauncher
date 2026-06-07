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
}
