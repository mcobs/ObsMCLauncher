using ObsMCLauncher.Core.Plugins;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 测试插件命令注册、执行、注销和清理功能
/// </summary>
public class PluginCommandTests
{
    private PluginContext CreateContext(string pluginId = "test-plugin")
    {
        return new PluginContext(pluginId);
    }

    [Fact]
    public void RegisterCommand_AndExecute_InvokesHandler()
    {
        var ctx = CreateContext("cmd-test-1");
        object? receivedPayload = null;
        ctx.RegisterCommand("my-action", payload => receivedPayload = payload);

        var result = PluginContext.ExecuteCommand("cmd-test-1.my-action", "test-data");

        Assert.True(result);
        Assert.Equal("test-data", receivedPayload);
    }

    [Fact]
    public void RegisterCommand_WithNullPayload_InvokesHandler()
    {
        var ctx = CreateContext("cmd-test-2");
        object? receivedPayload = "not-null";
        ctx.RegisterCommand("null-action", payload => receivedPayload = payload);

        var result = PluginContext.ExecuteCommand("cmd-test-2.null-action", null);

        Assert.True(result);
        Assert.Null(receivedPayload);
    }

    [Fact]
    public void RegisterCommand_ComplexPayload_PassedCorrectly()
    {
        var ctx = CreateContext("cmd-test-3");
        object? receivedPayload = null;
        ctx.RegisterCommand("complex-action", payload => receivedPayload = payload);

        var payload = new Dictionary<string, object>
        {
            ["key"] = "value",
            ["number"] = 42
        };

        var result = PluginContext.ExecuteCommand("cmd-test-3.complex-action", payload);

        Assert.True(result);
        Assert.Same(payload, receivedPayload);
    }

    [Fact]
    public void ExecuteCommand_UnknownCommand_ReturnsFalse()
    {
        var result = PluginContext.ExecuteCommand("nonexistent.command", null);
        Assert.False(result);
    }

    [Fact]
    public void UnregisterCommand_CommandNoLongerExecutable()
    {
        var ctx = CreateContext("cmd-test-4");
        int callCount = 0;
        ctx.RegisterCommand("temp-action", _ => callCount++);

        // 先确认能执行
        var result1 = PluginContext.ExecuteCommand("cmd-test-4.temp-action", null);
        Assert.True(result1);
        Assert.Equal(1, callCount);

        // 注销后不能再执行
        ctx.UnregisterCommand("temp-action");
        var result2 = PluginContext.ExecuteCommand("cmd-test-4.temp-action", null);
        Assert.False(result2);
        Assert.Equal(1, callCount); // 仍然是1，没有增加
    }

    [Fact]
    public void RegisterCommand_SameCommandIdOverwrites()
    {
        var ctx = CreateContext("cmd-test-5");
        int firstCallCount = 0;
        int secondCallCount = 0;

        ctx.RegisterCommand("overwrite-action", _ => firstCallCount++);
        ctx.RegisterCommand("overwrite-action", _ => secondCallCount++);

        var result = PluginContext.ExecuteCommand("cmd-test-5.overwrite-action", null);

        Assert.True(result);
        Assert.Equal(0, firstCallCount); // 被覆盖，不再调用
        Assert.Equal(1, secondCallCount); // 新handler被调用
    }

    [Fact]
    public void RemovePluginCommands_RemovesAllCommandsForPlugin()
    {
        var ctx = CreateContext("cmd-test-6");
        ctx.RegisterCommand("action-a", _ => { });
        ctx.RegisterCommand("action-b", _ => { });
        ctx.RegisterCommand("action-c", _ => { });

        // 确认命令存在
        Assert.True(PluginContext.ExecuteCommand("cmd-test-6.action-a", null));
        Assert.True(PluginContext.ExecuteCommand("cmd-test-6.action-b", null));
        Assert.True(PluginContext.ExecuteCommand("cmd-test-6.action-c", null));

        // 移除插件所有命令
        PluginContext.RemovePluginCommands("cmd-test-6");

        // 确认命令已移除
        Assert.False(PluginContext.ExecuteCommand("cmd-test-6.action-a", null));
        Assert.False(PluginContext.ExecuteCommand("cmd-test-6.action-b", null));
        Assert.False(PluginContext.ExecuteCommand("cmd-test-6.action-c", null));
    }

    [Fact]
    public void RemovePluginCommands_DoesNotAffectOtherPlugins()
    {
        var ctx1 = CreateContext("plugin-a");
        var ctx2 = CreateContext("plugin-b");

        ctx1.RegisterCommand("shared-name", _ => { });
        ctx2.RegisterCommand("shared-name", _ => { });

        Assert.True(PluginContext.ExecuteCommand("plugin-a.shared-name", null));
        Assert.True(PluginContext.ExecuteCommand("plugin-b.shared-name", null));

        // 只移除 plugin-a 的命令
        PluginContext.RemovePluginCommands("plugin-a");

        Assert.False(PluginContext.ExecuteCommand("plugin-a.shared-name", null));
        Assert.True(PluginContext.ExecuteCommand("plugin-b.shared-name", null));

        // 清理
        PluginContext.RemovePluginCommands("plugin-b");
    }

    [Fact]
    public void ExecuteCommand_HandlerThrows_ReturnsFalse()
    {
        var ctx = CreateContext("cmd-test-7");
        ctx.RegisterCommand("error-action", _ => throw new InvalidOperationException("test error"));

        var result = PluginContext.ExecuteCommand("cmd-test-7.error-action", null);

        Assert.False(result);
    }

    [Fact]
    public void RegisterCommand_MultiplePluginsDifferentPrefixes()
    {
        var ctx1 = CreateContext("my-plugin");
        var ctx2 = CreateContext("other-plugin");

        string? received1 = null;
        string? received2 = null;

        ctx1.RegisterCommand("do-thing", _ => received1 = "from-plugin-1");
        ctx2.RegisterCommand("do-thing", _ => received2 = "from-plugin-2");

        PluginContext.ExecuteCommand("my-plugin.do-thing", null);
        Assert.Equal("from-plugin-1", received1);
        Assert.Null(received2);

        PluginContext.ExecuteCommand("other-plugin.do-thing", null);
        Assert.Equal("from-plugin-1", received1);
        Assert.Equal("from-plugin-2", received2);

        // 清理
        PluginContext.RemovePluginCommands("my-plugin");
        PluginContext.RemovePluginCommands("other-plugin");
    }

    [Fact]
    public void RegisterCommand_CardCommandIdFormat()
    {
        // 验证卡片命令ID格式：command:{pluginId}.{commandId}
        var ctx = CreateContext("demo-plugin");
        ctx.RegisterCommand("open-settings", _ => { });

        // 卡片上的 CommandId 应该是 "command:demo-plugin.open-settings"
        var cardCommandId = $"command:demo-plugin.open-settings";
        Assert.StartsWith("command:", cardCommandId);

        // 从卡片命令ID中提取完整命令ID
        var fullCommandId = cardCommandId.Substring(8);
        Assert.Equal("demo-plugin.open-settings", fullCommandId);

        var result = PluginContext.ExecuteCommand(fullCommandId, null);
        Assert.True(result);

        // 清理
        PluginContext.RemovePluginCommands("demo-plugin");
    }
}
