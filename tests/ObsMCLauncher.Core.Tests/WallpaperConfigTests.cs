using System;
using System.Linq;
using System.Text.Json;
using ObsMCLauncher.Core.Models;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 背景壁纸配置模型测试。
/// 锁定 DYNAMIC_BACKGROUND_DESIGN.md 的决策：删除 WallpaperPath（测试版不做迁移）、
/// 新增 8 个参数字段、条目 Id 与类型探测结果不持久化、APNG 只识别不播放。
/// </summary>
public class WallpaperConfigTests
{
    [Fact]
    public void LauncherConfig_WallpaperPathField_Removed()
    {
        // 测试版明确不做配置迁移：旧字段必须彻底消失，
        // 否则会出现"列表"与"单路径"两套数据源，语义必然打架
        Assert.Null(typeof(LauncherConfig).GetProperty("WallpaperPath"));
    }

    [Fact]
    public void LauncherConfig_WallpaperDefaults_AsDesigned()
    {
        var c = new LauncherConfig();

        Assert.Empty(c.WallpaperItems);
        Assert.False(c.WallpaperEnabled);

        // 与既有行为保持一致的 4 项
        Assert.Equal(0.35, c.WallpaperOpacity);
        Assert.Equal(1, c.WallpaperStretch);
        Assert.False(c.WallpaperExtendToNav);
        Assert.Equal(0.7, c.NavBackgroundOpacity);

        // 背景模糊默认关闭：老配置读进来必须与"以前一模一样"，不能凭新字段改变观感
        Assert.Equal(0, c.WallpaperBlurRadius);

        // 新增的 8 项
        Assert.True(c.WallpaperPlayAnimated);
        Assert.Equal(24, c.WallpaperMaxFps);
        Assert.Equal(1920, c.WallpaperMaxDecodeEdge);
        Assert.Equal(0, c.WallpaperSlideIntervalSeconds);
        Assert.Equal(0, c.WallpaperSlideMode);
        Assert.Equal(600, c.WallpaperTransitionMs);
        Assert.False(c.WallpaperPauseOnUnfocused);
        Assert.True(c.WallpaperPauseOnBattery);
    }

    [Theory]
    [InlineData(false, 0, false)] // 开关关，列表空
    [InlineData(true, 0, false)]  // 开关开但列表空 → 不生效（"清空列表"不产生歧义）
    [InlineData(false, 1, false)] // 有内容但开关关
    [InlineData(true, 1, true)]   // 两者都满足
    public void IsWallpaperActive_RequiresBothSwitchAndItems(bool enabled, int itemCount, bool expected)
    {
        var c = new LauncherConfig { WallpaperEnabled = enabled };
        for (var i = 0; i < itemCount; i++)
        {
            c.WallpaperItems.Add(new WallpaperItem { Path = $@"C:\bg\{i}.gif" });
        }

        Assert.Equal(expected, c.IsWallpaperActive);
    }

    [Fact]
    public void WallpaperItems_RoundTrip_PreservesOrderAndPath()
    {
        var c = new LauncherConfig();
        c.WallpaperItems.Add(new WallpaperItem { Path = @"C:\bg\a.gif" });
        c.WallpaperItems.Add(new WallpaperItem { Path = @"C:\bg\b.png" });

        var back = JsonSerializer.Deserialize<LauncherConfig>(JsonSerializer.Serialize(c))!;

        // 顺序即轮播顺序，必须原样保留
        Assert.Equal(
            new[] { @"C:\bg\a.gif", @"C:\bg\b.png" },
            back.WallpaperItems.Select(i => i.Path).ToArray());
    }

    [Fact]
    public void WallpaperItem_IdNotSerialized_PathIs()
    {
        var json = JsonSerializer.Serialize(new WallpaperItem { Path = @"C:\bg\a.gif" });

        Assert.Contains("Path", json);
        Assert.DoesNotContain("id", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WallpaperItem_Id_UniquePerInstance()
    {
        // Id 是会话内的稳定引用（UI 选中态 / 拖拽排序），不要求跨进程稳定
        var a = new WallpaperItem { Path = "same" };
        var b = new WallpaperItem { Path = "same" };

        Assert.False(string.IsNullOrWhiteSpace(a.Id));
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void LegacyConfig_WithWallpaperPath_DoesNotThrow_AndFallsBackToEmpty()
    {
        // 测试版不做迁移的契约：旧配置里的 WallpaperPath 被静默忽略，
        // 不抛异常、不残留半截状态，壁纸回落到"未设置"；其余字段照常读出
        const string legacy = """
        {
          "WallpaperEnabled": true,
          "WallpaperPath": "C:\\bg\\old.png",
          "WallpaperOpacity": 0.5
        }
        """;

        var c = JsonSerializer.Deserialize<LauncherConfig>(legacy);

        Assert.NotNull(c);
        Assert.True(c!.WallpaperEnabled);
        Assert.Equal(0.5, c.WallpaperOpacity);
        Assert.Empty(c.WallpaperItems);
        Assert.False(c.IsWallpaperActive);
    }

    [Fact]
    public void WallpaperBlurRadius_RoundTrip_AndAbsentKeyFallsBackToOff()
    {
        // 新字段必须能原样存取
        var c = new LauncherConfig { WallpaperBlurRadius = 24 };
        var back = JsonSerializer.Deserialize<LauncherConfig>(JsonSerializer.Serialize(c))!;
        Assert.Equal(24, back.WallpaperBlurRadius);

        // 老配置文件里没有这个键 → 反序列化后必须是 0（模糊默认关），
        // 否则升级启动器会凭空把用户原来的清晰背景变糊
        var legacy = JsonSerializer.Deserialize<LauncherConfig>("""{ "WallpaperOpacity": 0.5 }""")!;
        Assert.Equal(0, legacy.WallpaperBlurRadius);
    }

    [Fact]
    public void WallpaperKind_CoversAnimatedFormatsAndApngRejection()
    {
        // GIF / 动画 WebP 走正常播放路径；APNG 需要能被表达出来以便"识别 + 拒收"
        Assert.True(Enum.IsDefined(typeof(WallpaperKind), WallpaperKind.Static));
        Assert.True(Enum.IsDefined(typeof(WallpaperKind), WallpaperKind.Gif));
        Assert.True(Enum.IsDefined(typeof(WallpaperKind), WallpaperKind.AnimatedWebP));
        Assert.True(Enum.IsDefined(typeof(WallpaperKind), WallpaperKind.ApngUnsupported));
    }
}
