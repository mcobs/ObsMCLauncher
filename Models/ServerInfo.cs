using System;
using System.Text.Json.Serialization;

namespace ObsMCLauncher.Models
{
    /// <summary>
    /// 服务器信息
    /// </summary>
    public class ServerInfo
    {
        /// <summary>
        /// 服务器名称
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 服务器地址（IP或域名）
        /// </summary>
        public string Address { get; set; } = "";

        /// <summary>
        /// 服务器端口（默认25565）
        /// </summary>
        public int Port { get; set; } = 25565;

        /// <summary>
        /// 服务器图标路径（可选）
        /// </summary>
        public string? IconPath { get; set; }

        /// <summary>
        /// 服务器描述
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 服务器分组
        /// </summary>
        public string? Group { get; set; }

        /// <summary>
        /// 是否在线（需要ping检测）
        /// </summary>
        [JsonIgnore]
        public bool IsOnline { get; set; }

        /// <summary>
        /// 在线玩家数
        /// </summary>
        [JsonIgnore]
        public int OnlinePlayers { get; set; }

        /// <summary>
        /// 最大玩家数
        /// </summary>
        [JsonIgnore]
        public int MaxPlayers { get; set; }

        /// <summary>
        /// 延迟（毫秒）
        /// </summary>
        [JsonIgnore]
        public int Ping { get; set; }

        /// <summary>
        /// 服务器版本（如果可用）
        /// </summary>
        [JsonIgnore]
        public string? Version { get; set; }

        /// <summary>
        /// 最后ping时间
        /// </summary>
        [JsonIgnore]
        public DateTime? LastPingTime { get; set; }

        /// <summary>
        /// 格式化的延迟显示
        /// </summary>
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

        /// <summary>
        /// 格式化的玩家数显示
        /// </summary>
        [JsonIgnore]
        public string FormattedPlayers
        {
            get
            {
                if (!IsOnline) return "离线";
                return $"{OnlinePlayers}/{MaxPlayers}";
            }
        }

        /// <summary>
        /// 完整的服务器地址（包含端口）
        /// </summary>
        [JsonIgnore]
        public string FullAddress => Port == 25565 ? Address : $"{Address}:{Port}";
    }
}

