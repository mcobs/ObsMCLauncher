using System;

namespace ObsMCLauncher.Models
{
    /// <summary>
    /// 游戏账号类型
    /// </summary>
    public enum AccountType
    {
        Offline,    // 离线账户
        Microsoft,  // 微软账户
        Yggdrasil   // 外置登录账户
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

    // ========== 微软账户专用字段 ==========
    
    /// <summary>
    /// 访问令牌（微软账户）
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// 刷新令牌（微软账户）
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// 访问令牌过期时间（微软账户）
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Minecraft 访问令牌
    /// </summary>
    public string? MinecraftAccessToken { get; set; }

    /// <summary>
    /// Minecraft UUID（正版账户的真实UUID）
    /// </summary>
    public string? MinecraftUUID { get; set; }

    // ========== Yggdrasil 外置登录专用字段 ==========

    /// <summary>
    /// Yggdrasil 服务器 ID（外置登录）
    /// </summary>
    public string? YggdrasilServerId { get; set; }

    /// <summary>
    /// Yggdrasil 访问令牌（外置登录）
    /// </summary>
    public string? YggdrasilAccessToken { get; set; }

    /// <summary>
    /// Yggdrasil 客户端令牌（外置登录）
    /// </summary>
    public string? YggdrasilClientToken { get; set; }

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
                AccountType.Yggdrasil => $"{Username} (外置)",
                _ => Username
            };
        }
    }

    /// <summary>
    /// 检查访问令牌是否已过期
    /// </summary>
    public bool IsTokenExpired()
    {
        if (Type == AccountType.Offline) return false;
        if (ExpiresAt == null) return true;
        return DateTime.Now >= ExpiresAt.Value.AddMinutes(-5); // 提前5分钟刷新
    }
}
}

