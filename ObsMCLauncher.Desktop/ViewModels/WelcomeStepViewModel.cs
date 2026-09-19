using CommunityToolkit.Mvvm.Input;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 欢迎向导里一个可前后翻页的步骤页基类：统一底部「上一步 / 下一步」两个圆形按钮的行为，
/// 子类只管页面内容与前进条件（仿 ClassIsland 的 <c>IWelcomePage</c> + 窗口级导航命令）。
/// </summary>
/// <remarks>
/// 步骤顺序、前进与回退都由 <see cref="WelcomeViewModel"/> 决定，页面自己不持有索引，
/// 这样"某个流程少走几步"（首次启动的设置流程 vs 数据迁移流程）只是换一份步骤列表，
/// 不需要在每个页面上加条件判断。
/// </remarks>
public abstract partial class WelcomeStepViewModel : ViewModelBase
{
    protected WelcomeStepViewModel(WelcomeViewModel owner, string title, string subtitle)
    {
        Owner = owner;
        Title = title;
        Subtitle = subtitle;
    }

    /// <summary>所属的欢迎流程（步骤列表与前进/回退都由它决定）</summary>
    protected WelcomeViewModel Owner { get; }

    /// <summary>页面大标题</summary>
    public string Title { get; }

    /// <summary>页面副标题（一句话说明这一步在做什么）</summary>
    public string Subtitle { get; }

    /// <summary>是否可以回退（流程第一步为 false，底部不显示「上一步」）</summary>
    public bool CanGoBack => Owner.CanGoBackFrom(this);

    /// <summary>「下一步」是否可用（如许可页未勾选同意时为 false）</summary>
    public virtual bool CanGoNext => true;

    /// <summary>「下一步」按钮的提示文字（完成页改为"完成"）</summary>
    public virtual string NextToolTip => "下一步";

    /// <summary>
    /// 进入本页时刷新与位置相关的可绑定属性。由 <see cref="WelcomeViewModel"/> 在导航后调用：
    /// 步骤 VM 是构造期一次性建好并复用的，不刷新的话"上一步"的显隐会停在第一次进入时的状态。
    /// </summary>
    internal void RaiseStepStateChanged()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        NextCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanProceed))]
    private void Next() => Advance();

    [RelayCommand]
    private void Back()
    {
        Owner.SaveConfig();
        Owner.GoBack();
    }

    /// <summary>「下一步」的可用性（转发给虚属性，便于 NotifyCanExecuteChanged 统一刷新）</summary>
    private bool CanProceed() => CanGoNext;

    /// <summary>
    /// 点「下一步」之后的行为，默认就是前进一步。
    /// 顺手把配置落盘：向导页是"改动即生效、离开本页才写盘"，避免拖滑块时逐帧写文件。
    /// </summary>
    protected virtual void Advance()
    {
        Owner.SaveConfig();
        Owner.GoNext();
    }
}
