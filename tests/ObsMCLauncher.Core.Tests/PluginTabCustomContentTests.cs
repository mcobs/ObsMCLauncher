using ObsMCLauncher.Core.Plugins;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 测试PluginTabViewModel方案A：自定义UI内容支持
/// 由于Core层不依赖Avalonia，这里测试IPluginContext.RegisterTab重载和PluginContext回调机制
/// </summary>
public class PluginTabCustomContentTests
{
    private PluginContext CreateContext(string pluginId = "tab-test-plugin")
    {
        return new PluginContext(pluginId);
    }

    [Fact]
    public void RegisterTab_WithCustomContent_InvokesOnTabRegisteredWithContent()
    {
        var ctx = CreateContext();
        string? receivedTitle = null;
        string? receivedTabId = null;
        object? receivedContent = null;
        object? receivedPayload = null;

        PluginContext.OnTabRegisteredWithContent = (title, tabId, customContent, payload) =>
        {
            receivedTitle = title;
            receivedTabId = tabId;
            receivedContent = customContent;
            receivedPayload = payload;
        };

        var mockContent = new { Type = "MockControl", Width = 300 };
        ctx.RegisterTab("我的插件页", "my-tab", mockContent, "Star", "extra-data");

        Assert.Equal("我的插件页", receivedTitle);
        Assert.Equal("my-tab", receivedTabId);
        Assert.Same(mockContent, receivedContent);
        Assert.Equal("extra-data", receivedPayload);

        // 清理
        PluginContext.OnTabRegisteredWithContent = null;
    }

    [Fact]
    public void RegisterTab_WithNullCustomContent_InvokesOnTabRegisteredWithContent()
    {
        var ctx = CreateContext();
        object? receivedContent = "not-null";

        PluginContext.OnTabRegisteredWithContent = (title, tabId, customContent, payload) =>
        {
            receivedContent = customContent;
        };

        // 显式传 null 给 object? 参数以避免重载歧义
        ctx.RegisterTab("空内容页", "empty-tab", (object?)null);

        Assert.Null(receivedContent);

        // 清理
        PluginContext.OnTabRegisteredWithContent = null;
    }

    [Fact]
    public void RegisterTab_WithoutCustomContent_InvokesOnTabRegistered()
    {
        var ctx = CreateContext();
        string? receivedPluginId = null;
        string? receivedTitle = null;
        string? receivedTabId = null;

        PluginContext.OnTabRegistered = (pluginId, title, tabId, icon, payload) =>
        {
            receivedPluginId = pluginId;
            receivedTitle = title;
            receivedTabId = tabId;
        };

        ctx.RegisterTab("普通页", "normal-tab", "Star", null);

        Assert.Equal("tab-test-plugin", receivedPluginId);
        Assert.Equal("普通页", receivedTitle);
        Assert.Equal("normal-tab", receivedTabId);

        // 清理
        PluginContext.OnTabRegistered = null;
    }

    [Fact]
    public void RegisterTab_WithCustomContent_DoesNotInvokeOnTabRegistered()
    {
        var ctx = CreateContext();
        bool originalCalled = false;

        PluginContext.OnTabRegistered = (pluginId, title, tabId, icon, payload) =>
        {
            originalCalled = true;
        };

        PluginContext.OnTabRegisteredWithContent = (title, tabId, customContent, payload) =>
        {
            // 新回调
        };

        ctx.RegisterTab("自定义页", "custom-tab", new object());

        Assert.False(originalCalled);

        // 清理
        PluginContext.OnTabRegistered = null;
        PluginContext.OnTabRegisteredWithContent = null;
    }

    [Fact]
    public void RegisterTab_WithCustomContent_PayloadDefaultsToNull()
    {
        var ctx = CreateContext();
        object? receivedPayload = "not-null";

        PluginContext.OnTabRegisteredWithContent = (title, tabId, customContent, payload) =>
        {
            receivedPayload = payload;
        };

        ctx.RegisterTab("测试页", "test-tab", new object());

        Assert.Null(receivedPayload);

        // 清理
        PluginContext.OnTabRegisteredWithContent = null;
    }

    [Fact]
    public void RegisterTab_WithCustomContent_IconDefaultsToNull()
    {
        var ctx = CreateContext();
        // icon参数在新的重载中也有默认值null，这里验证回调能正常触发
        bool callbackInvoked = false;

        PluginContext.OnTabRegisteredWithContent = (title, tabId, customContent, payload) =>
        {
            callbackInvoked = true;
        };

        ctx.RegisterTab("无图标页", "no-icon-tab", new object());

        Assert.True(callbackInvoked);

        // 清理
        PluginContext.OnTabRegisteredWithContent = null;
    }

    [Fact]
    public void RegisterTab_MultipleTabsWithContent_AllRegistered()
    {
        var ctx = CreateContext();
        var registeredTabs = new List<string>();

        PluginContext.OnTabRegisteredWithContent = (title, tabId, customContent, payload) =>
        {
            registeredTabs.Add(tabId);
        };

        ctx.RegisterTab("页面1", "tab-1", new object());
        ctx.RegisterTab("页面2", "tab-2", new object());
        ctx.RegisterTab("页面3", "tab-3", new object());

        Assert.Equal(3, registeredTabs.Count);
        Assert.Contains("tab-1", registeredTabs);
        Assert.Contains("tab-2", registeredTabs);
        Assert.Contains("tab-3", registeredTabs);

        // 清理
        PluginContext.OnTabRegisteredWithContent = null;
    }

    [Fact]
    public void RegisterTab_MixedRegistration_BothCallbacksInvoked()
    {
        var ctx = CreateContext();
        int originalCount = 0;
        int withContentCount = 0;

        PluginContext.OnTabRegistered = (pluginId, title, tabId, icon, payload) =>
        {
            originalCount++;
        };

        PluginContext.OnTabRegisteredWithContent = (title, tabId, customContent, payload) =>
        {
            withContentCount++;
        };

        // 调用原始重载（4参数）
        ctx.RegisterTab("普通页", "normal-tab", "Star", null);
        // 调用新重载（5参数，带自定义内容）
        ctx.RegisterTab("自定义页", "custom-tab", new object());
        // 调用新重载（5参数，无自定义内容但显式指定）
        ctx.RegisterTab("空自定义页", "empty-custom-tab", (object?)null, null, "data");

        Assert.Equal(1, originalCount);
        Assert.Equal(2, withContentCount);

        // 清理
        PluginContext.OnTabRegistered = null;
        PluginContext.OnTabRegisteredWithContent = null;
    }

    [Fact]
    public void RegisterTab_WithCustomContent_CallbackNotSet_DoesNotThrow()
    {
        var ctx = CreateContext();
        PluginContext.OnTabRegisteredWithContent = null;

        var exception = Record.Exception(() =>
            ctx.RegisterTab("测试页", "test-tab", new object()));

        Assert.Null(exception);
    }

    [Fact]
    public void UnregisterTab_InvokesOnTabUnregistered()
    {
        var ctx = CreateContext();
        string? unregisteredPluginId = null;
        string? unregisteredTabId = null;

        PluginContext.OnTabUnregistered = (pluginId, tabId) =>
        {
            unregisteredPluginId = pluginId;
            unregisteredTabId = tabId;
        };

        ctx.UnregisterTab("my-tab");

        Assert.Equal("tab-test-plugin", unregisteredPluginId);
        Assert.Equal("my-tab", unregisteredTabId);

        // 清理
        PluginContext.OnTabUnregistered = null;
    }
}
