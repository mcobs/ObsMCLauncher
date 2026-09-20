using Avalonia.Controls;

namespace ObsMCLauncher.Desktop.Views;

/// <summary>
/// 游戏崩溃弹窗（ContentDialog）的内容体，DataContext 为 GameCrashViewModel。
/// 弹窗按钮由 GameCrashDialog 负责，这里只渲染状态内容。
/// </summary>
public partial class GameCrashDialogContent : UserControl
{
    public GameCrashDialogContent()
    {
        InitializeComponent();
    }
}
