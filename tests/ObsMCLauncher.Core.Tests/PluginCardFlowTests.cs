using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Plugins;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 验证自建卡片（插件主页卡片）的完整生命周期：
/// 创建 -> 编辑 -> 删除 -> 交互（命令执行）-> 批量清理
/// 使用与 HomeViewModel 等价的内存模型，避免依赖 Avalonia/UI 线程
/// </summary>
public class PluginCardFlowTests : IDisposable
{
    /// <summary>
    /// 简化版 HomeViewModel 卡片集合，使用与 HomeViewModel.OnPluginCardRegistered
    /// 等价的注册/注销逻辑（详见 HomeViewModel.cs#L291-L364）
    /// </summary>
    private class CardRegistry
    {
        public ObservableCollection<HomeCardInfo> Cards { get; } = new();

        public void Register(string cardId, string title, string description,
            string? icon, string? commandId, object? payload, bool isEnabled = true)
        {
            var existing = Cards.FirstOrDefault(c => c.CardId == cardId);
            if (existing != null)
            {
                existing.Title = title;
                existing.Description = description;
                existing.Icon = icon;
                existing.CommandId = commandId;
                existing.Payload = payload;
                existing.IsEnabled = isEnabled;
            }
            else
            {
                Cards.Add(new HomeCardInfo
                {
                    CardId = cardId,
                    Title = title,
                    Description = description,
                    Icon = icon,
                    CommandId = commandId,
                    Payload = payload,
                    IsPluginCard = true,
                    PluginId = cardId.Split('.')[0],
                    IsEnabled = isEnabled
                });
            }
        }

        public bool Unregister(string cardId)
        {
            var card = Cards.FirstOrDefault(c => c.CardId == cardId);
            if (card != null && card.IsPluginCard)
            {
                Cards.Remove(card);
                return true;
            }
            return false;
        }

        public int RemoveAllByPlugin(string pluginId)
        {
            var toRemove = Cards.Where(c => c.IsPluginCard && c.PluginId == pluginId).ToList();
            foreach (var c in toRemove) Cards.Remove(c);
            return toRemove.Count;
        }
    }

    private const string PluginId = "card-plugin";

    public PluginCardFlowTests()
    {
        // 清理本测试类可能注册的命令（防止并行测试间状态污染）
        PluginContext.RemovePluginCommands(PluginId);
        PluginContext.RemovePluginCommands("plugin-a");
        PluginContext.RemovePluginCommands("plugin-b");
    }

    public void Dispose()
    {
        PluginContext.RemovePluginCommands(PluginId);
        PluginContext.RemovePluginCommands("plugin-a");
        PluginContext.RemovePluginCommands("plugin-b");
    }

    // ===================== 创建流程 =====================

    [Fact]
    public void Card_Create_NewCardAppearsInRegistry()
    {
        var registry = new CardRegistry();
        var ctx = new PluginContext(PluginId);

        // 通过 PluginContext 回调触发注册
        PluginContext.OnHomeCardRegistered = (cardId, title, desc, icon, cmd, payload) =>
            registry.Register(cardId, title, desc, icon, cmd, payload);

        ctx.RegisterHomeCard("daily-tip", "每日提示", "今天也要好好挖矿", "Star", "show-tip", null);

        Assert.Single(registry.Cards);
        var card = registry.Cards.First();
        Assert.Equal($"{PluginId}.daily-tip", card.CardId);
        Assert.Equal("每日提示", card.Title);
        Assert.Equal("Star", card.Icon);
        Assert.True(card.IsPluginCard);
        Assert.Equal(PluginId, card.PluginId);
    }

    [Fact]
    public void Card_Create_MultipleCards_AllRegistered()
    {
        var registry = new CardRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnHomeCardRegistered = (id, t, d, i, c, p) => registry.Register(id, t, d, i, c, p);

        ctx.RegisterHomeCard("card1", "卡片1", "desc1", null, null, null);
        ctx.RegisterHomeCard("card2", "卡片2", "desc2", null, null, null);
        ctx.RegisterHomeCard("card3", "卡片3", "desc3", null, null, null);

        Assert.Equal(3, registry.Cards.Count);
    }

    [Fact]
    public void Card_Create_IdIsPrefixedWithPluginId()
    {
        var registry = new CardRegistry();
        var ctx = new PluginContext("my-plugin");
        PluginContext.OnHomeCardRegistered = (id, t, d, i, c, p) => registry.Register(id, t, d, i, c, p);

        ctx.RegisterHomeCard("feature1", "功能1", "desc", null, null, null);

        Assert.Equal("my-plugin.feature1", registry.Cards.First().CardId);
    }

    // ===================== 编辑流程 =====================

