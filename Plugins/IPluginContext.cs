using System;
using System.Windows.Controls;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Plugins
{
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
        /// 通知管理器
        /// </summary>
        NotificationManager NotificationManager { get; }
        
        /// <summary>
        /// 对话框管理器
        /// </summary>
        DialogManager DialogManager { get; }
        
        /// <summary>
        /// 注册插件标签页（显示在"更多"页面）
        /// </summary>
        /// <param name="title">标签页标题</param>
        /// <param name="content">标签页内容（WPF Page或UserControl）</param>
        /// <param name="icon">图标名称（MaterialDesign图标）</param>
        void RegisterTab(string title, object content, string? icon = null);
        
        /// <summary>
        /// 订阅事件
        /// </summary>
        /// <param name="eventName">事件名称</param>
        /// <param name="handler">事件处理器</param>
        void SubscribeEvent(string eventName, Action<object> handler);
        
        /// <summary>
        /// 发布事件
        /// </summary>
        /// <param name="eventName">事件名称</param>
        /// <param name="eventData">事件数据</param>
        void PublishEvent(string eventName, object eventData);
        
        /// <summary>
        /// 注册主页卡片
        /// </summary>
        /// <param name="cardId">卡片唯一标识符</param>
        /// <param name="title">卡片标题</param>
        /// <param name="description">卡片描述</param>
        /// <param name="content">卡片内容（WPF UIElement，如Grid、StackPanel等）</param>
        /// <param name="icon">图标名称（MaterialDesign图标，可选）</param>
        /// <param name="onClick">点击事件处理器（可选）</param>
        void RegisterHomeCard(string cardId, string title, string description, 
                              System.Windows.UIElement content, string? icon = null, Action? onClick = null);
        
        /// <summary>
        /// 注销主页卡片
        /// </summary>
        /// <param name="cardId">卡片唯一标识符</param>
        void UnregisterHomeCard(string cardId);
    }
}

