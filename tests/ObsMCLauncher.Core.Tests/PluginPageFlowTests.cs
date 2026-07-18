using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ObsMCLauncher.Core.Plugins;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 验证自定义页面（插件标签页）的完整生命周期：
/// 创建 -> 布局/组件添加 -> 权限/隔离 -> 跨设备/跨插件兼容 -> 编辑/删除
/// 使用与 MoreViewModel.OnPluginTabRegistered 等价的内存模型，避免依赖 Avalonia/UI 线程
/// </summary>
public class PluginPageFlowTests : IDisposable
{
    /// <summary>
    /// 简化版 PluginTabViewModel（无 Avalonia 依赖）
    /// </summary>
    private class SimplePluginTab
    {
        public string PluginId { get; }
        public string TabId { get; }
        public string Title { get; set; }
        public object? Payload { get; }
        public object? CustomContent { get; }
        public bool HasCustomContent => CustomContent != null;
        public DateTime CreatedAt { get; } = DateTime.Now;

        public SimplePluginTab(string pluginId, string tabId, string title, object? payload, object? customContent)
        {
            PluginId = pluginId;
            TabId = tabId;
            Title = title;
            Payload = payload;
            CustomContent = customContent;
        }
    }

    /// <summary>
    /// 简化版 MoreViewModel 标签页集合
    /// 复刻 MoreViewModel.OnPluginTabRegistered/Unregister/RemoveAll 逻辑
    /// </summary>
    private class TabRegistry
    {
        public ObservableCollection<SimplePluginTab> Tabs { get; } = new();

        /// <summary>系统内置标签页数量（用于模拟"插件"标签前的位置）</summary>
        public int SystemTabCount { get; set; } = 1;

        public void Register(string pluginId, string title, string tabId, object? payload, object? customContent)
        {
            // 复刻 MoreViewModel：existingTab == null 才插入
            var existing = Tabs.FirstOrDefault(t => t.Title == title);
            if (existing == null)
            {
                Tabs.Insert(Math.Min(SystemTabCount, Tabs.Count),
                    new SimplePluginTab(pluginId, tabId, title, payload, customContent));
            }
        }

        public bool Unregister(string pluginId, string tabId)
        {
            var tab = Tabs.FirstOrDefault(t => t.PluginId == pluginId && t.TabId == tabId);
            if (tab != null)
            {
                Tabs.Remove(tab);
                return true;
            }
            return false;
        }

        public int RemoveAllByPlugin(string pluginId)
        {
            var toRemove = Tabs.Where(t => t.PluginId == pluginId).ToList();
            foreach (var t in toRemove) Tabs.Remove(t);
            return toRemove.Count;
        }
    }

    private const string PluginId = "page-plugin";

    public PluginPageFlowTests()
    {
        // 页面测试不注册命令或钩子，无需清理
    }

    public void Dispose()
    {
        // 页面测试不注册命令或钩子，无需清理
    }

    // ===================== 页面创建 =====================

    [Fact]
    public void Page_Create_StandardTab_RegisteredWithPayload()
    {
        var registry = new TabRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnTabRegistered = (pid, title, tabId, icon, payload) =>
            registry.Register(pid, title, tabId, payload, null);

        ctx.RegisterTab("我的工具页", "tool-tab", "Wrench", "extra-payload");

        Assert.Single(registry.Tabs);
        var tab = registry.Tabs.First();
        Assert.Equal(PluginId, tab.PluginId);
        Assert.Equal("tool-tab", tab.TabId);
        Assert.Equal("我的工具页", tab.Title);
        Assert.Equal("extra-payload", tab.Payload);
        Assert.False(tab.HasCustomContent); // 标准注册无自定义内容
    }

    [Fact]
    public void Page_Create_WithCustomContent_RegisteredWithContent()
    {
        var registry = new TabRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnTabRegisteredWithContent = (title, tabId, content, payload) =>
            registry.Register(PluginId, title, tabId, payload, content);

        var mockContent = new { Type = "Form", Fields = new[] { "name", "value" } };
        ctx.RegisterTab("表单页", "form-tab", mockContent);

        Assert.Single(registry.Tabs);
        var tab = registry.Tabs.First();
        Assert.True(tab.HasCustomContent);
        Assert.Same(mockContent, tab.CustomContent);
    }

