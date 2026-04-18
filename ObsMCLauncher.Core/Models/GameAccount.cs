using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ObsMCLauncher.Core.Models;

public enum AccountType
{
    Offline,
    Microsoft,
    Yggdrasil
}

public class GameAccount : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Username { get; set; } = string.Empty;

    public AccountType Type { get; set; }

    public string? Email { get; set; }

    public string UUID { get; set; } = Guid.NewGuid().ToString("N");

    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime LastUsed { get; set; } = DateTime.Now;

    public string? AccessToken { get; set; }

    public string? RefreshToken { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string? MinecraftAccessToken { get; set; }

    public string? MinecraftUUID { get; set; }

    public string? YggdrasilServerId { get; set; }

    public string? YggdrasilAccessToken { get; set; }

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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
