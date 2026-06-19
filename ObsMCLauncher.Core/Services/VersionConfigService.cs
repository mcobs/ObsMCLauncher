using System;
using System.IO;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

/// <summary>
/// 版本隔离配置的兼容访问层
/// 实际数据已迁移到 OMCL/init.json，由 VersionInitService 管理
/// </summary>
public class VersionConfig
{
    public bool? UseVersionIsolation { get; set; } = null;
}

public static class VersionConfigService
{
    /// <summary>
    /// 加载版本隔离配置（从 init.json 读取并转换）
    /// </summary>
    public static VersionConfig LoadVersionConfig(string versionPath)
    {
        var mode = VersionInitService.GetIsolationMode(versionPath);
        return new VersionConfig
        {
            UseVersionIsolation = mode switch
            {
                "enabled" => true,
                "disabled" => false,
                _ => null
            }
        };
    }

    public static bool? GetVersionIsolation(string versionPath)
    {
        return LoadVersionConfig(versionPath).UseVersionIsolation;
    }

    /// <summary>
    /// 获取版本隔离模式字符串: "global" / "enabled" / "disabled"
    /// </summary>
    public static string GetIsolationMode(string versionPath) =>
        VersionInitService.GetIsolationMode(versionPath);

    /// <summary>
    /// 设置版本隔离模式
    /// </summary>
    public static void SetIsolationMode(string versionPath, string mode) =>
        VersionInitService.SetIsolationMode(versionPath, mode);

    [Obsolete("使用 SetIsolationMode 代替")]
    public static bool SetVersionIsolation(string versionPath, bool? useIsolation)
    {
        var mode = useIsolation switch
        {
            true => "enabled",
            false => "disabled",
            _ => "global"
        };
        VersionInitService.SetIsolationMode(versionPath, mode);
        return true;
    }
}
