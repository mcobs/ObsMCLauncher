using ObsMCLauncher.Core.Plugins;
using ObsMCLauncher.Core.Plugins.Events;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 测试插件事件系统的核心功能：事件名称常量、事件数据模型、事件订阅和触发
/// </summary>
public class PluginEventTests
{
    /// <summary>
    /// 创建一个PluginContext实例用于测试
    /// </summary>
    private PluginContext CreateContext()
    {
        return new PluginContext("test-plugin");
    }

    [Fact]
    public void EventNames_VersionInstalling_HasCorrectValue()
    {
        Assert.Equal("VersionInstalling", IPluginContext.EventNames.VersionInstalling);
    }

    [Fact]
    public void EventNames_VersionInstalled_HasCorrectValue()
    {
        Assert.Equal("VersionInstalled", IPluginContext.EventNames.VersionInstalled);
    }

    [Fact]
    public void EventNames_AccountChanged_HasCorrectValue()
    {
        Assert.Equal("AccountChanged", IPluginContext.EventNames.AccountChanged);
    }

    [Fact]
    public void EventNames_DownloadProgress_HasCorrectValue()
    {
        Assert.Equal("DownloadProgress", IPluginContext.EventNames.DownloadProgress);
    }

    [Fact]
    public void EventNames_GameLaunched_HasCorrectValue()
    {
        Assert.Equal("GameLaunched", IPluginContext.EventNames.GameLaunched);
    }

    [Fact]
    public void EventNames_VersionSelected_HasCorrectValue()
    {
        Assert.Equal("VersionSelected", IPluginContext.EventNames.VersionSelected);
    }

    [Fact]
    public void EventNames_UiReady_HasCorrectValue()
    {
        Assert.Equal("UiReady", IPluginContext.EventNames.UiReady);
    }

    [Fact]
    public void NotifyUiReady_MarksReadyAndFiresExactlyOnce()
    {
        // UiReady 是进程级一次性状态：先清一次，保证起点确定
        PluginContext.ClearAllStateForTests();

        var ctx = CreateContext();
        var calls = new List<object?>();
        void Handler(object? data) => calls.Add(data);
        ctx.SubscribeEvent(IPluginContext.EventNames.UiReady, Handler);

        try
        {
            Assert.False(ctx.IsUiReady);

            var root = new object();
            PluginContext.NotifyUiReady(root);

            // 第二次调用应被忽略：窗口重开 / 页面重新导航都不该重复广播
            PluginContext.NotifyUiReady(new object());

            Assert.True(ctx.IsUiReady);

            var args = Assert.IsType<PluginUiReadyEventArgs>(Assert.Single(calls));
            Assert.Same(root, args.Root);
        }
        finally
        {
            ctx.UnsubscribeEvent(IPluginContext.EventNames.UiReady, Handler);
            PluginContext.ClearAllStateForTests();
        }
    }

    [Fact]
    public void NotifyUiReady_NullRoot_StillMarksReadyAndFires()
    {
        // 无 UI 宿主（如测试环境）时 Root 为 null，状态仍要正确置位，插件据此跳过
        PluginContext.ClearAllStateForTests();

        var ctx = CreateContext();
        object? captured = null;
        void Handler(object? data) => captured = data;
        ctx.SubscribeEvent(IPluginContext.EventNames.UiReady, Handler);

        try
        {
            PluginContext.NotifyUiReady(null);

            Assert.True(ctx.IsUiReady);
            var args = Assert.IsType<PluginUiReadyEventArgs>(captured);
            Assert.Null(args.Root);
        }
        finally
        {
            ctx.UnsubscribeEvent(IPluginContext.EventNames.UiReady, Handler);
            PluginContext.ClearAllStateForTests();
        }
    }

