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
using ObsMCLauncher.Desktop.ViewModels.Notifications;
using ObsMCLauncher.Desktop.Windows;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class MoreViewModel : ViewModelBase
{
    private readonly NotificationService _notificationService;
    private readonly PluginLoader _pluginLoader;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _versionText = $"版本 {VersionInfo.DisplayVersion}";

    [ObservableProperty]
    private bool _isCheckingUpdate;

    public ObservableCollection<TabItemViewModel> Tabs { get; }

    public AboutViewModel About { get; }
    public PluginsViewModel Plugins { get; }
    public ScreenshotsViewModel Screenshots { get; }
    public ServersViewModel Servers { get; }

    public MoreViewModel(NotificationService notificationService, PluginLoader pluginLoader)
    {
        _notificationService = notificationService;
        _pluginLoader = pluginLoader;

        About = new AboutViewModel(notificationService);
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
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var existingTab = Tabs.FirstOrDefault(t => t.Header == title);
                if (existingTab == null)
                {
                    var pluginTabViewModel = new PluginTabViewModel(pluginId, tabId, title, payload);
                    var tabItem = new TabItemViewModel(title, pluginTabViewModel);

                    var pluginTabIndex = Tabs.IndexOf(Tabs.First(t => t.Header == "插件"));
                    Tabs.Insert(pluginTabIndex + 1, tabItem);

                    DebugLogger.Info("MoreViewModel", $"已添加插件标签页: {title} (插件: {pluginId})");
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
            _ = Servers.LoadAsync();
        }
        else if (selectedTab.Content is PluginTabViewModel pluginTab)
        {
            pluginTab.Initialize();
            DebugLogger.Info("MoreViewModel", $"激活插件标签页: {pluginTab.Title}");
        }
    }

    public async Task InitializeAsync()
    {
        await Plugins.InitializeAsync();
        await Screenshots.LoadAsync();
        await Servers.LoadAsync();
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

            var newRelease = await UpdateService.CheckForUpdatesAsync();
            if (newRelease != null)
            {
                _notificationService.Show("发现新版本", $"有新版本可用: {newRelease.TagName}", NotificationType.Success);
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
    private int _titleClickCount = 0;
    private DateTime _lastTitleClickTime = DateTime.MinValue;
    private const int ClickResetMs = 2000;

    [ObservableProperty]
    private string _versionText = $"版本 {VersionInfo.DisplayVersion}";

    [ObservableProperty]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    private bool _showDebugConsole;

    public Action? RequestOpenDebugConsole { get; set; }

    public AboutViewModel(NotificationService notificationService)
    {
        _notificationService = notificationService;
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

            var newRelease = await UpdateService.CheckForUpdatesAsync();
            if (newRelease != null)
            {
                _notificationService.Show("发现新版本", $"有新版本可用: {newRelease.TagName}", NotificationType.Success);
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
}
