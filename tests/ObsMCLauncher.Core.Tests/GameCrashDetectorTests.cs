using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Crash;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// GameCrashDetector 的单元测试：退出码判定、按启动时刻过滤历史报告、
/// 版本隔离目录定位、hs_err 识别。
/// </summary>
public class GameCrashDetectorTests
{
    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "omcl_crashdetect_" + Guid.NewGuid());

    private static LauncherConfig ConfigFor(string root, GameDirectoryType type = GameDirectoryType.RootFolder) =>
        new()
        {
            GameDirectoryLocation = DirectoryLocation.Custom,
            CustomGameDirectory = root,
            GameDirectoryType = type
        };

    private static void WriteCrashReport(string dir, string fileName, string content = "---- Minecraft Crash Report ----")
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content);
    }

    [Fact]
    public async Task ExitCodeZero_IsNotCrash_AndDoesNotWait()
    {
        var root = NewRoot();
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var info = await GameCrashDetector.WaitForCrashReportAsync(
                "1.20.1", 0, DateTime.Now, ConfigFor(root));

            Assert.False(info.IsCrash);
            Assert.False(info.ReportFound);
            // 正常退出不应进入等待轮询
            Assert.True(sw.ElapsedMilliseconds < GameCrashDetector.ReportFlushTimeoutMs / 2,
                $"正常退出不应等待报告落盘，实际耗时 {sw.ElapsedMilliseconds}ms");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task CrashWithFreshReport_FindsItImmediately()
    {
        var root = NewRoot();
        try
        {
            var launchTime = DateTime.Now.AddSeconds(-30);
            WriteCrashReport(Path.Combine(root, "crash-reports"), "crash-2026-09-20_12.00.00-client.txt");

            var info = await GameCrashDetector.WaitForCrashReportAsync(
                "1.20.1", 1, launchTime, ConfigFor(root));

            Assert.True(info.IsCrash);
            Assert.True(info.ReportFound);
            Assert.Equal(CrashReportKind.MinecraftCrashReport, info.Kind);
            Assert.Equal("crash-2026-09-20_12.00.00-client.txt", info.ReportFileName);
            Assert.NotNull(info.ReportTime);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void CrashWithOnlyStaleReport_IsCrashButNoReport()
    {
        var root = NewRoot();
        try
        {
            var crashDir = Path.Combine(root, "crash-reports");
            WriteCrashReport(crashDir, "crash-old.txt");

            // 把报告时间改成 1 小时前，再声称"本次启动在 30 秒前"
            var path = Path.Combine(crashDir, "crash-old.txt");
            File.SetLastWriteTime(path, DateTime.Now.AddHours(-1));
            File.SetCreationTime(path, DateTime.Now.AddHours(-1));

            var info = GameCrashDetector.FindNewestReport("1.20.1", DateTime.Now.AddSeconds(-30), ConfigFor(root));

            Assert.Null(info);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void IsolatedVersion_FindsReportInVersionDirectory()
    {
        var root = NewRoot();
        try
        {
            WriteCrashReport(
                Path.Combine(root, "versions", "1.20.1-forge", "crash-reports"),
                "crash-2026-09-20_11.30.00-client.txt");

            var info = GameCrashDetector.FindNewestReport(
                "1.20.1-forge", DateTime.Now.AddMinutes(-5), ConfigFor(root, GameDirectoryType.VersionFolder));

            Assert.NotNull(info);
            Assert.Equal("crash-2026-09-20_11.30.00-client.txt", info!.FileName);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void HsErrLog_IsDetected()
    {
        var root = NewRoot();
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "hs_err_pid4321.log"), "# A fatal error has been detected by the Java Runtime Environment:");

            var info = GameCrashDetector.FindNewestReport("1.20.1", DateTime.Now.AddMinutes(-2), ConfigFor(root));

            Assert.NotNull(info);
            Assert.Equal(CrashReportKind.JvmFatalErrorLog, info!.Kind);
            Assert.Equal("hs_err_pid4321.log", info.FileName);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void NewestReportWins_WhenSeveralExist()
    {
        var root = NewRoot();
        try
        {
            var crashDir = Path.Combine(root, "crash-reports");
            WriteCrashReport(crashDir, "crash-older.txt");
            WriteCrashReport(crashDir, "crash-newer.txt");

            var older = Path.Combine(crashDir, "crash-older.txt");
            var newer = Path.Combine(crashDir, "crash-newer.txt");
            // 创建时间也要一起改：检测器取「创建/写入较晚者」，只改写入时间会被现在时刻的创建时间盖掉
            File.SetCreationTime(older, DateTime.Now.AddMinutes(-4));
            File.SetLastWriteTime(older, DateTime.Now.AddMinutes(-4));
            File.SetCreationTime(newer, DateTime.Now.AddMinutes(-1));
            File.SetLastWriteTime(newer, DateTime.Now.AddMinutes(-1));

            var info = GameCrashDetector.FindNewestReport("1.20.1", DateTime.Now.AddMinutes(-5), ConfigFor(root));

            Assert.NotNull(info);
            Assert.Equal("crash-newer.txt", info!.FileName);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task NoReportAtAll_TimesOutAndReportsCrashWithoutReport()
    {
        var root = NewRoot();
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var info = await GameCrashDetector.WaitForCrashReportAsync(
                "1.20.1", -1073741819, DateTime.Now, ConfigFor(root));

            Assert.True(info.IsCrash);
            Assert.False(info.ReportFound);
            Assert.Null(info.ReportPath);
            // 应该等满超时而不是立刻返回（报告可能还在写）
            Assert.True(sw.ElapsedMilliseconds >= GameCrashDetector.ReportFlushTimeoutMs - 500,
                $"应等待报告落盘，实际耗时 {sw.ElapsedMilliseconds}ms");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void MissingDirectory_ReturnsNullWithoutThrowing()
    {
        var info = GameCrashDetector.FindNewestReport(
            "does-not-exist", DateTime.Now.AddMinutes(-1), ConfigFor(NewRoot()));

        Assert.Null(info);
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch
        {
        }
    }
}