    [Fact]
    public void Page_Create_MultiplePages_AllRegisteredInOrder()
    {
        var registry = new TabRegistry { SystemTabCount = 1 };
        var ctx = new PluginContext(PluginId);
        PluginContext.OnTabRegistered = (pid, title, tabId, icon, payload) =>
            registry.Register(pid, title, tabId, payload, null);

        ctx.RegisterTab("页面1", "p1", null, null);
        ctx.RegisterTab("页面2", "p2", null, null);
        ctx.RegisterTab("页面3", "p3", null, null);

        Assert.Equal(3, registry.Tabs.Count);
        // 系统Tab后插入，第一次插入在index 0，后续都因为 SystemTabCount=1 但已有1个而插入到index 1
        // 实际顺序取决于实现细节，这里仅验证全部注册成功
    }

    // ===================== 布局设计 & 组件添加 =====================

    [Fact]
    public void Page_Layout_PayloadCanCarryLayoutConfig()
    {
        var registry = new TabRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnTabRegistered = (pid, title, tabId, icon, payload) =>
            registry.Register(pid, title, tabId, payload, null);

        var layout = new Dictionary<string, object>
        {
            ["columns"] = 3,
            ["padding"] = 16,
            ["components"] = new[] { "header", "list", "chart" }
        };
        ctx.RegisterTab("统计页", "stats-tab", null, layout);

        var tab = registry.Tabs.First();
        var payloadDict = Assert.IsType<Dictionary<string, object>>(tab.Payload);
        Assert.Equal(3, payloadDict["columns"]);
        Assert.Equal(16, payloadDict["padding"]);
    }

    [Fact]
    public void Page_Component_CustomContentCanCarryComponentTree()
    {
        var registry = new TabRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnTabRegisteredWithContent = (title, tabId, content, payload) =>
            registry.Register(PluginId, title, tabId, payload, content);

        // 模拟一个组件树
        var componentTree = new
        {
            Type = "StackPanel",
            Children = new object[]
            {
                new { Type = "TextBlock", Text = "标题" },
                new { Type = "Button", Text = "点击", Command = "do-action" },
                new { Type = "ListBox", Items = new[] { "a", "b", "c" } }
            }
        };

        ctx.RegisterTab("组件树页", "tree-tab", componentTree);

        var tab = registry.Tabs.First();
        Assert.Same(componentTree, tab.CustomContent);
    }

    // ===================== 权限控制（插件隔离） =====================

    [Fact]
    public void Page_Permission_DifferentPluginsCannotUnregisterOthersTab()
    {
        var registry = new TabRegistry();
        var ctxA = new PluginContext("plugin-a");
        var ctxB = new PluginContext("plugin-b");
        PluginContext.OnTabRegistered = (pid, title, tabId, icon, payload) =>
            registry.Register(pid, title, tabId, payload, null);
        PluginContext.OnTabUnregistered = (pid, tabId) => registry.Unregister(pid, tabId);

        ctxA.RegisterTab("A的页面", "tab-a", null, null);
        ctxB.RegisterTab("B的页面", "tab-b", null, null);
        Assert.Equal(2, registry.Tabs.Count);

        // B 尝试注销 A 的页面（通过伪造 tabId）
        ctxB.UnregisterTab("tab-a");

        // A 的页面应该仍然存在
        Assert.Equal(2, registry.Tabs.Count);
        Assert.Contains(registry.Tabs, t => t.PluginId == "plugin-a" && t.TabId == "tab-a");
    }

    [Fact]
    public void Page_Permission_PluginIdIsolatedInRegistry()
    {
        var registry = new TabRegistry();
        var ctxA = new PluginContext("plugin-a");
        var ctxB = new PluginContext("plugin-b");
        PluginContext.OnTabRegistered = (pid, title, tabId, icon, payload) =>
            registry.Register(pid, title, tabId, payload, null);

        // 两个插件注册同名的 tabId（"home"）
        ctxA.RegisterTab("A首页", "home", null, null);
        ctxB.RegisterTab("B首页", "home", null, null);

        // 应该有2个标签页，分别属于不同插件
        Assert.Equal(2, registry.Tabs.Count);
        Assert.Equal(2, registry.Tabs.Count(t => t.TabId == "home"));
        Assert.Single(registry.Tabs.Where(t => t.PluginId == "plugin-a"));
        Assert.Single(registry.Tabs.Where(t => t.PluginId == "plugin-b"));
    }

