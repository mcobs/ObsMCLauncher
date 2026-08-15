using System;
using CommunityToolkit.Mvvm.ComponentModel;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 欢迎窗口 ViewModel：持有欢迎流程各分页与流转状态。
/// 流程：欢迎首页 → 开源许可页 →（数据迁移入口时）数据迁移页 → 完成。
/// 首次启动流程完成后写入配置标记 WelcomeCompleted。
/// </summary>
public partial class WelcomeViewModel : ViewModelBase
{
    /// <summary>欢迎流程完成后触发（窗口据此关闭）</summary>
    public event EventHandler? Completed;

    /// <summary>请求 Frame 导航到指定分页 ViewModel</summary>
    public event EventHandler<object>? PageNavigationRequested;

    /// <summary>是否为首次启动流程（完成后写入配置标记）</summary>
    public bool IsFirstRun { get; }

    /// <summary>欢迎首页（Frame 第一页，含开场动画）</summary>
    public WelcomePageViewModel Page { get; }

    /// <summary>开源许可页</summary>
    public WelcomeLicensePageViewModel LicensePage { get; }

    /// <summary>数据迁移页</summary>
    public WelcomeMigrationPageViewModel MigrationPage { get; }

    /// <summary>开场动画是否已结束（控制窗口底部数据迁移按钮的显示）</summary>
    [ObservableProperty]
    private bool introCompleted;

    /// <summary>许可页同意后的去向：true 进入数据迁移页，false 直接完成流程</summary>
    internal bool ContinueToMigration { get; set; }

    public WelcomeViewModel(bool isFirstRun)
    {
        IsFirstRun = isFirstRun;
        Page = new WelcomePageViewModel(this);
        LicensePage = new WelcomeLicensePageViewModel(this);
        MigrationPage = new WelcomeMigrationPageViewModel(this);
    }

    /// <summary>请求导航到欢迎流程内的某个分页</summary>
    public void RequestNavigate(object pageViewModel)
    {
        PageNavigationRequested?.Invoke(this, pageViewModel);
    }

    /// <summary>数据迁移入口：先经过许可页，同意后进入数据迁移页</summary>
    public void RequestMigration()
    {
        ContinueToMigration = true;
        RequestNavigate(LicensePage);
    }

    public void Complete()
    {
        if (IsFirstRun)
        {
            var config = LauncherConfig.Load();
            config.WelcomeCompleted = true;
            config.Save();
        }

        Completed?.Invoke(this, EventArgs.Empty);
    }
}
