using Avalonia.Controls;
using Avalonia.Interactivity;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views;

public partial class WelcomeMigrationPageView : UserControl
{
    public WelcomeMigrationPageView()
    {
        InitializeComponent();
    }

    private void SettingsExpanderSource_OnClick(object? sender, RoutedEventArgs e)
    {
        // 选定导入来源，进入选择数据页
        (DataContext as WelcomeMigrationPageViewModel)?.NextCommand.Execute(null);
    }
}
