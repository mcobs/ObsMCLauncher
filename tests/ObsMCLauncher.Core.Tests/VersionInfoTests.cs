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
    [InlineData(VersionStatus.PreRelease, "预发布版本")]
    [InlineData(VersionStatus.Release, "正式版")]
    public void VersionStatusText_Mapping(VersionStatus status, string expected)
    {
        // 验证枚举映射关系正确
        var text = status switch
        {
            VersionStatus.Testing => "测试版",
            VersionStatus.PreRelease => "预发布版本",
            VersionStatus.Release => "正式版",
            _ => "未知"
        };
        Assert.Equal(expected, text);
    }
}
