using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using ObsMCLauncher.Core.Services.Crash;
using ObsMCLauncher.Desktop.Services;
using ObsMCLauncher.Desktop.ViewModels;
using ObsMCLauncher.Desktop.ViewModels.Notifications;
using ObsMCLauncher.Desktop.Views;

namespace ObsMCLauncher.Desktop.ViewModels.Dialogs;

/// <summary>
/// 游戏崩溃弹窗（FluentAvalonia <see cref="ContentDialog"/>）。
///
/// 按钮分工：
/// <list type="bullet">
/// <item>对话框页脚只放两个**在打开前就定好**的按钮：「分析崩溃日志」（主）与「关闭」；</item>
/// <item>其余操作（打开目录 / 查看原文 / 复制分析结果 / 打开游戏日志文件夹）都在内容体里。</item>
/// </list>
///
/// 为什么不在运行期改按钮文案：实测 FA 2.4.1 里 `PrimaryButtonText` 改了能生效，
/// 但**打开时为空、之后再赋值的次按钮不会真正渲染出文字**（会留一个空白按钮）。
/// 所以页脚按钮文案一次性定死，状态切换只动 <see cref="ContentDialog.IsPrimaryButtonEnabled"/>。
///
/// 点「分析崩溃日志」时用 <c>args.Cancel = true</c> 阻止对话框关闭，
/// 让它就地切换到"分析中 → 结果"，避免关掉再开第二个弹窗。
/// </summary>
public sealed class GameCrashDialog
{
    private readonly Window _host;
    private readonly GameCrashViewModel _viewModel;

    public ContentDialog Dialog { get; }

    /// <summary>承载弹窗的窗口（需要把它带到前台时用）</summary>
    public Window Host => _host;

    /// <summary>弹窗内容的状态机（探针 / 需要观察状态时用）</summary>
    public GameCrashViewModel ViewModel => _viewModel;

    public GameCrashDialog(GameCrashInfo info, Window host, NotificationService? notificationService = null)
    {
        _host = host;
        _viewModel = new GameCrashViewModel(info, host, notificationService);

        Dialog = new ContentDialog
        {
            Title = "游戏崩溃了",
            Content = new GameCrashDialogContent { DataContext = _viewModel },
            DefaultButton = ContentDialogButton.Primary,
            // 文案在打开前一次定死，之后只切启用状态（见类注释）
            PrimaryButtonText = "分析崩溃日志",
            CloseButtonText = "关闭",
        };

        // 分析结果内容偏宽，把对话框的宽度上限放宽（FA 用 ContentDialogMaxWidth 资源控制，
        // 默认 548 太窄；这里只覆盖本弹窗自己的资源，不影响其他对话框）
        if (Application.Current?.TryGetResource("ContentDialogMaxWidth", null, out _) == true)
        {
            Dialog.Resources["ContentDialogMaxWidth"] = 720.0;
        }

        Dialog.PrimaryButtonClick += OnPrimaryButtonClick;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Dialog.Closed += OnDialogClosed;

        // 告诉插件"用户正在看这份崩溃"
        PluginCrashContextService.SetCrashContext(info.ReportPath, info.VersionId);

        SyncButtons();

        // 用户此前勾选过「以后自动分析」时直接进入"分析中"，避免先闪一下询问界面
        _viewModel.StartAutoAnalyzeIfNeeded();
        SyncButtons();
    }

    public Task<ContentDialogResult> ShowAsync() => Dialog.ShowAsync(_host);

    private void OnDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        // 只在上下文仍指向本弹窗的报告时清空，避免把崩溃分析页的上下文一起清掉
        PluginCrashContextService.ClearIf(_viewModel.SlotContext.CrashReportPath);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameCrashViewModel.State))
        {
            SyncButtons();
        }
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        => args.Cancel = RequestAnalyze();

    /// <summary>
    /// 点了「分析崩溃日志」。返回 true 表示"不要关闭对话框"——就地转成
    /// 分析中 → 结果，用户无需关掉再开第二个弹窗。
    /// 事件处理器是它的薄包装，这样无需真实点击也能验证该行为。
    /// </summary>
    public bool RequestAnalyze()
    {
        if (_viewModel.State != GameCrashViewState.Prompt) return false;

        _viewModel.AnalyzeCommand.Execute(null);
        return true;
    }

    /// <summary>状态变化后同步页脚：只有询问态的主按钮可用</summary>
    private void SyncButtons()
    {
        Dialog.IsPrimaryButtonEnabled = _viewModel.State == GameCrashViewState.Prompt;
    }
}
