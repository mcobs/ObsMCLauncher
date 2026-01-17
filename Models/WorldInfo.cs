using System;
using System.IO;

namespace ObsMCLauncher.Models
{
    /// <summary>
    /// 世界/存档信息
    /// </summary>
    public class WorldInfo
    {
        /// <summary>
        /// 世界名称（文件夹名称）
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 世界完整路径
        /// </summary>
        public string FullPath { get; set; } = "";

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime LastPlayed { get; set; }

        /// <summary>
        /// 世界大小（字节）
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// 缩略图路径（如果存在）
        /// </summary>
        public string? ThumbnailPath { get; set; }

        /// <summary>
        /// 游戏模式（从level.dat读取）
        /// </summary>
        public string? GameMode { get; set; }

        /// <summary>
        /// 难度（从level.dat读取）
        /// </summary>
        public string? Difficulty { get; set; }

        /// <summary>
        /// 种子（从level.dat读取）
        /// </summary>
        public long? Seed { get; set; }

        /// <summary>
        /// 游戏版本（从level.dat读取）
        /// </summary>
        public string? GameVersion { get; set; }

        /// <summary>
        /// 是否已启用（某些启动器支持禁用存档）
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 格式化的大小字符串
        /// </summary>
        public string FormattedSize
        {
            get
            {
                if (Size < 1024)
                    return $"{Size} B";
                if (Size < 1024 * 1024)
                    return $"{Size / 1024.0:F2} KB";
                if (Size < 1024 * 1024 * 1024)
                    return $"{Size / (1024.0 * 1024.0):F2} MB";
                return $"{Size / (1024.0 * 1024.0 * 1024.0):F2} GB";
            }
        }

        /// <summary>
        /// 格式化最后游玩时间
        /// </summary>
        public string FormattedLastPlayed
        {
            get
            {
                var now = DateTime.Now;
                var diff = now - LastPlayed;

                if (diff.TotalDays < 1)
                {
                    if (diff.TotalHours < 1)
                        return $"{(int)diff.TotalMinutes} 分钟前";
                    return $"{(int)diff.TotalHours} 小时前";
                }
                if (diff.TotalDays < 7)
                    return $"{(int)diff.TotalDays} 天前";
                if (diff.TotalDays < 30)
                    return $"{(int)(diff.TotalDays / 7)} 周前";
                if (diff.TotalDays < 365)
                    return $"{(int)(diff.TotalDays / 30)} 个月前";
                return LastPlayed.ToString("yyyy-MM-dd");
            }
        }
    }
}

