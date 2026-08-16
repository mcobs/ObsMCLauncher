using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services.Migration;

/// <summary>
/// 从 Hello Minecraft! Launcher (HMCL) 导入数据，支持两代格式：
/// - 3.16+ 新格式：HMCL 目录下 config\launcher-settings.json、config\game-directories.json、
///   config\user-game-directories.json、config\game-settings.json（全局游戏设置预设）、
///   config\authlib-injector-servers.json，实例设置在 versions\&lt;id&gt;\.hmcl\config\instance-game-settings.json
/// - ≤3.15.x 旧格式：HMCL 目录（或 %APPDATA%\hmcl）下 hmcl.json（含 config/configurations/authlibInjectorServers），
///   实例设置在 versions\&lt;id&gt;\hmclversion.cfg
/// </summary>
public static partial class HmclMigrationService
{
    private const string Tag = "HmclMigration";

    [GeneratedRegex(@"^https?://([^/:]+)")]
    private static partial Regex HostRegex();

    /// <summary>判断目录是否为 HMCL 3.16+ 新格式的数据目录</summary>
    public static bool LooksLikeHmclNewDirectory(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        return File.Exists(Path.Combine(dir, "config", "launcher-settings.json"));
    }

    /// <summary>判断目录是否含有 HMCL ≤3.15.x 旧格式的 hmcl.json</summary>
    public static bool LooksLikeHmclLegacyConfig(string path)
    {
        return File.Exists(path);
    }

