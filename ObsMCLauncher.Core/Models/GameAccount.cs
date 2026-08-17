using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ObsMCLauncher.Core.Security;

namespace ObsMCLauncher.Core.Models;

public enum AccountType
{
    Offline,
    Microsoft,
    Yggdrasil
}

/// <summary>
/// 账号令牌状态（用于界面展示令牌过期提示）。
/// </summary>
public enum TokenState
{
    /// <summary>离线账号，无令牌</summary>
    None,

    /// <summary>令牌有效</summary>
    Valid,

    /// <summary>即将过期（7 天内）</summary>
    ExpiringSoon,

    /// <summary>已过期</summary>
    Expired,

    /// <summary>无过期时间，状态未知</summary>
    Unknown
}

public class GameAccount : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    private string _username = string.Empty;

    public string Username
    {
        get => _username;
        set
        {
            if (_username != value)
            {
                _username = value;
                OnPropertyChanged();
            }
        }
    }

    public AccountType Type { get; set; }

    private string? _email;

    public string? Email
    {
        get => _email;
        set
        {
            if (_email != value)
            {
                _email = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DetailText));
            }
        }
    }

    public string UUID { get; set; } = Guid.NewGuid().ToString("N");

    private bool _isDefault;

    public bool IsDefault
    {
        get => _isDefault;
        set
        {
            if (_isDefault != value)
            {
                _isDefault = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    private DateTime _lastUsed = DateTime.Now;

    public DateTime LastUsed
    {
        get => _lastUsed;
        set
        {
            if (_lastUsed != value)
            {
                _lastUsed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LastUsedText));
                OnPropertyChanged(nameof(DetailText));
            }
        }
    }

    [Sensitive]
    public string? AccessToken { get; set; }

    [Sensitive]
    public string? RefreshToken { get; set; }

    private DateTime? _expiresAt;

    public DateTime? ExpiresAt
    {
        get => _expiresAt;
        set
        {
            if (_expiresAt != value)
            {
                _expiresAt = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TokenState));
                OnPropertyChanged(nameof(TokenStatusText));
                OnPropertyChanged(nameof(HasTokenStatus));
            }
        }
    }

    [Sensitive]
    public string? MinecraftAccessToken { get; set; }

    public string? MinecraftUUID { get; set; }

    public string? YggdrasilServerId { get; set; }

    [Sensitive]
    public string? YggdrasilAccessToken { get; set; }

    [Sensitive]
    public string? YggdrasilClientToken { get; set; }

    public string? SkinUrl { get; set; }

    public string? CachedSkinPath { get; set; }

    public DateTime? SkinLastUpdated { get; set; }

    private object? _avatar;

    [System.Text.Json.Serialization.JsonIgnore]
    public object? Avatar
    {
        get => _avatar;
        set
        {
            if (_avatar != value)
            {
                _avatar = value;
                OnPropertyChanged();
            }
        }
    }

    public string DisplayName => Type switch
    {
        AccountType.Offline => $"{Username} (离线)",
        AccountType.Microsoft => $"{Username} (微软)",
        AccountType.Yggdrasil => $"{Username} (外置)",
        _ => Username
    };

    public bool IsTokenExpired()
    {
        if (Type == AccountType.Offline) return false;
        if (ExpiresAt == null) return true;
        return DateTime.Now >= ExpiresAt.Value.AddMinutes(-5);
    }

    // ===== 以下为界面展示辅助属性（不参与持久化） =====

    /// <summary>外置登录服务器显示名（由 ViewModel 加载时填充）</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ServerName { get; set; }

    /// <summary>令牌状态</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TokenState TokenState
    {
        get
        {
            if (Type == AccountType.Offline) return TokenState.None;
            if (ExpiresAt == null) return TokenState.Unknown;
            if (DateTime.Now >= ExpiresAt.Value) return TokenState.Expired;
            if (ExpiresAt.Value - DateTime.Now < TimeSpan.FromDays(7)) return TokenState.ExpiringSoon;
            return TokenState.Valid;
        }
    }

    /// <summary>令牌状态文案（无警示时不显示）</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string TokenStatusText => TokenState switch
    {
        TokenState.Expired => "令牌已过期",
        TokenState.ExpiringSoon => $"令牌将于 {ExpiresAt:MM-dd HH:mm} 过期",
        TokenState.Unknown => "令牌状态未知",
        _ => string.Empty
    };

    /// <summary>是否显示令牌状态提示（仅警示状态显示）</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasTokenStatus => TokenState is TokenState.ExpiringSoon or TokenState.Expired or TokenState.Unknown;

    /// <summary>最近使用时间文案</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string LastUsedText => LastUsed == default ? string.Empty : $"最近使用：{LastUsed:yyyy-MM-dd HH:mm}";

    /// <summary>详情行文案（最近使用 / 邮箱 / 认证服务器）</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DetailText
    {
        get
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrEmpty(LastUsedText)) parts.Add(LastUsedText);
            if (Type == AccountType.Microsoft && !string.IsNullOrEmpty(Email)) parts.Add($"邮箱：{Email}");
            if (Type == AccountType.Yggdrasil && !string.IsNullOrEmpty(ServerName)) parts.Add($"服务器：{ServerName}");
            return string.Join(" · ", parts);
        }
    }

    private bool _isHighlighted;

    /// <summary>是否高亮（新增账号后短暂高亮引导）</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (_isHighlighted != value)
            {
                _isHighlighted = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
