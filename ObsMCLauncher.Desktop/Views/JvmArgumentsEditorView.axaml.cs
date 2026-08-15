using Avalonia.Controls;

namespace ObsMCLauncher.Desktop.Views;

/// <summary>
/// JVM 参数编辑器（chips + 预设 + 自由编辑）。
/// 版本实例「版本设置」页与设置「游戏设置」页共用，DataContext 为 <see cref="ViewModels.JvmArgumentsEditorViewModel"/>。
/// </summary>
public partial class JvmArgumentsEditorView : UserControl
{
    public JvmArgumentsEditorView()
    {
        InitializeComponent();
    }
}
