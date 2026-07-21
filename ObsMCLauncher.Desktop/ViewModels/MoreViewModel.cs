using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Plugins;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.ViewModels.Dialogs;
using ObsMCLauncher.Desktop.ViewModels.Notifications;
using ObsMCLauncher.Desktop.Windows;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class MoreViewModel : ViewModelBase
{
    private readonly NotificationService _notificationService;
    private readonly PluginLoader _pluginLoader;
    private readonly DialogService _dialogService;
    private ViewModelBase? _previousTabContent;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _versionText = $"版本 {VersionInfo.DisplayVersion}";

    public string UpdateChannelText => $"更新通道: {UpdateService.GetChannelDisplayName(UpdateService.CurrentChannel)}";

    /// <summary>
    /// 刷新版本和通道显示信息（在导航到"更多"页面时调用）
    /// </summary>
    public void RefreshChannelInfo()
    {
        OnPropertyChanged(nameof(UpdateChannelText));
    }

    [ObservableProperty]
    private bool _isCheckingUpdate;

    public ObservableCollection<TabItemViewModel> Tabs { get; }

    public AboutViewModel About { get; }
    public PluginsViewModel Plugins { get; }
    public ScreenshotsViewModel Screenshots { get; }
    public ServersViewModel Servers { get; }

    public MoreViewModel(NotificationService notificationService, PluginLoader pluginLoader, DialogService dialogService)
    {
        _notificationService = notificationService;
        _pluginLoader = pluginLoader;
        _dialogService = dialogService;

        About = new AboutViewModel(notificationService, dialogService);
        About.RequestOpenDebugConsole = OpenDebugConsole;
        Plugins = new PluginsViewModel(_pluginLoader, notificationService);
        Screenshots = new ScreenshotsViewModel(notificationService);
        Servers = new ServersViewModel(notificationService);

        Tabs = new ObservableCollection<TabItemViewModel>
        {
            new TabItemViewModel("关于", About),
            new TabItemViewModel("插件", Plugins),
            new TabItemViewModel("截图管理", Screenshots),
            new TabItemViewModel("服务器收藏", Servers)
        };

        // 注意：PluginContext.OnTabRegistered 现在在 MainWindowViewModel 中设置
    }

    public void OnPluginTabRegistered(string pluginId, string title, string tabId, string? icon, object? payload)
    {
        OnPluginTabRegisteredWithContent(pluginId, title, tabId, null, payload);
    }

    public void OnPluginTabRegisteredWithContent(string pluginId, string title, string tabId, Avalonia.Controls.Control? customContent, object? payload)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var existingTab = Tabs.FirstOrDefault(t => t.Header == title);
                if (existingTab == null)
                {
                    var pluginTabViewModel = new PluginTabViewModel(pluginId, tabId, title, payload, customContent);
                    var tabItem = new TabItemViewModel(title, pluginTabViewModel);

                    var pluginTabIndex = Tabs.IndexOf(Tabs.First(t => t.Header == "插件"));
                    Tabs.Insert(pluginTabIndex + 1, tabItem);

                    DebugLogger.Info("MoreViewModel", $"已添加插件标签页: {title} (插件: {pluginId}, 自定义UI: {customContent != null})");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error("MoreViewModel", $"添加插件标签页失败: {ex.Message}");
            }
        });
    }

    public void OnPluginTabUnregistered(string pluginId, string tabId)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var tabToRemove = Tabs.FirstOrDefault(t => t.Content is PluginTabViewModel vm && vm.TabId == tabId);
                if (tabToRemove != null)
                {
                    Tabs.Remove(tabToRemove);
                    DebugLogger.Info("MoreViewModel", $"已移除插件标签页: {tabToRemove.Header} (插件: {pluginId})");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error("MoreViewModel", $"移除插件标签页失败: {ex.Message}");
            }
        });
    }

    public void RemoveAllPluginTabs(string pluginId)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var tabsToRemove = Tabs.Where(t => t.Content is PluginTabViewModel vm && vm.PluginId == pluginId).ToList();
                foreach (var tab in tabsToRemove)
                {
                    Tabs.Remove(tab);
                }
                DebugLogger.Info("MoreViewModel", $"已移除插件 {pluginId} 的所有标签页，共 {tabsToRemove.Count} 个");
            }
            catch (Exception ex)
            {
                DebugLogger.Error("MoreViewModel", $"移除插件标签页失败: {ex.Message}");
            }
        });
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value < 0 || value >= Tabs.Count) return;

        if (_previousTabContent is ServersViewModel prevServers)
        {
            prevServers.StopAutoRefresh();
        }

        var selectedTab = Tabs[value];

        if (selectedTab.Content is PluginsViewModel)
        {
            _ = Plugins.InitializeAsync();
        }
        else if (selectedTab.Content is ScreenshotsViewModel)
        {
            _ = Screenshots.LoadAsync();
        }
        else if (selectedTab.Content is ServersViewModel)
        {
            Servers.Load();
            _ = Servers.ActivateAsync();
        }
        else if (selectedTab.Content is PluginTabViewModel pluginTab)
        {
            pluginTab.Initialize();
            DebugLogger.Info("MoreViewModel", $"激活插件标签页: {pluginTab.Title}");
        }

        _previousTabContent = selectedTab.Content;
    }

    public async Task InitializeAsync()
    {
        await Plugins.InitializeAsync();
        await Screenshots.LoadAsync();
        Servers.Load();
    }

    private void OpenDebugConsole()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var win = new DevConsoleWindow();
            win.Show();
        });
    }

    [RelayCommand]
    private async Task CheckUpdate()
    {
        if (IsCheckingUpdate) return;

        try
        {
            IsCheckingUpdate = true;
            _notificationService.Show("检查更新", "正在检查更新...", NotificationType.Info);

            var result = await UpdateService.CheckForUpdatesAsync();
            if (result != null)
            {
                await HandleUpdateResultAsync(result);
            }
            else
            {
                _notificationService.Show("已是最新版本", $"当前版本 {VersionInfo.DisplayVersion} 已是最新版本", NotificationType.Success);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("检查更新失败", ex.Message, NotificationType.Error);
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private async Task HandleUpdateResultAsync(UpdateCheckResult result)
    {
        if (result.CanAutoUpdate)
        {
            var notes = string.IsNullOrEmpty(result.ReleaseNotes) ? $"新版本 {result.Version} 已发布" : result.ReleaseNotes;
            var confirmed = await _dialogService.ShowUpdateDialogAsync(
                "发现新版本",
                notes,
                "下载并安装",
                "稍后再说");

            if (confirmed && result.VelopackUpdateInfo != null)
            {
                var dialog = _dialogService.UpdateDialogCurrent;
                if (dialog != null)
                {
                    dialog.IsDownloading = true;
                    dialog.DownloadStatusText = "正在下载更新...";
                    dialog.ConfirmText = "下载中...";
                    _dialogService.ReopenUpdateDialog();

                    try
                    {
                        await UpdateService.DownloadAndApplyUpdateAsync(
                            result.VelopackUpdateInfo,
                            progress =>
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    dialog.DownloadProgress = progress;
                                    dialog.DownloadStatusText = $"正在下载更新... {progress}%";
                                });
                            });
                    }
                    catch (Exception ex)
                    {
                        dialog.IsDownloading = false;
                        dialog.ConfirmText = "下载并安装";
                        _notificationService.Show("下载更新失败", ex.Message, NotificationType.Error);
                        return;
                    }
                }
                else
                {
                    try
                    {
                        await UpdateService.DownloadAndApplyUpdateAsync(result.VelopackUpdateInfo);
                    }
                    catch (Exception ex)
                    {
                        _notificationService.Show("下载更新失败", ex.Message, NotificationType.Error);
                        return;
                    }
                }
            }
        }
        else
        {
            _notificationService.Show("发现新版本", $"有新版本 {result.Version} 可用，正在打开下载页面...", NotificationType.Success);
            UpdateService.OpenLatestReleasePage();
        }
    }
}

