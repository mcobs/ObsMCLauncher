using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services.Migration;

/// <summary>单条迁移条目的状态</summary>
public enum MigrationItemState
{
    /// <summary>成功导入</summary>
    Imported,

    /// <summary>跳过（来源没有该项或无法对应）</summary>
    Skipped,

    /// <summary>导入时出现问题</summary>
    Warning,
}

/// <summary>一条可展示给用户的迁移明细</summary>
public class MigrationItem
{
    public MigrationItem(string category, string name, string detail, MigrationItemState state)
    {
        Category = category;
        Name = name;
        Detail = detail;
        State = state;
    }

    /// <summary>分类：应用设置 / 游戏设置 / 登录服务器</summary>
    public string Category { get; }

    /// <summary>条目名称</summary>
    public string Name { get; }

    /// <summary>补充说明</summary>
    public string Detail { get; }

    /// <summary>条目状态</summary>
    public MigrationItemState State { get; }
}

/// <summary>PCL2 数据迁移结果统计</summary>
public class PclMigrationResult
{
    /// <summary>成功导入的应用设置项数</summary>
    public int AppSettingsImported { get; set; }

    /// <summary>成功导入的游戏版本数</summary>
    public int VersionsImported { get; set; }

    /// <summary>迁移过程中收集到的问题（不中断流程）</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>迁移明细条目（用于完成页展示）</summary>
    public List<MigrationItem> Items { get; } = [];

    internal void AddItem(string category, string name, string detail, MigrationItemState state = MigrationItemState.Imported)
    {
        Items.Add(new MigrationItem(category, name, detail, state));
        if (state == MigrationItemState.Warning)
            Warnings.Add($"{name}：{detail}");
    }
}

/// <summary>
/// 从 Plain Craft Launcher 2 导入数据。
/// 数据来源：
/// - 应用设置：PCL 目录下 PCL\Setup.ini（冒号分隔的 ini）+ 注册表 HKCU\Software\PCL（正式版；旧版为 PlainCraftLauncher / PCLDebug）
/// - 游戏设置：游戏目录 versions\&lt;版本&gt;\PCL\Setup.ini → 同位置 OMCL\init.json
/// PCL 的设置分散存储（一部分在 ini、一部分在注册表），读取时两处合并、注册表优先。
/// </summary>
public static class PclMigrationService
{
    private const string Tag = "PclMigration";

    // 注册表候选：PCL 为当前版本使用，PlainCraftLauncher / PCLDebug 为历史版本
    private static readonly string[] RegistryFolders = ["PCL", "PlainCraftLauncher", "PCLDebug"];

    /// <summary>判断目录是否像一个 PCL 安装目录（PCL\Setup.ini 或 PCL.exe 存在）</summary>
    public static bool LooksLikePclDirectory(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        return File.Exists(Path.Combine(dir, "PCL", "Setup.ini"))
               || File.Exists(Path.Combine(dir, "PCL.exe"))
               || File.Exists(Path.Combine(dir, "Plain Craft Launcher.exe"));
    }

    /// <summary>
    /// 执行迁移。只导入 PCL 中显式保存过的设置，未保存的项保持 OMCL 默认值。
    /// </summary>
    /// <param name="pclDirectory">PCL 安装目录</param>
    /// <param name="importAppSettings">是否导入应用设置</param>
    /// <param name="importGameSettings">是否导入各版本的游戏设置</param>
    public static PclMigrationResult Migrate(string pclDirectory, bool importAppSettings, bool importGameSettings)
    {
        var result = new PclMigrationResult();

        DebugLogger.Info(Tag, $"开始迁移：目录={pclDirectory}，应用设置={importAppSettings}，游戏设置={importGameSettings}");

        var setupIni = ReadPclIni(Path.Combine(pclDirectory, "PCL", "Setup.ini"));
        var registry = ReadPclRegistry();

        // 合并两处设置：注册表优先，ini 兜底
        var merged = new Dictionary<string, string>(setupIni, StringComparer.Ordinal);
        foreach (var (key, value) in registry)
            merged[key] = value;

        DebugLogger.Info(Tag, $"Setup.ini 读取完成：{setupIni.Count} 个键；注册表读取完成：{registry.Count} 个键；合并后 {merged.Count} 个键");

        var config = LauncherConfig.Load();

        string? gameDir = null;

        if (importAppSettings)
        {
            gameDir = MigrateAppSettings(merged, pclDirectory, config, result);
            config.Save();
            DebugLogger.Info(Tag, $"应用设置导入完成：{result.AppSettingsImported} 项，配置已保存");
        }

        if (importGameSettings)
        {
            // 优先使用迁移过来的游戏目录，否则用 OMCL 当前游戏目录
            gameDir ??= config.GameDirectory;
            DebugLogger.Info(Tag, $"游戏设置导入使用游戏目录：{gameDir}");
            MigrateInstances(gameDir, result);
        }

        DebugLogger.Info(Tag, $"迁移结束：应用设置 {result.AppSettingsImported} 项，版本 {result.VersionsImported} 个，警告 {result.Warnings.Count} 条");

        return result;
    }

