using System;
using Avalonia.Controls;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views;

public partial class WelcomePageView : UserControl
{
    public WelcomePageView()
    {
        InitializeComponent();
    }

    private void Intro_OnAnimationEnd(object? sender, EventArgs e)
    {
        ContentRoot.Classes.Add("anim");
        // 通知 ViewModel 动画已结束（窗口底部的数据迁移按钮随之显示）
        (DataContext as WelcomePageViewModel)?.OnIntroCompleted();
    }
}