    [Fact]
    public void TriggerGlobalEvent_VersionSelected_FiresEventWithData()
    {
        var ctx = CreateContext();
        object? capturedData = null;
        ctx.SubscribeEvent(IPluginContext.EventNames.VersionSelected, data => capturedData = data);

        var args = new VersionSelectedEventArgs
        {
            VersionId = "1.21.4-Fabric",
            PreviousVersionId = "1.20.1-Forge"
        };

        PluginContext.TriggerGlobalEvent(IPluginContext.EventNames.VersionSelected, args);

        Assert.NotNull(capturedData);
        var result = Assert.IsType<VersionSelectedEventArgs>(capturedData);
        Assert.Equal("1.21.4-Fabric", result.VersionId);
        Assert.Equal("1.20.1-Forge", result.PreviousVersionId);
    }

    [Fact]
    public void VersionSelectedEventArgs_DefaultValues_AreCorrect()
    {
        var args = new VersionSelectedEventArgs();
        Assert.Equal("", args.VersionId);
        Assert.Equal("", args.PreviousVersionId);
    }

    [Fact]
    public void EventNames_GameClosed_HasCorrectValue()
    {
        Assert.Equal("GameClosed", IPluginContext.EventNames.GameClosed);
    }

    [Fact]
    public void EventNames_VersionDownloaded_HasCorrectValue()
    {
        Assert.Equal("VersionDownloaded", IPluginContext.EventNames.VersionDownloaded);
    }

    [Fact]
    public void TriggerGlobalEvent_VersionInstalling_FiresEventWithData()
    {
        var ctx = CreateContext();
        object? capturedData = null;
        ctx.SubscribeEvent(IPluginContext.EventNames.VersionInstalling, data => capturedData = data);

        var args = new VersionInstallingEventArgs
        {
            McVersion = "1.21.4",
            VersionName = "1.21.4-Forge",
            LoaderType = "forge",
            LoaderVersion = "51.0.43",
            GameDirectory = @"C:\Users\test\.minecraft"
        };

        PluginContext.TriggerGlobalEvent(IPluginContext.EventNames.VersionInstalling, args);

        Assert.NotNull(capturedData);
        var result = Assert.IsType<VersionInstallingEventArgs>(capturedData);
        Assert.Equal("1.21.4", result.McVersion);
        Assert.Equal("1.21.4-Forge", result.VersionName);
        Assert.Equal("forge", result.LoaderType);
        Assert.Equal("51.0.43", result.LoaderVersion);
        Assert.Equal(@"C:\Users\test\.minecraft", result.GameDirectory);
    }