    /// <summary>导入应用设置，返回解析出的当前游戏目录（可能为 null）</summary>
    private static string? MigrateAppSettings(
        Dictionary<string, string> setup,
        string pclDirectory,
        LauncherConfig config,
        PclMigrationResult result)
    {
        string? gameDir = null;
        var imported = 0;

        bool TryGet(string key, out string? value)
        {
            if (setup.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            {
                value = v;
                return true;
            }
            value = null;
            return false;
        }

        // JVM 参数（全局）
        if (TryGet("LaunchAdvanceJvm", out var jvm))
        {
            config.JvmArguments = jvm!.Trim();
            imported++;
            result.AddItem("应用设置", "JVM 参数", jvm.Trim());
        }

        // 内存：仅在 PCL 使用“自定义”模式时迁移，单位 GB → MB
        if (setup.TryGetValue("LaunchRamType", out var ramTypeStr)
            && int.TryParse(ramTypeStr, out var ramType) && ramType == 1
            && setup.TryGetValue("LaunchRamCustom", out var ramStr)
            && int.TryParse(ramStr, out var ramGb) && ramGb > 0)
        {
            config.MaxMemory = Math.Min(ramGb * 1024, 65536);
            imported++;
            result.AddItem("应用设置", "最大内存", $"{ramGb} GB");
        }
        else if (setup.TryGetValue("LaunchRamType", out var rtRaw))
        {
            // 0=自动 2=动态调整，OMCL 无法对应
            result.AddItem("应用设置", "最大内存", $"PCL 为自动模式（LaunchRamType={rtRaw}），未导入", MigrationItemState.Skipped);
        }

        // 全局版本隔离：0=关闭，其余（1~4 各种隔离策略）OMCL 统一映射为版本隔离
        if (setup.TryGetValue("LaunchArgumentIndieV2", out var indieStr)
            && int.TryParse(indieStr, out var indie))
        {
            config.GameDirectoryType = indie == 0 ? GameDirectoryType.RootFolder : GameDirectoryType.VersionFolder;
            imported++;
            result.AddItem("应用设置", "版本隔离", indie == 0 ? "关闭" : "开启");
        }

        // 下载源策略：0=尽量镜像，1/2=偏向官方
        if (setup.TryGetValue("ToolDownloadSource", out var srcStr) && int.TryParse(srcStr, out var src))
        {
            config.MirrorSourceMode = src == 0 ? MirrorSourceMode.PreferMirror : MirrorSourceMode.OfficialOnly;
            imported++;
            result.AddItem("应用设置", "下载源", src == 0 ? "优先镜像源" : "官方源");
        }

        // 下载线程数
        if (setup.TryGetValue("ToolDownloadThread", out var threadStr)
            && int.TryParse(threadStr, out var threads) && threads > 0)
        {
            config.MaxDownloadThreads = Math.Min(threads, 64);
            imported++;
            result.AddItem("应用设置", "下载线程数", $"{Math.Min(threads, 64)}");
        }

        // Java 路径：值为具体路径时迁移（“使用全局设置”等非路径值跳过）
        if (TryGet("LaunchArgumentJavaSelect", out var javaSel) && javaSel!.Contains(Path.DirectorySeparatorChar))
        {
            config.JavaSelectionMode = 2;
            config.CustomJavaPath = ExpandPclPath(javaSel, pclDirectory);
            imported++;
            result.AddItem("应用设置", "全局 Java", config.CustomJavaPath);
        }
        else if (setup.TryGetValue("LaunchArgumentJavaSelect", out var jsRaw))
        {
            result.AddItem("应用设置", "全局 Java", $"PCL 值“{jsRaw}”非路径，未导入", MigrationItemState.Skipped);
        }

        // SSL 验证（PCL 默认不验证，取反映射）
        if (setup.TryGetValue("ToolDownloadCert", out var certStr) && bool.TryParse(certStr, out var cert))
        {
            config.SkipSslValidation = !cert;
            imported++;
            result.AddItem("应用设置", "SSL 证书验证", cert ? "开启" : "关闭");
        }

        // 更新通道偏好
        if (setup.TryGetValue("ToolUpdateRelease", out var relStr) && bool.TryParse(relStr, out var rel))
        {
            setup.TryGetValue("ToolUpdateSnapshot", out var snapStr);
            var snap = bool.TryParse(snapStr, out var s) && s;
            if (rel || snap)
            {
                config.AutoCheckUpdate = true;
                config.UpdateChannel = snap ? UpdateChannel.Beta : UpdateChannel.Stable;
                imported++;
                result.AddItem("应用设置", "更新通道", snap ? "测试版" : "正式版");
            }
        }

        // 游戏文件夹：当前选中目录设为 OMCL 游戏目录，其余加入自定义目录列表
        if (TryGet("LaunchFolderSelect", out var selectRaw))
        {
            var selected = ExpandPclPath(selectRaw!, pclDirectory).TrimEnd('\\', '/');
            if (Directory.Exists(selected))
            {
                gameDir = selected;
                config.GameDirectoryLocation = DirectoryLocation.Custom;
                config.CustomGameDirectory = selected;
                imported++;
                result.AddItem("应用设置", "当前游戏文件夹", selected);

                // LaunchFolders 格式：名称>路径|名称>路径
                var extraCount = 0;
                if (setup.TryGetValue("LaunchFolders", out var foldersRaw))
                {
                    foreach (var entry in foldersRaw.Split('|', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var idx = entry.LastIndexOf('>');
                        if (idx <= 0) continue;
                        var path = ExpandPclPath(entry[(idx + 1)..], pclDirectory).TrimEnd('\\', '/');
                        if (path.Length == 0) continue;
                        if (!Directory.Exists(path))
                        {
                            result.AddItem("应用设置", $"文件夹 {entry[..idx]}", $"路径不存在：{path}", MigrationItemState.Skipped);
                            continue;
                        }
                        if (config.CustomGameDirectories.Contains(path, StringComparer.OrdinalIgnoreCase)
                            || string.Equals(path, selected, StringComparison.OrdinalIgnoreCase))
                            continue;

                        config.CustomGameDirectories.Add(path);
                        extraCount++;
                        result.AddItem("应用设置", $"附加文件夹 {entry[..idx]}", path);
                    }
                }
                if (extraCount == 0 && !setup.ContainsKey("LaunchFolders"))
                    result.AddItem("应用设置", "附加游戏文件夹", "PCL 未保存其他文件夹", MigrationItemState.Skipped);
            }
            else
            {
                result.AddItem("应用设置", "当前游戏文件夹", $"PCL 记录的目录不存在：{selected}", MigrationItemState.Warning);
            }
        }
        else
        {
            result.AddItem("应用设置", "当前游戏文件夹", "PCL 未保存游戏文件夹设置", MigrationItemState.Skipped);
        }

        result.AppSettingsImported = imported;
        return gameDir;
    }

    /// <summary>扫描游戏目录下各版本的 PCL\Setup.ini，写入对应 OMCL\init.json</summary>
    private static void MigrateInstances(string gameDir, PclMigrationResult result)
    {
        var versionsDir = Path.Combine(gameDir, "versions");
        if (!Directory.Exists(versionsDir))
        {
            DebugLogger.Warn(Tag, $"versions 目录不存在，跳过游戏设置导入：{versionsDir}");
            result.AddItem("游戏设置", "版本扫描", $"versions 目录不存在：{versionsDir}", MigrationItemState.Warning);
            return;
        }

        var scanned = 0;
        foreach (var versionDir in Directory.EnumerateDirectories(versionsDir))
        {
            var pclIniPath = Path.Combine(versionDir, "PCL", "Setup.ini");
            if (!File.Exists(pclIniPath)) continue;
            scanned++;

            try
            {
                var ini = ReadPclIni(pclIniPath);
                var parts = new List<string>();
                var data = VersionInitService.Load(versionDir);

                // 内存：0=跟随全局 1=自定义 2=自动，仅迁移自定义；单位 GB → MB
                if (ini.TryGetValue("VersionRamType", out var rt) && rt == "1"
                    && ini.TryGetValue("VersionRamCustom", out var rgb)
                    && int.TryParse(rgb, out var gb) && gb > 0
                    && data.MaxMemory == null)
                {
                    data.MaxMemory = Math.Min(gb * 1024, 65536);
                    parts.Add($"内存 {gb} GB");
                }

                // 版本隔离：V2 为布尔（True=隔离），旧版 Indie 1=开启 2=关闭；均未保存则跟随全局
                if (data.IsolationMode == "global")
                {
                    if (ini.TryGetValue("VersionArgumentIndieV2", out var v2) && bool.TryParse(v2, out var indieV2))
                    {
                        data.IsolationMode = indieV2 ? "enabled" : "disabled";
                        parts.Add(indieV2 ? "版本隔离" : "不隔离");
                    }
                    else if (ini.TryGetValue("VersionArgumentIndie", out var v1) && int.TryParse(v1, out var indieV1)
                             && indieV1 is 1 or 2)
                    {
                        data.IsolationMode = indieV1 == 1 ? "enabled" : "disabled";
                        parts.Add(indieV1 == 1 ? "版本隔离" : "不隔离");
                    }
                }
                else if (data.IsolationMode != "global")
                {
                    parts.Add($"隔离保持 {data.IsolationMode}");
                }

                // 实例级 Java 路径
                if (string.IsNullOrWhiteSpace(data.CustomJavaPath)
                    && ini.TryGetValue("VersionArgumentJavaSelect", out var vJava)
                    && !string.IsNullOrWhiteSpace(vJava) && vJava.Contains(Path.DirectorySeparatorChar))
                {
                    data.CustomJavaPath = vJava.Trim();
                    parts.Add("自定义 Java");
                }

                // 实例级 JVM 参数
                if (string.IsNullOrWhiteSpace(data.JvmArguments)
                    && ini.TryGetValue("VersionAdvanceJvm", out var vJvm)
                    && !string.IsNullOrWhiteSpace(vJvm))
                {
                    data.JvmArguments = vJvm.Trim();
                    parts.Add("JVM 参数");
                }

                // 自定义标题 → 版本描述
                if (string.IsNullOrWhiteSpace(data.Description)
                    && ini.TryGetValue("VersionArgumentTitle", out var vTitle)
                    && !string.IsNullOrWhiteSpace(vTitle))
                {
                    data.Description = vTitle.Trim();
                    parts.Add("自定义标题");
                }

                if (parts.Count > 0)
                {
                    VersionInitService.Save(versionDir, data);
                    result.VersionsImported++;
                    result.AddItem("游戏设置", Path.GetFileName(versionDir), string.Join("、", parts));
                }
                else
                {
                    result.AddItem("游戏设置", Path.GetFileName(versionDir), "跟随全局设置，无需迁移", MigrationItemState.Skipped);
                }
            }
            catch (Exception ex)
            {
                result.AddItem("游戏设置", Path.GetFileName(versionDir), $"导入失败：{ex.Message}", MigrationItemState.Warning);
            }
        }

        if (scanned == 0)
            result.AddItem("游戏设置", "版本扫描", "未找到任何版本的 PCL 实例设置（versions\\*\\PCL\\Setup.ini）", MigrationItemState.Skipped);
    }

    /// <summary>解析 PCL 的 ini（格式为每行“键:值”，无节）</summary>
    private static Dictionary<string, string> ReadPclIni(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            DebugLogger.Info(Tag, $"ini 文件不存在：{path}");
            return dict;
        }

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var idx = line.IndexOf(':');
                if (idx <= 0) continue;
                dict[line[..idx].Trim()] = line[(idx + 1)..].Trim();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Warn(Tag, $"读取 ini 失败 [{path}]: {ex.Message}");
        }

        return dict;
    }

