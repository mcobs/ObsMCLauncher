using System;

namespace ObsMCLauncher.Core.Models
{
    /// <summary>
    /// Yggdrasil 外置登录服务器配置
    /// </summary>
    public class YggdrasilServer
    {
        /// <summary>
        /// 服务器唯一标识
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 服务器名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 服务器 API 地址（完整URL或简化地址）
        /// 例如：https://littleskin.cn/api/yggdrasil 或 littleskin.cn
        /// </summary>
        public string ApiUrl { get; set; } = string.Empty;

        /// <summary>
        /// 是否为内置服务器
        /// </summary>
        public bool IsBuiltIn { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后使用时间
        /// </summary>
        public DateTime LastUsed { get; set; } = DateTime.Now;

        /// <summary>
        /// 获取完整的 API 地址
        /// 支持 ALI（API 地址指示）功能，自动补全协议和路径
        /// </summary>
        public string GetFullApiUrl()
        {
            var url = ApiUrl.Trim();

            // 如果已经是完整URL，直接返回
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            // 支持 ALI 简化地址（如 littleskin.cn）
            // 自动添加 https:// 和 /api/yggdrasil
            if (!url.Contains("/"))
            {
                return $"https://{url}/api/yggdrasil";
            }

            // 如果有路径但没有协议，添加 https://
            return $"https://{url}";
        }

        /// <summary>
        /// 验证服务器配置是否有效
        /// </summary>
        public bool IsValid()
        {
            if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(ApiUrl))
            {
                return false;
            }

            try
            {
                var fullUrl = GetFullApiUrl();
                var uri = new Uri(fullUrl);
                return uri.Scheme == "http" || uri.Scheme == "https";
            }
            catch
            {
                return false;
            }
        }
    }
}
