using System;
using System.Collections.Generic;
using System.Linq;
using ObsMCLauncher.Core.Plugins;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 新增插件扩展API的单元/集成/边界测试
/// 覆盖：LogMessage / GetInstalledVersions / GetCurrentAccount /
///      RegisterGameLaunchHook / RequestDownload
/// </summary>
public class PluginExtendedApiTests : IDisposable
{
    private const string PluginId = "ext-api-test";

    public PluginExtendedApiTests()
    {
        // 仅清理本测试类可能注册的启动钩子（防止并行测试间状态污染）
        PluginContext.RemovePluginLaunchHooks(PluginId);
        PluginContext.RemovePluginLaunchHooks("plugin-a");
        PluginContext.RemovePluginLaunchHooks("plugin-b");
        PluginContext.RemovePluginLaunchHooks("plugin-z");
        PluginContext.RemovePluginLaunchHooks("integration-plugin");
        ResetAllCallbacks();
    }

    public void Dispose()
    {
        PluginContext.RemovePluginLaunchHooks(PluginId);
        PluginContext.RemovePluginLaunchHooks("plugin-a");
        PluginContext.RemovePluginLaunchHooks("plugin-b");
        PluginContext.RemovePluginLaunchHooks("plugin-z");
        PluginContext.RemovePluginLaunchHooks("integration-plugin");
        ResetAllCallbacks();
    }

    private static void ResetAllCallbacks()
    {
        PluginContext.OnLogMessage = null;
        PluginContext.OnGetInstalledVersions = null;
        PluginContext.OnGetCurrentAccount = null;
        PluginContext.OnRequestDownload = null;
    }

    private static PluginContext CreateContext(string pluginId = PluginId)
        => new(pluginId);

    // ===================== LogMessage =====================

    [Theory]
    [InlineData(PluginLogLevel.Debug, "调试信息")]
    [InlineData(PluginLogLevel.Info, "提示信息")]
    [InlineData(PluginLogLevel.Warning, "警告信息")]
    [InlineData(PluginLogLevel.Error, "错误信息")]
    public void LogMessage_NormalInput_InvokesCallback(PluginLogLevel level, string message)
    {
        PluginLogLevel? receivedLevel = null;
        string? receivedMsg = null;
        string? receivedId = null;
        PluginContext.OnLogMessage = (pid, lvl, msg) =>
        {
            receivedId = pid;
            receivedLevel = lvl;
            receivedMsg = msg;
        };

        var ctx = CreateContext();
        ctx.LogMessage(level, message);

        Assert.Equal(PluginId, receivedId);
        Assert.Equal(level, receivedLevel);
        Assert.Equal(message, receivedMsg);
    }

    [Fact]
    public void LogMessage_EmptyMessage_DoesNotInvokeCallback()
    {
        bool invoked = false;
        PluginContext.OnLogMessage = (_, _, _) => invoked = true;

        var ctx = CreateContext();
        ctx.LogMessage(PluginLogLevel.Info, "");

        Assert.False(invoked);
    }

    [Fact]
    public void LogMessage_NullMessage_DoesNotInvokeCallback()
    {
        bool invoked = false;
        PluginContext.OnLogMessage = (_, _, _) => invoked = true;

        var ctx = CreateContext();
        ctx.LogMessage(PluginLogLevel.Info, null!);

        Assert.False(invoked);
    }

    [Fact]
    public void LogMessage_CallbackNotSet_DoesNotThrow()
    {
        var ctx = CreateContext();
        var ex = Record.Exception(() => ctx.LogMessage(PluginLogLevel.Info, "test"));
        Assert.Null(ex);
    }

    [Fact]
    public void LogMessage_CallbackThrows_DoesNotPropagate()
    {
        PluginContext.OnLogMessage = (_, _, _) => throw new InvalidOperationException("boom");
        var ctx = CreateContext();
        var ex = Record.Exception(() => ctx.LogMessage(PluginLogLevel.Info, "test"));
        Assert.Null(ex);
    }

    // ===================== GetInstalledVersions =====================

    [Fact]
    public void GetInstalledVersions_ReturnsDataFromCallback()
    {
        var expected = new List<PluginVersionInfo>
        {
            new() { VersionId = "1.20.1-Forge", McVersion = "1.20.1", LoaderType = "forge" },
            new() { VersionId = "1.21.4", McVersion = "1.21.4", LoaderType = "vanilla" }
        };
        PluginContext.OnGetInstalledVersions = _ => expected;

        var ctx = CreateContext();
        var result = ctx.GetInstalledVersions();

        Assert.Equal(2, result.Count);
        Assert.Equal("1.20.1-Forge", result[0].VersionId);
        Assert.Equal("forge", result[0].LoaderType);
        Assert.Equal("1.21.4", result[1].McVersion);
    }

