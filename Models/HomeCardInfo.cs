using System;
using System.Windows;
using System.Windows.Media;

namespace ObsMCLauncher.Models
{
    /// <summary>
    /// 主页卡片信息
    /// </summary>
    public class HomeCardInfo
    {
        /// <summary>
        /// 卡片唯一标识符
        /// </summary>
        public string CardId { get; set; } = string.Empty;
        
        /// <summary>
        /// 卡片标题
        /// </summary>
        public string Title { get; set; } = string.Empty;
        
        /// <summary>
        /// 卡片描述
        /// </summary>
        public string Description { get; set; } = string.Empty;
        
        /// <summary>
        /// 卡片内容（UIElement）
        /// </summary>
        public UIElement? Content { get; set; }
        
        /// <summary>
        /// 图标名称（MaterialDesign图标）
        /// </summary>
        public string? Icon { get; set; }
        
        /// <summary>
        /// 点击事件处理器
        /// </summary>
        public Action? OnClick { get; set; }
        
        /// <summary>
        /// 是否为插件卡片
        /// </summary>
        public bool IsPluginCard { get; set; }
        
        /// <summary>
        /// 插件ID（如果是插件卡片）
        /// </summary>
        public string? PluginId { get; set; }
    }
}