    [Fact]
    public void Card_Edit_ExistingCardUpdated()
    {
        var registry = new CardRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnHomeCardRegistered = (id, t, d, i, c, p) => registry.Register(id, t, d, i, c, p);

        // 第一次注册
        ctx.RegisterHomeCard("edit-test", "原标题", "原描述", null, null, null);
        var original = registry.Cards.First();
        Assert.Equal("原标题", original.Title);

        // 同 cardId 再次注册 = 编辑
        ctx.RegisterHomeCard("edit-test", "新标题", "新描述", "NewIcon", "new-cmd", "new-payload");
        Assert.Single(registry.Cards); // 仍是1张卡片，没有新增

        var edited = registry.Cards.First();
        Assert.Equal("新标题", edited.Title);
        Assert.Equal("新描述", edited.Description);
        Assert.Equal("NewIcon", edited.Icon);
        Assert.Equal("new-cmd", edited.CommandId);
        Assert.Equal("new-payload", edited.Payload);
    }

    [Fact]
    public void Card_Edit_PreservesCardIdAndPluginId()
    {
        var registry = new CardRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnHomeCardRegistered = (id, t, d, i, c, p) => registry.Register(id, t, d, i, c, p);

        ctx.RegisterHomeCard("card1", "原标题", "原描述", null, null, null);
        ctx.RegisterHomeCard("card1", "新标题", "新描述", null, null, null);

        var card = registry.Cards.First();
        Assert.Equal($"{PluginId}.card1", card.CardId);
        Assert.Equal(PluginId, card.PluginId);
        Assert.True(card.IsPluginCard);
    }

    // ===================== 删除流程 =====================

    [Fact]
    public void Card_Delete_RemovesFromRegistry()
    {
        var registry = new CardRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnHomeCardRegistered = (id, t, d, i, c, p) => registry.Register(id, t, d, i, c, p);
        PluginContext.OnHomeCardUnregistered = id => registry.Unregister(id);

        ctx.RegisterHomeCard("delete-test", "测试", "desc", null, null, null);
        Assert.Single(registry.Cards);

        ctx.UnregisterHomeCard("delete-test");
        Assert.Empty(registry.Cards);
    }

    [Fact]
    public void Card_Delete_NonExistent_DoesNotThrow()
    {
        var registry = new CardRegistry();
        PluginContext.OnHomeCardUnregistered = id => registry.Unregister(id);

        var ctx = new PluginContext(PluginId);
        var ex = Record.Exception(() => ctx.UnregisterHomeCard("non-existent"));
        Assert.Null(ex);
    }

    [Fact]
    public void Card_Delete_DoesNotAffectOtherCards()
    {
        var registry = new CardRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnHomeCardRegistered = (id, t, d, i, c, p) => registry.Register(id, t, d, i, c, p);
        PluginContext.OnHomeCardUnregistered = id => registry.Unregister(id);

        ctx.RegisterHomeCard("card1", "卡片1", "desc", null, null, null);
        ctx.RegisterHomeCard("card2", "卡片2", "desc", null, null, null);
        ctx.RegisterHomeCard("card3", "卡片3", "desc", null, null, null);

        ctx.UnregisterHomeCard("card2");

        Assert.Equal(2, registry.Cards.Count);
        Assert.Contains(registry.Cards, c => c.CardId == $"{PluginId}.card1");
        Assert.Contains(registry.Cards, c => c.CardId == $"{PluginId}.card3");
        Assert.DoesNotContain(registry.Cards, c => c.CardId == $"{PluginId}.card2");
    }

    // ===================== 交互流程（命令执行） =====================

    [Fact]
    public void Card_Interact_ClickTriggersRegisteredCommand()
    {
        var ctx = new PluginContext(PluginId);
        object? receivedPayload = null;
        ctx.RegisterCommand("open-feature", payload => receivedPayload = payload);

        // 模拟卡片点击，命令ID格式为 "command:{pluginId}.{commandId}"
        var cardCommandId = $"command:{PluginId}.open-feature";
        var fullCommandId = cardCommandId.Substring(8); // 去掉 "command:" 前缀

        bool executed = PluginContext.ExecuteCommand(fullCommandId, "click-data");

        Assert.True(executed);
        Assert.Equal("click-data", receivedPayload);
    }

    [Fact]
    public void Card_Interact_CommandNotRegistered_ReturnsFalse()
    {
        // 未注册任何命令
        var result = PluginContext.ExecuteCommand($"{PluginId}.non-existent", null);
        Assert.False(result);
    }

    [Fact]
    public void Card_Interact_PayloadPassedToCommand()
    {
        var ctx = new PluginContext(PluginId);
        Dictionary<string, object>? received = null;
        ctx.RegisterCommand("complex-cmd", payload => received = payload as Dictionary<string, object>);

        var payload = new Dictionary<string, object> { ["action"] = "navigate", ["target"] = "settings" };
        PluginContext.ExecuteCommand($"{PluginId}.complex-cmd", payload);

        Assert.NotNull(received);
        Assert.Equal("navigate", received!["action"]);
        Assert.Equal("settings", received["target"]);
    }

