using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views;

public partial class WelcomeGamePageView : UserControl
{
    public WelcomeGamePageView()
    {
        InitializeComponent();
    }

    /// <summary>挑选自定义游戏文件夹（与迁移页一样走 TopLevel 的 StorageProvider）</summary>
    private async void BrowseGameDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WelcomeGamePageViewModel vm) return;

        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null) return;

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择游戏文件夹",
            AllowMultiple = false
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            vm.SetCustomGameDirectory(path);
        }
    }
}
