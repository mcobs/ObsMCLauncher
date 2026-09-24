using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Plugins;

/// <summary>
/// 插件上下文接口
/// 提供插件访问启动器功能的API
/// </summary>
public interface IPluginContext
{
    /// <summary>
    /// 全局事件名称常量
    /// </summary>
    public static class EventNames
    {
        /// <summary>游戏启动</summary>
        public const string GameLaunched = "GameLaunched";

        /// <summary>游戏关闭</summary>
        public const string GameClosed = "GameClosed";

        /// <summary>版本下载完成</summary>
        public const string VersionDownloaded = "VersionDownloaded";

        /// <summary>版本安装开始</summary>
        public const string VersionInstalling = "VersionInstalling";

        /// <summary>版本安装完成</summary>
        public const string VersionInstalled = "VersionInstalled";

        /// <summary>账户变更</summary>
        public const string AccountChanged = "AccountChanged";

        /// <summary>
        /// 用户切换了选中的版本（主页版本下拉栏 / 版本管理页）。
        /// 事件数据为 <see cref="VersionSelectedEventArgs"/>；选中项没有真正变化时不触发。
        /// </summary>
        public const string VersionSelected = "VersionSelected";

        /// <summary>下载进度更新</summary>
        public const string DownloadProgress = "DownloadProgress";

        /// <summary>
        /// 崩溃已确认（进程退出码非 0）。
        /// 事件数据为 <see cref="PluginCrashReportInfo"/>：此时启动器已经等过报告落盘，
        /// <see cref="PluginCrashReportInfo.ReportFound"/> 明确告诉插件有没有找到报告文件。
        /// </summary>
        public const string CrashDetected = "CrashDetected";
    }

    /// <summary>
    /// 插件 API 版本 = 启动器版本去掉预发布 / 构建后缀（1.1.0-beta.1 → "1.1.0"）。
    /// 主版本递进意味着插件 API 可能有破坏性变更；小版本只新增、不改动。
    /// 需要完整版本字符串（含预发布标识）请用 <see cref="LauncherVersion"/>。
    /// </summary>
    string ApiVersion { get; }

    /// <summary>
    /// 获取崩溃报告列表（只读快照），按时间倒序。
    /// </summary>
    /// <param name="versionId">限定版本；传 null 表示所有版本（含游戏主目录）</param>
    IReadOnlyList<PluginCrashReportInfo> GetCrashReports(string? versionId = null);

    /// <summary>
    /// 用启动器内置规则引擎分析一份崩溃报告。
    /// 同步、纯本地、无网络；报告不存在或无法解析时返回 null。
    /// </summary>
    /// <param name="reportPath">报告文件的完整路径</param>
    PluginCrashAnalysis? AnalyzeCrashReport(string reportPath);

    /// <summary>
    /// 读取报告原文（用于交给自己的模型/服务分析）。
    /// </summary>
    /// <param name="reportPath">报告文件的完整路径</param>
    /// <param name="maxChars">最多返回的字符数，超出截断；上限 2,000,000</param>
    /// <param name="sanitize">是否脱敏（默认 true）：抹掉用户名与 accessToken 等隐私字段</param>
    /// <returns>文件不存在时返回 null</returns>
    string? ReadCrashReportText(string reportPath, int maxChars = 200_000, bool sanitize = true);

    /// <summary>
    /// 手动对一段文本做脱敏（发送到外部服务前建议再过一遍）。
    /// </summary>
    string SanitizeCrashReportText(string text);

    /// <summary>
    /// 取"用户当前正在看的崩溃上下文"（崩溃弹窗 / 崩溃分析页当前选中的报告）。
    /// 槽位里的插件控件也会拿到同样内容的 DataContext；无上下文时返回 null。
    /// </summary>
    PluginSlotContext? GetActiveCrashContext();

    /// <summary>
    /// 把自绘控件注册进启动器的具名槽位。
    /// 槽位 id 是**开放字符串**：<see cref="PluginSlotRegistry.KnownSlots"/> 只是"启动器保证有宿主容器"的那几个，
    /// 其它 id 也能登记，只是要等该 id 的宿主出现才会渲染。
    /// 想完全自己决定挂在哪、改什么，请用 <see cref="GetUiRoot"/> / <see cref="TryFindControlByName"/>。
    /// </summary>
    /// <param name="slotId">槽位标识</param>
    /// <param name="itemId">插件内唯一的内容标识；同 id 重复注册视为更新</param>
    /// <param name="content">Avalonia 控件实例</param>
    /// <param name="order">排序值，小的在前</param>
    bool AddSlotContent(string slotId, string itemId, object content, int order = 0);

