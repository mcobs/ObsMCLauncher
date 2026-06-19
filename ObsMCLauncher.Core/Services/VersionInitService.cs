using System;
using System.IO;
using System.Text.Json;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

/// <summary>
/// 统一管理版本目录下 OMCL/init.json 的读写
/// 所有版本级别的配置（分组、隔离、内存、描述等）都通过此服务访问
/// </summary>
public static class VersionInitService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    private static string GetInitJsonPath(string versionPath) =>
        Path.Combine(versionPath, "OMCL", "init.json");

    /// <summary>
    /// 读取版本配置，文件不存在或解析失败时返回默认实例
    /// </summary>
    public static VersionInitData Load(string versionPath)
    {
        if (string.IsNullOrEmpty(versionPath)) return new VersionInitData();

        var initPath = GetInitJsonPath(versionPath);
        if (!File.Exists(initPath)) return new VersionInitData();

        try
        {
            var json = File.ReadAllText(initPath);
            return JsonSerializer.Deserialize<VersionInitData>(json, JsonOptions) ?? new VersionInitData();
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("VersionInit", $"读取 init.json 失败 [{versionPath}]: {ex.Message}");
            return new VersionInitData();
        }
    }

    /// <summary>
    /// 保存完整配置到 init.json
    /// </summary>
    public static bool Save(string versionPath, VersionInitData data)
    {
        if (string.IsNullOrEmpty(versionPath)) return false;

        try
        {
            var omclDir = Path.Combine(versionPath, "OMCL");
            if (!Directory.Exists(omclDir)) Directory.CreateDirectory(omclDir);

            var initPath = Path.Combine(omclDir, "init.json");
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(initPath, json);
            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("VersionInit", $"保存 init.json 失败 [{versionPath}]: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 更新 init.json 中的部分字段，保留其它字段不变
    /// </summary>
    public static void Update(string versionPath, Action<VersionInitData> updater)
    {
        if (string.IsNullOrEmpty(versionPath)) return;
        var data = Load(versionPath);
        updater(data);
        Save(versionPath, data);
    }

    /// <summary>
    /// 确保 init.json 存在，不存在则创建默认配置
    /// </summary>
    public static void EnsureExists(string versionPath)
    {
        if (string.IsNullOrEmpty(versionPath)) return;
        var initPath = GetInitJsonPath(versionPath);
        if (File.Exists(initPath)) return;

        var omclDir = Path.Combine(versionPath, "OMCL");
        if (!Directory.Exists(omclDir)) Directory.CreateDirectory(omclDir);
        Save(versionPath, new VersionInitData());
    }

    // ===== 分组 =====
    public static string GetGroup(string versionPath) => Load(versionPath).Group;

    public static void SetGroup(string versionPath, string groupId) =>
        Update(versionPath, d => d.Group = groupId);

    // ===== 版本隔离 =====
    public static string GetIsolationMode(string versionPath) => Load(versionPath).IsolationMode;

    public static void SetIsolationMode(string versionPath, string mode) =>
        Update(versionPath, d => d.IsolationMode = mode);

    // ===== 内存 =====
    public static (int? max, int? min) GetMemory(string versionPath)
    {
        var d = Load(versionPath);
        return (d.MaxMemory, d.MinMemory);
    }

    public static void SetMemory(string versionPath, int? max, int? min) =>
        Update(versionPath, d =>
        {
            d.MaxMemory = max;
            d.MinMemory = min;
        });

    // ===== 描述 =====
    public static string GetDescription(string versionPath) => Load(versionPath).Description;

    public static void SetDescription(string versionPath, string description) =>
        Update(versionPath, d => d.Description = description ?? "");

    /// <summary>
    /// 按约定格式生成默认描述：
    /// &lt;版本类型&gt;,&lt;版本&gt;,&lt;加载器&gt;&lt;optifine版本（可选）&gt;，&lt;其他信息（可选）&gt;
    /// </summary>
    public static string GenerateDefaultDescription(
        string versionType,
        string versionId,
        string loaderType = "vanilla",
        string? loaderVersion = null,
        string? optifineVersion = null,
        string? otherInfo = null)
    {
        // 版本类型映射到中文展示名
        var typeStr = versionType?.ToLower() switch
        {
            "release" => "正式版",
            "snapshot" => "快照版",
            "old_alpha" => "远古Alpha",
            "old_beta" => "远古Beta",
            _ => string.IsNullOrEmpty(versionType) ? "未知" : versionType
        };

        // 加载器展示名
        var (loaderName, isVanilla) = loaderType?.ToLower() switch
        {
            "forge" => ("Forge", false),
            "fabric" => ("Fabric", false),
            "quilt" => ("Quilt", false),
            "neoforge" => ("NeoForge", false),
            "optifine" => ("OptiFine", false),
            _ => ("原版", true)
        };

        // 加载器信息（带版本号）
        var loaderPart = isVanilla
            ? loaderName
            : string.IsNullOrEmpty(loaderVersion)
                ? loaderName
                : $"{loaderName} {loaderVersion}";

        // OptiFine 作为附加项与其它加载器共存时，追加其版本
        if (!string.IsNullOrEmpty(optifineVersion) &&
            !string.Equals(loaderType, "optifine", StringComparison.OrdinalIgnoreCase))
        {
            loaderPart += $" OptiFine {optifineVersion}";
        }

        var desc = $"{typeStr},{versionId},{loaderPart}";
        if (!string.IsNullOrEmpty(otherInfo))
            desc += $"，{otherInfo}";

        return desc;
    }
}