    [Fact]
    public void TriggerGlobalEvent_VersionInstalled_Success_FiresEventWithData()
    {
        var ctx = CreateContext();
        object? capturedData = null;
        ctx.SubscribeEvent(IPluginContext.EventNames.VersionInstalled, data => capturedData = data);

        var args = new VersionInstalledEventArgs
        {
            McVersion = "1.21.4",
            VersionName = "1.21.4-Forge",
            LoaderType = "forge",
            LoaderVersion = "51.0.43",
            GameDirectory = @"C:\Users\test\.minecraft",
            VersionDirectory = @"C:\Users\test\.minecraft\versions\1.21.4-Forge",
            Success = true
        };

        PluginContext.TriggerGlobalEvent(IPluginContext.EventNames.VersionInstalled, args);

        Assert.NotNull(capturedData);
        var result = Assert.IsType<VersionInstalledEventArgs>(capturedData);
        Assert.Equal("1.21.4", result.McVersion);
        Assert.Equal(@"C:\Users\test\.minecraft\versions\1.21.4-Forge", result.VersionDirectory);
        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void TriggerGlobalEvent_VersionInstalled_Failure_FiresEventWithData()
    {
        var ctx = CreateContext();
        object? capturedData = null;
        ctx.SubscribeEvent(IPluginContext.EventNames.VersionInstalled, data => capturedData = data);

        var args = new VersionInstalledEventArgs
        {
            McVersion = "1.21.4",
            VersionName = "1.21.4-Forge",
            LoaderType = "forge",
            GameDirectory = @"C:\Users\test\.minecraft",
            Success = false,
            ErrorMessage = "安装失败：网络错误"
        };

        PluginContext.TriggerGlobalEvent(IPluginContext.EventNames.VersionInstalled, args);

        Assert.NotNull(capturedData);
        var result = Assert.IsType<VersionInstalledEventArgs>(capturedData);
        Assert.False(result.Success);
        Assert.Equal("安装失败：网络错误", result.ErrorMessage);
    }

    [Fact]
    public void TriggerGlobalEvent_AccountChanged_Switched_FiresEventWithData()
    {
        var ctx = CreateContext();
        object? capturedData = null;
        ctx.SubscribeEvent(IPluginContext.EventNames.AccountChanged, data => capturedData = data);

        var args = new AccountChangedEventArgs
        {
            ChangeType = AccountChangeType.Switched,
            AccountId = "acc-123",
            Username = "TestPlayer",
            AccountType = "Microsoft"
        };

        PluginContext.TriggerGlobalEvent(IPluginContext.EventNames.AccountChanged, args);

        Assert.NotNull(capturedData);
        var result = Assert.IsType<AccountChangedEventArgs>(capturedData);
        Assert.Equal(AccountChangeType.Switched, result.ChangeType);
        Assert.Equal("acc-123", result.AccountId);
        Assert.Equal("TestPlayer", result.Username);
        Assert.Equal("Microsoft", result.AccountType);
    }

    [Fact]
    public void TriggerGlobalEvent_AccountChanged_Added_FiresEventWithData()
    {
        var ctx = CreateContext();
        object? capturedData = null;
        ctx.SubscribeEvent(IPluginContext.EventNames.AccountChanged, data => capturedData = data);

        var args = new AccountChangedEventArgs
        {
            ChangeType = AccountChangeType.Added,
            AccountId = "acc-456",
            Username = "OfflinePlayer",
            AccountType = "Offline"
        };

        PluginContext.TriggerGlobalEvent(IPluginContext.EventNames.AccountChanged, args);

        Assert.NotNull(capturedData);
        var result = Assert.IsType<AccountChangedEventArgs>(capturedData);
        Assert.Equal(AccountChangeType.Added, result.ChangeType);
        Assert.Equal("Offline", result.AccountType);
    }

    [Fact]
    public void TriggerGlobalEvent_AccountChanged_Removed_FiresEventWithData()
    {
        var ctx = CreateContext();
        object? capturedData = null;
        ctx.SubscribeEvent(IPluginContext.EventNames.AccountChanged, data => capturedData = data);

        var args = new AccountChangedEventArgs
        {
            ChangeType = AccountChangeType.Removed,
            AccountId = "acc-789",
            Username = "DeletedPlayer",
            AccountType = "Yggdrasil"
        };

        PluginContext.TriggerGlobalEvent(IPluginContext.EventNames.AccountChanged, args);

        Assert.NotNull(capturedData);
        var result = Assert.IsType<AccountChangedEventArgs>(capturedData);
        Assert.Equal(AccountChangeType.Removed, result.ChangeType);
    }

    [Fact]
    public void TriggerGlobalEvent_DownloadProgress_Downloading_FiresEventWithData()
    {
        var ctx = CreateContext();
        object? capturedData = null;
        ctx.SubscribeEvent(IPluginContext.EventNames.DownloadProgress, data => capturedData = data);

        var args = new DownloadProgressEventArgs
        {
            TaskId = "task-001",
            TaskName = "下载 Forge 1.21.4",
            TaskType = "Version",
            Progress = 55.5,
            StatusMessage = "正在下载库文件...",
            DownloadSpeed = 1024 * 1024 * 2.5,
            Status = DownloadStatus.Downloading
        };

        PluginContext.TriggerGlobalEvent(IPluginContext.EventNames.DownloadProgress, args);

        Assert.NotNull(capturedData);
        var result = Assert.IsType<DownloadProgressEventArgs>(capturedData);
        Assert.Equal("task-001", result.TaskId);
        Assert.Equal("下载 Forge 1.21.4", result.TaskName);
        Assert.Equal("Version", result.TaskType);
        Assert.Equal(55.5, result.Progress);
        Assert.Equal("正在下载库文件...", result.StatusMessage);
        Assert.Equal(DownloadStatus.Downloading, result.Status);
    }

    [Fact]
    public void TriggerGlobalEvent_DownloadProgress_Completed_FiresEventWithData()
    {
        var ctx = CreateContext();
        object? capturedData = null;
        ctx.SubscribeEvent(IPluginContext.EventNames.DownloadProgress, data => capturedData = data);

        var args = new DownloadProgressEventArgs
        {
            TaskId = "task-002",
            TaskName = "补全资源",
            TaskType = "Resource",
            Progress = 100,
            Status = DownloadStatus.Completed
        };

        PluginContext.TriggerGlobalEvent(IPluginContext.EventNames.DownloadProgress, args);

        Assert.NotNull(capturedData);
        var result = Assert.IsType<DownloadProgressEventArgs>(capturedData);
        Assert.Equal(100, result.Progress);
        Assert.Equal(DownloadStatus.Completed, result.Status);
    }

    [Fact]
    public void TriggerGlobalEvent_DownloadProgress_Failed_FiresEventWithData()
    {
        var ctx = CreateContext();
        object? capturedData = null;
        ctx.SubscribeEvent(IPluginContext.EventNames.DownloadProgress, data => capturedData = data);

        var args = new DownloadProgressEventArgs
        {
            TaskId = "task-003",
            TaskName = "下载 Mod",
            TaskType = "Mod",
            Progress = 30,
            StatusMessage = "连接超时",
            Status = DownloadStatus.Failed
        };

        PluginContext.TriggerGlobalEvent(IPluginContext.EventNames.DownloadProgress, args);

        Assert.NotNull(capturedData);
        var result = Assert.IsType<DownloadProgressEventArgs>(capturedData);
        Assert.Equal(DownloadStatus.Failed, result.Status);
        Assert.Equal("连接超时", result.StatusMessage);
    }

    [Fact]
    public void TriggerGlobalEvent_MultipleHandlers_AllInvoked()
    {
        var ctx = CreateContext();
        int callCount = 0;
        ctx.SubscribeEvent(IPluginContext.EventNames.VersionInstalling, _ => callCount++);
        ctx.SubscribeEvent(IPluginContext.EventNames.VersionInstalling, _ => callCount++);

        PluginContext.TriggerGlobalEvent(IPluginContext.EventNames.VersionInstalling, null);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public void TriggerGlobalEvent_NoSubscribers_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            PluginContext.TriggerGlobalEvent("NonExistentEvent_" + Guid.NewGuid(), null));

        Assert.Null(exception);
    }

