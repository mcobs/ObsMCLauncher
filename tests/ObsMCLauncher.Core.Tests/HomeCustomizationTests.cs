using System;
using System.Collections.Generic;
using System.Linq;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Plugins;
using ObsMCLauncher.Core.Services;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 主页组件注册表测试：内置组件自动注册、插件卡片注册/分组/清理
/// </summary>
public class HomeComponentRegistryTests : IDisposable
{
    public HomeComponentRegistryTests()
    {
        HomeComponentRegistry.ResetToBuiltinsForTests();
    }

    public void Dispose()
    {
        HomeComponentRegistry.ResetToBuiltinsForTests();
    }

    [Fact]
    public void Registry_Builtins_AutoRegistered()
    {
        var all = HomeComponentRegistry.GetAll();

        Assert.Contains(all, c => c.Id == HomeComponentRegistry.WelcomeId);
        Assert.Contains(all, c => c.Id == HomeComponentRegistry.NewsId);
        Assert.Contains(all, c => c.Id == HomeComponentRegistry.MultiplayerId);
        Assert.Contains(all, c => c.Id == HomeComponentRegistry.ModsId);
    }

    [Fact]
    public void Registry_ActionAreaComponents_NotRegistered()
    {
        // 操作区去组件化：账号/版本/启动/日志开关/分隔线不再出现在注册表
        var all = HomeComponentRegistry.GetAll();

        Assert.DoesNotContain(all, c => c.Id == HomeComponentRegistry.AccountPickerId);
        Assert.DoesNotContain(all, c => c.Id == HomeComponentRegistry.VersionPickerId);
        Assert.DoesNotContain(all, c => c.Id == HomeComponentRegistry.LaunchButtonId);
        Assert.DoesNotContain(all, c => c.Id == HomeComponentRegistry.LogToggleId);
        Assert.DoesNotContain(all, c => c.Id == HomeComponentRegistry.SeparatorId);
    }

    [Fact]
    public void Registry_BuiltinDescriptor_WelcomeHasFillSize()
    {
        Assert.Equal(HomeCardSize.Fill, HomeComponentRegistry.TryGet(HomeComponentRegistry.WelcomeId)!.DefaultSize);
        Assert.Null(HomeComponentRegistry.TryGet(HomeComponentRegistry.WelcomeId)!.PluginId);
    }

    [Fact]
    public void Registry_PluginCard_RegisteredWithPrefix()
    {
        HomeComponentRegistry.RegisterPluginCard("demo", "stats", "服务器状态", "查看在线情况", "M12 12", HomeCardSize.Large);

        var descriptor = HomeComponentRegistry.TryGet("demo.stats");
        Assert.NotNull(descriptor);
        Assert.Equal("服务器状态", descriptor!.Title);
        Assert.Equal("demo", descriptor.PluginId);
        Assert.True(descriptor.IsPlugin);
        Assert.Equal(HomeCardSize.Large, descriptor.DefaultSize);
    }

    [Fact]
    public void Registry_DuplicateRegister_UpdatesDescriptor()
    {
        HomeComponentRegistry.RegisterPluginCard("demo", "card", "旧标题", "", null, HomeCardSize.Medium);
        HomeComponentRegistry.RegisterPluginCard("demo", "card", "新标题", "", null, HomeCardSize.Large);

        var descriptor = HomeComponentRegistry.TryGet("demo.card");
        Assert.Equal("新标题", descriptor!.Title);
        Assert.Equal(HomeCardSize.Large, descriptor.DefaultSize);
        // 更新不产生重复项
        Assert.Single(HomeComponentRegistry.GetAll().Where(c => c.Id == "demo.card"));
    }

    [Fact]
    public void Registry_Unregister_RemovesComponent()
    {
        HomeComponentRegistry.RegisterPluginCard("demo", "card", "标题", "", null, HomeCardSize.Medium);
        Assert.True(HomeComponentRegistry.Contains("demo.card"));

        Assert.True(HomeComponentRegistry.Unregister("demo.card"));
        Assert.False(HomeComponentRegistry.Contains("demo.card"));
        // 再删返回 false
        Assert.False(HomeComponentRegistry.Unregister("demo.card"));
    }

