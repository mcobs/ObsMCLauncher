using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Plugins;
using ObsMCLauncher.Core.Services.Crash;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.Services;
using ObsMCLauncher.Desktop.ViewModels.Notifications;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 「更多 → 崩溃分析」标签页：扫描游戏目录的崩溃报告（crash-reports/*.txt 与 hs_err_pid*.log），
/// 选中即调用 <see cref="CrashReportAnalyzer"/> 给出原因分析与修复建议；
/// 也支持手动选择外部报告文件分析。
/// </summary>
public partial class CrashReportsViewModel : ViewModelBase
{
    private readonly NotificationService _notificationService;

    [ObservableProperty]
    private ObservableCollection<CrashReportInfo> _reports = new();

    [ObservableProperty]
    private ObservableCollection<string> _versions = new();

    [ObservableProperty]
    private string? _selectedVersion;

    [ObservableProperty]
    private CrashReportInfo? _selectedReport;

    [ObservableProperty]
    private CrashAnalysisResult? _analysis;

    /// <summary>槽位上下文：告诉插件"当前在看哪份报告"（作为插件内容的 DataContext）</summary>
    [ObservableProperty]
    private PluginSlotContext? _slotContext;

    /// <summary>当前分析对象显示名（列表项文件名或外部文件路径）</summary>
    [ObservableProperty]
    private string? _analysisSourceName;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private bool _isEmpty;

    private CancellationTokenSource? _analyzeCts;

    public CrashReportsViewModel(NotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    /// <summary>标签页激活时调用：加载版本筛选列表</summary>
    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            var gameDir = LauncherConfig.Load().GameDirectory;

            var versions = await Task.Run(() =>
                CrashReportScanner.Instance.GetVersionsWithCrashReports(gameDir));

            Versions.Clear();
            foreach (var v in versions) Versions.Add(v);
            SelectedVersion = Versions.FirstOrDefault();
            IsEmpty = Versions.Count == 0;

            await RefreshReportsAsync();
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"加载崩溃报告列表失败: {ex.Message}", NotificationType.Error);
            IsEmpty = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    partial void OnSelectedVersionChanged(string? value)
    {
        if (!IsLoading)
        {
            _ = RefreshReportsAsync();
        }
    }

    partial void OnSelectedReportChanged(CrashReportInfo? value)
    {
        if (value == null) return;
        UpdateSlotContext(value.FullPath, value.VersionName);
        _ = AnalyzeAsync(value.FullPath, value.FileName);
    }

    /// <summary>同步槽位上下文（插件内容据此知道当前报告），并更新"当前崩溃上下文"</summary>
    private void UpdateSlotContext(string? reportPath, string? versionId)
    {
        SlotContext = string.IsNullOrEmpty(reportPath)
            ? null
            : new PluginSlotContext
            {
                SlotId = string.Empty,
                CrashReportPath = reportPath,
                VersionId = versionId
            };

        PluginCrashContextService.SetCrashContext(reportPath, versionId);
    }

    private async Task RefreshReportsAsync()
    {
        try
        {
            var gameDir = LauncherConfig.Load().GameDirectory;
            var version = SelectedVersion;
            if (version == "全部") version = null;

            var reports = await Task.Run(() =>
                CrashReportScanner.Instance.GetCrashReports(gameDir, version));

            Reports.Clear();
            foreach (var r in reports) Reports.Add(r);
            IsEmpty = Reports.Count == 0;

            // 默认选中最新一份并展开分析
            SelectedReport = Reports.FirstOrDefault();
            if (SelectedReport == null)
            {
                Analysis = null;
                AnalysisSourceName = null;
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"扫描崩溃报告失败: {ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>分析指定文件（带取消与最小防抖，连续切换选中项时只保留最后一次）</summary>
    private async Task AnalyzeAsync(string filePath, string displayName)
    {
        _analyzeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _analyzeCts = cts;
        var token = cts.Token;

        try
        {
            IsAnalyzing = true;
            await Task.Delay(120, token);

            var result = await Task.Run(() =>
                CrashReportAnalyzer.Instance.AnalyzeFile(filePath), token);

            if (token.IsCancellationRequested) return;

            Analysis = result;
            AnalysisSourceName = displayName;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            DebugLogger.Error("CrashReports", $"分析崩溃报告失败: {ex}");
            _notificationService.Show("分析失败", $"无法分析该报告: {ex.Message}", NotificationType.Error);
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsAnalyzing = false;
            }
        }
    }

    /// <summary>手动选择一个崩溃报告 / 日志文件进行分析</summary>
    [RelayCommand]
    private async Task AnalyzeExternalFileAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;
            var storage = desktop.MainWindow?.StorageProvider;
            if (storage == null) return;

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择崩溃报告或日志文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("崩溃报告/日志") { Patterns = new[] { "*.txt", "*.log" } },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            });

            if (files.Count == 0) return;

            var path = files[0].Path.LocalPath;
            if (!File.Exists(path)) return;

            // 取消列表选中态，避免选中项变化覆盖外部分析结果
            SelectedReport = null;
            UpdateSlotContext(path, null);
            await AnalyzeAsync(path, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _notificationService.Show("分析失败", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private void OpenReportFolder(CrashReportInfo? report)
    {
        var target = report?.FullPath;
        if (string.IsNullOrEmpty(target) || !File.Exists(target)) return;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{target}\"");
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Path.GetDirectoryName(target)!,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"打开目录失败: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private void ViewReport(CrashReportInfo? report)
    {
        var target = report?.FullPath;
        if (string.IsNullOrEmpty(target) || !File.Exists(target))
        {
            _notificationService.Show("错误", "报告文件不存在", NotificationType.Error);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"打开报告失败: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task CopyReportAsync(CrashReportInfo? report)
    {
        var target = report?.FullPath;
        if (string.IsNullOrEmpty(target) || !File.Exists(target)) return;

        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;
            if (desktop.MainWindow?.Clipboard == null) return;

            var content = await File.ReadAllTextAsync(target);
            await desktop.MainWindow.Clipboard.SetTextAsync(content);
            _notificationService.Show("已复制", "报告全文已复制到剪贴板，可直接粘贴求助", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _notificationService.Show("复制失败", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private Task DeleteReportAsync(CrashReportInfo? report)
    {
        if (report == null) return Task.CompletedTask;

        try
        {
            if (CrashReportScanner.Instance.DeleteReport(report.FullPath))
            {
                Reports.Remove(report);
                if (SelectedReport == report)
                {
                    SelectedReport = Reports.FirstOrDefault();
                    if (SelectedReport == null)
                    {
                        Analysis = null;
                        AnalysisSourceName = null;
                    }
                }
                IsEmpty = Reports.Count == 0;
                _notificationService.Show("已删除", report.FileName, NotificationType.Success);
            }
            else
            {
                _notificationService.Show("错误", "删除失败，文件可能已被移除", NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"删除失败: {ex.Message}", NotificationType.Error);
        }

        return Task.CompletedTask;
    }
}
