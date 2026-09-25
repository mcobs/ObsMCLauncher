using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ObsMCLauncher.Core.Services.Modpack;

/// <summary>
/// 给整合包目录/文件标一个中文用途，方便用户判断"这个条目要不要导出"。
///
/// 匹配顺序（目录与文件共用同一套优先级）：
/// <list type="number">
/// <item>完整路径精确匹配（<c>config/sodium</c> → "Sodium 设置"）；</item>
/// <item>只看自己这一层的名字（<c>mods/kubejs</c> → "KubeJS 脚本"）；</item>
/// <item>逐级去掉末段后仍只在完整路径表里找（<c>config/sodium/presets</c> → "Sodium 设置"）。</item>
/// </list>
/// 认不出来的返回空串 —— 调用方不显示任何标签，而不是显示"未知"。
/// </summary>
public static class ModpackContentPurpose
{
    /// <summary>完整相对路径 → 用途（用于区分同名目录，如 config/ftbquests 与 ftbquests）。</summary>
    private static readonly Dictionary<string, string> FolderByFullPath = new(StringComparer.OrdinalIgnoreCase)
    {
        ["config/sodium"] = "Sodium 设置",
        ["config/iris"] = "Iris 设置",
        ["config/oculus"] = "Oculus 设置",
        ["config/fabric"] = "Fabric 设置",
        ["config/quilt"] = "Quilt 设置",
        ["config/jei"] = "JEI 设置",
        ["config/emi"] = "EMI 设置",
        ["config/roughlyenoughitems"] = "REI 设置",
        ["config/ftbquests"] = "FTB 任务",
        ["config/mekanism"] = "通用机械",
        ["config/create"] = "机械动力",
        ["config/thermal"] = "热力系列",
        ["config/botania"] = "植物魔法",
        ["config/tconstruct"] = "匠魂",
        ["config/apotheosis"] = "神化",
        ["config/ars_nouveau"] = "新生魔艺",
        ["config/occultism"] = "神秘学",
        ["config/iceandfire"] = "冰火传说",
        ["config/twilightforest"] = "暮色森林",
        ["defaultconfigs/ftbquests"] = "FTB 任务（默认）",
        ["journeymap/data"] = "JourneyMap 数据",
        ["mods/.connector"] = "Sinytra Connector",
        ["CustomSkinLoader/caches"] = "皮肤缓存",
        ["PCL/Pictures"] = "PCL 背景图片",
        ["PCL/Musics"] = "PCL 背景音乐",
        ["PCL/Help"] = "PCL 帮助文档"
    };

    /// <summary>目录名 → 用途（只看最后一段）。</summary>
    private static readonly Dictionary<string, string> FolderByName = new(StringComparer.OrdinalIgnoreCase)
    {
        // ===== 整合包主干 =====
        ["mods"] = "模组",
        ["config"] = "配置",
        ["defaultconfigs"] = "默认配置",
        ["localconfigs"] = "本地配置",
        ["resourcepacks"] = "资源包",
        ["texturepacks"] = "材质包",
        ["shaderpacks"] = "光影包",
        ["datapacks"] = "数据包",
        ["scripts"] = "脚本",
        ["kubejs"] = "KubeJS 脚本",
        ["openloader"] = "OpenLoader",
        ["packmenu"] = "自定义主菜单",
        ["saves"] = "存档",
        ["screenshots"] = "截图",
        ["logs"] = "日志",
        ["crash-reports"] = "崩溃报告",
        ["local"] = "本地数据",

        // ===== 启动器自己的数据（默认不勾选，避免跟着整合包发出去）=====
        ["PCL"] = "PCL 启动器数据",
        // 版本目录下的 OMCL 其实只有 init.json（该版本的隔离/内存/Java/备注等设置），不是整个启动器数据目录
        ["OMCL"] = "启动器版本配置",
        ["hmcl"] = "HMCL 启动器数据",
        [".minecraft"] = "游戏数据",

        // ===== 常见模组的配置目录 =====
        ["sodium"] = "Sodium 渲染",
        ["iris"] = "Iris 光影",
        ["oculus"] = "Oculus 光影",
        ["rubidium"] = "Rubidium 渲染",
        ["embeddium"] = "Embeddium 渲染",
        ["jei"] = "JEI 物品查询",
        ["emi"] = "EMI 物品查询",
        ["roughlyenoughitems"] = "REI 物品查询",
        ["journeymap"] = "JourneyMap 地图",
        ["xaerominimap"] = "Xaero 小地图",
        ["xaeroworldmap"] = "Xaero 世界地图",
        ["ftbquests"] = "FTB 任务",
        ["ftbchunks"] = "FTB 区块",
        ["ftbteams"] = "FTB 队伍",
        ["patchouli_books"] = "帕秋莉手册",
        ["structures"] = "结构",
        ["blueprints"] = "蓝图",
        ["schematics"] = "蓝图",
        ["tconstruct"] = "匠魂",
        ["botania"] = "植物魔法",
        ["thermal"] = "热力系列",
        ["forestry"] = "林业",
        ["ic2"] = "工业时代",
        ["immersiveengineering"] = "沉浸工程",
        ["computercraft"] = "电脑",
        ["opencomputers"] = "开放式电脑",
        ["mekanism"] = "通用机械",
        ["create"] = "机械动力",
        ["apotheosis"] = "神化",
        ["ars_nouveau"] = "新生魔艺",
        ["twilightforest"] = "暮色森林",
        ["iceandfire"] = "冰火传说",
        ["customnpcs"] = "自定义 NPC",
        ["storagedrawers"] = "储物抽屉",
        ["cookingforblockheads"] = "厨房",
        ["farmingforblockheads"] = "农业市场",
        ["sereneseasons"] = "静谧四季",
        ["quark"] = "夸克",
        ["voicechat"] = "语音聊天",
        ["emotecraft"] = "表情动作",
        ["figura"] = "Figura 模型",
        ["betterf3"] = "更好的 F3",
        ["modmenu"] = "Mod Menu",
        ["cloth-config"] = "Cloth Config",
        ["fabric"] = "Fabric",
        ["quilt"] = "Quilt",

        // ===== 非整合包数据 =====
        ["backups"] = "备份",
        ["backup"] = "备份",
        ["downloads"] = "下载缓存",
        ["cache"] = "缓存",
        ["playerdata"] = "玩家数据",
        ["stats"] = "统计数据",
        ["advancements"] = "进度"
    };

