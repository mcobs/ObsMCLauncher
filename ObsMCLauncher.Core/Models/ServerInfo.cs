using System;
using System.Text.Json.Serialization;

namespace ObsMCLauncher.Core.Models;

public class ServerInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public string Name { get; set; } = "";

    public string Address { get; set; } = "";

    public int Port { get; set; } = 25565;

    public string? IconPath { get; set; }

    public string? Description { get; set; }

    public string? Notes { get; set; }

    [JsonIgnore]
    public bool IsOnline { get; set; }

    [JsonIgnore]
    public int OnlinePlayers { get; set; }

    [JsonIgnore]
    public int MaxPlayers { get; set; }

    [JsonIgnore]
    public int Ping { get; set; }

    [JsonIgnore]
    public string? Version { get; set; }

    [JsonIgnore]
    public DateTime? LastPingTime { get; set; }

    [JsonIgnore]
    public string? Motd { get; set; }

    [JsonIgnore]
    public string FormattedVersion => string.IsNullOrEmpty(Version) ? "未知" : Version;

    [JsonIgnore]
    public string StatusText => IsOnline ? "在线" : "离线";

    [JsonIgnore]
    public string PingLevel
    {
        get
        {
            if (Ping <= 0) return "未知";
            if (Ping < 50) return "优秀";
            if (Ping < 100) return "良好";
            if (Ping < 200) return "一般";
            return "较差";
        }
    }

    [JsonIgnore]
    public string FormattedPing
    {
        get
        {
            if (Ping <= 0) return "未知";
            if (Ping < 50) return $"{Ping}ms (优秀)";
            if (Ping < 100) return $"{Ping}ms (良好)";
            if (Ping < 200) return $"{Ping}ms (一般)";
            return $"{Ping}ms (较差)";
        }
    }

    [JsonIgnore]
    public string FormattedPlayers
    {
        get
        {
            if (!IsOnline) return "离线";
            return $"{OnlinePlayers}/{MaxPlayers}";
        }
    }

    [JsonIgnore]
    public string FullAddress => Port == 25565 ? Address : $"{Address}:{Port}";
}
