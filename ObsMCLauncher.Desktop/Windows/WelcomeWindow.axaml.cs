using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Media.Animation;
using FluentAvalonia.UI.Navigation;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Windows;

/// <summary>
/// 欢迎窗口：首次启动时在主窗口显示前打开（流程不过完不显示主界面）。
/// 开场字母动画位于欢迎首页（WelcomePageView）内，结束后页面自行淡入内容；
/// 窗口打开时内容区加 anim 类淡入，内部 Frame 做分页导航。
/// </summary>
public partial class WelcomeWindow : Window
{
    private readonly WelcomeViewModel _vm;
    private bool _frameNavigated;

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

        VersionText.Text = $"v{VersionInfo.DisplayVersion}";

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

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
