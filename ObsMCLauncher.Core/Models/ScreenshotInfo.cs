using System;

namespace ObsMCLauncher.Core.Models;

public class ScreenshotInfo
{
    public string FileName { get; set; } = "";

    public string FullPath { get; set; } = "";

    public long Size { get; set; }

    public DateTime CreatedTime { get; set; }

    public DateTime LastModified { get; set; }

    public string? VersionName { get; set; }

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
