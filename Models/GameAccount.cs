using System;

namespace ObsMCLauncher.Models
{
    /// <summary>
    /// 游戏账号类型
    /// </summary>
    public enum AccountType
    {
        Offline,    // 离线账户
        Microsoft   // 微软账户
    }

    /// <summary>
    /// 游戏账号模型
    /// </summary>
    public class GameAccount
    {
        /// <summary>
        /// 账号唯一标识
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// 账号类型
        /// </summary>
        public AccountType Type { get; set; }

        /// <summary>
        /// 邮箱（微软账户）
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// UUID（游戏内唯一标识）
        /// </summary>
        public string UUID { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 是否为默认账号
        /// </summary>
        public bool IsDefault { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后使用时间
        /// </summary>
        public DateTime LastUsed { get; set; } = DateTime.Now;

        /// <summary>
        /// 获取显示名称
        /// </summary>
        public string DisplayName
        {
            get
            {
                return Type switch
                {
                    AccountType.Offline => $"{Username} (离线)",
                    AccountType.Microsoft => $"{Username} (微软)",
                    _ => Username
                };
            }
        }
    }
}

