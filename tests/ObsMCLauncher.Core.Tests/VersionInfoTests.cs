using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Tests;

public class VersionInfoTests
{
    [Fact]
    public void Version_NotNullOrEmpty()
    {
        Assert.False(string.IsNullOrEmpty(VersionInfo.Version));
    }

    [Fact]
    public void ShortVersion_SameAsVersion()
    {
        Assert.Equal(VersionInfo.Version, VersionInfo.ShortVersion);
    }

    [Fact]
    public void UserAgent_ContainsProductName()
    {
        Assert.Contains(VersionInfo.ProductName, VersionInfo.UserAgent);
    }

    [Fact]
    public void UserAgent_ContainsVersion()
    {
        Assert.Contains(VersionInfo.Version, VersionInfo.UserAgent);
    }

    [Fact]
    public void DisplayVersion_ContainsVersion()
    {
        Assert.Contains(VersionInfo.Version, VersionInfo.DisplayVersion);
    }

    [Fact]
    public void DisplayVersion_ContainsStatusText()
    {
        var statusText = VersionInfo.VersionStatusText;
        Assert.Contains(statusText, VersionInfo.DisplayVersion);
    }

    [Fact]
    public void Copyright_ContainsYear()
    {
        Assert.Contains(VersionInfo.ReleaseDate.Year.ToString(), VersionInfo.Copyright);
    }

    [Fact]
    public void Copyright_ContainsProductName()
    {
        Assert.Contains(VersionInfo.ProductName, VersionInfo.Copyright);
    }

    [Fact]
    public void GetDetailedVersionInfo_ContainsAllFields()
    {
        var info = VersionInfo.GetDetailedVersionInfo();
        Assert.Contains(VersionInfo.Version, info);
        Assert.Contains(VersionInfo.CodeName, info);
        Assert.Contains(VersionInfo.VersionStatusText, info);
    }

    [Theory]
    [InlineData(VersionStatus.Testing, "测试版")]
    [InlineData(VersionStatus.Preview, "预览版")]
    [InlineData(VersionStatus.PreRelease, "预发布版")]
    [InlineData(VersionStatus.Release, "正式版")]
    public void VersionStatusText_Mapping(VersionStatus status, string expected)
    {
        // 验证枚举映射关系正确
        var text = status switch
        {
            VersionStatus.Testing => "测试版",
            VersionStatus.Preview => "预览版",
            VersionStatus.PreRelease => "预发布版",
            VersionStatus.Release => "正式版",
            _ => "未知"
        };
        Assert.Equal(expected, text);
    }

    [Fact]
    public void ResolveAppBaseDirectory_CurrentDir_StepsUpToParent()
    {
        var parent = Path.Combine(Path.GetTempPath(), "VelopackApp");
        var result = VersionInfo.ResolveAppBaseDirectory(Path.Combine(parent, "current"));
        Assert.Equal(parent + Path.DirectorySeparatorChar, result);
    }

    [Fact]
    public void ResolveAppBaseDirectory_CurrentDir_WithTrailingSeparator_StepsUp()
    {
        var parent = Path.Combine(Path.GetTempPath(), "VelopackApp");
        var current = Path.Combine(parent, "current") + Path.DirectorySeparatorChar;
        var result = VersionInfo.ResolveAppBaseDirectory(current);
        Assert.Equal(parent + Path.DirectorySeparatorChar, result);
    }

    [Fact]
    public void ResolveAppBaseDirectory_CurrentDir_PlainFolder_StepsUp()
    {
        // 生产环境解压部署时 current 是普通文件夹（非junction/symlink），也必须跳出，
        // 否则 OMCL/.minecraft 数据目录会落在被更新流程覆盖的 current 内
        var parent = Path.Combine(Path.GetTempPath(), "ObsMCLauncher");
        var result = VersionInfo.ResolveAppBaseDirectory(Path.Combine(parent, "current"));
        Assert.Equal(parent + Path.DirectorySeparatorChar, result);
    }

    [Fact]
    public void ResolveAppBaseDirectory_CurrentDirAtRoot_ReturnsRoot()
    {
        var root = Path.GetPathRoot(Path.GetTempPath());
        Assert.NotNull(root);
        var result = VersionInfo.ResolveAppBaseDirectory(Path.Combine(root, "current"));
        Assert.Equal(root, result);
    }

