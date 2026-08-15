using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
        // 选定导入来源，进入配置页
        (DataContext as WelcomeMigrationPageViewModel)?.NextCommand.Execute(null);
    }

    private async void BrowsePclButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WelcomeMigrationPageViewModel vm) return;

        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null) return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 PCL 主程序",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("PCL 主程序")
                {
                    Patterns = ["*.exe"]
                },
                new FilePickerFileType("所有文件") { Patterns = ["*"] }
            ]
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            vm.SetPclExecutable(path);
        }
    }
}