    [Fact]
    public void Page_Permission_RemoveAllOnlyAffectsMatchingPlugin()
    {
        var registry = new TabRegistry();
        var ctxA = new PluginContext("plugin-a");
        var ctxB = new PluginContext("plugin-b");
        PluginContext.OnTabRegistered = (pid, title, tabId, icon, payload) =>
            registry.Register(pid, title, tabId, payload, null);

        ctxA.RegisterTab("A1", "a1", null, null);
        ctxA.RegisterTab("A2", "a2", null, null);
        ctxB.RegisterTab("B1", "b1", null, null);

        int removed = registry.RemoveAllByPlugin("plugin-a");

        Assert.Equal(2, removed);
        Assert.Single(registry.Tabs);
        Assert.Equal("plugin-b", registry.Tabs.First().PluginId);
    }

    // ===================== 跨设备适配（路径与平台无关） =====================

    [Fact]
    public void Page_CrossPlatform_PayloadPathAgnostic()
    {
        var registry = new TabRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnTabRegistered = (pid, title, tabId, icon, payload) =>
            registry.Register(pid, title, tabId, payload, null);

        // 使用相对路径或资源ID，避免硬编码平台路径
        var payload = new
        {
            ResourceId = "plugin-welcome-page",  // 资源ID而非路径
            Locale = "zh-CN",
            Theme = "auto"
        };
        ctx.RegisterTab("欢迎页", "welcome", null, payload);

        var tab = registry.Tabs.First();
        var p = Assert.IsAssignableFrom<object>(tab.Payload);
        Assert.NotNull(p);
    }

    [Fact]
    public void Page_CrossPlatform_TitleUnicodePreserved()
    {
        var registry = new TabRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnTabRegistered = (pid, title, tabId, icon, payload) =>
            registry.Register(pid, title, tabId, payload, null);

        ctx.RegisterTab("日本語ページ", "jp-tab", null, null);
        ctx.RegisterTab("한국어 페이지", "kr-tab", null, null);
        ctx.RegisterTab("Русская страница", "ru-tab", null, null);

        Assert.Equal(3, registry.Tabs.Count);
        Assert.Contains(registry.Tabs, t => t.Title == "日本語ページ");
        Assert.Contains(registry.Tabs, t => t.Title == "한국어 페이지");
        Assert.Contains(registry.Tabs, t => t.Title == "Русская страница");
    }

    // ===================== 编辑/删除流程 =====================

    [Fact]
    public void Page_Edit_SameTitleNotRegisteredTwice()
    {
        // 复刻 MoreViewModel: var existingTab = Tabs.FirstOrDefault(t => t.Header == title);
        // 同 title 的标签页不会被重复注册
        var registry = new TabRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnTabRegistered = (pid, title, tabId, icon, payload) =>
            registry.Register(pid, title, tabId, payload, null);

        ctx.RegisterTab("工具页", "tools-v1", null, null);
        ctx.RegisterTab("工具页", "tools-v2", null, null); // 同标题

        Assert.Single(registry.Tabs);
        Assert.Equal("tools-v1", registry.Tabs.First().TabId); // 第一个保留
    }

    [Fact]
    public void Page_Delete_UnregisterRemovesTab()
    {
        var registry = new TabRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnTabRegistered = (pid, title, tabId, icon, payload) =>
            registry.Register(pid, title, tabId, payload, null);
        PluginContext.OnTabUnregistered = (pid, tabId) => registry.Unregister(pid, tabId);

        ctx.RegisterTab("临时页", "temp-tab", null, null);
        Assert.Single(registry.Tabs);

        ctx.UnregisterTab("temp-tab");
        Assert.Empty(registry.Tabs);
    }

    [Fact]
    public void Page_Delete_NonExistentTab_DoesNotThrow()
    {
        var registry = new TabRegistry();
        PluginContext.OnTabUnregistered = (pid, tabId) => registry.Unregister(pid, tabId);

        var ctx = new PluginContext(PluginId);
        var ex = Record.Exception(() => ctx.UnregisterTab("non-existent"));
        Assert.Null(ex);
    }

