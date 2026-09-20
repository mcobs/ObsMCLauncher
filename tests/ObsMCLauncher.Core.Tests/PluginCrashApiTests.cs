using System;
using System.IO;
using System.Linq;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Plugins;
using ObsMCLauncher.Core.Services.Crash;
using ObsMCLauncher.Core.Utils;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 崩溃插件 API（PluginContext 上的崩溃数据 / 版本 / 事件）与脱敏的单元测试。
/// 不依赖 UI：所有方法都在 Core 内完成。
/// </summary>
public class PluginCrashApiTests : IDisposable
{
    private static readonly Func<string> OriginalGameDirectoryProvider;

    static PluginCrashApiTests()
    {
        // GameDirectoryProvider 是 internal 静态可注入点，测试之间要还原
        OriginalGameDirectoryProvider = PluginContext.GameDirectoryProvider;
    }

    private readonly PluginContext _ctx = new("test.crashplugin");

    public PluginCrashApiTests()
    {
        PluginSlotRegistry.ResetForTests();
        PluginContext.OnGetActiveCrashContext = null;
        PluginContext.OnGetSlotHost = null;
        PluginContext.OnGetUiRoot = null;
        PluginContext.OnFindControlByName = null;
        PluginContext.OnRunOnUiThread = null;
    }

    public void Dispose()
    {
        PluginSlotRegistry.ResetForTests();
        PluginContext.GameDirectoryProvider = OriginalGameDirectoryProvider;
        PluginContext.OnGetActiveCrashContext = null;
        PluginContext.OnGetSlotHost = null;
        PluginContext.OnGetUiRoot = null;
        PluginContext.OnFindControlByName = null;
        PluginContext.OnRunOnUiThread = null;
    }

    private const string OomReport = """
        ---- Minecraft Crash Report ----
        // Don't be sad, have a hug! <3

        Time: 2026-09-20 10:00:00
        Description: Exception in server tick loop

        java.lang.OutOfMemoryError: Java heap space
        	at net.minecraft.server.MinecraftServer.tick(MinecraftServer.java:100)
        	at net.minecraft.client.main.Main.main(Main.java:50)

        A detailed walkthrough of the error, its code path and all known details is as follows:
        ---------------------------------------------------------------------------------------

        -- System Details --
        Details:
        	Minecraft Version: 1.20.1
        	Operating System: Windows 11 (amd64) version 10.0
        	Java Version: 17.0.8, Microsoft
        """;

    private static string WriteTempFile(string content, string extension = ".txt")
    {
        var path = Path.Combine(Path.GetTempPath(), "omcl_plugintest_" + Guid.NewGuid() + extension);
        File.WriteAllText(path, content);
        return path;
    }

    // ---------------- 版本与能力 ----------------

    [Fact]
    public void ApiVersion_IsCurrentPluginApiVersion()
    {
        Assert.Equal(PluginApi.Version, _ctx.ApiVersion);
    }

    [Fact]
    public void ApiVersion_MatchesLauncherMajorVersion()
    {
        // 约定：ApiVersion = 启动器版本的主版本号（v1.2.3 → 1，带后缀时取主干）
        var expected = int.Parse(VersionInfo.Version.Split('-', 2)[0].Split('.')[0]);

        Assert.Equal(expected, _ctx.ApiVersion);
    }

    [Fact]
    public void ApiVersion_SharesMajorWithLauncherVersion()
    {
        // 与 LauncherVersion（完整字符串）也要对得上，避免两处取不同来源
        var majorFromLauncher = int.Parse(_ctx.LauncherVersion.Split('-', 2)[0].Split('.')[0]);

        Assert.Equal(majorFromLauncher, _ctx.ApiVersion);
    }

    [Fact]
    public void EventNames_CrashDetected_IsStable()
    {
        // 事件名是插件契约的一部分，改了就是破坏插件
        Assert.Equal("CrashDetected", IPluginContext.EventNames.CrashDetected);
    }

