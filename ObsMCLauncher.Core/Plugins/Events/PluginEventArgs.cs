namespace ObsMCLauncher.Core.Plugins.Events;

/// <summary>
/// 版本安装开始事件数据
/// </summary>
public class VersionInstallingEventArgs
{
    /// <summary>
    /// Minecraft 版本号（如 1.21.4）
    /// </summary>
    public string McVersion { get; set; } = "";

    /// <summary>
    /// 自定义版本名称
    /// </summary>
    public string VersionName { get; set; } = "";

    /// <summary>
    /// 加载器类型（vanilla, forge, fabric, quilt, neoforge, optifine 等）
    /// </summary>
    public string LoaderType { get; set; } = "vanilla";

    /// <summary>
    /// 加载器版本（如 Forge 的 1.21.4-51.0.43）
    /// </summary>
    public string? LoaderVersion { get; set; }

    /// <summary>
    /// 游戏目录路径
    /// </summary>
    public string GameDirectory { get; set; } = "";
}

/// <summary>
/// 版本安装完成事件数据
/// </summary>
public class VersionInstalledEventArgs
{
    /// <summary>
    /// Minecraft 版本号
    /// </summary>
    public string McVersion { get; set; } = "";

    /// <summary>
    /// 安装后的版本名称
    /// </summary>
    public string VersionName { get; set; } = "";

    /// <summary>
    /// 加载器类型
    /// </summary>
    public string LoaderType { get; set; } = "vanilla";

    /// <summary>
    /// 加载器版本
    /// </summary>
    public string? LoaderVersion { get; set; }

    /// <summary>
    /// 游戏目录路径
    /// </summary>
    public string GameDirectory { get; set; } = "";

    /// <summary>
    /// 版本安装目录（完整路径）
    /// </summary>
    public string VersionDirectory { get; set; } = "";

    /// <summary>
    /// 是否安装成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 失败时的错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 账户变更事件数据
/// </summary>
public class AccountChangedEventArgs
{
    /// <summary>
    /// 变更类型
    /// </summary>
    public AccountChangeType ChangeType { get; set; }

    /// <summary>
    /// 受影响的账户ID
    /// </summary>
    public string AccountId { get; set; } = "";

    /// <summary>
    /// 账户用户名
    /// </summary>
    public string Username { get; set; } = "";

    /// <summary>
    /// 账户类型
    /// </summary>
    public string AccountType { get; set; } = "";
}

/// <summary>
/// 账户变更类型
/// </summary>
public enum AccountChangeType
{
    /// <summary>
    /// 默认账户切换
    /// </summary>
    Switched,

    /// <summary>
    /// 账户添加
    /// </summary>
    Added,

    /// <summary>
    /// 账户删除
    /// </summary>
    Removed,

    /// <summary>
    /// 账户信息更新
    /// </summary>
    Updated
}

/// <summary>
/// 下载进度事件数据
/// </summary>
public class DownloadProgressEventArgs
{
    /// <summary>
    /// 下载任务ID
    /// </summary>
    public string TaskId { get; set; } = "";

    /// <summary>
    /// 下载任务名称
    /// </summary>
    public string TaskName { get; set; } = "";

    /// <summary>
    /// 下载任务类型
    /// </summary>
    public string TaskType { get; set; } = "";

    /// <summary>
    /// 当前进度（0-100）
    /// </summary>
    public double Progress { get; set; }

    /// <summary>
    /// 状态消息
    /// </summary>
    public string? StatusMessage { get; set; }

    /// <summary>
    /// 下载速度（字节/秒）
    /// </summary>
    public double DownloadSpeed { get; set; }

    /// <summary>
    /// 下载状态
    /// </summary>
    public DownloadStatus Status { get; set; }
}

/// <summary>
/// 下载状态
/// </summary>
public enum DownloadStatus
{
    /// <summary>
    /// 下载中
    /// </summary>
    Downloading,

    /// <summary>
    /// 已完成
    /// </summary>
    Completed,

    /// <summary>
    /// 失败
    /// </summary>
    Failed,

    /// <summary>
    /// 已取消
    /// </summary>
    Cancelled
}
