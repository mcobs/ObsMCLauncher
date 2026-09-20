using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Plugins;

/// <summary>
/// 插件 API 版本。
///
/// 直接取启动器版本的**主版本号**（v1.2.3 取 1；带 -preview / -beta 等后缀时取主干第一段）。
/// 语义就一句话：<b>大版本递进 = 插件 API 可能发生破坏性变更；小版本只新增、不改动</b>。
/// 没有细分能力探测——插件用 <c>IPluginContext.LauncherVersion</c>（完整版本字符串）自行判断。
/// </summary>
public static class PluginApi
{
    /// <summary>当前插件 API 版本 = 启动器主版本号</summary>
    public static int Version { get; } = ParseMajor(VersionInfo.Version);

    /// <summary>
    /// 从版本字符串取主版本号：先去掉 -xxx 后缀，再取第一段。
    /// 解析失败时退回 1（宁可按"旧版本"降级，也不要给出无意义的值）。
    /// </summary>
    private static int ParseMajor(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return 1;

        var main = version.Split('-', 2)[0];
        var first = main.Split('.')[0];
        return int.TryParse(first, out var major) ? major : 1;
    }
}
