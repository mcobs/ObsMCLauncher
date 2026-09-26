using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Services.Crash;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 退出码非 0 后定位崩溃报告的回归测试。
///
/// 背景（2026-09-26）：日志里明明有 `versions/&lt;版本&gt;/logs/latest.log`（里面有完整堆栈），
/// 启动器却写「退出代码 1，报告：&lt;未找到&gt;」。原因是只认 `crash-reports/crash-*.txt` 与 `hs_err_pid*.log`，
/// 而 Mod 加载期崩溃（Fabric 的 preLaunch 阶段）根本不会生成 crash-reports，堆栈只在游戏日志里。
/// </summary>
public class CrashReportDetectionTests : IDisposable
{
    private readonly string _gameDir;

    public CrashReportDetectionTests()
    {
        _gameDir = Path.Combine(Path.GetTempPath(), "omcl-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_gameDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_gameDir))
                Directory.Delete(_gameDir, true);
        }
        catch
        {
            // 清理失败不影响断言
        }
    }

    // GameDirectory 是计算属性（按 GameDirectoryLocation 推导），要指向临时目录得走 Custom
    private LauncherConfig BuildConfig()
        => new()
        {
            GameDirectoryLocation = DirectoryLocation.Custom,
            CustomGameDirectory = _gameDir,
            GameDirectoryType = GameDirectoryType.VersionFolder
        };

    private string VersionDir(string versionId)
    {
        var dir = Path.Combine(_gameDir, "versions", versionId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void FindsCrashReportWhenItExists()
    {
        const string versionId = "crashed-version";
        var dir = VersionDir(versionId);
        var crashDir = Path.Combine(dir, "crash-reports");
        Directory.CreateDirectory(crashDir);
        var report = Path.Combine(crashDir, "crash-2026-09-26_09.24.10-client.txt");
        File.WriteAllText(report, "---- Minecraft Crash Report ----");

        var found = GameCrashDetector.FindNewestReport(versionId, DateTime.Now.AddMinutes(-1), BuildConfig());

        Assert.NotNull(found);
        Assert.Equal("crash-2026-09-26_09.24.10-client.txt", found!.FileName);
        Assert.Equal(CrashReportKind.MinecraftCrashReport, found.Kind);
    }

    [Fact]
    public void FallsBackToGameLogWhenNoCrashReportWasWritten()
    {
        const string versionId = "fabric-prelaunch-crash";
        var dir = VersionDir(versionId);
        // 只有 logs/latest.log（Fabric preLaunch 阶段崩溃的典型形态：没有 crash-reports 目录）
        var logsDir = Path.Combine(dir, "logs");
        Directory.CreateDirectory(logsDir);
        File.WriteAllText(Path.Combine(logsDir, "latest.log"),
            "Caused by: java.lang.ClassNotFoundException: org.lwjgl.system.Platform");

        var found = GameCrashDetector.FindNewestReport(versionId, DateTime.Now.AddMinutes(-1), BuildConfig());

        Assert.NotNull(found);
        Assert.Equal("latest.log", found!.FileName);
        Assert.Equal(CrashReportKind.GameLog, found.Kind);
    }

    [Fact]
    public void PrefersRealCrashReportOverGameLogFallback()
    {
        const string versionId = "both-present";
        var dir = VersionDir(versionId);
        var crashDir = Path.Combine(dir, "crash-reports");
        Directory.CreateDirectory(crashDir);
        File.WriteAllText(Path.Combine(crashDir, "crash-2026-09-26_09.24.10-client.txt"), "crash");
        // 日志比报告更新（游戏一直往里写），但不该因此抢走"报告"的位置
        var logsDir = Path.Combine(dir, "logs");
        Directory.CreateDirectory(logsDir);
        File.WriteAllText(Path.Combine(logsDir, "latest.log"), "log");
        File.SetLastWriteTime(Path.Combine(logsDir, "latest.log"), DateTime.Now.AddMinutes(1));

        var found = GameCrashDetector.FindNewestReport(versionId, DateTime.Now.AddMinutes(-5), BuildConfig());

        Assert.NotNull(found);
        Assert.Equal(CrashReportKind.MinecraftCrashReport, found!.Kind);
    }

    [Fact]
    public void IgnoresStaleReportsFromPreviousSessions()
    {
        const string versionId = "old-crash";
        var dir = VersionDir(versionId);
        var logsDir = Path.Combine(dir, "logs");
        Directory.CreateDirectory(logsDir);
        var log = Path.Combine(logsDir, "latest.log");
        File.WriteAllText(log, "old");
        // 两个时间都要改：检测器取 CreationTime / LastWriteTime 里**较晚**的那个，
        // 只改 LastWrite 的话新建文件的 CreationTime 还是"现在"，会被当成本次崩溃
        var stale = DateTime.Now.AddHours(-2);
        File.SetCreationTime(log, stale);
        File.SetLastWriteTime(log, stale);

        // 本次启动在 5 分钟前 —— 两小时前的日志不该被当成这次崩溃
        var found = GameCrashDetector.FindNewestReport(versionId, DateTime.Now.AddMinutes(-5), BuildConfig());

        Assert.Null(found);
    }
}
