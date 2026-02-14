using System.Text.Json.Serialization;

namespace ObsMCLauncher.Core.Models;

/// <summary>
/// OptiFine 版本信息模型（来自 BMCLAPI）
/// </summary>
public class OptifineVersionModel
{
    /// <summary>
    /// MongoDB 文档 ID
    /// </summary>
    [JsonPropertyName("_id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// MineCraft 版本
    /// 例如：1.20.1, 1.19.2
    /// </summary>
    [JsonPropertyName("mcversion")]
    public string McVersion { get; set; } = string.Empty;

    /// <summary>
    /// OptiFine 补丁版本
    /// 例如：H9, I5
    /// </summary>
    [JsonPropertyName("patch")]
    public string Patch { get; set; } = string.Empty;

    /// <summary>
    /// OptiFine 类型
    /// 通常为：HD_U（高清，超级）
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 文档版本
    /// </summary>
    [JsonPropertyName("__v")]
    public int Version { get; set; }

    /// <summary>
    /// OptiFine 文件名
    /// 例如：OptiFine_1.19.2_HD_U_H9.jar
    /// </summary>
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    /// <summary>
    /// 兼容的 Forge 版本（可选）
    /// 例如：Forge 43.1.1
    /// </summary>
    [JsonPropertyName("forge")]
    public string? Forge { get; set; }

    /// <summary>
    /// 获取完整的 OptiFine 版本字符串
    /// 例如：HD_U_H9
    /// </summary>
    public string FullVersion => $"{Type}_{Patch}";

    /// <summary>
    /// 获取显示名称
    /// 例如：OptiFine 1.20.1 HD_U_H9
    /// </summary>
    public string DisplayName => $"OptiFine {McVersion} {FullVersion}";

    /// <summary>
    /// 获取安装后的游戏版本 ID
    /// 例如：1.20.1-OptiFine_HD_U_H9
    /// </summary>
    public string GetInstalledVersionId()
    {
        return $"{McVersion}-OptiFine_{FullVersion}";
    }
}
