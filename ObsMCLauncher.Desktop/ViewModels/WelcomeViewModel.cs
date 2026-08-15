using System;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 欢迎窗口 ViewModel：持有欢迎流程状态与首页（Frame 第一页）。
/// 首次启动流程完成后写入配置标记 WelcomeCompleted。
/// </summary>
public class WelcomeViewModel : ViewModelBase
{
    /// <summary>欢迎流程完成后触发（窗口据此关闭）</summary>
    public event EventHandler? Completed;

    /// <summary>是否为首次启动流程（完成后写入配置标记）</summary>
    public bool IsFirstRun { get; }

    /// <summary>欢迎首页（Frame 第一页）</summary>
    public WelcomePageViewModel Page { get; }

    public WelcomeViewModel(bool isFirstRun)
    {
        IsFirstRun = isFirstRun;
        Page = new WelcomePageViewModel(this);
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