    [Fact]
    public void TriggerGlobalEvent_HandlerThrows_OtherHandlersStillInvoked()
    {
        var ctx = CreateContext();
        int secondCallCount = 0;
        ctx.SubscribeEvent(IPluginContext.EventNames.AccountChanged, _ => throw new InvalidOperationException("test error"));
        ctx.SubscribeEvent(IPluginContext.EventNames.AccountChanged, _ => secondCallCount++);

        PluginContext.TriggerGlobalEvent(IPluginContext.EventNames.AccountChanged, null);

        // 第二个handler应该仍然被调用（PluginContext内部catch了异常）
        Assert.Equal(1, secondCallCount);
    }

    [Fact]
    public void VersionInstallingEventArgs_DefaultValues_AreCorrect()
    {
        var args = new VersionInstallingEventArgs();
        Assert.Equal("", args.McVersion);
        Assert.Equal("", args.VersionName);
        Assert.Equal("vanilla", args.LoaderType);
        Assert.Null(args.LoaderVersion);
        Assert.Equal("", args.GameDirectory);
    }

    [Fact]
    public void VersionInstalledEventArgs_DefaultValues_AreCorrect()
    {
        var args = new VersionInstalledEventArgs();
        Assert.Equal("", args.McVersion);
        Assert.Equal("", args.VersionName);
        Assert.Equal("vanilla", args.LoaderType);
        Assert.Null(args.LoaderVersion);
        Assert.Equal("", args.GameDirectory);
        Assert.Equal("", args.VersionDirectory);
        Assert.False(args.Success);
        Assert.Null(args.ErrorMessage);
    }