    [Fact]
    public void Registry_GetGrouped_BuiltinFirst_PluginsByPluginId()
    {
        HomeComponentRegistry.RegisterPluginCard("zeta", "c1", "标题", "", null, HomeCardSize.Medium);
        HomeComponentRegistry.RegisterPluginCard("alpha", "c2", "标题", "", null, HomeCardSize.Medium);
        HomeComponentRegistry.RegisterPluginCard("alpha", "c3", "标题", "", null, HomeCardSize.Medium);

        var groups = HomeComponentRegistry.GetGrouped();

        Assert.Equal(3, groups.Count);
        Assert.Null(groups[0].PluginId);
        Assert.True(groups[0].Components.All(c => c.PluginId == null));
        Assert.Equal("alpha", groups[1].PluginId);
        Assert.Equal(2, groups[1].Components.Count);
        Assert.Equal("zeta", groups[2].PluginId);
    }

    [Fact]
    public void Registry_RemovePluginComponents_RemovesOnlyMatchingPlugin()
    {
        HomeComponentRegistry.RegisterPluginCard("demo", "c1", "标题", "", null, HomeCardSize.Medium);
        HomeComponentRegistry.RegisterPluginCard("demo", "c2", "标题", "", null, HomeCardSize.Medium);
        HomeComponentRegistry.RegisterPluginCard("other", "c3", "标题", "", null, HomeCardSize.Medium);

        int removed = HomeComponentRegistry.RemovePluginComponents("demo");

        Assert.Equal(2, removed);
        Assert.False(HomeComponentRegistry.Contains("demo.c1"));
        Assert.False(HomeComponentRegistry.Contains("demo.c2"));
        Assert.True(HomeComponentRegistry.Contains("other.c3"));
        // 内置组件不受影响
        Assert.True(HomeComponentRegistry.Contains(HomeComponentRegistry.NewsId));
    }

    [Fact]
    public void Registry_InvalidInput_Ignored()
    {
        HomeComponentRegistry.RegisterPluginCard("", "card", "标题", "", null, HomeCardSize.Medium);
        HomeComponentRegistry.RegisterPluginCard("demo", "", "标题", "", null, HomeCardSize.Medium);

        Assert.Empty(HomeComponentRegistry.GetAll().Where(c => c.PluginId == "demo"));
    }
}

/// <summary>
/// 主页布局模型测试：旧版 HomeCards 配置迁移、布局增删操作、操作区组件清理
/// </summary>
public class HomeLayoutTests
{
    [Fact]
    public void Layout_CreateDefault_NoLegacy_HasCardRowsOnly()
    {
        var layout = HomeLayoutConfig.CreateDefault(null);

        // 操作区去组件化后：欢迎行 + 卡片行，共两行
        Assert.Equal(2, layout.Rows.Count);
        // 第一行：欢迎卡独占
        Assert.Single(layout.Rows[0].Components);
        Assert.Equal(HomeComponentRegistry.WelcomeId, layout.Rows[0].Components[0].Id);
        Assert.Equal(HomeCardSize.Fill, layout.Rows[0].Components[0].Size);
        // 第二行：三张默认卡片
        Assert.Equal(3, layout.Rows[1].Components.Count);
        Assert.Equal(HomeComponentRegistry.NewsId, layout.Rows[1].Components[0].Id);
        Assert.Equal(HomeComponentRegistry.MultiplayerId, layout.Rows[1].Components[1].Id);
        Assert.Equal(HomeComponentRegistry.ModsId, layout.Rows[1].Components[2].Id);
        // 布局中不含操作区组件
        Assert.False(layout.Contains(HomeComponentRegistry.SeparatorId));
        Assert.False(layout.Contains(HomeComponentRegistry.AccountPickerId));
        Assert.False(layout.Contains(HomeComponentRegistry.LaunchButtonId));
    }

    [Fact]
    public void Layout_Append_AddsToLastRow()
    {
        var layout = new HomeLayoutConfig();

        layout.Append("a", HomeCardSize.Medium);
        layout.Append("b", HomeCardSize.Small);

        Assert.Single(layout.Rows);
        Assert.Equal(2, layout.Rows[0].Components.Count);
    }

    [Fact]
    public void Layout_CreateDefault_WelcomeDisabled_NoWelcomeRow()
    {
        var legacy = new List<HomeCardConfig>
        {
            new() { CardId = HomeComponentRegistry.WelcomeId, IsEnabled = false, Order = 0 }
        };

        var layout = HomeLayoutConfig.CreateDefault(legacy);

        Assert.False(layout.Contains(HomeComponentRegistry.WelcomeId));
        // 只剩卡片行
        Assert.Single(layout.Rows);
    }

