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
}