public class TabItemViewModel
{
    public string Header { get; }
    public ViewModelBase Content { get; }

    public TabItemViewModel(string header, ViewModelBase content)
    {
        Header = header;
        Content = content;
    }
}

public partial class AboutViewModel : ViewModelBase
{
    private readonly NotificationService _notificationService;
    private readonly DialogService _dialogService;
    private int _titleClickCount = 0;
    private DateTime _lastTitleClickTime = DateTime.MinValue;
    private const int ClickResetMs = 2000;

    [ObservableProperty]
    private string _versionText = $"版本 {VersionInfo.DisplayVersion}";

    public string UpdateChannelText => $"更新通道: {UpdateService.GetChannelDisplayName(UpdateService.CurrentChannel)}";

    /// <summary>
    /// 刷新版本和通道显示信息（在导航到"更多"页面时调用）
    /// </summary>
    public void RefreshChannelInfo()
    {
        OnPropertyChanged(nameof(UpdateChannelText));
    }

    [ObservableProperty]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    private bool _showDebugConsole;

    public Action? RequestOpenDebugConsole { get; set; }

    public AboutViewModel(NotificationService notificationService, DialogService dialogService)
    {
        _notificationService = notificationService;
        _dialogService = dialogService;
    }

    public void OnTitleClick()
    {
        var now = DateTime.Now;
        var timeSinceLastClick = (now - _lastTitleClickTime).TotalMilliseconds;
        if (timeSinceLastClick > ClickResetMs)
            _titleClickCount = 0;

        _titleClickCount++;
        _lastTitleClickTime = now;

        if (_titleClickCount >= 5)
        {
            _titleClickCount = 0;
            RequestOpenDebugConsole?.Invoke();
        }
    }