    [Fact]
    public void Layout_CreateDefault_CardDisabled_ExcludedFromRow()
    {
        var legacy = new List<HomeCardConfig>
        {
            new() { CardId = HomeComponentRegistry.NewsId, IsEnabled = false, Order = 1 }
        };

        var layout = HomeLayoutConfig.CreateDefault(legacy);

        Assert.False(layout.Contains(HomeComponentRegistry.NewsId));
        // 欢迎行 + 卡片行（news 被禁用后卡片行剩 2 张）
        Assert.Equal(2, layout.Rows[1].Components.Count);
    }

    [Fact]
    public void Layout_CreateDefault_PluginCardsAppendedByOrder()
    {
        var legacy = new List<HomeCardConfig>
        {
            new() { CardId = "demo.tip", IsPluginCard = true, PluginId = "demo", IsEnabled = true, Order = 0 },
            new() { CardId = "demo.stats", IsPluginCard = true, PluginId = "demo", IsEnabled = true, Order = 5 },
            new() { CardId = "demo.off", IsPluginCard = true, PluginId = "demo", IsEnabled = false, Order = 1 }
        };

        var layout = HomeLayoutConfig.CreateDefault(legacy);

        // 启用的插件卡片进入卡片行，禁用的排除
        Assert.True(layout.Contains("demo.tip"));
        Assert.True(layout.Contains("demo.stats"));
        Assert.False(layout.Contains("demo.off"));
        // Order=0 的插件卡片排在最前（Rows[0] 是欢迎行，卡片行是 Rows[1]）
        Assert.Equal("demo.tip", layout.Rows[1].Components[0].Id);
        Assert.Equal("demo.stats", layout.Rows[1].Components[^1].Id);
    }

    [Fact]
    public void Layout_CreateDefault_LegacyOrderRespected()
    {
        var legacy = new List<HomeCardConfig>
        {
            new() { CardId = HomeComponentRegistry.ModsId, IsEnabled = true, Order = 0 },
            new() { CardId = HomeComponentRegistry.NewsId, IsEnabled = true, Order = 1 },
            new() { CardId = HomeComponentRegistry.MultiplayerId, IsEnabled = true, Order = 2 }
        };

        var layout = HomeLayoutConfig.CreateDefault(legacy);

        Assert.Equal(HomeComponentRegistry.ModsId, layout.Rows[1].Components[0].Id);
        Assert.Equal(HomeComponentRegistry.NewsId, layout.Rows[1].Components[1].Id);
        Assert.Equal(HomeComponentRegistry.MultiplayerId, layout.Rows[1].Components[2].Id);
    }

    [Fact]
    public void Layout_Operations_ContainsRemoveAppend()
    {
        var layout = new HomeLayoutConfig();
        layout.Append("a", HomeCardSize.Medium);
        layout.Append("b", HomeCardSize.Large);
        layout.Append("c", HomeCardSize.Small);

        Assert.Single(layout.Rows);
        Assert.Equal(3, layout.Rows[0].Components.Count);
        Assert.True(layout.Contains("b"));

        Assert.True(layout.Remove("b"));
        Assert.False(layout.Contains("b"));
        Assert.False(layout.Remove("b"));

        // 全部移除后清空行为
        layout.Remove("a");
        layout.Remove("c");
        layout.RemoveEmptyRows();
        Assert.Single(layout.Rows);
        Assert.Empty(layout.Rows[0].Components);

        // 空布局 Append 自动建行
        var empty = new HomeLayoutConfig();
        empty.Append("x", HomeCardSize.Fill);
        Assert.Single(empty.Rows);
        Assert.Single(empty.Rows[0].Components);
    }

    [Fact]
    public void Layout_RemoveEmptyRows_KeepsAtLeastOneRow()
    {
        var layout = new HomeLayoutConfig();
        layout.Rows.Add(new HomeRowConfig());
        layout.Rows.Add(new HomeRowConfig());

        layout.RemoveEmptyRows();

        Assert.Single(layout.Rows);
    }

    [Fact]
    public void Config_GetHomeLayout_LazyMigrationFromLegacy()
    {
        var config = new LauncherConfig();
        config.HomeCards = new List<HomeCardConfig>
        {
            new() { CardId = HomeComponentRegistry.WelcomeId, IsEnabled = false, Order = 0 }
        };

        var layout = config.GetHomeLayout();

        // 按旧配置迁移：欢迎卡被禁用
        Assert.False(layout.Contains(HomeComponentRegistry.WelcomeId));
        // 第二次调用返回同一实例（不再重复迁移）
        Assert.Same(layout, config.GetHomeLayout());
        // 迁移结果同时写回属性，随 Save 持久化
        Assert.Same(layout, config.HomeLayout);
    }

