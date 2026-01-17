using System.Collections.Generic;

namespace ObsMCLauncher.Models
{
    /// <summary>
    /// 主页卡片配置（用于保存用户设置）
    /// </summary>
    public class HomeCardConfig
    {
        /// <summary>
        /// 卡片唯一标识符
        /// </summary>
        public string CardId { get; set; } = string.Empty;
        
        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; } = true;
        
        /// <summary>
        /// 排序顺序（数字越小越靠前）
        /// </summary>
        public int Order { get; set; } = 0;
    }
}

