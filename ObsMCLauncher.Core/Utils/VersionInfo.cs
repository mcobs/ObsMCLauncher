using System;

namespace ObsMCLauncher.Core.Utils;

public enum VersionStatus
{
    Testing,
    PreRelease,
    Release
}

public static class VersionInfo
{
    public static readonly string Version = "1.0.0";

    public static readonly string CodeName = "GrassBlock";

    public static readonly VersionStatus Status = VersionStatus.Testing;

    public static readonly string ProductName = "ObsMCLauncher";

    public static readonly string FullProductName = "黑曜石MC启动器";

    public static readonly DateTime ReleaseDate = new DateTime(2026, 01, 17);

    public static string ShortVersion => Version;

    public static string DisplayVersion
    {
        get
        {
            var statusText = Status switch
            {
                VersionStatus.Testing => "测试版",
                VersionStatus.PreRelease => "预发布版本",
                VersionStatus.Release => "正式版",
                _ => "未知"
            };
            return $"{Version} ({statusText})";
        }
    }

    public static string VersionStatusText => Status switch
    {
        VersionStatus.Testing => "测试版",
        VersionStatus.PreRelease => "预发布版本",
        VersionStatus.Release => "正式版",
        _ => "未知"
    };

    public static string Copyright => $"© {ReleaseDate.Year} {ProductName}";

    public static string GetDetailedVersionInfo()
    {
        return $@"
产品名称: {FullProductName}
版本号: {Version}
版本状态: {VersionStatusText}
版本代号: {CodeName}
发布日期: {ReleaseDate:yyyy-MM-dd}
            ".Trim();
    }
}