    // ---------------- 读取与脱敏 ----------------

    [Fact]
    public void Sanitize_StripsUserNamesAndTokens()
    {
        var raw = @"--username mcbbser --accessToken abc123token "
                  + @"--gameDir C:\Users\mcbbser\AppData\Roaming\.minecraft "
                  + @"Setting user: mcbbser "
                  + @"http://x.example/refresh?accessToken=t0k3n9";

        var sanitized = CrashReportSanitizer.Sanitize(raw);

        Assert.DoesNotContain("mcbbser", sanitized);
        Assert.DoesNotContain("abc123token", sanitized);
        Assert.DoesNotContain("t0k3n9", sanitized);
        Assert.Contains("<username>", sanitized);
        Assert.Contains("<token>", sanitized);
        Assert.Contains(@"C:\Users\<user>\", sanitized);
    }

    [Fact]
    public void Sanitize_LeavesUnixHomeUserToo()
    {
        var raw = "/home/mcbbser/.minecraft/crash-reports and /Users/mcbbser/Library";

        var sanitized = CrashReportSanitizer.Sanitize(raw);

        Assert.DoesNotContain("mcbbser", sanitized);
        Assert.Contains("/home/<user>/", sanitized);
        Assert.Contains("/Users/<user>/", sanitized);
    }

    [Fact]
    public void ReadCrashReportText_TruncatesAndSanitizes()
    {
        var path = WriteTempFile("--username mcbbser " + new string('x', 5000));
        try
        {
            var text = _ctx.ReadCrashReportText(path, maxChars: 2000, sanitize: true);

            Assert.NotNull(text);
            Assert.True(text!.Length <= 2000, $"应截断到 2000 字符，实际 {text.Length}");
            Assert.DoesNotContain("mcbbser", text);

            var unsanitized = _ctx.ReadCrashReportText(path, maxChars: 2000, sanitize: false);
            Assert.Contains("mcbbser", unsanitized);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadCrashReportText_MissingFile_ReturnsNull()
    {
        Assert.Null(_ctx.ReadCrashReportText(Path.Combine(Path.GetTempPath(), "not_exist_" + Guid.NewGuid())));
    }

    // ---------------- 分析 ----------------

    [Fact]
    public void AnalyzeCrashReport_ReturnsMappedDto()
    {
        var path = WriteTempFile(OomReport);
        try
        {
            var result = _ctx.AnalyzeCrashReport(path);

            Assert.NotNull(result);
            Assert.Equal(path, result!.ReportPath);
            Assert.Equal("1.20.1", result.MinecraftVersion);
            Assert.Contains("内存", result.Headline);
            Assert.NotEmpty(result.Causes);
            Assert.Equal("Memory", result.Causes[0].Category);
            Assert.Equal("High", result.Causes[0].Confidence);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnalyzeCrashReport_MissingFile_ReturnsNull()
    {
        Assert.Null(_ctx.AnalyzeCrashReport(Path.Combine(Path.GetTempPath(), "not_exist_" + Guid.NewGuid())));
    }

    [Fact]
    public void Mapper_SanitizesPreviewAndEvidence()
    {
        var internalResult = new CrashAnalysisResult
        {
            FileName = "crash-x.txt",
            Kind = CrashReportKind.MinecraftCrashReport,
            MinecraftVersion = "1.20.1",
            RawPreview = "--username mcbbser at C:\\Users\\mcbbser\\x",
        };
        internalResult.Causes.Add(new CrashCause
        {
            Category = "Memory",
            Title = "内存不足",
            // 证据常引用报告原文里的路径（含用户名）
            Evidence = @"Problematic frame: C:\Users\mcbbser\.minecraft\jvm.dll",
            Suggestion = "加内存",
            Confidence = CrashConfidence.High
        });

        var dto = PluginCrashMapper.ToPlugin(internalResult);

        Assert.DoesNotContain("mcbbser", dto.RawPreview);
        Assert.DoesNotContain("mcbbser", dto.Causes[0].Evidence);
        Assert.Contains(@"C:\Users\<user>\", dto.Causes[0].Evidence);
        Assert.Equal("High", dto.Causes[0].Confidence);
    }

    [Fact]
    public void Mapper_FromGameCrashInfo_MarksNotFound()
    {
        var info = new GameCrashInfo
        {
            VersionId = "1.20.1",
            ExitCode = -1073741819,
            IsCrash = true,
            ReportFound = false
        };

        var dto = PluginCrashMapper.ToPlugin(info);

        Assert.False(dto.ReportFound);
        Assert.Equal(-1073741819, dto.ExitCode);
        Assert.Equal("1.20.1", dto.VersionId);
        Assert.Equal(string.Empty, dto.ReportPath);
    }

    // ---------------- 列表 ----------------

    [Fact]
    public void GetCrashReports_UsesInjectedGameDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "omcl_plugincrash_" + Guid.NewGuid());
        try
        {
            var crashDir = Path.Combine(root, "crash-reports");
            Directory.CreateDirectory(crashDir);
            File.WriteAllText(Path.Combine(crashDir, "crash-2026-09-20_10.00.00-client.txt"), OomReport);

            PluginContext.GameDirectoryProvider = () => root;

            var reports = _ctx.GetCrashReports();

            Assert.Single(reports);
            var dto = reports[0];
            Assert.Equal("crash-2026-09-20_10.00.00-client.txt", dto.FileName);
            Assert.Equal("Minecraft", dto.Kind);
            Assert.True(dto.ReportFound);
            Assert.Equal(0, dto.ExitCode);
            Assert.True(dto.SizeBytes > 0);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void GetCrashReports_EmptyDirectory_ReturnsEmptyNotThrow()
    {
        var root = Path.Combine(Path.GetTempPath(), "omcl_plugincrash_empty_" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            PluginContext.GameDirectoryProvider = () => root;

            Assert.Empty(_ctx.GetCrashReports());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    // ---------------- 上下文 / UI 线程接线 ----------------

    [Fact]
    public void GetActiveCrashContext_ReturnsWiredValue()
    {
        Assert.Null(_ctx.GetActiveCrashContext());

        var expected = new PluginSlotContext { SlotId = "", CrashReportPath = "x.txt", VersionId = "1.20.1" };
        PluginContext.OnGetActiveCrashContext = () => expected;

        Assert.Same(expected, _ctx.GetActiveCrashContext());
    }

    [Fact]
    public void RunOnUiThread_UsesDelegateWhenWired()
    {
        var ran = 0;

        // 未接线：直接同步执行
        _ctx.RunOnUiThread(() => ran++);
        Assert.Equal(1, ran);

        // 接线后：走回调
        PluginContext.OnRunOnUiThread = a => a();
        _ctx.RunOnUiThread(() => ran++);
        Assert.Equal(2, ran);
    }

    [Fact]
    public void GetSlotHost_ReturnsNullWhenNotWired()
    {
        Assert.Null(_ctx.GetSlotHost("crash.dialog.actions"));
    }

    // ---------------- 不受槽位限制的 UI 访问 ----------------

    [Fact]
    public void GetUiRoot_ReturnsWiredWindow()
    {
        Assert.Null(_ctx.GetUiRoot());

        var fakeWindow = new object();
        PluginContext.OnGetUiRoot = () => fakeWindow;

        Assert.Same(fakeWindow, _ctx.GetUiRoot());
    }

    [Fact]
    public void TryFindControlByName_ReturnsWiredControl_AndNullForEmptyName()
    {
        var fakeControl = new object();
        PluginContext.OnFindControlByName = name => string.Equals(name, "CrashDialogHeader") ? fakeControl : null;

        Assert.Same(fakeControl, _ctx.TryFindControlByName("CrashDialogHeader"));
        Assert.Null(_ctx.TryFindControlByName("不存在的控件"));
        Assert.Null(_ctx.TryFindControlByName(""));
    }
}
