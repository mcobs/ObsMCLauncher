using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views;

public partial class WelcomeMigrationPageView : UserControl
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public WelcomeMigrationPageView()
    {
        InitializeComponent();
    }

    private void SourcePcl_OnClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as WelcomeMigrationPageViewModel)?.SelectSource(MigrationSource.Pcl2);
    }

    private void SourceHmclNew_OnClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as WelcomeMigrationPageViewModel)?.SelectSource(MigrationSource.HmclNew);
    }

    private void SourceHmclLegacy_OnClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as WelcomeMigrationPageViewModel)?.SelectSource(MigrationSource.HmclLegacy);
    }

    private async void BrowseSourceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WelcomeMigrationPageViewModel vm) return;

        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null) return;

        var filters = BuildFileFilters(vm.SelectedSource);

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择启动器主程序",
            AllowMultiple = false,
            FileTypeFilter = filters
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            vm.SetSourceExecutable(path);
        }
    }

    /// <summary>按来源与操作系统构造文件选择器过滤（PCL 为 exe；HMCL 在 Windows 为 exe/jar，其他系统为 jar）</summary>
    private static System.Collections.Generic.List<FilePickerFileType> BuildFileFilters(MigrationSource source)
    {
        var filters = new System.Collections.Generic.List<FilePickerFileType>();

        switch (source)
        {
            case MigrationSource.HmclNew:
            case MigrationSource.HmclLegacy:
                if (IsWindows)
                {
                    filters.Add(new FilePickerFileType("HMCL 程序") { Patterns = ["*.exe", "*.jar"] });
                }
                else
                {
                    filters.Add(new FilePickerFileType("HMCL jar 包") { Patterns = ["*.jar"] });
                }
                break;
            default:
                filters.Add(new FilePickerFileType("可执行文件") { Patterns = ["*.exe"] });
                break;
        }

        filters.Add(new FilePickerFileType("所有文件") { Patterns = ["*"] });
        return filters;
    }
}