    /// <summary>移除自己注册的一条槽位内容</summary>
    bool RemoveSlotContent(string slotId, string itemId);

    /// <summary>清空本插件在某个槽位里的全部内容</summary>
    void ClearSlotContent(string slotId);

    /// <summary>当前可用的槽位 id 列表</summary>
    IReadOnlyList<string> GetSlotIds();

    /// <summary>
    /// 取槽位宿主容器（Avalonia Panel），拿到后可自行增删改其中的控件——
    /// 容器里既有插件内容也有启动器自己的控件，所以这也是"直接改启动器 UI"的入口。
    /// 槽位当前没有挂载（页面未打开）时返回 null；页面挂载/卸载会变化，插件应容错重试。
    /// </summary>
    object? GetSlotHost(string slotId);

    /// <summary>
    /// 取 UI 根（启动器主窗口，Avalonia Window）。
    /// 这是**不受槽位限制**的入口：拿到后插件可自己遍历视觉树，往任意容器增删控件、改任意控件属性——
    /// 也就是"直接修改页面组件"的完全形态，不需要启动器预先开任何槽位。
    /// 代价是页面结构会随版本变化，兼容风险由插件自己承担。
    /// </summary>
    object? GetUiRoot();

    /// <summary>
    /// 按控件名（XAML 的 x:Name / Name）在已打开的窗口里查找控件。
    /// 适合"我知道要找哪个控件"的场景，比自己遍历树稳一点（名字由启动器维护）。
    /// 找不到或窗口没打开时返回 null。
    /// </summary>
    object? TryFindControlByName(string name);

    /// <summary>把回调调度到 UI 线程执行；已在 UI 线程时直接执行</summary>
    void RunOnUiThread(Action action);
    /// <summary>
    /// 获取启动器版本信息
    /// </summary>
    string LauncherVersion { get; }

    /// <summary>
    /// 获取插件数据目录（用于保存配置和数据）
    /// </summary>
    string PluginDataDirectory { get; }

    /// <summary>
    /// 获取启动器基础目录（Velopack 部署模式下已自动定位到 current 的父级，
    /// 不会落在会被更新整体替换的 current 目录内）
    /// </summary>
    string LauncherBaseDirectory { get; }

    /// <summary>
    /// 获取启动器数据目录（基础目录下的 OMCL 文件夹，存放配置/账户/缓存等）
    /// </summary>
    string LauncherDataDirectory { get; }

    /// <summary>
    /// 获取当前激活的游戏目录（.minecraft 根目录，随设置中切换的目录实时变化）
    /// </summary>
    string GameDirectory { get; }

    /// <summary>
    /// 注册插件标签页（显示在"更多"页面）
    /// </summary>
    /// <param name="title">标签页标题</param>
    /// <param name="tabId">标签页唯一标识符</param>
    /// <param name="icon">图标名称（可选）</param>
    /// <param name="payload">自定义数据（可选）</param>
    void RegisterTab(string title, string tabId, string? icon = null, object? payload = null);

    /// <summary>
    /// 注册带自定义UI内容的插件标签页
    /// </summary>
    /// <param name="title">标签页标题</param>
    /// <param name="tabId">标签页唯一标识符</param>
    /// <param name="customContent">自定义UI控件（Avalonia UserControl 实例）</param>
    /// <param name="icon">图标名称（可选）</param>
    /// <param name="payload">自定义数据（可选）</param>
    void RegisterTab(string title, string tabId, object? customContent, string? icon = null, object? payload = null);

    /// <summary>
    /// 注销插件标签页
    /// </summary>
    /// <param name="tabId">标签页唯一标识符</param>
    void UnregisterTab(string tabId);

    /// <summary>
    /// 订阅事件
    /// </summary>
    /// <param name="eventName">事件名称</param>
    /// <param name="handler">事件处理器</param>
    void SubscribeEvent(string eventName, Action<object?> handler);

    /// <summary>
    /// 退订事件
    /// </summary>
    /// <param name="eventName">事件名称</param>
    /// <param name="handler">事件处理器（需与订阅时相同引用）</param>
    void UnsubscribeEvent(string eventName, Action<object?> handler);

