using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 欢迎首页（Frame 第一页）：问候 + 下一步 + 底部版本号/数据迁移入口。
/// 下一步进入开源许可页；数据迁移入口先经过许可页再进迁移页。
/// </summary>
public partial class WelcomePageViewModel : ViewModelBase
{
    private readonly WelcomeViewModel _owner;

    public WelcomePageViewModel(WelcomeViewModel owner)
    {
        _owner = owner;
    }

    /// <summary>显示用版本号</summary>
    public string Version { get; } = $"v{VersionInfo.DisplayVersion}";

    /// <summary>开场动画是否结束（控制底部版本号与数据迁移入口的显示）</summary>
    public bool IntroCompleted => _owner.IntroCompleted;

    /// <summary>开场动画结束回调（由视图触发）</summary>
    internal void OnIntroCompleted()
    {
        _owner.IntroCompleted = true;
        OnPropertyChanged(nameof(IntroCompleted));
    }

    [RelayCommand]
    private void Next()
    {
        _owner.RequestNavigate(_owner.LicensePage);
    }

    [RelayCommand]
    private void Migration()
    {
        _owner.RequestMigration();
    }
}
