using ObsMCLauncher.Core.Services.Mirror;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 镜像地址改写规则的测试。域名映射写反过一次——把 MCIM 明确不支持的 mediafilez 转发了，
/// 真正在用的 media.forgecdn.net 反而没转发，这里把规则钉住。
/// </summary>
public class MirrorUrlHelperTests
{
    private const string Mcim = "https://mod.mcimirror.top";

    [Theory]
    // API：官方域名整体换成带前缀的镜像域名
    [InlineData("https://api.modrinth.com/v2/project/sodium", Mcim + "/modrinth/v2/project/sodium")]
    [InlineData("https://api.curseforge.com/v1/mods/238222", Mcim + "/curseforge/v1/mods/238222")]
    // CDN：去掉官方域名，路径直接挂在镜像根上
    [InlineData("https://cdn.modrinth.com/data/AANobbMI/icon.png", Mcim + "/data/AANobbMI/icon.png")]
    [InlineData("https://edge.forgecdn.net/files/3040/523/jei.jar", Mcim + "/files/3040/523/jei.jar")]
    [InlineData("https://media.forgecdn.net/avatars/29/69/635838945588716414.jpeg", Mcim + "/avatars/29/69/635838945588716414.jpeg")]
    public void RewriteUrl_MapsSupportedHosts(string original, string expected)
    {
        Assert.Equal(expected, MirrorUrlHelper.RewriteUrl(original));
    }

    [Theory]
    // MCIM 文档明确说明不要转发 mediafilez.forgecdn.net
    [InlineData("https://mediafilez.forgecdn.net/avatars/29/69/635838945588716414.jpeg")]
    // 与镜像无关的地址一律原样返回
    [InlineData("https://github.com/owner/repo/releases/download/v1/mod.zip")]
    [InlineData("https://example.com/data/AANobbMI/icon.png")]
    public void RewriteUrl_LeavesOtherUrlsUntouched(string url)
    {
        Assert.Equal(url, MirrorUrlHelper.RewriteUrl(url));
    }

    [Theory]
    [InlineData("https://api.modrinth.com/v2/project/sodium")]
    [InlineData("https://api.curseforge.com/v1/mods/238222")]
    [InlineData("https://cdn.modrinth.com/data/AANobbMI/icon.png")]
    [InlineData("https://edge.forgecdn.net/files/3040/523/jei.jar")]
    [InlineData("https://media.forgecdn.net/avatars/29/69/635838945588716414.jpeg")]
    public void GetOriginalUrl_RoundTripsThroughRewrite(string original)
    {
        var rewritten = MirrorUrlHelper.RewriteUrl(original);
        Assert.NotEqual(original, rewritten);
        Assert.Equal(original, MirrorUrlHelper.GetOriginalUrl(rewritten));
    }

    [Theory]
    // CDN 路径本身区分来源，还原时不能一律当成 Modrinth
    [InlineData(Mcim + "/data/AANobbMI/icon.png", "https://cdn.modrinth.com/data/AANobbMI/icon.png")]
    [InlineData(Mcim + "/files/3040/523/jei.jar", "https://edge.forgecdn.net/files/3040/523/jei.jar")]
    [InlineData(Mcim + "/avatars/29/69/x.jpeg", "https://media.forgecdn.net/avatars/29/69/x.jpeg")]
    [InlineData(Mcim + "/modrinth/v2/search", "https://api.modrinth.com/v2/search")]
    [InlineData(Mcim + "/curseforge/v1/mods/search", "https://api.curseforge.com/v1/mods/search")]
    public void GetOriginalUrl_MapsBackToCorrectHost(string mirrorUrl, string expected)
    {
        Assert.Equal(expected, MirrorUrlHelper.GetOriginalUrl(mirrorUrl));
    }

    [Theory]
    [InlineData("https://cdn.modrinth.com/data/AANobbMI/icon.png")]
    [InlineData("")]
    public void GetOriginalUrl_LeavesNonMirrorUrlsUntouched(string url)
    {
        Assert.Equal(url, MirrorUrlHelper.GetOriginalUrl(url));
    }

    [Theory]
    // 官方地址
    [InlineData("https://api.modrinth.com/v2/search", MirrorPlatform.Modrinth)]
    [InlineData("https://cdn.modrinth.com/data/AANobbMI/icon.png", MirrorPlatform.Modrinth)]
    [InlineData("https://api.curseforge.com/v1/mods/search", MirrorPlatform.CurseForge)]
    [InlineData("https://edge.forgecdn.net/files/3040/523/jei.jar", MirrorPlatform.CurseForge)]
    [InlineData("https://media.forgecdn.net/avatars/29/69/x.jpeg", MirrorPlatform.CurseForge)]
    [InlineData("https://mediafilez.forgecdn.net/avatars/29/69/x.jpeg", MirrorPlatform.CurseForge)]
    // 镜像地址：CDN 路径也要能区分，否则会误标另一个平台不可用
    [InlineData(Mcim + "/modrinth/v2/search", MirrorPlatform.Modrinth)]
    [InlineData(Mcim + "/data/AANobbMI/icon.png", MirrorPlatform.Modrinth)]
    [InlineData(Mcim + "/curseforge/v1/mods/search", MirrorPlatform.CurseForge)]
    [InlineData(Mcim + "/files/3040/523/jei.jar", MirrorPlatform.CurseForge)]
    [InlineData(Mcim + "/avatars/29/69/x.jpeg", MirrorPlatform.CurseForge)]
    // 认不出来的地址
    [InlineData("https://github.com/owner/repo/releases/download/v1/mod.zip", MirrorPlatform.None)]
    [InlineData(Mcim + "/statistics", MirrorPlatform.None)]
    [InlineData("", MirrorPlatform.None)]
    public void GetPlatform_ClassifiesBothOfficialAndMirrorUrls(string url, MirrorPlatform expected)
    {
        Assert.Equal(expected, MirrorUrlHelper.GetPlatform(url));
    }
}
