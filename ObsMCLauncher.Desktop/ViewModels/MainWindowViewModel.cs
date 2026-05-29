using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
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
    private bool isNavCollapsed;

    private double _navWidthValue = 200;
    public GridLength NavWidth
    {
        get => new GridLength(_navWidthValue);
    }

    private double _navRotationAngle;
    public double NavRotationAngle
    {
        get => _navRotationAngle;
        private set => SetProperty(ref _navRotationAngle, value);
    }

    private double _navTextOpacity = 1;
    public double NavTextOpacity
    {
        get => _navTextOpacity;
        private set => SetProperty(ref _navTextOpacity, value);
    }

    partial void OnIsNavCollapsedChanged(bool value)
    {
        AnimateNavWidth(value ? 48 : 200);
        AnimateNavRotation(value ? 90 : 0);
        AnimateNavTextOpacity(value ? 0 : 1);
    }

    private async void AnimateNavWidth(double targetWidth)
    {
        const int steps = 15;
        const int delay = 12;
        var startWidth = _navWidthValue;
        var diff = targetWidth - startWidth;

        for (int i = 1; i <= steps; i++)
        {
            var progress = (double)i / steps;
            var easedProgress = 1 - Math.Pow(1 - progress, 3);
            _navWidthValue = startWidth + diff * easedProgress;
            OnPropertyChanged(nameof(NavWidth));
            await System.Threading.Tasks.Task.Delay(delay);
        }

        _navWidthValue = targetWidth;
        OnPropertyChanged(nameof(NavWidth));
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

    private async void AnimateNavTextOpacity(double targetOpacity)
    {
        const int steps = 10;
        const int delay = 15;
        var startOpacity = NavTextOpacity;
        var diff = targetOpacity - startOpacity;

        for (int i = 1; i <= steps; i++)
        {
            var progress = (double)i / steps;
            NavTextOpacity = startOpacity + diff * progress;
            await System.Threading.Tasks.Task.Delay(delay);
        }

        NavTextOpacity = targetOpacity;
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
        _moreViewModel = new MoreViewModel(Notifications, _pluginLoader);

        // 初始化插件通知回调（必须在加载插件之前设置）
        InitializePluginCallbacks();

        // 启动时加载所有插件（必须在初始化回调之后）
        LoadPluginsOnStartup();

        // 从配置加载通知设置
        var config = LauncherConfig.Load();
        _notificationPosition = config.NotificationPosition;
        Notifications.NotificationPosition = config.NotificationPosition;
        Notifications.AutoCloseSeconds = config.NotificationAutoCloseSeconds;

        NavItems.Add(new NavItemViewModel("主页", _homeViewModel, "🏠"));
        NavItems.Add(new NavItemViewModel("多人联机", new MultiplayerViewModel(Notifications, Dialogs), "🌐"));
        NavItems.Add(new NavItemViewModel("账号管理", new AccountManagementViewModel(), "👤"));
        NavItems.Add(new NavItemViewModel("版本管理", new VersionDownloadViewModel(dispatcher, Notifications), "📥"));
        NavItems.Add(new NavItemViewModel("资源下载", new ResourcesViewModel(), "📦"));

        BottomNavItems.Add(new NavItemViewModel("设置", new SettingsViewModel(Notifications, _homeViewModel), "⚙️"));
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
    }

    [RelayCommand]
    private void ToggleNav()
    {
        IsNavCollapsed = !IsNavCollapsed;
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
                // 释放NotificationService
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
