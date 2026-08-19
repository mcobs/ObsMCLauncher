using System;
using System.IO;

namespace ObsMCLauncher.Core.Utils;

public enum VersionStatus
{
    Testing,
    Preview,
    PreRelease,
    Release
}

public static class VersionInfo
{
    /// <summary>
    /// 版本号：发布前由人工在此更新，格式为 semver（如 1.0.1-pre.1）
    /// </summary>
    public static readonly string Version = "1.1.0";

    public static readonly string CodeName = "GrassBlock";

    public static readonly VersionStatus Status = VersionStatus.Testing;

    public static readonly string ProductName = "ObsMCLauncher";

    public static readonly string FullProductName = "黑曜石MC启动器";

    public static readonly DateTime ReleaseDate = new DateTime(2026, 08, 15);

    public static string ShortVersion => Version;

    public static string UserAgent => $"{ProductName}/{Version}";

    public static string DisplayVersion
    {
        get
        {
            var statusText = Status switch
            {
                VersionStatus.Testing => "测试版",
                VersionStatus.Preview => "预览版",
                VersionStatus.PreRelease => "预发布版",
                VersionStatus.Release => "正式版",
                _ => "未知"
            };
            return $"{Version} ({statusText})";
        }
    }

    public static string VersionStatusText => Status switch
    {
        VersionStatus.Testing => "测试版",
        VersionStatus.Preview => "预览版",
        VersionStatus.PreRelease => "预发布版",
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

    /// <summary>
    /// 获取应用基础目录，用于定位OMCL等数据目录。
    /// Velopack部署模式下程序在 current/ 子目录运行。标准安装中 current 是
    /// junction/symlink，但解压部署或文件系统不支持 reparse point 时它是普通文件夹；
    /// 无论哪种情况 current 都会被更新流程整体替换。因此只要基目录名为 current
    /// 就一律向上退一层，避免数据目录（OMCL、.minecraft 等）落在会被覆盖的目录里。
    /// 便携模式和开发模式直接使用 BaseDirectory。
    /// </summary>
    public static string GetAppBaseDirectory()
    {
        return ResolveAppBaseDirectory(AppDomain.CurrentDomain.BaseDirectory);
    }

    /// <summary>
    /// 解析应用基础目录（纯函数，便于测试）。
    /// </summary>
    internal static string ResolveAppBaseDirectory(string baseDirectory)
    {
        var baseDir = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dirName = Path.GetFileName(baseDir);
        var parentDir = Path.GetDirectoryName(baseDir);

        if (dirName == "current" && parentDir != null)
        {
            // Velopack部署目录标记：current 可能是junction/symlink，也可能是普通文件夹，
            // 统一向上退一层，确保OMCL等数据目录落在 current 之外
            return parentDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        }

        return baseDir + Path.DirectorySeparatorChar;
    }
}
