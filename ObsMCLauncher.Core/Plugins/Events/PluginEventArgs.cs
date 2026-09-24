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
/// 版本选中变更事件数据（用户切换主页/版本管理页里选中的版本）。
/// 只有选中项真正变化时才触发；版本详情可通过 IPluginContext.GetSelectedVersion() 查询。
/// </summary>
public class VersionSelectedEventArgs
{
    /// <summary>
    /// 新选中的版本ID
    /// </summary>
    public string VersionId { get; set; } = "";

    /// <summary>
    /// 切换前选中的版本ID；此前没有选中过任何版本时为空
    /// </summary>
    public string PreviousVersionId { get; set; } = "";
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

/// <summary>
/// 主界面就绪事件数据（<c>UiReady</c>）。
///
/// 触发时机：主窗口已打开、首页已进入视觉树——这是 <c>IPluginContext.GetUiRoot()</c>
/// 第一次能拿到非 null 主窗口的时刻。插件在 <c>OnLoad</c> 里拿不到窗口是正常的：
/// 插件加载发生在 MainWindowViewModel 构造期间，那时窗口还没创建。
///
/// 每个进程只广播一次；错过广播的插件（例如启动后才被启用的插件）用
/// <c>IPluginContext.IsUiReady</c> 判断当前是否已经就绪，再补做一次即可。
/// </summary>
public class PluginUiReadyEventArgs
{
    /// <summary>
    /// UI 根（Avalonia <c>Window</c>），与 <c>IPluginContext.GetUiRoot()</c> 返回同一实例。
    /// 直接下发是为了省掉一次查询和竞态：插件里 `is Window root` 判断后再用。
    /// Core 不引用 Avalonia，所以这里声明为 <see cref="object"/>。
    /// </summary>
    public object? Root { get; set; }
}
