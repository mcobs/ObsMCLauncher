using System;
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
                result.Warnings.Add("未找到 hmcl.json");
                DebugLogger.Warn(Tag, "未找到 hmcl.json，中止迁移");
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
            DebugLogger.Warn(Tag, "launcher-settings.json 读取失败");
            return null;
        }
        var settings = settingsOpt.Value;

        // 下载源：mojang=官方，bmclapi=镜像
        if (TryGetString(settings, "fileDownloadSource", out var source) && !string.IsNullOrEmpty(source))
        {
            config.MirrorSourceMode = source == "bmclapi" ? MirrorSourceMode.PreferMirror : MirrorSourceMode.OfficialOnly;
            imported++;
            DebugLogger.Info(Tag, $"导入下载源：{source} → {config.MirrorSourceMode}");
        }

        // 下载线程数（自动模式下跳过）
        if (TryGetBool(settings, "autoDownloadThreads", out var auto) && !auto
            && TryGetInt(settings, "downloadThreads", out var threads) && threads > 0)
        {
            config.MaxDownloadThreads = Math.Min(threads, 64);
            imported++;
            DebugLogger.Info(Tag, $"导入下载线程数：{threads}");
        }
        else if (settings.TryGetProperty("autoDownloadThreads", out var autoEl) && autoEl.GetBoolean())
        {
            DebugLogger.Info(Tag, "跳过下载线程数：HMCL 为自动模式");
        }

        // 游戏目录列表：selectedGameDirectory 指向的为当前目录，其余加入自定义列表
        var directories = ReadJsonFile(Path.Combine(hmclDirectory, "config", "game-directories.json"));
        if (directories != null && directories.Value.TryGetProperty("directories", out var listEl) && listEl.ValueKind == JsonValueKind.Array)
        {
            TryGetString(settings, "selectedGameDirectory", out var selectedId);
            foreach (var entry in listEl.EnumerateArray())
            {
                if (!TryGetString(entry, "path", out var path) || string.IsNullOrWhiteSpace(path)) continue;
                if (!Directory.Exists(path))
                {
                    DebugLogger.Info(Tag, $"跳过游戏目录（不存在）：{path}");
                    continue;
                }

                var isSelected = TryGetString(entry, "id", out var id) && id == selectedId;
                if (isSelected && gameDir == null)
                {
                    gameDir = path;
                    config.GameDirectoryLocation = DirectoryLocation.Custom;
                    config.CustomGameDirectory = path;
                    imported++;
                    DebugLogger.Info(Tag, $"导入当前游戏目录：{path}");
                }
                else if (!config.CustomGameDirectories.Contains(path, StringComparer.OrdinalIgnoreCase)
                         && !string.Equals(path, gameDir, StringComparison.OrdinalIgnoreCase))
                {
                    config.CustomGameDirectories.Add(path);
                    DebugLogger.Info(Tag, $"导入附加游戏目录：{path}");
                }
            }
        }

        // authlib-injector 服务器
        imported += MigrateAuthlibServers(Path.Combine(hmclDirectory, "config", "authlib-injector-servers.json"), result);

        result.AppSettingsImported = imported;
        return gameDir;
    }

    /// <summary>扫描新格式实例设置 versions\&lt;id&gt;\.hmcl\config\instance-game-settings.json</summary>
    private static void MigrateNewInstances(string gameDir, PclMigrationResult result)
    {
        var versionsDir = Path.Combine(gameDir, "versions");
        if (!Directory.Exists(versionsDir))
        {
            DebugLogger.Warn(Tag, $"versions 目录不存在，跳过游戏设置导入：{versionsDir}");
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
                var changed = ApplyGameSettings(json.Value, data, $"[{Path.GetFileName(versionDir)}]");
                if (changed)
                {
                    VersionInitService.Save(versionDir, data);
                    result.VersionsImported++;
                }
                else
                {
                    DebugLogger.Info(Tag, $"[{Path.GetFileName(versionDir)}] 无可迁移项，跳过");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Warn(Tag, $"导入版本 {Path.GetFileName(versionDir)} 失败：{ex.Message}");
                result.Warnings.Add($"导入版本 {Path.GetFileName(versionDir)} 失败：{ex.Message}");
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
            result.Warnings.Add("hmcl.json 解析失败");
            return null;
        }
        var root = rootOpt.Value;

        // config 节：下载源与线程
        if (root.TryGetProperty("config", out var configEl) && configEl.ValueKind == JsonValueKind.Object)
        {
            if (TryGetString(configEl, "downloadType", out var source) && !string.IsNullOrEmpty(source))
            {
                config.MirrorSourceMode = source == "bmclapi" ? MirrorSourceMode.PreferMirror : MirrorSourceMode.OfficialOnly;
                imported++;
                DebugLogger.Info(Tag, $"导入下载源：{source} → {config.MirrorSourceMode}");
            }

            if (TryGetBool(configEl, "autoDownloadThreads", out var auto) && !auto
                && TryGetInt(configEl, "downloadThreads", out var threads) && threads > 0)
            {
                config.MaxDownloadThreads = Math.Min(threads, 64);
                imported++;
                DebugLogger.Info(Tag, $"导入下载线程数：{threads}");
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
                    DebugLogger.Info(Tag, $"跳过游戏目录（不存在）：{path}");
                    continue;
                }

                if (profile.Name == selectedName && gameDir == null)
                {
                    gameDir = path;
                    config.GameDirectoryLocation = DirectoryLocation.Custom;
                    config.CustomGameDirectory = path;
                    imported++;
                    DebugLogger.Info(Tag, $"导入当前游戏目录（profile {profile.Name}）：{path}");
                }
                else if (!config.CustomGameDirectories.Contains(path, StringComparer.OrdinalIgnoreCase)
                         && !string.Equals(path, gameDir, StringComparison.OrdinalIgnoreCase))
                {
                    config.CustomGameDirectories.Add(path);
                    DebugLogger.Info(Tag, $"导入附加游戏目录（profile {profile.Name}）：{path}");
                }
            }

            // 选中 profile 的 global 节为全局游戏设置
            if (selectedName != null
                && profilesEl.TryGetProperty(selectedName, out var selected)
                && selected.TryGetProperty("global", out var global)
                && global.ValueKind == JsonValueKind.Object)
            {
                imported += ApplyGlobalGameSettings(global, config);
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
            DebugLogger.Warn(Tag, $"versions 目录不存在，跳过游戏设置导入：{versionsDir}");
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
                    DebugLogger.Info(Tag, $"[{Path.GetFileName(versionDir)}] 跟随全局设置，跳过");
                    continue;
                }

                var data = VersionInitService.Load(versionDir);
                var changed = ApplyGameSettings(versionJson, data, $"[{Path.GetFileName(versionDir)}]");
                if (changed)
                {
                    VersionInitService.Save(versionDir, data);
                    result.VersionsImported++;
                }
                else
                {
                    DebugLogger.Info(Tag, $"[{Path.GetFileName(versionDir)}] 无可迁移项，跳过");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Warn(Tag, $"导入版本 {Path.GetFileName(versionDir)} 失败：{ex.Message}");
                result.Warnings.Add($"导入版本 {Path.GetFileName(versionDir)} 失败：{ex.Message}");
            }
        }
    }

    // ==================== 共用逻辑 ====================

    /// <summary>把 HMCL 的游戏设置 JSON（新格式 instance-game-settings / 旧格式 global、hmclversion.cfg 字段名一致）写入 init.json，返回是否有变更</summary>
    private static bool ApplyGameSettings(JsonElement json, VersionInitData data, string logPrefix)
    {
        var changed = false;

        // 内存：autoMemory=false 时才导入自定义值（单位 MB）
        if (TryGetBool(json, "autoMemory", out var autoMemory) && !autoMemory)
        {
            if (TryGetInt(json, "maxMemory", out var max) && max > 0 && data.MaxMemory == null)
            {
                data.MaxMemory = Math.Min(max, 65536);
                changed = true;
                DebugLogger.Info(Tag, $"{logPrefix} 导入最大内存：{max} MB");
            }

            if (TryGetInt(json, "minMemory", out var min) && min > 0 && data.MinMemory == null)
            {
                data.MinMemory = Math.Min(min, 65536);
                changed = true;
                DebugLogger.Info(Tag, $"{logPrefix} 导入最小内存：{min} MB");
            }
        }

        // JVM 参数（旧字段名 javaArgs，新字段名 jvmOptions）
        var jvmArgs = "";
        if (!TryGetString(json, "jvmOptions", out jvmArgs))
        {
            TryGetString(json, "javaArgs", out jvmArgs);
        }

        if (!string.IsNullOrWhiteSpace(jvmArgs) && string.IsNullOrWhiteSpace(data.JvmArguments))
        {
            data.JvmArguments = jvmArgs.Trim();
            changed = true;
            DebugLogger.Info(Tag, $"{logPrefix} 导入 JVM 参数：{data.JvmArguments}");
        }

        // Java 路径：javaType=CUSTOM 时 customJavaPath 有效（旧格式 javaDir）
        var javaType = "";
        TryGetString(json, "javaType", out javaType);
        var customPath = "";
        if (!TryGetString(json, "customJavaPath", out customPath))
        {
            TryGetString(json, "javaDir", out customPath);
        }

        // 旧格式没有 javaType 字段，javaDir 非空即自定义
        var isCustomJava = string.IsNullOrEmpty(javaType)
            ? !string.IsNullOrWhiteSpace(customPath)
            : javaType == "CUSTOM" && !string.IsNullOrWhiteSpace(customPath);

        if (isCustomJava && string.IsNullOrWhiteSpace(data.CustomJavaPath))
        {
            data.CustomJavaPath = customPath!.Trim();
            changed = true;
            DebugLogger.Info(Tag, $"{logPrefix} 导入实例 Java：{data.CustomJavaPath}");
        }

        // 版本隔离：gameDirType=VERSION_FOLDER 或 runningDirectory 非空（新格式）
        if (data.IsolationMode == "global")
        {
            var gameDirType = "";
            if (TryGetString(json, "gameDirType", out gameDirType))
            {
                if (gameDirType == "VERSION_FOLDER")
                {
                    data.IsolationMode = "enabled";
                    changed = true;
                    DebugLogger.Info(Tag, $"{logPrefix} 导入版本隔离：{gameDirType}");
                }
            }
            else if (json.TryGetProperty("runningDirectory", out var runEl)
                     && runEl.ValueKind == JsonValueKind.String
                     && !string.IsNullOrWhiteSpace(runEl.GetString()))
            {
                data.IsolationMode = "enabled";
                changed = true;
                DebugLogger.Info(Tag, $"{logPrefix} 导入版本隔离：runningDirectory 非空");
            }
        }

        return changed;
    }

    /// <summary>把旧格式 profile 的 global 节写入全局配置（内存/JVM/Java）</summary>
    private static int ApplyGlobalGameSettings(JsonElement global, LauncherConfig config)
    {
        var imported = 0;

        if (TryGetBool(global, "autoMemory", out var autoMemory) && !autoMemory
            && TryGetInt(global, "maxMemory", out var max) && max > 0)
        {
            config.MaxMemory = Math.Min(max, 65536);
            imported++;
            DebugLogger.Info(Tag, $"导入全局最大内存：{max} MB");
        }

        if (!TryGetString(global, "javaArgs", out var jvmArgs) || string.IsNullOrWhiteSpace(jvmArgs))
        {
            TryGetString(global, "jvmOptions", out jvmArgs);
        }

        if (!string.IsNullOrWhiteSpace(jvmArgs))
        {
            config.JvmArguments = jvmArgs.Trim();
            imported++;
            DebugLogger.Info(Tag, $"导入全局 JVM 参数：{config.JvmArguments}");
        }

        if (TryGetString(global, "javaDir", out var javaDir) && !string.IsNullOrWhiteSpace(javaDir))
        {
            config.JavaSelectionMode = 2;
            config.CustomJavaPath = javaDir.Trim();
            imported++;
            DebugLogger.Info(Tag, $"导入全局 Java：{config.CustomJavaPath}");
        }

        return imported;
    }

    /// <summary>读取新格式 authlib-injector-servers.json 并导入</summary>
    private static int MigrateAuthlibServers(string path, PclMigrationResult result)
    {
        if (!File.Exists(path))
        {
            DebugLogger.Info(Tag, $"authlib-injector-servers.json 不存在：{path}");
            return 0;
        }

        var json = ReadJsonFile(path);
        if (json == null || !json.Value.TryGetProperty("servers", out var serversEl) || serversEl.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return ImportAuthlibServerUrls(serversEl, result);
    }

    /// <summary>把服务器 URL 数组导入 Yggdrasil 服务器列表（按名称去重，内置 LittleSkin 自动跳过）</summary>
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
                DebugLogger.Info(Tag, $"导入 authlib-injector 服务器：{name} ({url})");
            }
            catch (Exception ex)
            {
                DebugLogger.Info(Tag, $"跳过 authlib-injector 服务器 {url}：{ex.Message}");
            }
        }

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
