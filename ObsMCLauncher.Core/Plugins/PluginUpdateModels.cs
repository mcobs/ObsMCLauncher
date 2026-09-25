using System;

namespace ObsMCLauncher.Core.Plugins;

/// <summary>
/// 一个已安装插件相对市场的更新状态（展示用，不落盘）。
/// </summary>
public class PluginUpdateInfo
{
    /// <summary>插件 id</summary>
    public string PluginId { get; init; } = string.Empty;

    /// <summary>插件显示名</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>本地已安装版本</summary>
    public string InstalledVersion { get; init; } = string.Empty;

    /// <summary>可更新到的版本；无更新时等于本地版本</summary>
    public string AvailableVersion { get; init; } = string.Empty;

    /// <summary>是否存在新版本</summary>
    public bool HasUpdate { get; init; }

    /// <summary>新版本已下载并暂存，等待重启生效</summary>
    public bool IsStaged { get; init; }

    /// <summary>本地是否处于启用状态（存在 .disabled 标记即为禁用）</summary>
    public bool IsEnabled { get; init; }

    /// <summary>更新检查的来源：市场索引版本，或深度检查拿到的最新 Release 版本</summary>
    public bool IsFromReleaseCheck { get; init; }

    /// <summary>对应的市场条目（用于取下载地址）；可能为 null（插件已从市场下架）</summary>
    public MarketPlugin? Market { get; init; }
}

/// <summary>
/// 暂存更新的落盘元数据（&lt;OMCL&gt;/plugin-updates/{id}/pending.json）。
/// 该文件存在即代表"有一个已下载、等待下次启动应用的更新"。
/// </summary>
public class PendingPluginUpdate
{
    /// <summary>插件 id</summary>
    public string PluginId { get; set; } = string.Empty;

    /// <summary>应用前本地版本（仅用于展示与日志）</summary>
    public string FromVersion { get; set; } = string.Empty;

    /// <summary>将要应用的版本（取自新包内的 plugin.json）</summary>
    public string ToVersion { get; set; } = string.Empty;

    /// <summary>暂存时间</summary>
    public DateTime StagedAt { get; set; } = DateTime.Now;

    /// <summary>下载来源（仅用于排查）</summary>
    public string? SourceUrl { get; set; }

    /// <summary>
    /// 已尝试应用的次数。失败后保留暂存以便下次启动重试，
    /// 达到 <see cref="PluginUpdateService.MaxApplyAttempts"/> 则丢弃，避免每次启动都白跑一遍。
    /// </summary>
    public int ApplyAttempts { get; set; }
}

/// <summary>
/// 一次"应用待更新"的结果。
/// </summary>
public class PluginUpdateApplyResult
{
    /// <summary>插件 id</summary>
    public string PluginId { get; init; } = string.Empty;

    /// <summary>是否成功替换</summary>
    public bool Success { get; init; }

    /// <summary>替换失败后是否已从备份回滚到旧版本</summary>
    public bool RolledBack { get; init; }

    /// <summary>替换前版本</summary>
    public string? FromVersion { get; init; }

    /// <summary>替换后版本</summary>
    public string? ToVersion { get; init; }

    /// <summary>失败原因（成功时为 null）</summary>
    public string? Error { get; init; }
}