    [Fact]
    public void GetInstalledVersions_CallbackReturnsNull_ReturnsEmptyList()
    {
        PluginContext.OnGetInstalledVersions = _ => null!;
        var ctx = CreateContext();

        var result = ctx.GetInstalledVersions();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetInstalledVersions_CallbackNotSet_ReturnsEmptyList()
    {
        var ctx = CreateContext();
        var result = ctx.GetInstalledVersions();
        Assert.Empty(result);
    }

    [Fact]
    public void GetInstalledVersions_CallbackThrows_ReturnsEmptyList()
    {
        PluginContext.OnGetInstalledVersions = _ => throw new InvalidOperationException("err");
        var ctx = CreateContext();
        var result = ctx.GetInstalledVersions();
        Assert.Empty(result);
    }

    [Fact]
    public void GetInstalledVersions_PluginIdPassedToCallback()
    {
        string? receivedId = null;
        PluginContext.OnGetInstalledVersions = pid =>
        {
            receivedId = pid;
            return Array.Empty<PluginVersionInfo>();
        };

        var ctx = CreateContext("my-plugin-123");
        ctx.GetInstalledVersions();

        Assert.Equal("my-plugin-123", receivedId);
    }

    // ===================== GetCurrentAccount =====================

    [Fact]
    public void GetCurrentAccount_ReturnsAccountInfo()
    {
        var expected = new PluginAccountInfo
        {
            AccountId = "acc-001",
            Username = "Steve",
            AccountType = "Microsoft",
            UUID = "abcdef0123456789",
            IsDefault = true
        };
        PluginContext.OnGetCurrentAccount = () => expected;

        var ctx = CreateContext();
        var result = ctx.GetCurrentAccount();

        Assert.NotNull(result);
        Assert.Equal("Steve", result!.Username);
        Assert.Equal("Microsoft", result.AccountType);
        Assert.True(result.IsDefault);
    }

    [Fact]
    public void GetCurrentAccount_CallbackReturnsNull_ReturnsNull()
    {
        PluginContext.OnGetCurrentAccount = () => null;
        var ctx = CreateContext();
        Assert.Null(ctx.GetCurrentAccount());
    }

    [Fact]
    public void GetCurrentAccount_CallbackNotSet_ReturnsNull()
    {
        var ctx = CreateContext();
        Assert.Null(ctx.GetCurrentAccount());
    }

    [Fact]
    public void GetCurrentAccount_CallbackThrows_ReturnsNull()
    {
        PluginContext.OnGetCurrentAccount = () => throw new InvalidOperationException("err");
        var ctx = CreateContext();
        Assert.Null(ctx.GetCurrentAccount());
    }

    // ===================== RegisterGameLaunchHook =====================

    [Fact]
    public void RegisterLaunchHook_NormalInput_RegistersHook()
    {
        var ctx = CreateContext();
        int countBefore = PluginContext.GetRegisteredHookCount();

        ctx.RegisterGameLaunchHook("before-hook", GameLaunchPhase.BeforeLaunch, _ => { });

        Assert.Equal(countBefore + 1, PluginContext.GetRegisteredHookCount());
    }

    [Fact]
    public void RegisterLaunchHook_SameHookIdOverwrites()
    {
        var ctx = CreateContext();
        ctx.RegisterGameLaunchHook("hook1", GameLaunchPhase.BeforeLaunch, _ => { });
        ctx.RegisterGameLaunchHook("hook1", GameLaunchPhase.AfterLaunch, _ => { });

        Assert.Equal(1, PluginContext.GetRegisteredHookCount());
    }

    [Fact]
    public void RegisterLaunchHook_NullHandler_DoesNotRegister()
    {
        var ctx = CreateContext();
        int before = PluginContext.GetRegisteredHookCount();

        ctx.RegisterGameLaunchHook("hook1", GameLaunchPhase.BeforeLaunch, null!);

        Assert.Equal(before, PluginContext.GetRegisteredHookCount());
    }

    [Fact]
    public void RegisterLaunchHook_EmptyHookId_DoesNotRegister()
    {
        var ctx = CreateContext();
        int before = PluginContext.GetRegisteredHookCount();

        ctx.RegisterGameLaunchHook("", GameLaunchPhase.BeforeLaunch, _ => { });

        Assert.Equal(before, PluginContext.GetRegisteredHookCount());
    }

    [Fact]
    public void UnregisterLaunchHook_RemovesSpecificHook()
    {
        var ctx = CreateContext();
        ctx.RegisterGameLaunchHook("hook1", GameLaunchPhase.BeforeLaunch, _ => { });
        ctx.RegisterGameLaunchHook("hook2", GameLaunchPhase.AfterLaunch, _ => { });
        Assert.Equal(2, PluginContext.GetRegisteredHookCount());

        ctx.UnregisterGameLaunchHook("hook1");

        Assert.Equal(1, PluginContext.GetRegisteredHookCount());
    }

    [Fact]
    public void UnregisterLaunchHook_EmptyId_DoesNotThrow()
    {
        var ctx = CreateContext();
        var ex = Record.Exception(() => ctx.UnregisterGameLaunchHook(""));
        Assert.Null(ex);
    }

    [Fact]
    public void RemovePluginLaunchHooks_RemovesOnlyPluginHooks()
    {
        var ctxA = CreateContext("plugin-a");
        var ctxB = CreateContext("plugin-b");
        ctxA.RegisterGameLaunchHook("h1", GameLaunchPhase.BeforeLaunch, _ => { });
        ctxA.RegisterGameLaunchHook("h2", GameLaunchPhase.OnExited, _ => { });
        ctxB.RegisterGameLaunchHook("h3", GameLaunchPhase.BeforeLaunch, _ => { });
        Assert.Equal(3, PluginContext.GetRegisteredHookCount());

        PluginContext.RemovePluginLaunchHooks("plugin-a");

        Assert.Equal(1, PluginContext.GetRegisteredHookCount());
    }

    [Fact]
    public void TriggerLaunchHooks_BeforeLaunch_HandlerInvokedWithContext()
    {
        var ctx = CreateContext();
        GameLaunchHookContext? received = null;
        ctx.RegisterGameLaunchHook("h1", GameLaunchPhase.BeforeLaunch, c => received = c);

        var input = new GameLaunchHookContext { VersionId = "1.20.1", McVersion = "1.20.1" };
        var result = PluginContext.TriggerGameLaunchHooks(GameLaunchPhase.BeforeLaunch, input);

        Assert.Same(input, received);
        Assert.Same(input, result);
    }

    [Fact]
    public void TriggerLaunchHooks_CancelLaunch_StopsSubsequentHooks()
    {
        var ctx = CreateContext("plugin-z"); // z 排序在后
        int callCount = 0;

        var ctxA = CreateContext("plugin-a"); // a 排序在前
        ctxA.RegisterGameLaunchHook("cancel", GameLaunchPhase.BeforeLaunch, c =>
        {
            callCount++;
            c.CancelLaunch = true;
        });
        ctx.RegisterGameLaunchHook("after-cancel", GameLaunchPhase.BeforeLaunch, _ => callCount++);

        PluginContext.TriggerGameLaunchHooks(GameLaunchPhase.BeforeLaunch, new GameLaunchHookContext());

        Assert.Equal(1, callCount); // 只有第一个被调用
    }

    [Fact]
    public void TriggerLaunchHooks_DifferentPhase_NotInvoked()
    {
        var ctx = CreateContext();
        int callCount = 0;
        ctx.RegisterGameLaunchHook("h1", GameLaunchPhase.OnCrash, _ => callCount++);

        PluginContext.TriggerGameLaunchHooks(GameLaunchPhase.BeforeLaunch, new GameLaunchHookContext());

        Assert.Equal(0, callCount);
    }

    [Fact]
    public void TriggerLaunchHooks_HandlerThrows_OthersStillInvoked()
    {
        var ctxA = CreateContext("plugin-a");
        var ctxB = CreateContext("plugin-b");
        int secondCall = 0;
        ctxA.RegisterGameLaunchHook("err", GameLaunchPhase.OnExited, _ => throw new InvalidOperationException("boom"));
        ctxB.RegisterGameLaunchHook("ok", GameLaunchPhase.OnExited, _ => secondCall++);

        PluginContext.TriggerGameLaunchHooks(GameLaunchPhase.OnExited, new GameLaunchHookContext());

        Assert.Equal(1, secondCall);
    }

    [Fact]
    public void TriggerLaunchHooks_ModifiesContext_ExtraArgumentsPropagated()
    {
        var ctx = CreateContext();
        ctx.RegisterGameLaunchHook("add-args", GameLaunchPhase.BeforeLaunch, c =>
        {
            c.ExtraJvmArguments.Add("-Xmx4g");
            c.ExtraGameArguments.Add("--demo");
        });

        var input = new GameLaunchHookContext();
        var result = PluginContext.TriggerGameLaunchHooks(GameLaunchPhase.BeforeLaunch, input);

        Assert.Contains("-Xmx4g", result.ExtraJvmArguments);
        Assert.Contains("--demo", result.ExtraGameArguments);
    }

    [Fact]
    public void TriggerLaunchHooks_NullContext_ReturnsNewContext()
    {
        var result = PluginContext.TriggerGameLaunchHooks(GameLaunchPhase.BeforeLaunch, null!);
        Assert.NotNull(result);
    }

    // ===================== RequestDownload =====================

    [Fact]
    public void RequestDownload_ValidRequest_ReturnsTaskId()
    {
        PluginContext.OnRequestDownload = (pid, req) => "task-001";
        var ctx = CreateContext();

        var req = new PluginDownloadRequest
        {
            Url = "https://example.com/file.zip",
            FileName = "file.zip",
            TargetDirectory = "/tmp",
            TaskName = "测试下载"
        };
        var result = ctx.RequestDownload(req);

        Assert.Equal("task-001", result);
    }

    [Fact]
    public void RequestDownload_PluginIdPassedToCallback()
    {
        string? receivedId = null;
        PluginContext.OnRequestDownload = (pid, req) =>
        {
            receivedId = pid;
            return "task-001";
        };

        var ctx = CreateContext("my-plugin");
        ctx.RequestDownload(new PluginDownloadRequest
        {
            Url = "https://example.com/file.zip",
            FileName = "file.zip",
            TargetDirectory = "/tmp"
        });

        Assert.Equal("my-plugin", receivedId);
    }

    [Fact]
    public void RequestDownload_NullRequest_ReturnsEmpty()
    {
        var ctx = CreateContext();
        var result = ctx.RequestDownload(null!);
        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RequestDownload_EmptyUrl_ReturnsEmpty(string? url)
    {
        var ctx = CreateContext();
        var req = new PluginDownloadRequest
        {
            Url = url!,
            FileName = "file.zip",
            TargetDirectory = "/tmp"
        };
        Assert.Equal(string.Empty, ctx.RequestDownload(req));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RequestDownload_EmptyFileName_ReturnsEmpty(string fileName)
    {
        var ctx = CreateContext();
        var req = new PluginDownloadRequest
        {
            Url = "https://example.com/file.zip",
            FileName = fileName,
            TargetDirectory = "/tmp"
        };
        Assert.Equal(string.Empty, ctx.RequestDownload(req));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RequestDownload_EmptyTargetDirectory_ReturnsEmpty(string dir)
    {
        var ctx = CreateContext();
        var req = new PluginDownloadRequest
        {
            Url = "https://example.com/file.zip",
            FileName = "file.zip",
            TargetDirectory = dir
        };
        Assert.Equal(string.Empty, ctx.RequestDownload(req));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/file.zip")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/plain;base64,SGVsbG8=")]
    public void RequestDownload_NonHttpProtocol_ReturnsEmpty(string url)
    {
        var ctx = CreateContext();
        var req = new PluginDownloadRequest
        {
            Url = url,
            FileName = "file.zip",
            TargetDirectory = "/tmp"
        };
        Assert.Equal(string.Empty, ctx.RequestDownload(req));
    }

    [Theory]
    [InlineData("file/with/slash.zip")]
    [InlineData("path\\backslash.zip")]
    [InlineData("C:drive.zip")]
    public void RequestDownload_FileNameWithSeparator_ReturnsEmpty(string fileName)
    {
        var ctx = CreateContext();
        var req = new PluginDownloadRequest
        {
            Url = "https://example.com/file.zip",
            FileName = fileName,
            TargetDirectory = "/tmp"
        };
        Assert.Equal(string.Empty, ctx.RequestDownload(req));
    }

    [Fact]
    public void RequestDownload_CallbackReturnsNull_ReturnsEmpty()
    {
        PluginContext.OnRequestDownload = (_, _) => null!;
        var ctx = CreateContext();
        var req = new PluginDownloadRequest
        {
            Url = "https://example.com/file.zip",
            FileName = "file.zip",
            TargetDirectory = "/tmp"
        };
        Assert.Equal(string.Empty, ctx.RequestDownload(req));
    }

    [Fact]
    public void RequestDownload_CallbackNotSet_ReturnsEmpty()
    {
        var ctx = CreateContext();
        var req = new PluginDownloadRequest
        {
            Url = "https://example.com/file.zip",
            FileName = "file.zip",
            TargetDirectory = "/tmp"
        };
        Assert.Equal(string.Empty, ctx.RequestDownload(req));
    }

    [Fact]
    public void RequestDownload_CallbackThrows_ReturnsEmpty()
    {
        PluginContext.OnRequestDownload = (_, _) => throw new InvalidOperationException("err");
        var ctx = CreateContext();
        var req = new PluginDownloadRequest
        {
            Url = "https://example.com/file.zip",
            FileName = "file.zip",
            TargetDirectory = "/tmp"
        };
        Assert.Equal(string.Empty, ctx.RequestDownload(req));
    }

    [Fact]
    public void RequestDownload_HttpProtocol_Accepted()
    {
        PluginContext.OnRequestDownload = (_, _) => "task-002";
        var ctx = CreateContext();
        var req = new PluginDownloadRequest
        {
            Url = "http://example.com/file.zip",
            FileName = "file.zip",
            TargetDirectory = "/tmp"
        };
        Assert.Equal("task-002", ctx.RequestDownload(req));
    }

    [Fact]
    public void RequestDownload_HttpsUpperCase_Accepted()
    {
        PluginContext.OnRequestDownload = (_, _) => "task-003";
        var ctx = CreateContext();
        var req = new PluginDownloadRequest
        {
            Url = "HTTPS://example.com/file.zip",
            FileName = "file.zip",
            TargetDirectory = "/tmp"
        };
        Assert.Equal("task-003", ctx.RequestDownload(req));
    }

    [Fact]
    public void RequestDownload_WithSha1_PassedToCallback()
    {
        string? receivedSha1 = null;
        PluginContext.OnRequestDownload = (_, req) =>
        {
            receivedSha1 = req.Sha1;
            return "task-004";
        };

        var ctx = CreateContext();
        ctx.RequestDownload(new PluginDownloadRequest
        {
            Url = "https://example.com/file.zip",
            FileName = "file.zip",
            TargetDirectory = "/tmp",
            Sha1 = "abc123"
        });

        Assert.Equal("abc123", receivedSha1);
    }

    // ===================== 集成测试：模拟完整插件加载流程 =====================

    [Fact]
    public void Integration_FullPluginLifecycle_WorksCorrectly()
    {
        // 模拟插件加载时注册所有内容
        var logs = new List<(PluginLogLevel, string)>();
        PluginContext.OnLogMessage = (_, level, msg) => logs.Add((level, msg));
        PluginContext.OnGetInstalledVersions = _ => new List<PluginVersionInfo>
        {
            new() { VersionId = "1.21.4", McVersion = "1.21.4", LoaderType = "fabric" }
        };
        PluginContext.OnGetCurrentAccount = () => new PluginAccountInfo
        {
            Username = "Player1",
            AccountType = "Microsoft"
        };
        PluginContext.OnRequestDownload = (_, _) => "integration-task";

        var ctx = CreateContext("integration-plugin");

        // 1. 写日志
        ctx.LogMessage(PluginLogLevel.Info, "插件已加载");
        Assert.Single(logs);
        Assert.Equal("插件已加载", logs[0].Item2);

        // 2. 获取版本列表
        var versions = ctx.GetInstalledVersions();
        Assert.Single(versions);

        // 3. 获取账户
        var account = ctx.GetCurrentAccount();
        Assert.Equal("Player1", account!.Username);

        // 4. 注册启动钩子
        GameLaunchHookContext? hookCtx = null;
        ctx.RegisterGameLaunchHook("before", GameLaunchPhase.BeforeLaunch, c =>
        {
            hookCtx = c;
            c.ExtraJvmArguments.Add("-Dplugin.active=true");
        });

        // 5. 触发启动钩子
        var launchCtx = new GameLaunchHookContext { VersionId = "1.21.4" };
        PluginContext.TriggerGameLaunchHooks(GameLaunchPhase.BeforeLaunch, launchCtx);

        Assert.Same(launchCtx, hookCtx);
        Assert.Contains("-Dplugin.active=true", launchCtx.ExtraJvmArguments);

        // 6. 提交下载
        var taskId = ctx.RequestDownload(new PluginDownloadRequest
        {
            Url = "https://example.com/mod.jar",
            FileName = "mod.jar",
            TargetDirectory = "/tmp/mods"
        });
        Assert.Equal("integration-task", taskId);

        // 7. 卸载插件
        ctx.UnregisterGameLaunchHook("before");
        Assert.Equal(0, PluginContext.GetRegisteredHookCount());
    }
}
