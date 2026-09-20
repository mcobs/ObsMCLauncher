using System;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Plugins;
using ObsMCLauncher.Core.Services.Crash;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.ViewModels.Notifications;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>崩溃提示窗口的展示状态</summary>
public enum GameCrashViewState
{
    /// <summary>询问用户是否分析</summary>
    Prompt,

    /// <summary>正在分析</summary>
    Analyzing,

    /// <summary>已给出分析结果</summary>
    Result,

    /// <summary>判定为崩溃但没找到报告（启动即失败 / 进程被强杀等）</summary>
    NoReport
}

/// <summary>
/// 游戏崩溃提示窗口：先询问「是否分析崩溃日志」，确认后调用 <see cref="CrashReportAnalyzer"/> 并就地展示结论。
/// </summary>
public partial class GameCrashViewModel : ViewModelBase
{
    /// <summary>承载弹窗的宿主窗口（也用于剪贴板）</summary>
    private readonly Window _host;
    private readonly GameCrashInfo _info;
    private readonly NotificationService? _notificationService;

    public GameCrashViewModel(GameCrashInfo info, Window host, NotificationService? notificationService = null)
    {
        _info = info;
        _host = host;
        _notificationService = notificationService;

        try
        {
            AutoAnalyzeNextTime = LauncherConfig.Load().AutoAnalyzeCrashOnExit;
        }
        catch
        {
            AutoAnalyzeNextTime = false;
        }

        State = info.ReportFound ? GameCrashViewState.Prompt : GameCrashViewState.NoReport;

        SlotContext = new PluginSlotContext
        {
            SlotId = string.Empty,
            CrashReportPath = info.ReportPath,
            VersionId = info.VersionId
        };
    }

    /// <summary>
    /// 槽位上下文：作为插件内容的 DataContext，告诉插件"当前在分析哪份报告"。
    /// </summary>
    public PluginSlotContext SlotContext { get; }

    [ObservableProperty]
    private GameCrashViewState _state;

    [ObservableProperty]
    private CrashAnalysisResult? _analysis;

    /// <summary>「以后自动分析，不再询问」勾选状态，写入配置</summary>
    [ObservableProperty]
    private bool _autoAnalyzeNextTime;

    // ---- 头部信息 ----

    public string VersionText => $"版本 {_info.VersionId}";

    public string ExitCodeText => $"{_info.ExitCode}（{DescribeExitCode(_info.ExitCode)}）";

    public string ReportNameText => _info.ReportFileName ?? "未找到报告文件";

    public string ReportTimeText => _info.ReportTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—";

    public string HeaderText => _info.IsCrash ? "检测到游戏异常退出" : "游戏已退出";

    public string SubHeaderText => _info.ReportFound
        ? "已定位到本次崩溃的报告文件，可以立即分析原因"
        : "未能定位到崩溃报告，可先查看游戏日志";

    // ---- 状态开关（XAML 里直接绑，免去枚举转换器）----

    public bool IsPromptState => State == GameCrashViewState.Prompt;
    public bool IsAnalyzingState => State == GameCrashViewState.Analyzing;
    public bool IsResultState => State == GameCrashViewState.Result;
    public bool IsNoReportState => State == GameCrashViewState.NoReport;

    partial void OnStateChanged(GameCrashViewState value)
    {
        OnPropertyChanged(nameof(IsPromptState));
        OnPropertyChanged(nameof(IsAnalyzingState));
        OnPropertyChanged(nameof(IsResultState));
        OnPropertyChanged(nameof(IsNoReportState));
        OnPropertyChanged(nameof(CanCopyResult));
    }

    public bool CanCopyResult => IsResultState;

    /// <summary>窗口打开后调用：若用户此前勾选过「自动分析」，直接跳过询问进入分析</summary>
    public void StartAutoAnalyzeIfNeeded()
    {
        if (AutoAnalyzeNextTime && _info.ReportFound)
        {
            _ = AnalyzeAsync();
        }
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (!_info.ReportFound || State == GameCrashViewState.Analyzing) return;

        State = GameCrashViewState.Analyzing;
        try
        {
            var path = _info.ReportPath!;
            Analysis = await Task.Run(() => CrashReportAnalyzer.Instance.AnalyzeFile(path));
            State = GameCrashViewState.Result;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("GameCrash", $"分析崩溃报告失败: {ex}");
            _notificationService?.Show("分析失败", $"无法分析该报告: {ex.Message}", NotificationType.Error);
            State = GameCrashViewState.NoReport;
        }
    }

    // 注意：没有 Close 命令——弹窗底部的"稍后再说/关闭"由 ContentDialog 自己的按钮槽负责，
    // VM 不需要知道宿主是窗口还是对话框。

