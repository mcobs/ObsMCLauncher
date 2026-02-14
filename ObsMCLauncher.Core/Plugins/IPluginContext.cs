using System;

namespace ObsMCLauncher.Core.Plugins;

/// <summary>
/// 插件上下文接口
/// 提供插件访问启动器功能的API
/// </summary>
public interface IPluginContext
{
    /// <summary>
    /// 获取启动器版本信息
    /// </summary>
    string LauncherVersion { get; }

    /// <summary>
    /// 获取插件数据目录（用于保存配置和数据）
    /// </summary>
    string PluginDataDirectory { get; }

    /// <summary>
    /// 注册插件标签页（显示在"更多"页面）
    /// </summary>
    /// <param name="title">标签页标题</param>
    /// <param name="tabId">标签页唯一标识符</param>
    /// <param name="icon">图标名称（可选）</param>
    /// <param name="payload">自定义数据（可选）</param>
    void RegisterTab(string title, string tabId, string? icon = null, object? payload = null);

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
}
