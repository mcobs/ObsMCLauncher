using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services.Crash;

/// <summary>
/// 扫描游戏目录下的崩溃报告文件（crash-reports/*.txt 与 hs_err_pid*.log）。
/// 崩溃报告生成在游戏的<b>运行目录</b>下：版本隔离开启时在 versions/&lt;版本名&gt;/ 内，
/// 否则在游戏主目录 —— 与 <see cref="LauncherConfig.GetRunDirectory"/> 同一套口径。
/// </summary>
public class CrashReportScanner
{
    private static CrashReportScanner? _instance;
    public static CrashReportScanner Instance => _instance ??= new CrashReportScanner();

    private CrashReportScanner() { }

    /// <summary>
    /// 扫描崩溃报告。versionName 为 null 或「全部」时扫描主目录 + 所有隔离版本目录。
    /// </summary>
    public List<CrashReportInfo> GetCrashReports(string gameDirectory, string? versionName = null)
    {
        var reports = new List<CrashReportInfo>();
        var config = LauncherConfig.Load();

        if (string.IsNullOrEmpty(versionName) || versionName == "全部")
        {
            ScanRunDirectory(Path.Combine(gameDirectory, "crash-reports"), "主目录", reports);
            ScanRunDirectory(gameDirectory, "主目录", reports, hsErrOnly: true);

            var versionsDir = Path.Combine(gameDirectory, "versions");
            if (Directory.Exists(versionsDir))
            {
                foreach (var versionDir in Directory.GetDirectories(versionsDir))
                {
                    var vName = Path.GetFileName(versionDir);
                    if (!config.IsVersionIsolated(vName)) continue;

                    ScanRunDirectory(Path.Combine(versionDir, "crash-reports"), vName, reports);
                    ScanRunDirectory(versionDir, vName, reports, hsErrOnly: true);
                }
            }
        }
        else
        {
            // 与截图管理同一口径：非隔离版本的报告落在主目录
            var runDir = config.IsVersionIsolated(versionName)
                ? Path.Combine(gameDirectory, "versions", versionName)
                : gameDirectory;
            var label = config.IsVersionIsolated(versionName) ? versionName : "主目录";

            ScanRunDirectory(Path.Combine(runDir, "crash-reports"), label, reports);
            ScanRunDirectory(runDir, label, reports, hsErrOnly: true);
        }

        return reports.OrderByDescending(r => r.CreatedTime).ToList();
    }

    /// <summary>
    /// 返回存在崩溃报告的版本筛选列表（「全部」「主目录」+ 各隔离版本）
    /// </summary>
    public List<string> GetVersionsWithCrashReports(string gameDirectory)
    {
        var versions = new List<string>();
        var config = LauncherConfig.Load();

        var hasMain = HasReports(Path.Combine(gameDirectory, "crash-reports"), hsErrDir: gameDirectory);

        var versionsWithReports = new List<string>();
        var versionsDir = Path.Combine(gameDirectory, "versions");
        if (Directory.Exists(versionsDir))
        {
            foreach (var versionDir in Directory.GetDirectories(versionsDir))
            {
                var versionName = Path.GetFileName(versionDir);
                if (!config.IsVersionIsolated(versionName)) continue;

                if (HasReports(Path.Combine(versionDir, "crash-reports"), hsErrDir: versionDir))
                {
                    versionsWithReports.Add(versionName);
                }
            }
        }

        if (hasMain || versionsWithReports.Count > 0)
        {
            versions.Add("全部");
        }
        if (hasMain)
        {
            versions.Add("主目录");
        }
        versions.AddRange(versionsWithReports);

        return versions;
    }

    private bool HasReports(string crashReportsDir, string hsErrDir)
    {
        try
        {
            if (Directory.Exists(crashReportsDir) &&
                Directory.GetFiles(crashReportsDir, "crash-*.txt").Length > 0)
            {
                return true;
            }
            if (Directory.Exists(hsErrDir) &&
                Directory.GetFiles(hsErrDir, "hs_err_pid*.log").Length > 0)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("CrashReport", $"检查崩溃报告目录失败: {ex.Message}");
        }
        return false;
    }

    private void ScanRunDirectory(string directory, string? versionName, List<CrashReportInfo> reports, bool hsErrOnly = false)
    {
        if (!Directory.Exists(directory)) return;

        try
        {
            IEnumerable<string> files;
            if (hsErrOnly)
            {
                files = Directory.GetFiles(directory, "hs_err_pid*.log");
            }
            else
            {
                files = Directory.GetFiles(directory, "crash-*.txt");
            }

            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    var fileTime = fileInfo.LastWriteTimeUtc > fileInfo.CreationTimeUtc
                        ? fileInfo.LastWriteTimeUtc.ToLocalTime()
                        : fileInfo.CreationTimeUtc.ToLocalTime();

                    var isHsErr = Path.GetFileName(file).StartsWith("hs_err_pid", StringComparison.OrdinalIgnoreCase);

                    reports.Add(new CrashReportInfo
                    {
                        FileName = Path.GetFileName(file),
                        FullPath = file,
                        Size = fileInfo.Length,
                        CreatedTime = fileTime,
                        VersionName = versionName,
                        Kind = isHsErr ? CrashReportKind.JvmFatalErrorLog : CrashReportKind.MinecraftCrashReport
                    });
                }
                catch (Exception ex)
                {
                    DebugLogger.Warn("CrashReport", $"读取崩溃报告文件失败: {file}, 错误: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("CrashReport", $"扫描崩溃报告目录失败: {directory}, 错误: {ex.Message}");
        }
    }

    public bool DeleteReport(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("CrashReport", $"删除崩溃报告失败: {ex.Message}");
            return false;
        }
    }
}