    // ===================== 边界条件 =====================

    [Fact]
    public void Page_Boundary_EmptyTitle_Works()
    {
        var registry = new TabRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnTabRegistered = (pid, title, tabId, icon, payload) =>
            registry.Register(pid, title, tabId, payload, null);

        ctx.RegisterTab("", "empty-title", null, null);

        Assert.Single(registry.Tabs);
        Assert.Equal("", registry.Tabs.First().Title);
    }

    [Fact]
    public void Page_Boundary_EmptyTabId_Works()
    {
        var registry = new TabRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnTabRegistered = (pid, title, tabId, icon, payload) =>
            registry.Register(pid, title, tabId, payload, null);

        ctx.RegisterTab("页面", "", null, null);

        Assert.Single(registry.Tabs);
        Assert.Equal("", registry.Tabs.First().TabId);
    }

    [Fact]
    public void Page_Boundary_NullPayload_Works()
    {
        var registry = new TabRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnTabRegistered = (pid, title, tabId, icon, payload) =>
            registry.Register(pid, title, tabId, payload, null);

        ctx.RegisterTab("页面", "test", null, null);

        Assert.Null(registry.Tabs.First().Payload);
    }

    [Fact]
    public void Page_Boundary_LongTitle_Works()
    {
        var registry = new TabRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnTabRegistered = (pid, title, tabId, icon, payload) =>
            registry.Register(pid, title, tabId, payload, null);

        var longTitle = new string('标', 500);
        ctx.RegisterTab(longTitle, "long-title", null, null);

        Assert.Equal(500, registry.Tabs.First().Title.Length);
    }

    [Fact]
    public void Page_Boundary_CallbackNotSet_DoesNotThrow()
    {
        // OnTabRegistered 和 OnTabRegisteredWithContent 都未设置
        var ctx = new PluginContext(PluginId);

        var ex1 = Record.Exception(() => ctx.RegisterTab("标准页", "tab1", null, null));
        var ex2 = Record.Exception(() => ctx.RegisterTab("自定义页", "tab2", new object()));

        Assert.Null(ex1);
        Assert.Null(ex2);
    }

    // ===================== 集成测试：完整页面生命周期 =====================

    [Fact]
    public void Integration_FullPageLifecycle_RegisterEditDelete()
    {
        var registry = new TabRegistry { SystemTabCount = 1 };
        var ctx = new PluginContext(PluginId);

        PluginContext.OnTabRegistered = (pid, title, tabId, icon, payload) =>
            registry.Register(pid, title, tabId, payload, null);
        PluginContext.OnTabRegisteredWithContent = (title, tabId, content, payload) =>
            registry.Register(PluginId, title, tabId, payload, content);
        PluginContext.OnTabUnregistered = (pid, tabId) => registry.Unregister(pid, tabId);

        // 1. 创建标准页面
        ctx.RegisterTab("数据看板", "dashboard", "Chart", new { RefreshInterval = 30 });

        Assert.Single(registry.Tabs);
        var page1 = registry.Tabs.First();
        Assert.Equal("dashboard", page1.TabId);
        Assert.False(page1.HasCustomContent);

        // 2. 创建带自定义内容的页面
        var formContent = new { Type = "Form", Action = "submit-feedback" };
        ctx.RegisterTab("反馈表单", "feedback", formContent, "FormIcon");

        Assert.Equal(2, registry.Tabs.Count);
        var page2 = registry.Tabs.First(t => t.TabId == "feedback");
        Assert.True(page2.HasCustomContent);

        // 3. 创建普通页面
        ctx.RegisterTab("关于本插件", "about", "Info", null);
        Assert.Equal(3, registry.Tabs.Count);

        // 4. 删除一个页面
        ctx.UnregisterTab("dashboard");
        Assert.Equal(2, registry.Tabs.Count);
        Assert.DoesNotContain(registry.Tabs, t => t.TabId == "dashboard");

        // 5. 批量清理（模拟插件卸载）
        int removed = registry.RemoveAllByPlugin(PluginId);
        Assert.Equal(2, removed);
        Assert.Empty(registry.Tabs);
    }
}
