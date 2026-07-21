using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Plugins;
using ObsMCLauncher.Desktop.ViewModels.Dialogs;
using ObsMCLauncher.Desktop.ViewModels.Notifications;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    public ObservableCollection<NavItemViewModel> NavItems { get; } = new();
    public ObservableCollection<NavItemViewModel> BottomNavItems { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    private NavItemViewModel? selectedNavItem;

    [ObservableProperty]
    private NavItemViewModel? selectedBottomNavItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaneOpen))]
    private bool isNavCollapsed;

    public bool IsPaneOpen => !IsNavCollapsed;

    private double _navRotationAngle;
    public double NavRotationAngle
    {
        get => _navRotationAngle;
        private set => SetProperty(ref _navRotationAngle, value);
    }

    partial void OnIsNavCollapsedChanged(bool value)
    {
        AnimateNavRotation(value ? 90 : 0);
    }

    private async void AnimateNavRotation(double targetAngle)
    {
        const int steps = 15;
        const int delay = 12;
        var startAngle = NavRotationAngle;
        var diff = targetAngle - startAngle;

        for (int i = 1; i <= steps; i++)
        {
            var progress = (double)i / steps;
            var easedProgress = 1 - Math.Pow(1 - progress, 3);
            NavRotationAngle = startAngle + diff * easedProgress;
            await System.Threading.Tasks.Task.Delay(delay);
        }

        NavRotationAngle = targetAngle;
    }

    public ViewModelBase? CurrentPage => SelectedNavItem?.Page ?? SelectedBottomNavItem?.Page;

    public DownloadManagerViewModel DownloadManager { get; }

    public NotificationService Notifications { get; } = new();

    public DialogService Dialogs { get; } = new();

    public string NavVersionText => $"v{ObsMCLauncher.Core.Utils.VersionInfo.ShortVersion}";

    public string NavCopyrightText => ObsMCLauncher.Core.Utils.VersionInfo.Copyright;

    [ObservableProperty]
    private NotificationPosition _notificationPosition;

    partial void OnNotificationPositionChanged(NotificationPosition value)
    {
        Notifications.NotificationPosition = value;
    }

    private readonly PluginLoader _pluginLoader;
    private HomeViewModel? _homeViewModel;
    private MoreViewModel? _moreViewModel;

    private bool _userManuallyToggled;
    private const double CollapseThreshold = 950;

    private double _windowWidth = double.NaN;
    public double WindowWidth
    {
        get => _windowWidth;
        set
        {
            if (SetProperty(ref _windowWidth, value))
            {
                CheckWidthBasedCollapse();
            }
        }
    }

    public MainWindowViewModel()
    {
        NavigationStore.MainWindow = this;

        var dispatcher = new ObsMCLauncher.Desktop.Services.AvaloniaDispatcher();
        ObsMCLauncher.Core.Services.Minecraft.DownloadTaskManager.Instance.SetDispatcher(dispatcher);
        ObsMCLauncher.Core.Services.Minecraft.DownloadBridge.Initialize();

        DownloadManager = new DownloadManagerViewModel(dispatcher);

        // 初始化插件系统
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "OMCL", "plugins");
        _pluginLoader = new PluginLoader(pluginsDir);

        // 创建主页ViewModel
        _homeViewModel = new HomeViewModel(dispatcher, Notifications);

        // 创建更多ViewModel
        _moreViewModel = new MoreViewModel(Notifications, _pluginLoader, Dialogs);

        // 初始化插件通知回调（必须在加载插件之前设置）
        InitializePluginCallbacks();

        // 启动时加载所有插件（必须在初始化回调之后）
        LoadPluginsOnStartup();

        // 从配置加载通知设置
        var config = LauncherConfig.Load();
        _notificationPosition = config.NotificationPosition;
        Notifications.NotificationPosition = config.NotificationPosition;
        Notifications.AutoCloseSeconds = config.NotificationAutoCloseSeconds;

        const string iconBase = "avares://ObsMCLauncher.Desktop/Assets/SidebarIcons/";
        NavItems.Add(new NavItemViewModel("主页", _homeViewModel, "🏠") { IconPath = iconBase + "dashboard.svg" });
        NavItems.Add(new NavItemViewModel("多人联机", new MultiplayerViewModel(Notifications, Dialogs), "🌐") { IconPath = iconBase + "multiplayer.svg" });
        NavItems.Add(new NavItemViewModel("账号管理", new AccountManagementViewModel(), "👤") { IconPath = iconBase + "accounts.svg" });
        NavItems.Add(new NavItemViewModel("版本管理", new VersionDownloadViewModel(dispatcher, Notifications), "📥") { IconPath = iconBase + "versions.svg" });
        NavItems.Add(new NavItemViewModel("资源下载", new ResourcesViewModel(), "📦") { IconPath = iconBase + "resources.svg" });

        BottomNavItems.Add(new NavItemViewModel("设置", new SettingsViewModel(Notifications, _homeViewModel), "⚙️") { IconPath = iconBase + "settings.svg" });
        BottomNavItems.Add(new NavItemViewModel("更多", _moreViewModel, "⋯"));

        SelectedNavItem = NavItems[0];
    }

    private void LoadPluginsOnStartup()
    {
        try
        {
            var pluginsDir = Path.Combine(AppContext.BaseDirectory, "OMCL", "plugins");
            DebugLogger.Info("MainWindow", $"插件目录: {pluginsDir}");
            DebugLogger.Info("MainWindow", $"目录存在: {Directory.Exists(pluginsDir)}");

            if (Directory.Exists(pluginsDir))
            {
                var pluginDirs = Directory.GetDirectories(pluginsDir);
                DebugLogger.Info("MainWindow", $"找到 {pluginDirs.Length} 个插件文件夹");

                foreach (var dir in pluginDirs)
                {
                    DebugLogger.Info("MainWindow", $"插件文件夹: {Path.GetFileName(dir)}");
                }
            }

            _pluginLoader.LoadAllPlugins();
            var loadedCount = _pluginLoader.LoadedPlugins.Count(p => p.IsLoaded);
            DebugLogger.Info("MainWindow", $"启动时加载了 {loadedCount} 个插件");

            foreach (var plugin in _pluginLoader.LoadedPlugins)
            {
                DebugLogger.Info("MainWindow", $"插件: {plugin.Name} (ID: {plugin.Id}) - 加载状态: {plugin.IsLoaded}");
                if (!string.IsNullOrEmpty(plugin.ErrorMessage))
                {
                    DebugLogger.Error("MainWindow", $"插件错误: {plugin.ErrorMessage}");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("MainWindow", $"启动时加载插件失败: {ex.Message}");
            DebugLogger.Error("MainWindow", $"堆栈: {ex.StackTrace}");
        }
    }

    private void InitializePluginCallbacks()
    {
        PluginContext.OnShowNotification = (title, message, type, duration) =>
        {
            var notifType = type.ToLowerInvariant() switch
            {
                "success" => NotificationType.Success,
                "warning" => NotificationType.Warning,
                "error" => NotificationType.Error,
                "progress" => NotificationType.Progress,
                _ => NotificationType.Info
            };
            return Notifications.Show(title, message, notifType, duration);
        };

        PluginContext.OnUpdateNotification = (id, message, progress) =>
        {
            Notifications.Update(id, message, progress);
        };

        PluginContext.OnCloseNotification = (id) =>
        {
            Notifications.Remove(id);
        };

        // 设置插件标签页和主页卡片回调
        PluginContext.OnTabRegistered = (pluginId, title, tabId, icon, payload) =>
        {
            DebugLogger.Info("Plugin", $"插件 {pluginId} 注册标签页: {title} (tabId: {tabId})");

            // 分发到MoreViewModel
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _moreViewModel?.OnPluginTabRegistered(pluginId, title, tabId, icon, payload);
            });
        };

        PluginContext.OnTabRegisteredWithContent = (title, tabId, customContent, payload) =>
        {
            DebugLogger.Info("Plugin", $"注册带自定义UI的标签页: {title} (tabId: {tabId}, hasContent: {customContent != null})");

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var control = customContent as Avalonia.Controls.Control;
                _moreViewModel?.OnPluginTabRegisteredWithContent("plugin", title, tabId, control, payload);
            });
        };

        PluginContext.OnHomeCardRegistered = (cardId, title, description, icon, commandId, payload) =>
        {
            DebugLogger.Info("Plugin", $"注册主页卡片: {title} (cardId: {cardId})");

            // 分发到HomeViewModel
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _homeViewModel?.OnPluginCardRegistered(cardId, title, description, icon, commandId, payload);
            });
        };

        PluginContext.OnHomeCardUnregistered = (cardId) =>
        {
            DebugLogger.Info("Plugin", $"注销主页卡片: {cardId}");

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _homeViewModel?.OnPluginCardUnregistered(cardId);
            });
        };

        PluginContext.OnTabUnregistered = (pluginId, tabId) =>
        {
            DebugLogger.Info("Plugin", $"注销标签页: {tabId} (插件: {pluginId})");

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _moreViewModel?.OnPluginTabUnregistered(pluginId, tabId);
            });
        };

        PluginLoader.OnPluginDisabled = (pluginId) =>
        {
            DebugLogger.Info("MainWindow", $"插件已禁用: {pluginId}");

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _moreViewModel?.RemoveAllPluginTabs(pluginId);
                _homeViewModel?.RemoveAllPluginCards(pluginId);
            });
        };

        PluginLoader.OnPluginEnabled = (pluginId) =>
        {
            DebugLogger.Info("MainWindow", $"插件已启用: {pluginId}");
        };

        PluginLoader.OnPluginRemoved = (pluginId) =>
        {
            DebugLogger.Info("MainWindow", $"插件已移除: {pluginId}");

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _moreViewModel?.RemoveAllPluginTabs(pluginId);
                _homeViewModel?.RemoveAllPluginCards(pluginId);
            });
        };
    }

    partial void OnSelectedNavItemChanged(NavItemViewModel? value)
    {
        if (value != null)
        {
            SelectedBottomNavItem = null;
        }
        
        OnPropertyChanged(nameof(CurrentPage));
        
        if (value?.Page is HomeViewModel homeVm)
        {
            _ = homeVm.LoadLocalAsync();
        }
        else if (value?.Page is VersionDownloadViewModel versionVm)
        {
            versionVm.RefreshInstalled();
        }
    }

    partial void OnSelectedBottomNavItemChanged(NavItemViewModel? value)
    {
        if (value != null)
        {
            SelectedNavItem = null;
        }
        
        OnPropertyChanged(nameof(CurrentPage));

        // 切换到"更多"页面时刷新更新通道显示
        if (value?.Title == "更多")
        {
            _moreViewModel?.RefreshChannelInfo();
        }
    }

    [RelayCommand]
    private void ToggleNav()
    {
        _userManuallyToggled = true;
        IsNavCollapsed = !IsNavCollapsed;
    }

    private void CheckWidthBasedCollapse()
    {
        if (double.IsNaN(_windowWidth))
            return;

        var shouldCollapse = _windowWidth < CollapseThreshold;

        if (_userManuallyToggled)
        {
            // 如果手动切换后的状态与当前宽度期望状态一致，重置手动标记
            if (shouldCollapse == IsNavCollapsed)
            {
                _userManuallyToggled = false;
            }
            return;
        }

        if (shouldCollapse != IsNavCollapsed)
        {
            IsNavCollapsed = shouldCollapse;
        }
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Notifications?.Dispose();
            }
            _disposed = true;
        }
    }

    ~MainWindowViewModel()
    {
        Dispose(false);
    }
}