    /// <summary>旧格式的 hmcl.json 可能位置：HMCL 目录（便携）与 %APPDATA%\hmcl（安装版）</summary>
    public static string[] GetLegacyConfigCandidates(string hmclDirectory)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return
        [
            Path.Combine(hmclDirectory, "hmcl.json"),
            Path.Combine(hmclDirectory, ".hmcl.json"),
            string.IsNullOrEmpty(appData) ? "" : Path.Combine(appData, "hmcl", "hmcl.json")
        ];
    }

    /// <summary>按新旧两种格式探测 HMCL 数据目录，返回可用于校验的说明；找到返回 true</summary>
    public static bool DetectHmclData(string hmclDirectory, bool newFormat, out string detail)
    {
        if (newFormat)
        {
            var found = LooksLikeHmclNewDirectory(hmclDirectory);
            detail = found ? "已找到 HMCL 3.16+ 配置" : "该目录中未找到 config\\launcher-settings.json";
            return found;
        }

        foreach (var candidate in GetLegacyConfigCandidates(hmclDirectory))
        {
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
            {
                detail = $"已找到旧版配置 {candidate}";
                return true;
            }
        }

        detail = "该目录中未找到 hmcl.json";
        return false;
    }

    /// <summary>
    /// 执行迁移。只导入 HMCL 中显式保存过的设置。
    /// </summary>
    public static PclMigrationResult Migrate(string hmclDirectory, bool newFormat, bool importAppSettings, bool importGameSettings)
    {
        var result = new PclMigrationResult();
        DebugLogger.Info(Tag, $"开始迁移：目录={hmclDirectory}，格式={(newFormat ? "3.16+" : "≤3.15.x")}，应用设置={importAppSettings}，游戏设置={importGameSettings}");

        var config = LauncherConfig.Load();
        string? gameDir = null;

        if (newFormat)
        {
            if (importAppSettings)
            {
                gameDir = MigrateNewAppSettings(hmclDirectory, config, result);
                config.Save();
            }

            if (importGameSettings)
            {
                gameDir ??= config.GameDirectory;
                MigrateNewInstances(gameDir, result);
            }
        }
        else
        {
            var hmclJsonPath = Array.Find(GetLegacyConfigCandidates(hmclDirectory), p => !string.IsNullOrEmpty(p) && File.Exists(p));
            if (hmclJsonPath == null)
            {
                result.AddItem("应用设置", "hmcl.json", "未找到旧版配置文件", MigrationItemState.Warning);
                return result;
            }

            if (importAppSettings)
            {
                gameDir = MigrateLegacyAppSettings(hmclJsonPath, config, result);
                config.Save();
            }

            if (importGameSettings)
            {
                gameDir ??= config.GameDirectory;
                MigrateLegacyInstances(gameDir, result);
            }
        }

        DebugLogger.Info(Tag, $"迁移结束：应用设置 {result.AppSettingsImported} 项，版本 {result.VersionsImported} 个，警告 {result.Warnings.Count} 条");
        return result;
    }

    // ==================== 3.16+ 新格式 ====================

    /// <summary>导入新格式应用设置，返回选中的游戏目录</summary>
    private static string? MigrateNewAppSettings(string hmclDirectory, LauncherConfig config, PclMigrationResult result)
    {
        string? gameDir = null;
        var imported = 0;
        var settingsOpt = ReadJsonFile(Path.Combine(hmclDirectory, "config", "launcher-settings.json"));
        if (settingsOpt == null)
        {
            result.AddItem("应用设置", "launcher-settings.json", "读取失败", MigrationItemState.Warning);
            return null;
        }
        var settings = settingsOpt.Value;

        // 下载源：新格式枚举 DEFAULT / OFFICIAL / MIRROR
        if (TryGetString(settings, "fileDownloadSource", out var source) && !string.IsNullOrEmpty(source))
        {
            if (source.Equals("MIRROR", StringComparison.OrdinalIgnoreCase))
            {
                config.MirrorSourceMode = MirrorSourceMode.PreferMirror;
                imported++;
                result.AddItem("应用设置", "下载源", "优先镜像源");
            }
            else if (source.Equals("OFFICIAL", StringComparison.OrdinalIgnoreCase))
            {
                config.MirrorSourceMode = MirrorSourceMode.OfficialOnly;
                imported++;
                result.AddItem("应用设置", "下载源", "官方源");
            }
        }

        // 下载线程数（自动模式下跳过）
        if (TryGetBool(settings, "autoDownloadThreads", out var auto) && !auto
            && TryGetInt(settings, "downloadThreads", out var threads) && threads > 0)
        {
            config.MaxDownloadThreads = Math.Min(threads, 64);
            imported++;
            result.AddItem("应用设置", "下载线程数", $"{Math.Min(threads, 64)}");
        }

        // 全局游戏设置：launcher-settings.json 的 defaultGameSettingsPreset 指向 game-settings.json 中的预设
        imported += MigrateNewGlobalGameSettings(hmclDirectory, settings, config, result);

        // 游戏目录列表：本地 game-directories.json 与用户级 user-game-directories.json，
        // selectedGameDirectory 指向的为当前目录，其余加入自定义列表
        TryGetString(settings, "selectedGameDirectory", out var selectedId);
        var dirFiles = new[]
        {
            Path.Combine(hmclDirectory, "config", "game-directories.json"),
            Path.Combine(hmclDirectory, "config", "user-game-directories.json")
        };

        var foundAnyDir = false;
        foreach (var dirFile in dirFiles)
        {
            var directories = ReadJsonFile(dirFile);
            if (directories == null || !directories.Value.TryGetProperty("directories", out var listEl) || listEl.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var entry in listEl.EnumerateArray())
            {
                if (!TryGetString(entry, "path", out var path) || string.IsNullOrWhiteSpace(path)) continue;
                if (!Directory.Exists(path))
                {
                    result.AddItem("应用设置", "游戏目录", $"路径不存在：{path}", MigrationItemState.Skipped);
                    continue;
                }
                foundAnyDir = true;

                var isSelected = TryGetString(entry, "id", out var id) && id == selectedId;
                if (isSelected && gameDir == null)
                {
                    gameDir = path;
                    config.GameDirectoryLocation = DirectoryLocation.Custom;
                    config.CustomGameDirectory = path;
                    imported++;
                    result.AddItem("应用设置", "当前游戏文件夹", path);
                }
                else if (!config.CustomGameDirectories.Contains(path, StringComparer.OrdinalIgnoreCase)
                         && !string.Equals(path, gameDir, StringComparison.OrdinalIgnoreCase))
                {
                    config.CustomGameDirectories.Add(path);
                    result.AddItem("应用设置", "附加游戏文件夹", path);
                }
            }
        }

        if (!foundAnyDir)
            result.AddItem("应用设置", "游戏目录", "配置中未保存任何游戏目录", MigrationItemState.Skipped);

        // authlib-injector 服务器
        imported += MigrateAuthlibServers(Path.Combine(hmclDirectory, "config", "authlib-injector-servers.json"), result);

        result.AppSettingsImported = imported;
        return gameDir;
    }

    /// <summary>导入新格式的全局游戏设置预设（内存 / JVM / Java）</summary>
    private static int MigrateNewGlobalGameSettings(string hmclDirectory, JsonElement settings, LauncherConfig config, PclMigrationResult result)
    {
        var presetsFile = ReadJsonFile(Path.Combine(hmclDirectory, "config", "game-settings.json"));
        if (presetsFile == null || !presetsFile.Value.TryGetProperty("presets", out var presetsEl) || presetsEl.ValueKind != JsonValueKind.Array)
        {
            result.AddItem("应用设置", "全局游戏设置", "未找到 config\\game-settings.json", MigrationItemState.Skipped);
            return 0;
        }

        // 默认预设由 launcher-settings.json 的 defaultGameSettingsPreset 指定，找不到就取第一个
        TryGetString(settings, "defaultGameSettingsPreset", out var defaultPresetId);
        JsonElement? preset = null;
        foreach (var p in presetsEl.EnumerateArray())
        {
            if (TryGetString(p, "id", out var pid) && pid == defaultPresetId)
            {
                preset = p;
                break;
            }
        }
        preset ??= presetsEl.GetArrayLength() > 0 ? presetsEl[0] : null;
        if (preset == null)
        {
            result.AddItem("应用设置", "全局游戏设置", "预设列表为空", MigrationItemState.Skipped);
            return 0;
        }

        return ApplyGlobalGameSettings(preset.Value, config, result, "全局游戏设置");
    }

    /// <summary>扫描新格式实例设置 versions\&lt;id&gt;\.hmcl\config\instance-game-settings.json</summary>
    private static void MigrateNewInstances(string gameDir, PclMigrationResult result)
    {
        var versionsDir = Path.Combine(gameDir, "versions");
        if (!Directory.Exists(versionsDir))
        {
            result.AddItem("游戏设置", "版本扫描", $"versions 目录不存在：{versionsDir}", MigrationItemState.Warning);
            return;
        }

        foreach (var versionDir in Directory.EnumerateDirectories(versionsDir))
        {
            var settingsPath = Path.Combine(versionDir, ".hmcl", "config", "instance-game-settings.json");
            if (!File.Exists(settingsPath)) continue;

            try
            {
                var json = ReadJsonFile(settingsPath);
                if (json == null) continue;

                var data = VersionInitService.Load(versionDir);
                var parts = new List<string>();

                // 新格式实例只覆盖 overrideProperties 中列出的属性，未列出的跟随预设
                var overrides = new HashSet<string>(StringComparer.Ordinal);
                if (json.Value.TryGetProperty("overrideProperties", out var overrideEl) && overrideEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var o in overrideEl.EnumerateArray())
                    {
                        if (o.ValueKind == JsonValueKind.String) overrides.Add(o.GetString()!);
                    }
                }

                var changed = ApplyGameSettings(json.Value, data, overrides, parts, newFormat: true);
                if (changed)
                {
                    VersionInitService.Save(versionDir, data);
                    result.VersionsImported++;
                    result.AddItem("游戏设置", Path.GetFileName(versionDir), string.Join("、", parts));
                }
                else
                {
                    result.AddItem("游戏设置", Path.GetFileName(versionDir), "未覆盖任何全局设置", MigrationItemState.Skipped);
                }
            }
            catch (Exception ex)
            {
                result.AddItem("游戏设置", Path.GetFileName(versionDir), $"导入失败：{ex.Message}", MigrationItemState.Warning);
            }
        }
    }

    // ==================== ≤3.15.x 旧格式 ====================

    /// <summary>导入旧格式应用设置（hmcl.json），返回选中的游戏目录</summary>
    private static string? MigrateLegacyAppSettings(string hmclJsonPath, LauncherConfig config, PclMigrationResult result)
    {
        string? gameDir = null;
        var imported = 0;
        var rootOpt = ReadJsonFile(hmclJsonPath);
        if (rootOpt == null)
        {
            result.AddItem("应用设置", "hmcl.json", "解析失败", MigrationItemState.Warning);
            return null;
        }
        var root = rootOpt.Value;

        // config 节：下载源与线程
        if (root.TryGetProperty("config", out var configEl) && configEl.ValueKind == JsonValueKind.Object)
        {
            if (TryGetString(configEl, "downloadType", out var source) && !string.IsNullOrEmpty(source))
            {
                // 旧格式值为 mojang / bmclapi，新版本可能是 official / mirror
                if (source.Equals("bmclapi", StringComparison.OrdinalIgnoreCase)
                    || source.Equals("mirror", StringComparison.OrdinalIgnoreCase))
                {
                    config.MirrorSourceMode = MirrorSourceMode.PreferMirror;
                    imported++;
                    result.AddItem("应用设置", "下载源", "优先镜像源 (BMCLAPI)");
                }
                else if (source.Equals("mojang", StringComparison.OrdinalIgnoreCase)
                         || source.Equals("official", StringComparison.OrdinalIgnoreCase))
                {
                    config.MirrorSourceMode = MirrorSourceMode.OfficialOnly;
                    imported++;
                    result.AddItem("应用设置", "下载源", "官方源");
                }
            }

            if (TryGetBool(configEl, "autoDownloadThreads", out var auto) && !auto
                && TryGetInt(configEl, "downloadThreads", out var threads) && threads > 0)
            {
                config.MaxDownloadThreads = Math.Min(threads, 64);
                imported++;
                result.AddItem("应用设置", "下载线程数", $"{Math.Min(threads, 64)}");
            }
        }

        // configurations 节：游戏目录（profile），last 为选中的
        if (root.TryGetProperty("configurations", out var profilesEl) && profilesEl.ValueKind == JsonValueKind.Object)
        {
            TryGetString(root, "last", out var lastName);

            // 优先取 last 指向的 profile，否则取 Default 或第一个
            string? selectedName = null;
            if (!string.IsNullOrEmpty(lastName) && profilesEl.TryGetProperty(lastName!, out _))
            {
                selectedName = lastName;
            }
            else if (profilesEl.TryGetProperty("Default", out _))
            {
                selectedName = "Default";
            }

            foreach (var profile in profilesEl.EnumerateObject())
            {
                if (!TryGetString(profile.Value, "gameDir", out var path) || string.IsNullOrWhiteSpace(path)) continue;
                if (!Directory.Exists(path))
                {
                    result.AddItem("应用设置", $"目录 {profile.Name}", $"路径不存在：{path}", MigrationItemState.Skipped);
                    continue;
                }

                if (profile.Name == selectedName && gameDir == null)
                {
                    gameDir = path;
                    config.GameDirectoryLocation = DirectoryLocation.Custom;
                    config.CustomGameDirectory = path;
                    imported++;
                    result.AddItem("应用设置", "当前游戏文件夹", $"{path}（配置 {profile.Name}）");
                }
                else if (!config.CustomGameDirectories.Contains(path, StringComparer.OrdinalIgnoreCase)
                         && !string.Equals(path, gameDir, StringComparison.OrdinalIgnoreCase))
                {
                    config.CustomGameDirectories.Add(path);
                    result.AddItem("应用设置", "附加游戏文件夹", $"{path}（配置 {profile.Name}）");
                }
            }

            // 选中 profile 的 global 节为全局游戏设置
            if (selectedName != null
                && profilesEl.TryGetProperty(selectedName, out var selected)
                && selected.TryGetProperty("global", out var global)
                && global.ValueKind == JsonValueKind.Object)
            {
                imported += ApplyGlobalGameSettings(global, config, result, $"全局游戏设置（{selectedName}）");
            }
        }

        // authlib-injector 服务器：旧格式直接存在根级数组
        if (root.TryGetProperty("authlibInjectorServers", out var serversEl) && serversEl.ValueKind == JsonValueKind.Array)
        {
            imported += ImportAuthlibServerUrls(serversEl, result);
        }

        result.AppSettingsImported = imported;
        return gameDir;
    }

    /// <summary>扫描旧格式实例设置 versions\&lt;id&gt;\hmclversion.cfg</summary>
    private static void MigrateLegacyInstances(string gameDir, PclMigrationResult result)
    {
        var versionsDir = Path.Combine(gameDir, "versions");
        if (!Directory.Exists(versionsDir))
        {
            result.AddItem("游戏设置", "版本扫描", $"versions 目录不存在：{versionsDir}", MigrationItemState.Warning);
            return;
        }

        foreach (var versionDir in Directory.EnumerateDirectories(versionsDir))
        {
            var settingsPath = Path.Combine(versionDir, "hmclversion.cfg");
            if (!File.Exists(settingsPath)) continue;

            try
            {
                var json = ReadJsonFile(settingsPath);
                if (json == null) continue;
                var versionJson = json.Value;

                // usesGlobal=true 表示该版本完全跟随全局设置，无需迁移
                if (TryGetBool(versionJson, "usesGlobal", out var usesGlobal) && usesGlobal)
                {
                    result.AddItem("游戏设置", Path.GetFileName(versionDir), "跟随全局设置，跳过", MigrationItemState.Skipped);
                    continue;
                }

                var data = VersionInitService.Load(versionDir);
                var parts = new List<string>();
                var changed = ApplyGameSettings(versionJson, data, null, parts, newFormat: false);
                if (changed)
                {
                    VersionInitService.Save(versionDir, data);
                    result.VersionsImported++;
                    result.AddItem("游戏设置", Path.GetFileName(versionDir), string.Join("、", parts));
                }
                else
                {
                    result.AddItem("游戏设置", Path.GetFileName(versionDir), "无实例级设置", MigrationItemState.Skipped);
                }
            }
            catch (Exception ex)
            {
                result.AddItem("游戏设置", Path.GetFileName(versionDir), $"导入失败：{ex.Message}", MigrationItemState.Warning);
            }
        }
    }

    // ==================== 共用逻辑 ====================

    /// <summary>
    /// 把 HMCL 的游戏设置 JSON 写入 init.json，返回是否有变更。
    /// 新格式（overrideProperties 机制）需传入 overrides，仅覆盖列出的属性；
    /// 旧格式传 null，字段存在即生效。
    /// </summary>
    private static bool ApplyGameSettings(JsonElement json, VersionInitData data, HashSet<string>? overrides, List<string> parts, bool newFormat)
    {
        var changed = false;

        bool IsOverridden(string prop) => overrides == null || overrides.Contains(prop);

        // 内存：autoMemory=false 时才导入自定义值（单位 MB）
        if (IsOverridden("autoMemory") && TryGetBool(json, "autoMemory", out var autoMemory) && !autoMemory)
        {
            if (IsOverridden("maxMemory") && TryGetInt(json, "maxMemory", out var max) && max > 0 && data.MaxMemory == null)
            {
                data.MaxMemory = Math.Min(max, 65536);
                changed = true;
                parts.Add($"内存 {data.MaxMemory / 1024.0:0.#} GB");
            }

            if (IsOverridden("minMemory") && TryGetInt(json, "minMemory", out var min) && min > 0 && data.MinMemory == null)
            {
                data.MinMemory = Math.Min(min, 65536);
                changed = true;
            }
        }

        // JVM 参数：新字段名 jvmOptions，旧字段名 javaArgs
        var jvmArgs = "";
        if (!TryGetString(json, "jvmOptions", out jvmArgs))
        {
            TryGetString(json, "javaArgs", out jvmArgs);
        }

        if (IsOverridden("jvmOptions") && !string.IsNullOrWhiteSpace(jvmArgs) && string.IsNullOrWhiteSpace(data.JvmArguments))
        {
            data.JvmArguments = jvmArgs!.Trim();
            changed = true;
            parts.Add("JVM 参数");
        }

        // Java 路径
        // 新格式：javaType=CUSTOM 时 customJavaPath 有效
        // 旧格式：java 字段为 "Custom"（或 javaVersionType=CUSTOM），javaDir 为路径
        var customPath = "";
        TryGetString(json, "customJavaPath", out customPath);
        if (string.IsNullOrWhiteSpace(customPath))
            TryGetString(json, "javaDir", out customPath);

        var isCustomJava = false;
        if (newFormat)
        {
            var javaType = "";
            TryGetString(json, "javaType", out javaType);
            isCustomJava = string.Equals(javaType, "CUSTOM", StringComparison.OrdinalIgnoreCase)
                           && !string.IsNullOrWhiteSpace(customPath);
        }
        else
        {
            var javaMode = "";
            TryGetString(json, "java", out javaMode);
            var javaVersionType = "";
            TryGetString(json, "javaVersionType", out javaVersionType);
            isCustomJava = (string.Equals(javaMode, "Custom", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(javaVersionType, "CUSTOM", StringComparison.OrdinalIgnoreCase))
                           && !string.IsNullOrWhiteSpace(customPath);
        }

        if (isCustomJava && string.IsNullOrWhiteSpace(data.CustomJavaPath))
        {
            data.CustomJavaPath = customPath!.Trim();
            changed = true;
            parts.Add("自定义 Java");
        }

        // 版本隔离
        if (data.IsolationMode == "global")
        {
            // 旧格式：gameDirType 枚举（ROOT_FOLDER/VERSION_FOLDER/CUSTOM，旧版本可能存数字 0/1/2）
            if (json.TryGetProperty("gameDirType", out var gameDirTypeEl))
            {
                var isolated = false;
                if (gameDirTypeEl.ValueKind == JsonValueKind.String)
                {
                    var gameDirType = gameDirTypeEl.GetString()!;
                    isolated = gameDirType is "VERSION_FOLDER" or "CUSTOM";
                }
                else if (gameDirTypeEl.ValueKind == JsonValueKind.Number && gameDirTypeEl.TryGetInt32(out var typeNum))
                {
                    isolated = typeNum is 1 or 2;
                }

                if (isolated)
                {
                    data.IsolationMode = "enabled";
                    changed = true;
                    parts.Add("版本隔离");
                }
                else if (overrides != null)
                {
                    // 显式设为 ROOT_FOLDER 时记录为不隔离
                    data.IsolationMode = "disabled";
                    changed = true;
                    parts.Add("不隔离");
                }
            }
            // 新格式：runningDirectory 非空表示自定义运行目录（视为隔离）
            else if (newFormat && IsOverridden("runningDirectory")
                     && json.TryGetProperty("runningDirectory", out var runEl)
                     && runEl.ValueKind == JsonValueKind.String
                     && !string.IsNullOrWhiteSpace(runEl.GetString()))
            {
                data.IsolationMode = "enabled";
                changed = true;
                parts.Add("版本隔离");
            }
        }

        return changed;
    }

    /// <summary>把全局游戏设置（旧格式 profile.global / 新格式 preset）写入全局配置</summary>
    private static int ApplyGlobalGameSettings(JsonElement global, LauncherConfig config, PclMigrationResult result, string label)
    {
        var imported = 0;
        var parts = new List<string>();

        if (TryGetBool(global, "autoMemory", out var autoMemory) && !autoMemory
            && TryGetInt(global, "maxMemory", out var max) && max > 0)
        {
            config.MaxMemory = Math.Min(max, 65536);
            imported++;
            parts.Add($"最大内存 {config.MaxMemory / 1024.0:0.#} GB");
        }

        var jvmArgs = "";
        if (!TryGetString(global, "javaArgs", out jvmArgs) || string.IsNullOrWhiteSpace(jvmArgs))
        {
            TryGetString(global, "jvmOptions", out jvmArgs);
        }

        if (!string.IsNullOrWhiteSpace(jvmArgs))
        {
            config.JvmArguments = jvmArgs!.Trim();
            imported++;
            parts.Add("JVM 参数");
        }

        // Java：新格式 javaType=CUSTOM + customJavaPath；旧格式 javaDir 直接为路径
        var javaDir = "";
        TryGetString(global, "customJavaPath", out javaDir);
        if (string.IsNullOrWhiteSpace(javaDir))
            TryGetString(global, "javaDir", out javaDir);

        var javaType = "";
        TryGetString(global, "javaType", out javaType);
        var isCustom = string.IsNullOrWhiteSpace(javaType) || javaType.Equals("CUSTOM", StringComparison.OrdinalIgnoreCase);

        if (isCustom && !string.IsNullOrWhiteSpace(javaDir))
        {
            config.JavaSelectionMode = 2;
            config.CustomJavaPath = javaDir!.Trim();
            imported++;
            parts.Add("自定义 Java");
        }

        if (parts.Count > 0)
            result.AddItem("应用设置", label, string.Join("、", parts));
        else
            result.AddItem("应用设置", label, "均为默认值", MigrationItemState.Skipped);

        return imported;
    }

    /// <summary>读取新格式 authlib-injector-servers.json 并导入</summary>
    private static int MigrateAuthlibServers(string path, PclMigrationResult result)
    {
        if (!File.Exists(path))
        {
            result.AddItem("登录服务器", "authlib-injector", "未找到服务器列表文件", MigrationItemState.Skipped);
            return 0;
        }

        var json = ReadJsonFile(path);
        if (json == null || !json.Value.TryGetProperty("servers", out var serversEl) || serversEl.ValueKind != JsonValueKind.Array)
        {
            result.AddItem("登录服务器", "authlib-injector", "服务器列表为空", MigrationItemState.Skipped);
            return 0;
        }

        return ImportAuthlibServerUrls(serversEl, result);
    }

    /// <summary>把服务器 URL 数组导入 Yggdrasil 服务器列表（按 URL 去重，内置 LittleSkin 自动跳过）</summary>
    private static int ImportAuthlibServerUrls(JsonElement serversEl, PclMigrationResult result)
    {
        var imported = 0;
        foreach (var server in serversEl.EnumerateArray())
        {
            if (!TryGetString(server, "url", out var url) || string.IsNullOrWhiteSpace(url)) continue;

            var name = ExtractHost(url);
            try
            {
                YggdrasilServerService.Instance.AddServer(name, url);
                imported++;
                result.AddItem("登录服务器", name, url);
            }
            catch (Exception ex)
            {
                result.AddItem("登录服务器", name, $"跳过：{ex.Message}", MigrationItemState.Skipped);
            }
        }

        if (imported == 0)
            result.AddItem("登录服务器", "authlib-injector", "没有可导入的服务器", MigrationItemState.Skipped);

        return imported;
    }

    /// <summary>从 URL 提取主机名作为服务器名称</summary>
    private static string ExtractHost(string url)
    {
        var match = HostRegex().Match(url.Trim());
        return match.Success ? match.Groups[1].Value : url;
    }

    /// <summary>读取 JSON 文件为 JsonElement，失败返回 null</summary>
    private static JsonElement? ReadJsonFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            DebugLogger.Warn(Tag, $"解析 JSON 失败 [{path}]: {ex.Message}");
            return null;
        }
    }

    private static bool TryGetString(JsonElement el, string name, out string? value)
    {
        value = null;
        if (!el.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String) return false;
        value = prop.GetString();
        return true;
    }

    private static bool TryGetInt(JsonElement el, string name, out int value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var prop)) return false;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value)) return true;
        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out value)) return true;
        return false;
    }

    private static bool TryGetBool(JsonElement el, string name, out bool value)
    {
        value = false;
        if (!el.TryGetProperty(name, out var prop)) return false;
        if (prop.ValueKind == JsonValueKind.True) { value = true; return true; }
        if (prop.ValueKind == JsonValueKind.False) { value = false; return true; }
        if (prop.ValueKind == JsonValueKind.String && bool.TryParse(prop.GetString(), out value)) return true;
        return false;
    }
}
