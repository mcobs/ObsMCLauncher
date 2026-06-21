using System;
using System.IO;
using System.Reflection;

namespace ObsMCLauncher.Core.Utils;

public enum VersionStatus
{
    Testing,
    PreRelease,
    Release
}

public static class VersionInfo
{
    /// <summary>
    /// 从程序集版本号读取，CI通过-p:Version=xxx注入
    /// 开发时默认为0.0.0-dev
    /// </summary>
    public static readonly string Version = GetAssemblyVersion();
    //public static readonly string Version = "1.0.0-rc.5";

    public static readonly string CodeName = "GrassBlock";

    public static readonly VersionStatus Status = VersionStatus.PreRelease;

    public static readonly string ProductName = "ObsMCLauncher";

    public static readonly string FullProductName = "黑曜石MC启动器";

    public static readonly DateTime ReleaseDate = new DateTime(2026, 06, 07);

    public static string ShortVersion => Version;

    public static string UserAgent => $"{ProductName}/{Version}";

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

    private static string GetAssemblyVersion()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version;
            if (ver != null && ver.Major != 0)
            {
                // 程序集版本格式为 major.minor.build.revision
                // 对应 semver 的 major.minor.patch
                return $"{ver.Major}.{ver.Minor}.{ver.Build}";
            }
        }
        catch { }

        return "0.0.0-dev";
    }

    /// <summary>
    /// 获取应用基础目录，用于定位OMCL等数据目录。
    /// Velopack安装模式下程序在current/子目录运行（junction/symlink），需要往上一层；
    /// 便携模式和开发模式直接使用BaseDirectory。
    /// </summary>
    public static string GetAppBaseDirectory()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dirName = Path.GetFileName(baseDir);
        var parentDir = Path.GetDirectoryName(baseDir);

        if (dirName == "current" && parentDir != null)
        {
            // Velopack安装模式下current是junction/symlink
            var dirInfo = new DirectoryInfo(baseDir);
            if (dirInfo.ResolveLinkTarget(true) != null)
            {
                return parentDir + Path.DirectorySeparatorChar;
            }
        }

        return baseDir + Path.DirectorySeparatorChar;
    }
}
