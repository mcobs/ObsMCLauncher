using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 欢迎流程：数据迁移页（仅导入）。仿 ClassIsland 数据迁移向导，Carousel 分步：
/// 选择导入来源 → 选择要导入的数据 → 导入中 → 导入完成。
/// 具体迁移逻辑暂为占位。
/// </summary>
public partial class WelcomeMigrationPageViewModel : ViewModelBase
{
    private readonly WelcomeViewModel _owner;

    /// <summary>导入来源名称</summary>
    public string SourceName { get; } = "Plain Craft Launcher 2（2.13.0.0+）";

    /// <summary>Carousel 当前页索引（0 选择来源 / 1 选择数据 / 2 导入中 / 3 完成）</summary>
    [ObservableProperty]
    private int pageIndex;

    /// <summary>是否导入游戏设置</summary>
    [ObservableProperty]
    private bool importGameSettings = true;

    /// <summary>是否导入应用设置</summary>
    [ObservableProperty]
    private bool importAppSettings = true;

    public WelcomeMigrationPageViewModel(WelcomeViewModel owner)
    {
        _owner = owner;
    }

    /// <summary>选定导入来源，进入选择数据页</summary>
    [RelayCommand]
    private void Next() => PageIndex = 1;

    /// <summary>返回来源选择页</summary>
    [RelayCommand]
    private void Back() => PageIndex = 0;

    /// <summary>开始导入（占位：稍作延时后进入完成页）</summary>
    [RelayCommand]
    private async Task StartImportAsync()
    {
        PageIndex = 2;
        // TODO: 按选中的来源与勾选项执行实际迁移
        await Task.Delay(1200);
        PageIndex = 3;
    }

    /// <summary>完成向导，结束欢迎流程</summary>
    [RelayCommand]
    private void Finish() => _owner.Complete();
}
