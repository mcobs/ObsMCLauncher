using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ObsMCLauncher.Core.Services.Crash;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.ViewModels.Dialogs;

namespace ObsMCLauncher.Desktop.Services;

/// <summary>
/// 游戏退出后的崩溃提示：判定崩溃 → 等崩溃报告落盘 → 在宿主窗口上弹 ContentDialog 询问是否分析。
/// 两个启动入口（主页启动、版本列表快速启动）都通过这里，逻辑只此一份。
/// 崩溃后**总是自动询问**（没有"是否提示"的开关；不想每次都被问就在弹窗里勾选"以后自动分析"）。
/// </summary>
public static class CrashAnalysisPromptService
{
    /// <summary>已提示过的 (版本, 启动时刻)，避免同一次启动重复弹窗</summary>
    private static string? _lastPromptKey;

    /// <summary>当前打开的崩溃弹窗（ContentDialog 同一宿主同时只能有一个）</summary>
    private static GameCrashDialog? _openDialog;

    /// <summary>
    /// 通知"游戏进程已退出"。仅在判定为崩溃时弹窗。
    /// 不阻塞调用方：等待报告落盘与弹窗都在后台完成。
    /// </summary>
    /// <param name="versionId">本次启动的版本 ID</param>
    /// <param name="exitCode">进程退出码</param>
    /// <param name="launchTime">本次启动时刻（用于排除历史崩溃报告）</param>
    public static void NotifyGameExit(string versionId, int exitCode, DateTime launchTime)
    {
        if (exitCode == 0) return;

        // 同一局只提示一次（GameLauncher 已保证 onGameExit 只回调一次，这里是兜底）
        var key = $"{versionId}|{launchTime.Ticks}";
        if (string.Equals(_lastPromptKey, key, StringComparison.Ordinal)) return;
        _lastPromptKey = key;

        _ = NotifyGameExitAsync(versionId, exitCode, launchTime);
    }

    private static async Task NotifyGameExitAsync(string versionId, int exitCode, DateTime launchTime)
    {
        try
        {
            var info = await GameCrashDetector.WaitForCrashReportAsync(versionId, exitCode, launchTime)
                .ConfigureAwait(false);

            DebugLogger.Info("GameCrash",
                $"版本 {versionId} 退出代码 {exitCode}，报告：{(info.ReportFound ? info.ReportPath : "<未找到>")}");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var host = ResolveHostWindow();
                    if (host == null)
                    {
                        // 弹窗必须挂在窗口上；没有任何窗口时只能记日志（例如已开启"启动后关闭启动器"）
                        DebugLogger.Warn("GameCrash", "没有可用的宿主窗口，跳过崩溃弹窗");
                        return;
                    }

                    // 已经有一个崩溃弹窗开着就复用它（把宿主窗口带到前台），不再叠一个
                    if (_openDialog != null)
                    {
                        _openDialog.Host.Activate();
                        return;
                    }

                    var dialog = new GameCrashDialog(info, host);
                    _openDialog = dialog;
                    _ = ShowAndReleaseAsync(dialog);
                }
                catch (Exception ex)
                {
                    DebugLogger.Error("GameCrash", $"打开崩溃弹窗失败: {ex.Message}");
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DebugLogger.Error("GameCrash", $"崩溃检测/提示失败: {ex}");
        }
    }

    private static async Task ShowAndReleaseAsync(GameCrashDialog dialog)
    {
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            DebugLogger.Error("GameCrash", $"崩溃弹窗显示异常: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_openDialog, dialog))
            {
                _openDialog = null;
            }
        }
    }

    /// <summary>
    /// 选宿主窗口：优先主窗口，其次任意可见窗口（例如"启动时显示游戏日志"留下的日志窗口）。
    /// ContentDialog 需要一个可见的 Window 作为承载。
    /// </summary>
    private static Window? ResolveHostWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        if (desktop.MainWindow is { IsVisible: true } main)
        {
            return main;
        }

        return desktop.Windows.FirstOrDefault(w => w.IsVisible);
    }

    /// <summary>等待中的崩溃检测（供测试或需要同步结果的调用方使用）</summary>
    public static Task<GameCrashInfo> DetectAsync(string versionId, int exitCode, DateTime launchTime, CancellationToken ct = default)
        => GameCrashDetector.WaitForCrashReportAsync(versionId, exitCode, launchTime, null, ct);
}
