using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Tests;

public class GitHubProxyHelperTests
{
    [Theory]
    [InlineData("https://github.com/user/repo", true)]
    [InlineData("https://raw.githubusercontent.com/user/repo/main/file", true)]
    [InlineData("https://user.github.io/page", true)]
    [InlineData("https://githubassets.com/asset", true)]
    [InlineData("https://example.com/file", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsGitHubUrl_Basic(string? url, bool expected)
    {
        Assert.Equal(expected, GitHubProxyHelper.IsGitHubUrl(url ?? ""));
    }

    [Theory]
    [InlineData("https://api.github.com/repos/user/repo/releases", true)]
    [InlineData("https://github.com/user/repo", false)]
    [InlineData("", false)]
    public void IsApiUrl_Basic(string url, bool expected)
    {
        Assert.Equal(expected, GitHubProxyHelper.IsApiUrl(url));
    }

    [Theory]
    [InlineData("https://github.com/user/repo/archive/main.zip",
                "https://gh-proxy.com/https://github.com/user/repo/archive/main.zip")]
    [InlineData("https://raw.githubusercontent.com/user/repo/main/file.txt",
                "https://gh-proxy.com/https://raw.githubusercontent.com/user/repo/main/file.txt")]
    public void WithProxy_GitHubUrl(string url, string expected)
    {
        Assert.Equal(expected, GitHubProxyHelper.WithProxy(url));
    }

    [Fact]
    public void WithProxy_ApiUrl_NoProxy()
    {
        var url = "https://api.github.com/repos/user/repo/releases";
        // API请求不加代理
        Assert.Equal(url, GitHubProxyHelper.WithProxy(url));
    }

    [Fact]
    public void WithProxy_NonGitHubUrl_NoChange()
    {
        var url = "https://example.com/file.zip";
        Assert.Equal(url, GitHubProxyHelper.WithProxy(url));
    }

    [Fact]
    public void WithProxy_AlreadyProxied_NoDoubleProxy()
    {
        var url = "https://gh-proxy.com/https://github.com/user/repo/file.zip";
        Assert.Equal(url, GitHubProxyHelper.WithProxy(url));
    }

    [Fact]
    public void WithProxy_Disabled_NoChange()
    {
        var url = "https://github.com/user/repo/file.zip";
        Assert.Equal(url, GitHubProxyHelper.WithProxy(url, useProxy: false));
    }

    [Fact]
    public void RemoveProxy_ProxiedUrl()
    {
        var proxied = "https://gh-proxy.com/https://github.com/user/repo/file.zip";
        var expected = "https://github.com/user/repo/file.zip";
        Assert.Equal(expected, GitHubProxyHelper.RemoveProxy(proxied));
    }

    [Fact]
    public void RemoveProxy_NonProxiedUrl()
    {
        var url = "https://github.com/user/repo/file.zip";
        Assert.Equal(url, GitHubProxyHelper.RemoveProxy(url));
    }

    [Fact]
    public void ConvertReleaseUrl_AddsProxy()
    {
        var url = "https://github.com/user/repo/releases/download/v1.0/app.zip";
        var expected = "https://gh-proxy.com/" + url;
        Assert.Equal(expected, GitHubProxyHelper.ConvertReleaseUrl(url));
    }
}
