using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Services.Modpack;

/// <summary>
/// 整合包导出内容过滤器。
///
/// 语义移植自 HMCL 的 <c>ModAdviser</c>，差别有两点（都在导出侧更安全的方向）：
/// <list type="number">
/// <item>Java 的 <c>String.matches</c> 是<b>整串匹配</b>，.NET 的 <c>Regex.IsMatch</c> 是<b>子串匹配</b> —— 这里统一加
/// <c>\A(?: ... )\z</c> 锚定，否则 <c>.*\.log</c> 会把 <c>a.log.bak</c> 也吞掉。</item>
/// <item>比较全部大小写不敏感（Windows 路径现实：用户目录里可能是 <c>Logs</c>）。</item>
/// </list>
/// 相对路径一律以 <c>/</c> 分隔；目录参与匹配时**不带**结尾斜杠，内部按 HMCL 的
/// <c>fileName.startsWith(s + "/")</c> 等价形式判断。
/// </summary>
public static class ModpackFileFilter
{
    /// <summary>
    /// 黑名单：命中即不出现在导出树里。
    /// 移植自 HMCL <c>ModAdviser.MODPACK_BLACK_LIST</c>，去掉与本启动器无关的项并补上本工程的目录约定。
    /// </summary>
    public static readonly IReadOnlyList<string> BlackList = new List<string>
    {
        // 通配（regex: 前缀，等价 Java 的整串匹配）
        @"regex:.*\.log",
        @"regex:.*\.dat_old",
        @"regex:.*\.old",
        @"regex:.*\.BakaCoreInfo",
        @"regex:.*-natives",

        // Minecraft / 各类启动器的会话与缓存文件
        "usernamecache.json", "usercache.json",
        "launcher_profiles.json", "launcher.pack.lzma",
        "launcher_accounts.json", "launcher_cef_log.txt", "launcher_log.txt",
        "launcher_msa_credentials.bin", "launcher_settings.json", "launcher_ui_state.json",
        "realms_persistence.json", "webcache2", "treatment_tags.json",
        "clientId.txt", "PCL.ini",
        ".hmcl", "backup", "pack.json", "launcher.jar", "cache", "modpack.cfg",
        "log4j2.xml", "hmclversion.cfg", "instance-game-settings.json",

        // 其它启动器的整合包清单（我们自己导出时会重新写）
        "manifest.json", "minecraftinstance.json", ".curseclient", "modrinth.index.json",

        ".fabric", ".mixin.out", ".optifine",

        // Minecraft 本体目录
        "jars", "logs", "versions", "assets", "libraries", "crash-reports",
        "NVIDIA", "AMD", "screenshots", "natives", "native", "$native", "$natives",
        "server-resource-packs", "command_history.txt",

        "downloads", "essential",

        // Mod 产生的数据目录
        "asm", "backups", "TCNodeTracker", "CustomDISkins", "data", "CustomSkinLoader/caches",
        "debug",
        ".replay_cache", "replay_recordings", "replay_videos",
        "irisUpdateInfo.json",
        "modernfix",
        "modtranslations",
        "schematics",
        "journeymap/data",
        "mods/.connector",

        // 本工程约定 + 系统垃圾文件
        ".temp",
        // 版本目录里的 OMCL 只有 init.json（已由 suggested 表"可见但不勾"），不需要额外挡；
        // 这里挡的是"游戏目录恰好就是启动器数据目录"（便携版）时的 OMCL/config ——
        // 那里面有 accounts.json / config.json，绝不能被勾上 OMCL 顺带发出去
        "OMCL/config",
        ".DS_Store", "Thumbs.db", "desktop.ini"
    };

    /// <summary>
    /// 建议排除：出现在导出树里但<b>默认不勾选</b>。移植自 HMCL <c>MODPACK_SUGGESTED_BLACK_LIST</c>。
    /// </summary>
    public static readonly IReadOnlyList<string> SuggestedBlackList = new List<string>
    {
        "fonts",
        "saves", "servers.dat", "options.txt",
        "blueprints",
        "optionsof.txt",
        "journeymap",
        "optionsshaders.txt",
        "mods/VoxelMods",

        // 启动器自己的数据：出现在树里（带用途标签）便于用户确认，但默认不勾 —— 不该跟着整合包发出去
        "PCL",
        "OMCL",

        // 启动器的版本级配置：导入到别的启动器没有意义
        "version_config.json"
    };

    /// <summary>
    /// 必选内容：整合包没了它们就不成立。UI 会把对应复选框**强制勾选并禁用**，
    /// 「全不选」「恢复默认」也不会把它们取消；<c>ModpackExportService</c> 端还会兜底补上。
    ///
    /// 刻意<b>不含</b>这些：
    /// <list type="bullet">
    /// <item><c>mods</c> —— 有人就想导出"只有配置/脚本"的包，或先把模组剔一遍，强制勾选反而是妨碍；</item>
    /// <item><c>config</c> —— 那里常混着玩家自己的设置（画质、按键、Sodium 选项），
    /// 整合包自带的默认配置本来就在 <c>defaultconfigs</c>。</item>
    /// </list>
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredList = new List<string>
    {
        "defaultconfigs",
        "datapacks",
        "kubejs",
        "scripts",
        "openloader"
    };

    /// <summary>可能托管在 Modrinth / CurseForge 上的资源所在目录（相对运行目录第一段）。</summary>
    private static readonly string[] RemoteResourceRoots = { "mods", "resourcepacks", "shaderpacks", "datapacks" };