    /// <summary>
    /// 发布事件
    /// </summary>
    /// <param name="eventName">事件名称</param>
    /// <param name="eventData">事件数据</param>
    void PublishEvent(string eventName, object? eventData);

    /// <summary>
    /// 注册主页卡片
    /// </summary>
    /// <param name="cardId">卡片唯一标识符</param>
    /// <param name="title">卡片标题</param>
    /// <param name="description">卡片描述</param>
    /// <param name="icon">图标名称（可选）</param>
    /// <param name="commandId">点击触发的命令ID（可选）</param>
    /// <param name="payload">自定义数据（可选）</param>
    void RegisterHomeCard(
        string cardId,
        string title,
        string description,
        string? icon = null,
        string? commandId = null,
        object? payload = null);

    /// <summary>
    /// 注册主页卡片（可指定默认尺寸档位）
    /// </summary>
    /// <param name="cardId">卡片唯一标识符</param>
    /// <param name="title">卡片标题</param>
    /// <param name="description">卡片描述</param>
    /// <param name="icon">图标名称（可选）</param>
    /// <param name="commandId">点击触发的命令ID（可选）</param>
    /// <param name="payload">自定义数据（可选）</param>
    /// <param name="defaultSize">默认尺寸档位（用户在主页自定义中可再调整）</param>
    void RegisterHomeCard(
        string cardId,
        string title,
        string description,
        string? icon,
        string? commandId,
        object? payload,
        HomeCardSize defaultSize);

    /// <summary>
    /// 注销主页卡片
    /// </summary>
    /// <param name="cardId">卡片唯一标识符</param>
    void UnregisterHomeCard(string cardId);

    /// <summary>
    /// 显示通知
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">消息内容</param>
    /// <param name="type">通知类型：info, success, warning, error, progress</param>
    /// <param name="durationSeconds">
    /// 持续时间（秒）：null / 省略时按类型取默认时长（进度类默认不自动关闭，其余约 3–5 秒并尊重用户设置）；
    /// 传 0 或负数表示不自动关闭，需调用 <see cref="CloseNotification"/> 手动关闭。
    /// </param>
    /// <returns>通知ID，用于更新或关闭</returns>
    string ShowNotification(string title, string message, string type = "info", int? durationSeconds = null);

    /// <summary>
    /// 更新通知内容
    /// </summary>
    /// <param name="notificationId">通知ID</param>
    /// <param name="message">新消息内容</param>
    /// <param name="progress">进度（0-100），仅progress类型有效</param>
    void UpdateNotification(string notificationId, string message, double? progress = null);

    /// <summary>
    /// 关闭通知
    /// </summary>
    /// <param name="notificationId">通知ID</param>
    void CloseNotification(string notificationId);

    /// <summary>
    /// 注册自定义命令，主页卡片点击 command:{commandId} 时执行
    /// </summary>
    /// <param name="commandId">命令ID（在插件内唯一）</param>
    /// <param name="handler">命令执行回调</param>
    void RegisterCommand(string commandId, Action<object?> handler);

    /// <summary>
    /// 注销自定义命令
    /// </summary>
    /// <param name="commandId">命令ID</param>
    void UnregisterCommand(string commandId);

    /// <summary>
    /// 写入启动器统一日志（与启动器自身日志同源，便于排查插件问题）
    /// </summary>
    /// <param name="level">日志级别</param>
    /// <param name="message">日志消息</param>
    void LogMessage(PluginLogLevel level, string message);

    /// <summary>
    /// 获取启动器中已安装的 Minecraft 版本列表（只读快照）
    /// </summary>
    /// <returns>版本信息只读列表；无任何版本时返回空列表</returns>
    IReadOnlyList<PluginVersionInfo> GetInstalledVersions();

    /// <summary>
    /// 获取当前默认/选中的账户信息（不含任何令牌字段）
    /// </summary>
    /// <returns>账户精简信息；未选中账户时返回 null</returns>
    PluginAccountInfo? GetCurrentAccount();

    /// <summary>
    /// 获取用户当前选中的版本（主页版本下拉栏里选中的那个）。
    /// 只读启动器配置与该版本的版本 JSON，不扫描整个版本目录、也不改写任何文件。
    /// </summary>
    /// <returns>版本精简信息；未选中版本或该版本已被删除时返回 null</returns>
    PluginVersionInfo? GetSelectedVersion();