    [Fact]
    public void Layout_RemoveLegacyActionComponents_CleansOldRows()
    {
        // 旧版本写盘的布局：操作区以组件形式存在
        var config = new LauncherConfig();
        config.HomeLayout = new HomeLayoutConfig();
        config.HomeLayout.Rows.Add(new HomeRowConfig
        {
            Components = [new HomeComponentConfig { Id = HomeComponentRegistry.WelcomeId }]
        });
        config.HomeLayout.Rows.Add(new HomeRowConfig
        {
            Components = [new HomeComponentConfig { Id = HomeComponentRegistry.SeparatorId }]
        });
        config.HomeLayout.Rows.Add(new HomeRowConfig
        {
            Components =
            [
                new HomeComponentConfig { Id = HomeComponentRegistry.AccountPickerId },
                new HomeComponentConfig { Id = HomeComponentRegistry.LaunchButtonId }
            ]
        });

        var layout = config.GetHomeLayout();

        // 操作区组件被清理，只剩欢迎行
        Assert.Single(layout.Rows);
        Assert.Equal(HomeComponentRegistry.WelcomeId, layout.Rows[0].Components[0].Id);
    }

    [Fact]
    public void Layout_RemoveLegacyActionComponents_NoLegacy_NoTouch()
    {
        var layout = new HomeLayoutConfig();
        layout.Rows.Add(new HomeRowConfig
        {
            Components = [new HomeComponentConfig { Id = HomeComponentRegistry.NewsId }]
        });

        layout.RemoveLegacyActionComponents();

        // 没有旧操作区组件时布局原样保留
        Assert.Single(layout.Rows);
        Assert.Equal(HomeComponentRegistry.NewsId, layout.Rows[0].Components[0].Id);
    }

    [Fact]
    public void Layout_RemoveLegacyActionComponents_AllRowsLegacy_KeepsOneRow()
    {
        var layout = new HomeLayoutConfig();
        layout.Rows.Add(new HomeRowConfig
        {
            Components = [new HomeComponentConfig { Id = HomeComponentRegistry.LaunchButtonId }]
        });

        layout.RemoveLegacyActionComponents();

        // 全部清掉后至少保留一个空行供放置组件
        Assert.Single(layout.Rows);
        Assert.Empty(layout.Rows[0].Components);
    }
}

/// <summary>
/// 插件主页组件 API 测试：卡片注册尺寸重载
/// </summary>
public class HomeComponentApiTests : IDisposable
{
    private const string PluginId = "home-comp-plugin";

    public HomeComponentApiTests()
    {
        PluginContext.RemovePluginCommands(PluginId);
        HomeComponentRegistry.ResetToBuiltinsForTests();
    }

    public void Dispose()
    {
        PluginContext.OnHomeCardRegistered = null;
        PluginContext.OnHomeCardUnregistered = null;
        PluginContext.RemovePluginCommands(PluginId);
        HomeComponentRegistry.ResetToBuiltinsForTests();
    }

    [Fact]
    public void Api_RegisterHomeCard_WithoutSize_DefaultsToMedium()
    {
        HomeCardSize? receivedSize = null;
        PluginContext.OnHomeCardRegistered = (_, _, _, _, _, _, size) => receivedSize = size;

        var ctx = new PluginContext(PluginId);
        ctx.RegisterHomeCard("tip", "提示", "描述");

        Assert.Equal(HomeCardSize.Medium, receivedSize);
    }

    [Fact]
    public void Api_RegisterHomeCard_WithSize_PassesSizeThrough()
    {
        string? receivedId = null;
        HomeCardSize? receivedSize = null;
        PluginContext.OnHomeCardRegistered = (id, _, _, _, _, _, size) =>
        {
            receivedId = id;
            receivedSize = size;
        };

        var ctx = new PluginContext(PluginId);
        ctx.RegisterHomeCard("stats", "状态", "描述", null, null, null, HomeCardSize.Large);

        Assert.Equal($"{PluginId}.stats", receivedId);
        Assert.Equal(HomeCardSize.Large, receivedSize);
    }
}
