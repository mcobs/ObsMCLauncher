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

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择 PCL 所在的文件夹",
            AllowMultiple = false
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            vm.SetPclDirectory(path);
        }
    }
}