    [Fact]
    public void AccountChangedEventArgs_DefaultValues_AreCorrect()
    {
        var args = new AccountChangedEventArgs();
        Assert.Equal(AccountChangeType.Switched, args.ChangeType);
        Assert.Equal("", args.AccountId);
        Assert.Equal("", args.Username);
        Assert.Equal("", args.AccountType);
    }

    [Fact]
    public void DownloadProgressEventArgs_DefaultValues_AreCorrect()
    {
        var args = new DownloadProgressEventArgs();
        Assert.Equal("", args.TaskId);
        Assert.Equal("", args.TaskName);
        Assert.Equal("", args.TaskType);
        Assert.Equal(0, args.Progress);
        Assert.Null(args.StatusMessage);
        Assert.Equal(0, args.DownloadSpeed);
        Assert.Equal(DownloadStatus.Downloading, args.Status);
    }

    [Fact]
    public void PluginContext_PluginDataDirectory_ContainsPluginId()
    {
        var ctx = new PluginContext("my-test-plugin");
        Assert.Contains("my-test-plugin", ctx.PluginDataDirectory);
    }

    /// <summary>
    /// 固化数据目录位置：必须与插件安装目录（OMCL/plugins）分离——
    /// 两者重合时"更新插件"会连带删掉用户配置。
    /// </summary>
    [Fact]
    public void PluginContext_PluginDataDirectory_IsSeparateFromInstallDirectory()
    {
        const string pluginId = "my-test-plugin";
        var expected = Path.Combine(VersionInfo.GetAppBaseDirectory(), "OMCL", "config", "plugins", pluginId);

        var ctx = new PluginContext(pluginId);
        Assert.Equal(expected, ctx.PluginDataDirectory);
        Assert.Equal(expected, PluginContext.GetPluginDataDirectory(pluginId));

        var installDir = Path.Combine(VersionInfo.GetAppBaseDirectory(), "OMCL", "plugins", pluginId);
        Assert.NotEqual(installDir, ctx.PluginDataDirectory);
        Assert.False(ctx.PluginDataDirectory.StartsWith(
            Path.Combine(VersionInfo.GetAppBaseDirectory(), "OMCL", "plugins") + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PluginContext_LauncherVersion_IsNotEmpty()
    {
        var ctx = CreateContext();
        Assert.NotEmpty(ctx.LauncherVersion);
    }

    [Fact]
    public void PluginContext_LauncherBaseDirectory_IsNotEmpty()
    {
        var ctx = CreateContext();
        Assert.False(string.IsNullOrEmpty(ctx.LauncherBaseDirectory));
        Assert.EndsWith(Path.DirectorySeparatorChar.ToString(), ctx.LauncherBaseDirectory);
    }

    [Fact]
    public void PluginContext_LauncherDataDirectory_IsUnderBaseDirectory()
    {
        var ctx = CreateContext();
        Assert.Equal(Path.Combine(ctx.LauncherBaseDirectory, "OMCL"), ctx.LauncherDataDirectory);
    }

    [Fact]
    public void PluginContext_GameDirectory_IsNotEmpty()
    {
        var ctx = CreateContext();
        Assert.False(string.IsNullOrEmpty(ctx.GameDirectory));
    }
}
