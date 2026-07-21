using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Tests;

public class UpdateServiceTests
{
    // ---- IsNewerVersion ----

    [Theory]
    [InlineData("v2.0.0", "1.0.0", true)]
    [InlineData("1.1.0", "1.0.0", true)]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("0.9.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.1.0", false)]
    [InlineData("v1.0.0", "v1.0.0", false)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.10.0", "1.9.0", true)]
    public void IsNewerVersion_Basic(string newVer, string curVer, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewerVersion(newVer, curVer));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0-rc.1", true)]       // 正式版 > 预发布版
    [InlineData("1.0.0-rc.1", "1.0.0", false)]      // 预发布版 < 正式版
    [InlineData("1.0.0-rc.2", "1.0.0-rc.1", true)]  // rc.2 > rc.1
    [InlineData("1.0.0-beta.2", "1.0.0-beta.1", true)]
    [InlineData("1.0.0-alpha.2", "1.0.0-alpha.1", true)]
    [InlineData("1.0.0-rc.1", "1.0.0-beta.1", true)]   // rc > beta
    [InlineData("1.0.0-beta.1", "1.0.0-alpha.1", true)] // beta > alpha
    [InlineData("1.0.0-alpha.1", "1.0.0-rc.1", false)]  // alpha < rc
    [InlineData("1.0.0-preview.2", "1.0.0-preview.1", true)]
    [InlineData("1.0.0-pre.1", "1.0.0-pre.1", false)]   // pre映射为preview
    [InlineData("1.0.0-test.1", "1.0.0-alpha.1", false)] // test映射为alpha，同类型同号
    public void IsNewerVersion_PreRelease(string newVer, string curVer, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewerVersion(newVer, curVer));
    }

    // ---- ParseVersionPart ----

    [Theory]
    [InlineData("1", 1)]
    [InlineData("0", 0)]
    [InlineData("10", 10)]
    [InlineData("1-rc", 1)]
    [InlineData("2-beta.1", 2)]
    [InlineData("abc", 0)]
    [InlineData("", 0)]
    public void ParseVersionPart_Basic(string part, int expected)
    {
        Assert.Equal(expected, UpdateService.ParseVersionPart(part));
    }

    // ---- GetPreReleaseInfo ----

    [Fact]
    public void GetPreReleaseInfo_NoPreRelease()
    {
        var result = UpdateService.GetPreReleaseInfo("1.0.0");
        Assert.Null(result);
    }

    [Theory]
    [InlineData("1.0.0-alpha.1", "alpha", 1)]
    [InlineData("1.0.0-beta.2", "beta", 2)]
    [InlineData("1.0.0-rc.5", "rc", 5)]
    [InlineData("1.0.0-preview.3", "preview", 3)]
    [InlineData("1.0.0-pre.2", "preview", 2)]   // pre映射为preview
    [InlineData("1.0.0-test.4", "alpha", 4)]     // test映射为alpha
    [InlineData("1.0.0-rc", "rc", 0)]            // 无编号默认0
    public void GetPreReleaseInfo_WithPreRelease(string version, string expectedType, int expectedNumber)
    {
        var result = UpdateService.GetPreReleaseInfo(version);
        Assert.NotNull(result);
        Assert.Equal(expectedType, result.Value.Type);
        Assert.Equal(expectedNumber, result.Value.Number);
    }

    // ---- UpdateCheckResult ----

    [Fact]
    public void UpdateCheckResult_CanAutoUpdate_WithVelopack()
    {
        var result = new UpdateCheckResult
        {
            HasUpdate = true,
            Version = "2.0.0",
            ReleaseNotes = "test",
            VelopackUpdateInfo = null,
            GitHubRelease = null
        };
        Assert.False(result.CanAutoUpdate);
    }

    [Fact]
    public void UpdateCheckResult_Properties()
    {
        var release = new GitHubRelease
        {
            TagName = "v2.0.0",
            Name = "Release 2.0.0",
            Body = "Bug fixes",
            HtmlUrl = "https://github.com/test",
            Prerelease = false
        };

        var result = new UpdateCheckResult
        {
            HasUpdate = true,
            Version = "2.0.0",
            ReleaseNotes = "Bug fixes",
            VelopackUpdateInfo = null,
            GitHubRelease = release
        };

        Assert.True(result.HasUpdate);
        Assert.Equal("2.0.0", result.Version);
        Assert.Equal("Bug fixes", result.ReleaseNotes);
        Assert.False(result.CanAutoUpdate);
        Assert.NotNull(result.GitHubRelease);
        Assert.Equal("v2.0.0", result.GitHubRelease.TagName);
    }

    // ---- GitHubRelease JSON反序列化 ----

    [Fact]
    public void GitHubRelease_Deserialize()
    {
        var json = @"{
            ""tag_name"": ""v1.0.1"",
            ""name"": ""First Release"",
            ""body"": ""Hello World"",
            ""html_url"": ""https://github.com/test/release"",
            ""published_at"": ""2026-01-01T00:00:00Z"",
            ""prerelease"": true,
            ""assets"": [
                {
                    ""name"": ""app.zip"",
                    ""browser_download_url"": ""https://github.com/test/app.zip"",
                    ""size"": 1024
                }
            ]
        }";

        var release = System.Text.Json.JsonSerializer.Deserialize<GitHubRelease>(json);
        Assert.NotNull(release);
        Assert.Equal("v1.0.1", release.TagName);
        Assert.Equal("First Release", release.Name);
        Assert.Equal("Hello World", release.Body);
        Assert.True(release.Prerelease);
        Assert.Single(release.Assets);
        Assert.Equal("app.zip", release.Assets[0].Name);
        Assert.Equal(1024, release.Assets[0].Size);
    }
}
