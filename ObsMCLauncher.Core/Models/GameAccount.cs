using System;

namespace ObsMCLauncher.Core.Models;

public enum AccountType
{
    Offline,
    Microsoft,
    Yggdrasil
}

public class GameAccount
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

    [System.Text.Json.Serialization.JsonIgnore]
    public object? Avatar { get; set; }

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
}
