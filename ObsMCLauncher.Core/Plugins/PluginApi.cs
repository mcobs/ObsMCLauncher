using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Plugins;

/// <summary>
/// 插件 API 版本。
///
/// 取启动器的**版本号并去掉预发布 / 构建后缀**：
/// <c>1.1.0-beta.1</c> → <c>1.1.0</c>；<c>1.1.0+20260924</c> → <c>1.1.0</c>。
/// 语义就一句话：<b>主版本递进 = 插件 API 可能发生破坏性变更；小版本只新增、不改动</b>。
/// 需要完整版本字符串（含预发布标识）请用 <c>IPluginContext.LauncherVersion</c>。
/// </summary>
public static class PluginApi
{
    /// <summary>当前插件 API 版本（去掉预发布 / 构建后缀的启动器版本，如 "1.1.0"）</summary>
    public static string Version { get; } = Normalize(VersionInfo.Version);

    /// <summary>
    /// 把任意版本字符串归一化为插件 API 版本：去掉可选的 <c>v</c> / <c>V</c> 前缀（GitHub tag 常见写法），
    /// 再截断第一个 <c>-</c>（预发布）与第一个 <c>+</c>（构建元数据）之后的内容。
    /// 解析失败（空串）时退回 "1.0.0"。
    /// </summary>
    public static string Normalize(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "1.0.0";

        var main = version.Trim();

        // 去掉 v / V 前缀（如 "v1.1.0" → "1.1.0"）
        if (main.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            main = main[1..];

        var dash = main.IndexOf('-');
        if (dash >= 0) main = main[..dash];

        var plus = main.IndexOf('+');
        if (plus >= 0) main = main[..plus];

        main = main.Trim();
        return main.Length == 0 ? "1.0.0" : main;
    }
}
