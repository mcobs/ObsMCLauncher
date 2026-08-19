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

        /// <summary>下载进度更新</summary>
        public const string DownloadProgress = "DownloadProgress";
    }
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
    /// <param name="durationSeconds">持续时间（秒），null表示无限，默认3秒</param>
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
