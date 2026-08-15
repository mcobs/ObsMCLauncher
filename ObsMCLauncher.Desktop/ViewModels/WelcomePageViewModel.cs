using CommunityToolkit.Mvvm.Input;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 欢迎首页（Frame 第一页）：问候语 + 开始使用。
/// 后续欢迎流程内的设置页可继续追加并经由 Frame 分页导航。
/// </summary>
public partial class WelcomePageViewModel : ViewModelBase
{
    private readonly WelcomeViewModel _owner;

    public WelcomePageViewModel(WelcomeViewModel owner)
    {
        _owner = owner;
    }

    [RelayCommand]
    private void Complete()
    {
        _owner.Complete();
    }
}