    /// <summary>
    /// 获取启动器中的全部账户（只读快照，不含任何令牌字段；默认账户看 <see cref="PluginAccountInfo.IsDefault"/>）
    /// </summary>
    IReadOnlyList<PluginAccountInfo> GetAccounts();

    /// <summary>
    /// 获取游戏进程运行状态（是否在运行、运行的是哪个版本、PID 与启动时间）。
    /// 启动器没在管进程（如已随游戏退出）时按未运行返回。
    /// </summary>
    PluginGameStatus GetGameStatus();

    /// <summary>
    /// 获取启动设置快照（内存、JVM 参数、Java 路径、游戏目录、启动后是否关闭启动器）
    /// </summary>
    PluginLaunchSettings GetLaunchSettings();

    /// <summary>
    /// 获取指定版本的运行目录（已套用版本隔离规则：隔离版本是 <c>versions/{版本ID}</c>，否则是游戏根目录）。
    /// 该版本的 mods / resourcepacks / shaderpacks / saves 都在它下面。
    /// </summary>
    /// <param name="versionId">版本ID（版本文件夹名）</param>
    /// <returns>运行目录完整路径；versionId 为空时返回空字符串，不校验版本是否存在</returns>
    string GetVersionRunDirectory(string versionId);

    /// <summary>
    /// 获取当前的所有下载任务（只读快照，含任务名、类型、状态与进度）
    /// </summary>
    IReadOnlyList<PluginDownloadTaskStatus> GetDownloadTasks();

    /// <summary>
    /// 注册游戏启动生命周期钩子，在指定阶段被回调
    /// </summary>
    /// <param name="hookId">钩子唯一标识（插件内唯一）</param>
    /// <param name="phase">触发阶段</param>
    /// <param name="handler">回调；BeforeLaunch 阶段可通过 ctx.CancelLaunch 中止启动</param>
    void RegisterGameLaunchHook(string hookId, GameLaunchPhase phase, Action<GameLaunchHookContext> handler);

    /// <summary>
    /// 注销启动生命周期钩子
    /// </summary>
    /// <param name="hookId">钩子唯一标识</param>
    void UnregisterGameLaunchHook(string hookId);

    /// <summary>
    /// 提交下载请求给启动器下载管理器统一调度
    /// </summary>
    /// <param name="request">下载请求（URL/目标目录/文件名/SHA-1 可选）</param>
    /// <returns>任务 ID；URL/目录非法或被拒绝时返回空字符串</returns>
    string RequestDownload(PluginDownloadRequest request);

    /// <summary>
    /// 查询指定下载任务的状态（用于轮询 RequestDownload 返回的任务进度）
    /// </summary>
    /// <param name="taskId">任务 ID（由 RequestDownload 返回）</param>
    /// <returns>任务精简状态；任务不存在时返回 null</returns>
    PluginDownloadTaskStatus? GetDownloadTaskStatus(string taskId);

    /// <summary>
    /// 读取插件自己的配置文件（存于插件数据目录下的 config.json）
    /// </summary>
    /// <typeparam name="T">配置类型</typeparam>
    /// <returns>反序列化后的配置；文件不存在或解析失败返回 default</returns>
    T? GetConfig<T>();

    /// <summary>
    /// 写入插件自己的配置文件（存于插件数据目录下的 config.json）
    /// </summary>
    /// <typeparam name="T">配置类型</typeparam>
    /// <param name="config">要保存的配置对象</param>
    void SaveConfig<T>(T config);

    /// <summary>
    /// 使用系统默认浏览器打开外部链接
    /// </summary>
    /// <param name="url">http/https 链接</param>
    /// <returns>是否成功打开</returns>
    bool OpenUrl(string url);

    /// <summary>
    /// 跳转到启动器内部页面（multiplier/resources/accounts/versions/settings/more/home）
    /// </summary>
    /// <param name="page">目标页面标识</param>
    void NavigateTo(string page);

    /// <summary>
    /// 注册异步游戏启动生命周期钩子（若回调需执行耗时/网络操作，请使用此异步版本）
    /// </summary>
    void RegisterGameLaunchHookAsync(string hookId, GameLaunchPhase phase, Func<GameLaunchHookContext, Task> handler);

    /// <summary>
    /// 注销异步启动生命周期钩子（同时注销同名同步钩子）
    /// </summary>
    void UnregisterGameLaunchHookAsync(string hookId);
}