    private static readonly Dictionary<string, Regex> RegexCache = new(StringComparer.Ordinal);
    private static readonly object RegexCacheLock = new();

    /// <summary>
    /// 构造本次导出的黑名单：通用黑名单 + 该版本自身的 <c>{name}.jar</c> / <c>{name}.json</c> / <c>{name}-natives</c>。
    /// </summary>
    public static List<string> BuildBlackList(string? versionName)
    {
        var list = new List<string>(BlackList);
        if (!string.IsNullOrWhiteSpace(versionName))
        {
            list.Add($"{versionName}.jar");
            list.Add($"{versionName}.json");
            list.Add($"{versionName}-natives");
        }
        return list;
    }

    /// <summary>
    /// 判断路径的建议状态。<paramref name="relativePath"/> 以 <c>/</c> 分隔、不含开头斜杠，
    /// 目录路径<b>不带</b>结尾斜杠。
    /// </summary>
    public static ModpackFileSuggestion GetSuggestion(
        string relativePath,
        bool isDirectory,
        IReadOnlyList<string>? blackList = null,
        IReadOnlyList<string>? suggestedBlackList = null)
    {
        if (string.IsNullOrEmpty(relativePath))
            return ModpackFileSuggestion.Suggested;

        if (Matches(blackList ?? BlackList, relativePath))
            return ModpackFileSuggestion.Hidden;

        // 建议排除项按"祖先也生效"处理：saves/ 不勾时，它下面的内容也一并不勾。
        // （HMCL 只对直接路径判定，导致 saves/world/level.dat 又变回勾选状态；这里刻意做得一致。）
        if (Matches(suggestedBlackList ?? SuggestedBlackList, relativePath))
            return ModpackFileSuggestion.Normal;

        return ModpackFileSuggestion.Suggested;
    }

    /// <summary>该路径是否属于"必选内容"（规则命中自己或任意祖先）。</summary>
    public static bool IsRequired(string relativePath)
        => !string.IsNullOrEmpty(relativePath) && Matches(RequiredList, relativePath);

    /// <summary>
    /// 路径是否命中规则表。
    ///
    /// 移植 HMCL <c>ModAdviser.match</c>，并做两处收敛：
    /// <list type="bullet">
    /// <item>规则命中自己的<b>祖先</b>也算命中（<c>logs</c> 会挡掉 <c>logs/a/b.log</c>），
    /// 否则文件只能靠 regex 才能被目录规则挡下；</item>
    /// <item>regex 规则同样参与目录匹配（否则 <c>.*-natives</c> 这类规则挡不住同名的原生库目录）。</item>
    /// </list>
    /// 规则形如 <c>regex:模式</c> 时按<b>整串</b>匹配（对齐 Java 的 <c>String.matches</c>）。
    /// </summary>
    public static bool Matches(IReadOnlyList<string> list, string relativePath)
    {
        if (list.Count == 0 || string.IsNullOrEmpty(relativePath))
            return false;

        foreach (var rule in list)
        {
            if (string.IsNullOrEmpty(rule))
                continue;

            if (rule.StartsWith("regex:", StringComparison.Ordinal))
            {
                var pattern = rule.Substring("regex:".Length);
                if (pattern.Length == 0)
                    continue;
                if (GetRegex(pattern).IsMatch(relativePath))
                    return true;
                continue;
            }

            if (relativePath.Equals(rule, StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith(rule + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 该文件是否值得拿去 Modrinth / CurseForge 做哈希查询。
    /// 只覆盖 mods / resourcepacks / shaderpacks / datapacks 下的 jar 与 zip（含被禁用的 <c>.disabled</c> 变体），
    /// 其余文件（配置文件、脚本等）一律直接进包。
    /// </summary>
    public static bool IsPotentiallyRemoteResource(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return false;

        var normalized = relativePath.Replace('\\', '/');
        var slash = normalized.IndexOf('/');
        if (slash <= 0)
            return false;

        var root = normalized.Substring(0, slash);
        var isInRoot = false;
        foreach (var candidate in RemoteResourceRoots)
        {
            if (root.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                isInRoot = true;
                break;
            }
        }
        if (!isInRoot)
            return false;

        var name = normalized.Substring(slash + 1);
        if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - ".disabled".Length);

        return name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>把相对路径归一化为 <c>/</c> 分隔、无开头斜杠、无结尾斜杠的形式。</summary>
    public static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return string.Empty;

        var normalized = relativePath.Replace('\\', '/').Trim('/');
        return normalized;
    }

    /// <summary>由运行目录与完整路径算出相对路径（<c>/</c> 分隔）。</summary>
    public static string MakeRelativePath(string runDirectory, string fullPath)
        => NormalizeRelativePath(Path.GetRelativePath(runDirectory, fullPath));

    private static Regex GetRegex(string pattern)
    {
        lock (RegexCacheLock)
        {
            if (RegexCache.TryGetValue(pattern, out var cached))
                return cached;

            Regex compiled;
            try
            {
                // Java 的 String.matches 是整串匹配，这里显式锚定；IgnoreCase 对齐我们的比较口径
                compiled = new Regex(@"\A(?:" + pattern + @")\z",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException)
            {
                // 非法规则不应让整次导出失败，退化为永不命中
                compiled = new Regex(@"(?!)", RegexOptions.CultureInvariant);
            }

            RegexCache[pattern] = compiled;
            return compiled;
        }
    }
}
