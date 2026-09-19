using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>欢迎向导的走法（决定分步列表里有哪几步）</summary>
public enum WelcomeFlowMode
{
    /// <summary>首次启动的设置流程：许可 → 外观 → 通用 → 游戏 → 下载 → 完成</summary>
    Setup,

    /// <summary>数据迁移流程（欢迎首页底部的侧路入口）：许可 → 数据迁移 → 完成</summary>
    Migration,
}

/// <summary>
/// 欢迎窗口 ViewModel：持有各分页与流程状态。
/// 流程：欢迎首页 →（进入流程）同意许可 → 各设置页 / 数据迁移页 → 完成页。
/// 首次启动流程完成后写入配置标记 <see cref="LauncherConfig.WelcomeCompleted"/>。
/// </summary>
/// <remarks>
/// 分步顺序由本类说了算，页面不持有索引：走设置流程还是走数据迁移流程，
/// 只是换一份步骤列表（见 <c>EnterFlow</c>）。这样"某个流程少走几步"
/// 不需要在每个页面上加分支判断。
/// </remarks>
public partial class WelcomeViewModel : ViewModelBase
{
    /// <summary>欢迎流程完成后触发（窗口据此关闭）</summary>
    public event EventHandler? Completed;

    /// <summary>请求 Frame 导航到指定分页 ViewModel</summary>
    public event EventHandler<object>? PageNavigationRequested;

    /// <summary>是否为首次启动流程（完成后写入配置标记）</summary>
    public bool IsFirstRun { get; }

    /// <summary>
    /// 向导全程共用的一份配置。
    /// <para>
    /// 每个分页各自 <c>LauncherConfig.Load()</c> 会互相覆盖——后保存的那份会把前一个页面的
    /// 改动整体冲掉，所以设置类分页一律通过本属性读写。
    /// </para>
    /// </summary>
    public LauncherConfig Config { get; }

    /// <summary>欢迎首页（Frame 第一页，含开场动画）</summary>
    public WelcomePageViewModel Page { get; }

    /// <summary>开源许可页（两条流程的第 1 步）</summary>
    public WelcomeLicensePageViewModel LicensePage { get; }

    /// <summary>外观设置页</summary>
    public WelcomeAppearancePageViewModel AppearancePage { get; }

    /// <summary>通用设置页</summary>
    public WelcomeGeneralPageViewModel GeneralPage { get; }

    /// <summary>游戏设置页</summary>
    public WelcomeGamePageViewModel GamePage { get; }

    /// <summary>下载设置页</summary>
    public WelcomeDownloadPageViewModel DownloadPage { get; }

    /// <summary>完成页（两条流程的最后一步）</summary>
    public WelcomeFinishPageViewModel FinishPage { get; }

    /// <summary>数据迁移页</summary>
    public WelcomeMigrationPageViewModel MigrationPage { get; }

    /// <summary>开场动画是否已结束（控制窗口底部数据迁移按钮的显示）</summary>
    [ObservableProperty]
    private bool introCompleted;

    /// <summary>当前走的是哪条流程</summary>
    public WelcomeFlowMode Mode { get; private set; } = WelcomeFlowMode.Setup;

    /// <summary>当前流程的分步列表</summary>
    public IReadOnlyList<WelcomeStepViewModel> Steps => _steps;

    private IReadOnlyList<WelcomeStepViewModel> _steps = [];

    /// <summary>当前步骤索引（-1 表示还没进入流程，停在欢迎首页）</summary>
    public int CurrentStepIndex { get; private set; } = -1;

    public WelcomeViewModel(bool isFirstRun)
    {
        IsFirstRun = isFirstRun;
        Config = LauncherConfig.Load();

        Page = new WelcomePageViewModel(this);
        LicensePage = new WelcomeLicensePageViewModel(this);
        AppearancePage = new WelcomeAppearancePageViewModel(this);
        GeneralPage = new WelcomeGeneralPageViewModel(this);
        GamePage = new WelcomeGamePageViewModel(this);
        DownloadPage = new WelcomeDownloadPageViewModel(this);
        FinishPage = new WelcomeFinishPageViewModel(this);
        MigrationPage = new WelcomeMigrationPageViewModel(this);
    }

    /// <summary>请求导航到欢迎流程内的某个分页</summary>
    public void RequestNavigate(object pageViewModel)
    {
        PageNavigationRequested?.Invoke(this, pageViewModel);
    }

    /// <summary>欢迎首页「下一步」：进入设置流程</summary>
    public void BeginSetupFlow() => EnterFlow(WelcomeFlowMode.Setup);

    /// <summary>欢迎首页「数据迁移」：同样先过许可页，同意后进入数据迁移页</summary>
    public void BeginMigrationFlow() => EnterFlow(WelcomeFlowMode.Migration);

    /// <summary>前进一步；已经是最后一步时结束整个流程</summary>
    public void GoNext()
    {
        if (CurrentStepIndex < 0)
        {
            EnterFlow(Mode);
            return;
        }

        if (CurrentStepIndex + 1 >= _steps.Count)
        {
            Complete();
            return;
        }

        CurrentStepIndex++;
        NavigateToCurrentStep();
    }

    /// <summary>回退一步（第一步原地不动）</summary>
    public void GoBack()
    {
        if (CurrentStepIndex <= 0) return;

        CurrentStepIndex--;
        NavigateToCurrentStep();
    }

    /// <summary>某个步骤页是否可以回退（供页面底部的「上一步」按钮控制显隐）</summary>
    internal bool CanGoBackFrom(WelcomeStepViewModel step) => IndexOf(step) > 0;

    /// <summary>
    /// 把向导里改动过的配置落盘。
    /// 分页在离开时各存一次、而不是每次改动即存：拖动圆角滑块会以每像素一次的频率触发 setter，
    /// 逐次写盘既没必要也伤盘。
    /// </summary>
    public void SaveConfig() => Config.Save();

    public void Complete()
    {
        if (IsFirstRun)
        {
            Config.WelcomeCompleted = true;
        }

        Config.Save();
        Completed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>切到指定流程：重建步骤列表并停在第 1 步</summary>
    private void EnterFlow(WelcomeFlowMode mode)
    {
        Mode = mode;
        _steps = mode == WelcomeFlowMode.Migration
            ? [LicensePage, MigrationPage, FinishPage]
            : [LicensePage, AppearancePage, GeneralPage, GamePage, DownloadPage, FinishPage];

        CurrentStepIndex = 0;
        NavigateToCurrentStep();
    }

    private void NavigateToCurrentStep()
    {
        if (CurrentStepIndex < 0 || CurrentStepIndex >= _steps.Count) return;

        var step = _steps[CurrentStepIndex];
        RequestNavigate(step);

        // 步骤 VM 是构造期一次性建好并复用的：不刷新的话，
        // 第二次进入同一页时「上一步」的显隐会停留在上一次的位置
        step.RaiseStepStateChanged();
    }

    private int IndexOf(WelcomeStepViewModel step)
    {
        for (var i = 0; i < _steps.Count; i++)
        {
            if (ReferenceEquals(_steps[i], step)) return i;
        }
        return -1;
    }
}
