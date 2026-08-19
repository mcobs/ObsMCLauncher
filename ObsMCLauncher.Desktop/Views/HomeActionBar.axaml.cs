using Avalonia.Controls;

namespace ObsMCLauncher.Desktop.Views;

/// <summary>
/// 主页底部固定操作区：账号选择、版本选择、启动按钮、日志开关。
/// 主页与设置页的模拟主页共用，保证视觉一致。
/// </summary>
public partial class HomeActionBar : UserControl
{
    public HomeActionBar()
    {
        InitializeComponent();
    }
}