    /// <summary>读取 PCL 写入注册表的应用设置（当前版本与历史版本的键都尝试）</summary>
    private static Dictionary<string, string> ReadPclRegistry()
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!OperatingSystem.IsWindows()) return dict;

        foreach (var folder in RegistryFolders)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($"Software\\{folder}");
                if (key == null)
                {
                    DebugLogger.Info(Tag, $"注册表项不存在：HKCU\\Software\\{folder}");
                    continue;
                }
                var count = 0;
                foreach (var name in key.GetValueNames())
                {
                    if (key.GetValue(name) is string value && !dict.ContainsKey(name))
                    {
                        dict[name] = value;
                        count++;
                    }
                }
                DebugLogger.Info(Tag, $"注册表 HKCU\\Software\\{folder} 读取到 {count} 个值");
            }
            catch (Exception ex)
            {
                DebugLogger.Warn(Tag, $"读取注册表 Software\\{folder} 失败: {ex.Message}");
            }
        }

        return dict;
    }

    /// <summary>展开 PCL 路径中的 $ 占位（$ = PCL 所在目录）并规范化分隔符</summary>
    private static string ExpandPclPath(string path, string pclDirectory)
    {
        return path.Replace("$", pclDirectory).Replace('/', Path.DirectorySeparatorChar);
    }
}
