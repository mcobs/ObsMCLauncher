using System;
using System.Collections.Generic;

namespace ObsMCLauncher.Core.Plugins;

/// <summary>
/// 插件日志级别
/// </summary>
public enum PluginLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

/// <summary>
/// 已安装版本精简信息（仅暴露插件需要的安全字段）
/// </summary>
public class PluginVersionInfo
{
    public string VersionId { get; set; } = string.Empty;

    public string McVersion { get; set; } = string.Empty;

    /// <summary>加载器类型：vanilla/forge/fabric/quilt/neoforge/optifine</summary>
    public string LoaderType { get; set; } = "vanilla";

    public string VersionDirectory { get; set; } = string.Empty;

    public DateTime? LastPlayed { get; set; }
}

/// <summary>
/// 当前账户的精简信息（不含任何令牌/敏感字段）
/// </summary>
public class PluginAccountInfo
{
    public string AccountId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    /// <summary>账户类型：Offline/Microsoft/Yggdrasil</summary>
    public string AccountType { get; set; } = string.Empty;

    public string UUID { get; set; } = string.Empty;

    public bool IsDefault { get; set; }
}

/// <summary>
/// 游戏启动生命周期钩子触发阶段
/// </summary>
public enum GameLaunchPhase
{
    /// <summary>启动前（可在此修改参数或拦截启动）</summary>
    BeforeLaunch,

    /// <summary>游戏进程已启动</summary>
    AfterLaunch,

    /// <summary>游戏进程退出</summary>
    OnExited,

    /// <summary>检测到崩溃</summary>
    OnCrash
}

/// <summary>
/// 启动钩子上下文，随阶段不同字段含义不同
/// </summary>
public class GameLaunchHookContext
{
    public string VersionId { get; set; } = string.Empty;

    public string McVersion { get; set; } = string.Empty;

    public string GameDirectory { get; set; } = string.Empty;

    public string JavaPath { get; set; } = string.Empty;

    /// <summary>仅 OnExited/OnCrash 阶段有效；正常退出为 0</summary>
    public int ExitCode { get; set; }

    /// <summary>仅 OnCrash 阶段有效</summary>
    public string? CrashReport { get; set; }

    /// <summary>BeforeLaunch 阶段设为 true 可中止启动</summary>
    public bool CancelLaunch { get; set; }

    /// <summary>BeforeLaunch 阶段可追加额外 JVM 参数</summary>
    public List<string> ExtraJvmArguments { get; } = new();

    /// <summary>BeforeLaunch 阶段可追加额外游戏参数</summary>
    public List<string> ExtraGameArguments { get; } = new();
}

/// <summary>
/// 插件提交的下载请求
/// </summary>
public class PluginDownloadRequest
{
    /// <summary>下载 URL（http/https）</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>下载任务显示名称</summary>
    public string TaskName { get; set; } = string.Empty;

    /// <summary>目标保存目录（启动器会校验是否在允许范围内）</summary>
    public string TargetDirectory { get; set; } = string.Empty;

    /// <summary>保存文件名（不含路径）</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>可选：SHA-1 校验值</summary>
    public string? Sha1 { get; set; }

    /// <summary>是否立即开始下载，false 表示仅创建任务</summary>
    public bool AutoStart { get; set; } = true;
}