    [Fact]
    public void ResolveAppBaseDirectory_NonCurrentDir_ReturnsBaseDir()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "bin", "Debug", "net8.0");
        var result = VersionInfo.ResolveAppBaseDirectory(baseDir);
        Assert.Equal(baseDir + Path.DirectorySeparatorChar, result);
    }

    [Fact]
    public void ResolveDataRoot_MacInsideAppBundle_UsesUserDataRoot()
    {
        // 程序在 ObsMCLauncher.app/Contents/MacOS 里跑：数据不能留在包内，
        // 否则删除应用=删数据，接入签名后写包内还会破坏签名
        var bundle = Path.Combine(Path.GetTempPath(), "Applications", "ObsMCLauncher.app", "Contents", "MacOS");
        var userData = Path.Combine(Path.GetTempPath(), "Home", "Library", "Application Support", "ObsMCLauncher")
                       + Path.DirectorySeparatorChar;

        var result = VersionInfo.ResolveDataRoot(bundle, VersionInfo.HostPlatform.MacOS, userData, isAppImage: false);

        Assert.Equal(userData, result);
    }

    [Fact]
    public void ResolveDataRoot_MacOutsideBundle_KeepsBaseDir()
    {
        // 开发/调试时程序不在 .app 里，仍用程序目录，避免把临时数据写到用户目录
        var baseDir = Path.Combine(Path.GetTempPath(), "bin", "Debug", "net8.0");
        var userData = Path.Combine(Path.GetTempPath(), "Home", "Library", "Application Support", "ObsMCLauncher")
                       + Path.DirectorySeparatorChar;

        var result = VersionInfo.ResolveDataRoot(baseDir, VersionInfo.HostPlatform.MacOS, userData, isAppImage: false);

        Assert.Equal(baseDir + Path.DirectorySeparatorChar, result);
    }

    [Fact]
    public void ResolveDataRoot_LinuxAppImage_UsesUserDataRoot()
    {
        // AppImage 挂载点是只读的，往里面写 OMCL 必然失败
        var mount = Path.Combine(Path.GetTempPath(), ".mount_ObsMCLauncher", "usr", "bin");
        var userData = Path.Combine(Path.GetTempPath(), "Home", ".local", "share", "ObsMCLauncher")
                       + Path.DirectorySeparatorChar;

        var result = VersionInfo.ResolveDataRoot(mount, VersionInfo.HostPlatform.Linux, userData, isAppImage: true);

        Assert.Equal(userData, result);
    }

    [Fact]
    public void ResolveDataRoot_LinuxWithoutAppImage_KeepsBaseDir()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "opt", "ObsMCLauncher");
        var userData = Path.Combine(Path.GetTempPath(), "Home", ".local", "share", "ObsMCLauncher")
                       + Path.DirectorySeparatorChar;

        var result = VersionInfo.ResolveDataRoot(baseDir, VersionInfo.HostPlatform.Linux, userData, isAppImage: false);

        Assert.Equal(baseDir + Path.DirectorySeparatorChar, result);
    }

    [Fact]
    public void ResolveDataRoot_Windows_IgnoresUserDataRoot()
    {
        // Windows 行为必须完全不变：安装模式仍然退到 current 的父级
        var baseDir = Path.Combine(Path.GetTempPath(), "ObsMCLauncher", "current");

        var result = VersionInfo.ResolveDataRoot(
            baseDir, VersionInfo.HostPlatform.Windows, "/home/x/.local/share/ObsMCLauncher/", isAppImage: true);

        Assert.Equal(Path.Combine(Path.GetTempPath(), "ObsMCLauncher") + Path.DirectorySeparatorChar, result);
    }

    [Theory]
    [InlineData("/Applications/ObsMCLauncher.app/Contents/MacOS", true)]
    [InlineData("/Users/a/Applications/ObsMCLauncher.app/Contents/", true)]
    [InlineData("/Applications/ObsMCLauncher.app", false)]
    [InlineData("/Users/a/Contents/MacOS", false)]
    [InlineData("/Users/a/App.app/Contents/MacOS/", true)]
    public void IsInsideAppBundle_DetectsBundle(string path, bool expected)
    {
        Assert.Equal(expected, VersionInfo.IsInsideAppBundle(path));
    }
}