    // ===================== 批量清理流程 =====================

    [Fact]
    public void Card_Cleanup_RemoveAllByPlugin_RemovesOnlyMatchingPlugin()
    {
        var registry = new CardRegistry();
        var ctxA = new PluginContext("plugin-a");
        var ctxB = new PluginContext("plugin-b");
        PluginContext.OnHomeCardRegistered = (id, t, d, i, c, p) => registry.Register(id, t, d, i, c, p);

        ctxA.RegisterHomeCard("c1", "卡片1", "desc", null, null, null);
        ctxA.RegisterHomeCard("c2", "卡片2", "desc", null, null, null);
        ctxB.RegisterHomeCard("c3", "卡片3", "desc", null, null, null);

        Assert.Equal(3, registry.Cards.Count);

        int removed = registry.RemoveAllByPlugin("plugin-a");

        Assert.Equal(2, removed);
        Assert.Single(registry.Cards);
        Assert.Equal("plugin-b.c3", registry.Cards.First().CardId);
    }

    [Fact]
    public void Card_Cleanup_NonExistentPlugin_ReturnsZero()
    {
        var registry = new CardRegistry();
        registry.Register("plugin-a.c1", "卡片", "desc", null, null, null);

        int removed = registry.RemoveAllByPlugin("non-existent");
        Assert.Equal(0, removed);
        Assert.Single(registry.Cards);
    }

    // ===================== 边界条件 =====================

    [Fact]
    public void Card_Boundary_SpecialCharactersInTitle_Works()
    {
        var registry = new CardRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnHomeCardRegistered = (id, t, d, i, c, p) => registry.Register(id, t, d, i, c, p);

        ctx.RegisterHomeCard("special", "<script>alert('xss')</script>", "desc", null, null, null);

        Assert.Equal("<script>alert('xss')</script>", registry.Cards.First().Title);
        // 注意：UI层应负责HTML转义，Core层只负责存储
    }

    [Fact]
    public void Card_Boundary_LongTitle_Works()
    {
        var registry = new CardRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnHomeCardRegistered = (id, t, d, i, c, p) => registry.Register(id, t, d, i, c, p);

        var longTitle = new string('A', 1000);
        ctx.RegisterHomeCard("long", longTitle, "desc", null, null, null);

        Assert.Equal(1000, registry.Cards.First().Title.Length);
    }

    [Fact]
    public void Card_Boundary_NullIcon_NullCommand_Works()
    {
        var registry = new CardRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnHomeCardRegistered = (id, t, d, i, c, p) => registry.Register(id, t, d, i, c, p);

        ctx.RegisterHomeCard("minimal", "标题", "描述"); // icon/command/payload 都为 null

        var card = registry.Cards.First();
        Assert.Null(card.Icon);
        Assert.Null(card.CommandId);
        Assert.Null(card.Payload);
        Assert.True(card.IsEnabled); // 默认启用
    }

    [Fact]
    public void Card_Boundary_DisabledCard_StillRegistered()
    {
        var registry = new CardRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnHomeCardRegistered = (id, t, d, i, c, p) => registry.Register(id, t, d, i, c, p, isEnabled: false);

        ctx.RegisterHomeCard("disabled", "禁用卡片", "desc", null, null, null);

        var card = registry.Cards.First();
        Assert.False(card.IsEnabled);
        // 即使被禁用，卡片仍然在集合中
        Assert.Single(registry.Cards);
    }

    // ===================== 集成测试：卡片+命令协同 =====================

    [Fact]
    public void Integration_CardWithCommand_ClickExecutesCommand()
    {
        var registry = new CardRegistry();
        var ctx = new PluginContext(PluginId);
        PluginContext.OnHomeCardRegistered = (id, t, d, i, c, p) => registry.Register(id, t, d, i, c, p);

        // 1. 注册命令
        int clickCount = 0;
        ctx.RegisterCommand("open-detail", _ => clickCount++);

        // 2. 注册带命令的卡片
        ctx.RegisterHomeCard("detail-card", "详情卡片", "点击查看详情", "Info", "open-detail", null);

        var card = registry.Cards.First();
        Assert.Equal("open-detail", card.CommandId);

        // 3. 模拟用户点击：从 CommandId 提取并执行
        var fullCommandId = $"{PluginId}.{card.CommandId}";
        PluginContext.ExecuteCommand(fullCommandId, null);
        PluginContext.ExecuteCommand(fullCommandId, null);
        PluginContext.ExecuteCommand(fullCommandId, null);

        Assert.Equal(3, clickCount);

        // 4. 卸载插件，命令失效
        PluginContext.RemovePluginCommands(PluginId);
        bool result = PluginContext.ExecuteCommand(fullCommandId, null);
        Assert.False(result);
    }
}
