using System;
using System.Linq;
using ObsMCLauncher.Core.Plugins;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// PluginSlotRegistry 的单元测试：注册/更新/排序/移除/按插件清理/未知槽位拒绝/变更通知。
/// 每个用例跑完都 ResetForTests，避免静态注册表互相污染。
/// </summary>
public class PluginSlotRegistryTests : IDisposable
{
    private const string Slot = "crash.dialog.actions";
    private const string AnotherSlot = "crash.page.toolbar";

    public PluginSlotRegistryTests() => PluginSlotRegistry.ResetForTests();

    public void Dispose() => PluginSlotRegistry.ResetForTests();

    [Fact]
    public void AddSlotContent_UnknownSlotId_StillAccepted()
    {
        // 槽位 id 是开放字符串：不在 KnownSlots 里也允许登记（等宿主出现才渲染），
        // 因为插件还有 GetUiRoot() 这条路可以自己挂载，注册表不该成为唯一的门
        var ok = PluginSlotRegistry.AddSlotContent("my.plugin.slot", "test.plugin", "btn", new object());

        Assert.True(ok);
        Assert.Equal(1, PluginSlotRegistry.TotalItemCount);
        Assert.Single(PluginSlotRegistry.GetSlotItems("my.plugin.slot"));
        // 但它不在"启动器保证有宿主"的名单里
        Assert.DoesNotContain("my.plugin.slot", PluginSlotRegistry.KnownSlots);
    }

    [Fact]
    public void AddSlotContent_EmptySlotId_Rejected()
    {
        Assert.False(PluginSlotRegistry.AddSlotContent("", "test.plugin", "btn", new object()));
        Assert.Equal(0, PluginSlotRegistry.TotalItemCount);
    }

    [Fact]
    public void AddSlotContent_NullContentOrEmptyId_ReturnsFalse()
    {
        Assert.False(PluginSlotRegistry.AddSlotContent(Slot, "test.plugin", "btn", null));
        Assert.False(PluginSlotRegistry.AddSlotContent(Slot, "test.plugin", "", new object()));
        Assert.Equal(0, PluginSlotRegistry.TotalItemCount);
    }

    [Fact]
    public void AddSlotContent_KnownSlot_RegistersAndNotifies()
    {
        var events = 0;
        PluginSlotRegistry.Changed += (_, _) => events++;

        var ok = PluginSlotRegistry.AddSlotContent(Slot, "test.plugin", "btn", "按钮", order: 5);

        Assert.True(ok);
        Assert.Equal(1, events);

        var items = PluginSlotRegistry.GetSlotItems(Slot);
        Assert.Single(items);
        Assert.Equal("test.plugin", items[0].PluginId);
        Assert.Equal("btn", items[0].ItemId);
        Assert.Equal(5, items[0].Order);
        Assert.Contains(Slot, PluginSlotRegistry.GetActiveSlotIds());
    }

    [Fact]
    public void AddSlotContent_SameItemId_UpdatesNotDuplicates()
    {
        PluginSlotRegistry.AddSlotContent(Slot, "test.plugin", "btn", "旧");
        PluginSlotRegistry.AddSlotContent(Slot, "test.plugin", "btn", "新");

        var items = PluginSlotRegistry.GetSlotItems(Slot);
        Assert.Single(items);
        Assert.Equal("新", items[0].Content);
    }

    [Fact]
    public void GetSlotItems_OrdersByOrderThenRegistrationSequence()
    {
        PluginSlotRegistry.AddSlotContent(Slot, "a", "3", "第三", order: 10);
        PluginSlotRegistry.AddSlotContent(Slot, "a", "1", "第一", order: -5);
        PluginSlotRegistry.AddSlotContent(Slot, "a", "2", "第二", order: -5);
        PluginSlotRegistry.AddSlotContent(Slot, "b", "0", "最先", order: -100);

        var labels = PluginSlotRegistry.GetSlotItems(Slot).Select(i => (string)i.Content).ToArray();

        Assert.Equal(new[] { "最先", "第一", "第二", "第三" }, labels);
    }

    [Fact]
    public void RemoveSlotContent_RemovesAndNotifies()
    {
        PluginSlotRegistry.AddSlotContent(Slot, "test.plugin", "btn", "按钮");
        PluginSlotChangedEventArgs? evt = null;
        PluginSlotRegistry.Changed += (_, e) => evt = e;

        var ok = PluginSlotRegistry.RemoveSlotContent(Slot, "test.plugin", "btn");

        Assert.True(ok);
        Assert.Empty(PluginSlotRegistry.GetSlotItems(Slot));
        Assert.NotNull(evt);
        Assert.True(evt!.Removed);
        Assert.False(PluginSlotRegistry.RemoveSlotContent(Slot, "test.plugin", "btn"));
    }

    [Fact]
    public void ClearSlotContent_OnlyClearsThatPluginInThatSlot()
    {
        PluginSlotRegistry.AddSlotContent(Slot, "a", "1", "a1");
        PluginSlotRegistry.AddSlotContent(Slot, "b", "1", "b1");
        PluginSlotRegistry.AddSlotContent(AnotherSlot, "a", "2", "a2");

        var removed = PluginSlotRegistry.ClearSlotContent(Slot, "a");

        Assert.Equal(1, removed);
        var rest = PluginSlotRegistry.GetSlotItems(Slot);
        Assert.Single(rest);
        Assert.Equal("b", rest[0].PluginId);
        Assert.Single(PluginSlotRegistry.GetSlotItems(AnotherSlot)); // 别的槽位没受影响
    }

    [Fact]
    public void ClearPluginSlotContent_ClearsEverySlotOfThatPlugin()
    {
        PluginSlotRegistry.AddSlotContent(Slot, "a", "1", "a1");
        PluginSlotRegistry.AddSlotContent(AnotherSlot, "a", "2", "a2");
        PluginSlotRegistry.AddSlotContent(Slot, "b", "1", "b1");

        var removed = PluginSlotRegistry.ClearPluginSlotContent("a");

        Assert.Equal(2, removed);
        Assert.Equal(1, PluginSlotRegistry.TotalItemCount);
        Assert.Empty(PluginSlotRegistry.GetSlotItems(AnotherSlot));
    }

    [Fact]
    public void ContextThroughPluginContext_WorksWithPluginIdNamespacing()
    {
        var ctx = new PluginContext("my.plugin");

        Assert.True(ctx.AddSlotContent(Slot, "btn", "按钮"));
        Assert.True(ctx.AddSlotContent("unknown.slot", "btn", "按钮")); // 开放 id：接受，但不保证有宿主

        var items = PluginSlotRegistry.GetSlotItems(Slot);
        Assert.Single(items);
        Assert.Equal("my.plugin", items[0].PluginId);

        Assert.True(ctx.RemoveSlotContent(Slot, "btn"));
        Assert.Empty(PluginSlotRegistry.GetSlotItems(Slot));
    }

    [Fact]
    public void GetSlotIds_ReturnsKnownSlots()
    {
        var ctx = new PluginContext("my.plugin");

        Assert.Equal(PluginSlotRegistry.KnownSlots, ctx.GetSlotIds());
        Assert.Contains(Slot, ctx.GetSlotIds());
    }

    [Fact]
    public void RemovePluginSlots_ClearsLikePluginUnload()
    {
        PluginSlotRegistry.AddSlotContent(Slot, "a", "1", "a1");
        PluginSlotRegistry.AddSlotContent(Slot, "b", "1", "b1");

        var removed = PluginContext.RemovePluginSlots("a");

        Assert.Equal(1, removed);
        Assert.Equal(1, PluginSlotRegistry.TotalItemCount);
    }
}