    [RelayCommand]
    private void OpenReportFolder()
    {
        var target = _info.ReportPath;
        if (string.IsNullOrEmpty(target) || !System.IO.File.Exists(target))
        {
            // 报告不存在时退化为打开该版本的日志文件夹（logs/latest.log 所在处）
            OpenDirectory(GameLogDirectory);
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{target}\"");
            }
            else
            {
                OpenDirectory(System.IO.Path.GetDirectoryName(target)!);
            }
        }
        catch (Exception ex)
        {
            _notificationService?.Show("错误", $"打开目录失败: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private void ViewReport()
    {
        var target = _info.ReportPath;
        if (string.IsNullOrEmpty(target) || !System.IO.File.Exists(target))
        {
            _notificationService?.Show("错误", "报告文件不存在", NotificationType.Error);
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
            _notificationService?.Show("错误", $"打开报告失败: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task CopyResultAsync()
    {
        if (Analysis == null) return;

        try
        {
            // 用宿主窗口的剪贴板：弹窗挂在哪个窗口上就取哪个，不依赖 MainWindow 是否还开着
            if (_host.Clipboard == null) return;

            await _host.Clipboard.SetTextAsync(BuildResultText(Analysis, _info));
            _notificationService?.Show("已复制", "分析结果已复制到剪贴板", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _notificationService?.Show("复制失败", ex.Message, NotificationType.Error);
        }
    }

    partial void OnAutoAnalyzeNextTimeChanged(bool value)
    {
        try
        {
            var config = LauncherConfig.Load();
            config.AutoAnalyzeCrashOnExit = value;
            config.Save();
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("GameCrash", $"保存自动分析设置失败: {ex.Message}");
        }
    }

    private string GameLogDirectory
    {
        get
        {
            try
            {
                return System.IO.Path.Combine(LauncherConfig.Load().GetRunDirectory(_info.VersionId), "logs");
            }
            catch
            {
                return "";
            }
        }
    }

    private void OpenDirectory(string directory)
    {
        try
        {
            if (string.IsNullOrEmpty(directory)) return;
            if (!System.IO.Directory.Exists(directory))
            {
                _notificationService?.Show("提示", "该目录不存在，游戏可能还没有生成日志", NotificationType.Info);
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _notificationService?.Show("错误", $"打开目录失败: {ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>把分析结果整理成可粘贴给他人求助的纯文本</summary>
    internal static string BuildResultText(CrashAnalysisResult analysis, GameCrashInfo info)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Minecraft 崩溃分析（版本 {info.VersionId}，退出代码 {info.ExitCode}）");
        sb.AppendLine($"结论：{analysis.Headline}");
        if (!string.IsNullOrEmpty(analysis.MinecraftVersion) || !string.IsNullOrEmpty(analysis.LoaderInfo))
        {
            sb.AppendLine($"环境：MC {analysis.MinecraftVersion ?? "?"}"
                          + (string.IsNullOrEmpty(analysis.LoaderInfo) ? "" : $" / {analysis.LoaderInfo}")
                          + (string.IsNullOrEmpty(analysis.JavaVersion) ? "" : $" / Java {analysis.JavaVersion}"));
        }
        sb.AppendLine();

        if (analysis.Causes.Count > 0)
        {
            sb.AppendLine("【可能原因】");
            for (var i = 0; i < analysis.Causes.Count; i++)
            {
                var c = analysis.Causes[i];
                sb.AppendLine($"{i + 1}. {c.Title}（{c.CategoryLabel} · {c.ConfidenceLabel}）");
                if (!string.IsNullOrWhiteSpace(c.Evidence)) sb.AppendLine($"   证据：{c.Evidence}");
                if (!string.IsNullOrWhiteSpace(c.Suggestion)) sb.AppendLine($"   建议：{c.Suggestion}");
            }
            sb.AppendLine();
        }

        if (analysis.SuspectedMods.Count > 0)
        {
            sb.AppendLine("【可疑 Mod】");
            sb.AppendLine(string.Join("、", analysis.SuspectedMods));
            sb.AppendLine();
        }

        sb.AppendLine($"报告文件：{info.ReportPath}");
        return sb.ToString().TrimEnd();
    }

    private static string DescribeExitCode(int exitCode)
    {
        // Windows 下 JVM 异常终止的常见返回值
        return exitCode switch
        {
            0 => "正常退出",
            1 => "一般性错误",
            -1 => "被强制结束",
            -1073741819 => "0xC0000005 访问冲突，多为显卡驱动或内存问题",
            -1073740791 => "0xC0000409 栈溢出/快速失败",
            -1073741510 => "0xC000013A 进程被 Ctrl+C 或窗口关闭中断",
            -1073740940 => "0xC0000374 堆损坏",
            _ => exitCode < 0
                ? $"0x{unchecked((uint)exitCode):X8}"
                : "非正常退出"
        };
    }
}
