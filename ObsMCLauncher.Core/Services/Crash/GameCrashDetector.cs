using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services.Crash;

/// <summary>
/// 游戏进程退出后的崩溃判定与报告定位结果。
/// </summary>
public class GameCrashInfo
{
    public string VersionId { get; set; } = "";

    public int ExitCode { get; set; }

    /// <summary>退出码非 0 即判定为崩溃（与 GameLauncher 的 OnCrash 钩子同口径）</summary>
    public bool IsCrash { get; set; }

    /// <summary>是否找到了本次崩溃产生的报告 / JVM 致命错误日志</summary>
    public bool ReportFound { get; set; }

    public string? ReportPath { get; set; }

    public CrashReportKind Kind { get; set; }

    /// <summary>报告写入时间（本地时间）</summary>
    public DateTime? ReportTime { get; set; }

    public string? ReportFileName => string.IsNullOrEmpty(ReportPath) ? null : Path.GetFileName(ReportPath);
}

/// <summary>
/// 在游戏进程退出后判断"这次是不是崩了"，并等待崩溃报告落盘。
/// Minecraft 写出 crash-reports 需要一点时间（尤其 Mod 加载期崩溃），
/// 因此这里轮询而不是只看一眼；同时用启动时刻过滤掉历史崩溃报告，避免误报上一局的报告。
/// </summary>
public static class GameCrashDetector
{
    /// <summary>等待崩溃报告落盘的最长时间</summary>
    public const int ReportFlushTimeoutMs = 3000;

    private const int PollIntervalMs = 400;

    /// <summary>时钟偏差容忍：报告写入时间早于启动时间在这个范围内仍算本次崩溃</summary>
    private const int ClockSkewToleranceSeconds = 10;

    /// <summary>
    /// 退出码非 0 时等待崩溃报告落盘并定位最新一份。
    /// 退出码为 0 时立即返回（IsCrash=false，不等待）。
    /// </summary>
    /// <param name="versionId">本次启动的版本 ID</param>
    /// <param name="exitCode">进程退出码</param>
    /// <param name="launchTime">本次启动时刻（本地时间），用于排除历史报告</param>
    /// <param name="config">配置（默认读当前配置；测试可注入）</param>
    public static async Task<GameCrashInfo> WaitForCrashReportAsync(
        string versionId,
        int exitCode,
        DateTime launchTime,
        LauncherConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        var info = new GameCrashInfo
        {
            VersionId = versionId,
            ExitCode = exitCode,
            IsCrash = exitCode != 0
        };

        if (!info.IsCrash) return info;

        var deadline = DateTime.UtcNow.AddMilliseconds(ReportFlushTimeoutMs);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var newest = FindNewestReport(versionId, launchTime, config);
            if (newest != null)
            {
                info.ReportFound = true;
                info.ReportPath = newest.FullPath;
                info.Kind = newest.Kind;
                info.ReportTime = newest.CreatedTime;
                return info;
            }

            if (DateTime.UtcNow >= deadline) return info;
            await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 定位该版本在这次启动之后产生的最新崩溃报告 / hs_err 日志；没有则返回 null。
    /// 查找位置 = 游戏运行目录（版本隔离开启时是 versions/&lt;版本名&gt;），
    /// 外加游戏主目录兜底（覆盖未隔离 / 老版本把报告写到主目录的情况）。
    /// </summary>
    public static CrashReportInfo? FindNewestReport(string versionId, DateTime launchTime, LauncherConfig? config = null)
    {
        config ??= LauncherConfig.Load();
        var candidates = new List<string>();

        string runDir;
        try
        {
            runDir = config.GetRunDirectory(versionId);
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("CrashDetect", $"解析运行目录失败: {ex.Message}");
            return null;
        }

        candidates.Add(runDir);
        if (!PathsEqual(runDir, config.GameDirectory))
        {
            candidates.Add(config.GameDirectory);
        }

        var notBefore = launchTime.AddSeconds(-ClockSkewToleranceSeconds);
        var found = new List<CrashReportInfo>();

        foreach (var dir in candidates)
        {
            CollectFrom(dir, notBefore, found);
        }

        if (found.Count == 0) return null;

        // 真正的崩溃报告优先于"只有日志"的兜底；同类里取最新的
        return found
            .OrderBy(r => r.Kind == CrashReportKind.GameLog ? 1 : 0)
            .ThenByDescending(r => r.CreatedTime)
            .First();

        static void CollectFrom(string directory, DateTime notBefore, List<CrashReportInfo> sink)
        {
            // crash-reports/*.txt
            var crashDir = Path.Combine(directory, "crash-reports");
            if (Directory.Exists(crashDir))
            {
                TryAdd(Directory.GetFiles(crashDir, "crash-*.txt"), CrashReportKind.MinecraftCrashReport, notBefore, sink);
            }

            // hs_err_pid*.log 落在运行目录根部
            if (Directory.Exists(directory))
            {
                TryAdd(Directory.GetFiles(directory, "hs_err_pid*.log"), CrashReportKind.JvmFatalErrorLog, notBefore, sink);
            }

            // ⚠️ 兜底：很多崩溃**不会**生成 crash-reports —— 比如 Fabric 在 preLaunch 阶段就挂了
            // （Mod 初始化异常），Minecraft 的崩溃报告处理器还没起来，堆栈只落在 logs/latest.log 里。
            // 以前这时一律显示"报告：<未找到>"，用户明明有日志却看不到。
            var logsDir = Path.Combine(directory, "logs");
            if (Directory.Exists(logsDir))
            {
                TryAdd(Directory.GetFiles(logsDir, "*.log"), CrashReportKind.GameLog, notBefore, sink);
            }

            // 启动器另外把游戏的 stderr 单独存了一份（没有 log4j，只有真正的异常输出）
            if (Directory.Exists(directory))
            {
                TryAdd(Directory.GetFiles(directory, "stderr_stream.log"), CrashReportKind.GameLog, notBefore, sink);
            }
        }

        static void TryAdd(string[] files, CrashReportKind kind, DateTime notBefore, List<CrashReportInfo> sink)
        {
            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    var time = fileInfo.LastWriteTimeUtc > fileInfo.CreationTimeUtc
                        ? fileInfo.LastWriteTimeUtc.ToLocalTime()
                        : fileInfo.CreationTimeUtc.ToLocalTime();

                    if (time < notBefore) continue;

                    sink.Add(new CrashReportInfo
                    {
                        FileName = fileInfo.Name,
                        FullPath = file,
                        Size = fileInfo.Length,
                        CreatedTime = time,
                        VersionName = null,
                        Kind = kind
                    });
                }
                catch (Exception ex)
                {
                    DebugLogger.Warn("CrashDetect", $"读取崩溃报告信息失败: {file}, 错误: {ex.Message}");
                }
            }
        }
    }

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
