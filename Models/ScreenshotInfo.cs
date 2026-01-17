using System;

namespace ObsMCLauncher.Models
{
    /// <summary>
    /// 游戏截图信息
    /// </summary>
    public class ScreenshotInfo
    {
        /// <summary>
        /// 截图文件名
        /// </summary>
        public string FileName { get; set; } = "";

        /// <summary>
        /// 完整路径
        /// </summary>
        public string FullPath { get; set; } = "";

        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedTime { get; set; }

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// 截图来源版本（如果来自版本隔离目录，否则为null）
        /// </summary>
        public string? VersionName { get; set; }

        /// <summary>
        /// 格式化的文件大小
        /// </summary>
        public string FormattedSize
        {
            get
            {
                if (Size < 1024) return $"{Size} B";
                if (Size < 1024 * 1024) return $"{Size / 1024.0:F2} KB";
                if (Size < 1024 * 1024 * 1024) return $"{Size / (1024.0 * 1024.0):F2} MB";
                return $"{Size / (1024.0 * 1024.0 * 1024.0):F2} GB";
            }
        }

        /// <summary>
        /// 格式化的创建时间
        /// </summary>
        public string FormattedCreatedTime
        {
            get
            {
                var now = DateTime.Now;
                var diff = now - CreatedTime;
                if (diff.TotalDays < 1)
                {
                    if (diff.TotalHours < 1)
                    {
                        if (diff.TotalMinutes < 1)
                            return "刚刚";
                        return $"{(int)diff.TotalMinutes}分钟前";
                    }
                    return $"{(int)diff.TotalHours}小时前";
                }
                if (diff.TotalDays < 7)
                    return $"{(int)diff.TotalDays}天前";
                return CreatedTime.ToString("yyyy-MM-dd");
            }
        }
    }
}

