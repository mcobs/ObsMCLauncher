using Avalonia.Controls;

namespace ObsMCLauncher.Desktop.Views;

/// <summary>
/// 崩溃分析结果展示面板（DataContext 为 CrashAnalysisResult）。
/// 「更多 → 崩溃分析」页与游戏崩溃提示窗口共用。
/// </summary>
public partial class CrashAnalysisPanel : UserControl
{
    public CrashAnalysisPanel()
    {
        InitializeComponent();
    }
}