    /// <summary>完整文件路径 → 用途（优先于按文件名匹配）。</summary>
    private static readonly Dictionary<string, string> FileByFullPath = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PCL/Setup.ini"] = "PCL 设置",
        ["PCL/Custom.xaml"] = "PCL 自定义界面",
    };

    /// <summary>文件名 → 用途。</summary>
    private static readonly Dictionary<string, string> FileByName = new(StringComparer.OrdinalIgnoreCase)
    {
        // ===== 游戏本体设置与数据 =====
        ["options.txt"] = "游戏设置",
        ["optionsof.txt"] = "OptiFine 设置",
        ["optionsshaders.txt"] = "光影设置",
        ["servers.dat"] = "服务器列表",
        ["hotbar.nbt"] = "快捷栏",
        ["realms_persistence.json"] = "Realms 设置",
        ["command_history.txt"] = "命令历史",
        ["usercache.json"] = "玩家缓存",
        ["usernamecache.json"] = "玩家名缓存",
        ["level.dat"] = "存档数据",
        ["level.dat_old"] = "存档备份",
        ["icon.png"] = "图标",
        ["pack.mcmeta"] = "数据包描述",
        ["pack.png"] = "数据包图标",

        // ===== 整合包清单 =====
        ["manifest.json"] = "CurseForge 清单",
        ["modrinth.index.json"] = "Modrinth 清单",
        ["mcbbs.packmeta"] = "HMCL 清单",

        // ===== 启动器自己的文件（默认不勾选）=====
        ["version_config.json"] = "启动器版本配置",
        ["init.json"] = "版本配置",
        ["PCL.ini"] = "PCL 配置",
        ["Setup.ini"] = "PCL 设置",
        ["hmclversion.cfg"] = "HMCL 版本标记",
        ["launcher_profiles.json"] = "官方启动器配置",
        ["launcher_accounts.json"] = "官方启动器账号",

        // ===== 模组文件 =====
        ["mods.toml"] = "模组元数据",
        ["fabric.mod.json"] = "模组元数据",
        ["quilt.mod.json"] = "模组元数据"
    };

    /// <summary>形如 1.20.1 / 1.7.10 的版本号，用来识别根目录下的 {mc版本}.json。</summary>
    private static readonly Regex GameVersionPattern =
        new(@"^\d+\.\d+(\.\d+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>取用途；<paramref name="isDirectory"/> 决定走哪张表。认不出来返回空串。</summary>
    public static string Describe(string relativePath, bool isDirectory)
        => isDirectory ? DescribeFolder(relativePath) : DescribeFile(relativePath);

    public static string DescribeFolder(string relativePath)
        => Resolve(relativePath, FolderByFullPath, FolderByName, extra: null);

    public static string DescribeFile(string relativePath)
        => Resolve(relativePath, FileByFullPath, FileByName, extra: DescribeRootFile);

    private static string Resolve(
        string relativePath,
        Dictionary<string, string> byFullPath,
        Dictionary<string, string> byName,
        Func<string, string, string>? extra)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;

        var path = relativePath.Replace('\\', '/').Trim('/');
        if (path.Length == 0)
            return string.Empty;

        if (byFullPath.TryGetValue(path, out var exact))
            return exact;

        var lastSlash = path.LastIndexOf('/');
        var name = lastSlash < 0 ? path : path.Substring(lastSlash + 1);
        if (byName.TryGetValue(name, out var byNameHit))
            return byNameHit;

        if (extra != null)
        {
            var fromExtra = extra(path, name);
            if (fromExtra.Length > 0)
                return fromExtra;
        }

        var candidate = path;
        while (lastSlash > 0)
        {
            candidate = candidate.Substring(0, lastSlash);
            if (byFullPath.TryGetValue(candidate, out var ancestor))
                return ancestor;

            lastSlash = candidate.LastIndexOf('/');
        }

        return string.Empty;
    }

    /// <summary>按"形状"识别文件：根目录下的 {版本}.json / {版本}.jar、根目录下的整合包压缩包等。</summary>
    private static string DescribeRootFile(string path, string name)
    {
        var isAtRoot = !path.Contains('/');
        if (!isAtRoot)
        {
            // 模组/资源包/光影包目录下的一等公民——父目录已经标了用途，这里不再重复
            return string.Empty;
        }

        var withoutExtension = name;
        var dot = name.LastIndexOf('.');
        var extension = dot < 0 ? "" : name.Substring(dot).ToLowerInvariant();
        if (dot > 0)
            withoutExtension = name.Substring(0, dot);

        return extension switch
        {
            ".json" when GameVersionPattern.IsMatch(withoutExtension) => "版本信息",
            ".jar" => "游戏本体",
            ".mrpack" or ".zip" when name.Contains("modpack", StringComparison.OrdinalIgnoreCase) => "整合包文件",
            _ => string.Empty
        };
    }
}