    [RelayCommand]
    private static void OpenGitHub() => OpenUrl("https://github.com/mcobs/ObsMCLauncher");

    [RelayCommand]
    private static void OpenForum() => OpenUrl("https://mcobs.cn/");

    [RelayCommand]
    private static void OpenBangBang93() => OpenUrl("https://afdian.com/a/bangbang93");

    [RelayCommand]
    private static void OpenMciLmLink() => OpenUrl("https://link.mcilm.top/");

    [RelayCommand]
    private static void OpenAuthlibInjector() => OpenUrl("https://github.com/yushijinhun/authlib-injector");

    [RelayCommand]
    private static void OpenMCIM() => OpenUrl("https://www.mcimirror.top/");

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private async Task CheckUpdate()
    {
        if (IsCheckingUpdate) return;

        try
        {
            IsCheckingUpdate = true;
            _notificationService.Show("检查更新", "正在检查更新...", NotificationType.Info);

            var result = await UpdateService.CheckForUpdatesAsync();
            if (result != null)
            {
                await HandleUpdateResultAsync(result);
            }
            else
            {
                _notificationService.Show("已是最新版本", $"当前版本 {VersionInfo.DisplayVersion} 已是最新版本", NotificationType.Success);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("检查更新失败", ex.Message, NotificationType.Error);
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private async Task HandleUpdateResultAsync(UpdateCheckResult result)
    {
        if (result.CanAutoUpdate)
        {
            var notes = string.IsNullOrEmpty(result.ReleaseNotes) ? $"新版本 {result.Version} 已发布" : result.ReleaseNotes;
            var confirmed = await _dialogService.ShowUpdateDialogAsync(
                "发现新版本",
                notes,
                "下载并安装",
                "稍后再说");

            if (confirmed && result.VelopackUpdateInfo != null)
            {
                var dialog = _dialogService.UpdateDialogCurrent;
                if (dialog != null)
                {
                    dialog.IsDownloading = true;
                    dialog.DownloadStatusText = "正在下载更新...";
                    dialog.ConfirmText = "下载中...";
                    _dialogService.ReopenUpdateDialog();

                    try
                    {
                        await UpdateService.DownloadAndApplyUpdateAsync(
                            result.VelopackUpdateInfo,
                            progress =>
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    dialog.DownloadProgress = progress;
                                    dialog.DownloadStatusText = $"正在下载更新... {progress}%";
                                });
                            });
                    }
                    catch (Exception ex)
                    {
                        dialog.IsDownloading = false;
                        dialog.ConfirmText = "下载并安装";
                        _notificationService.Show("下载更新失败", ex.Message, NotificationType.Error);
                        return;
                    }
                }
                else
                {
                    try
                    {
                        await UpdateService.DownloadAndApplyUpdateAsync(result.VelopackUpdateInfo);
                    }
                    catch (Exception ex)
                    {
                        _notificationService.Show("下载更新失败", ex.Message, NotificationType.Error);
                        return;
                    }
                }
            }
        }
        else
        {
            _notificationService.Show("发现新版本", $"有新版本 {result.Version} 可用，正在打开下载页面...", NotificationType.Success);
            UpdateService.OpenLatestReleasePage();
        }
    }
}
