using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;
using FluentAvalonia.UI.Navigation;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Windows;

/// <summary>
/// 欢迎窗口：首次启动时在主窗口显示前打开（流程不过完不显示主界面）。
/// 开场字母动画位于欢迎首页（WelcomePageView）内，结束后页面自行淡入内容；
/// 窗口打开时内容区加 anim 类淡入，内部 Frame 做分页导航。
/// 流程未完成就手动关闭时弹确认框，确认退出才真正关窗。
/// </summary>
public partial class WelcomeWindow : Window
{
    private readonly WelcomeViewModel _vm;
    private bool _frameNavigated;
    private bool _forceClose;
    private bool _closePromptShowing;

    /// <summary>是否完成了欢迎流程（未完成即关闭窗口视为取消启动）</summary>
    public bool IsCompleted { get; private set; }

    public WelcomeWindow() : this(isFirstRun: false)
    {
    }

    public WelcomeWindow(bool isFirstRun = false)
    {
        InitializeComponent();

        _vm = new WelcomeViewModel(isFirstRun);
        DataContext = _vm;

        _vm.Completed += (_, _) =>
        {
            IsCompleted = true;
            Close();
        };

        _vm.PageNavigationRequested += (_, pageVm) =>
        {
            WelcomeFrame.NavigateFromObject(pageVm, new FrameNavigationOptions
            {
                IsNavigationStackEnabled = false,
                TransitionInfoOverride = new EntranceNavigationTransitionInfo()
            });
        };

        Opened += OnOpened;

        WelcomeFrame.NavigationPageFactory = new ObsMCLauncher.Desktop.Views.ViewModelPageFactory();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // 内容区淡入
        ContentRoot.Classes.Add("anim");

        // Frame 第一页：问候页（开场动画在此页内，后续设置页可继续 Navigate）
        if (!_frameNavigated)
        {
            _frameNavigated = true;
            WelcomeFrame.NavigateFromObject(_vm.Page, new FrameNavigationOptions
            {
                IsNavigationStackEnabled = false,
                TransitionInfoOverride = new EntranceNavigationTransitionInfo()
            });
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        // 流程完成、已确认退出或崩溃流程中的关闭直接放行
        if (IsCompleted || _forceClose || (Application.Current as App)?.IsCrashFlowActive == true)
            return;

        // 未完成就手动关闭：先弹确认框
        e.Cancel = true;
        if (!_closePromptShowing)
        {
            _ = ConfirmCloseAsync();
        }
    }

    private async Task ConfirmCloseAsync()
    {
        _closePromptShowing = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = "退出 ObsMCLauncher",
                Content = new TextBlock
                {
                    Text = "您需要完成设置才能开始使用本应用。关闭此窗口将直接退出应用。",
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 420
                },
                PrimaryButtonText = "退出",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync(this);
            if (result == ContentDialogResult.Primary)
            {
                _forceClose = true;
                Close();
            }
        }
        finally
        {
            _closePromptShowing = false;
        }
    }
}
