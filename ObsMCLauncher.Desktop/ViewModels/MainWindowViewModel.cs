using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Plugins;
using ObsMCLauncher.Core.Services.Accounts;
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
    private bool isPaneOpen = true;

    partial void OnIsPaneOpenChanged(bool value)
    {
        // 仅在窗口宽度允许展开时记忆状态，窄窗口下的自适应收起不写入配置
        if (_windowWidth >= CollapseThreshold)
        {
            var config = LauncherConfig.Load();
            config.IsNavCollapsed = !value;
            config.Save();
        }
    }

    public NavItemViewModel? SelectedNavEntry => SelectedNavItem ?? SelectedBottomNavItem;

    public ViewModelBase? CurrentPage => SelectedNavItem?.Page ?? SelectedBottomNavItem?.Page;

    public DownloadManagerViewModel DownloadManager { get; }

    public NotificationService Notifications { get; } = new();

    public DialogService Dialogs { get; } = new();

    public string NavVersionText => $"v{ObsMCLauncher.Core.Utils.VersionInfo.ShortVersion}";

    [ObservableProperty]
    private NotificationPosition _notificationPosition;

    partial void OnNotificationPositionChanged(NotificationPosition value)
    {
        Notifications.NotificationPosition = value;
    }

    private readonly PluginLoader _pluginLoader;
    private HomeViewModel? _homeViewModel;
    private MoreViewModel? _moreViewModel;

    /// <summary>主页 ViewModel（供其它页面直接引用，避免按导航标题查找）</summary>
    public HomeViewModel Home => _homeViewModel!;

    /// <summary>账号管理 ViewModel（构造时创建，供主页等直接引用）</summary>
    public AccountManagementViewModel AccountManagement { get; private set; } = null!;

    /// <summary>设置 ViewModel（构造时创建，供主页等直接引用）</summary>
    public SettingsViewModel Settings { get; private set; } = null!;

    /// <summary>版本管理 ViewModel（构造时创建）</summary>
    public VersionDownloadViewModel VersionDownload { get; private set; } = null!;

    private const double CollapseThreshold = 950;

    private double _windowWidth = double.NaN;
    public double WindowWidth
    {
        get => _windowWidth;
        set => SetProperty(ref _windowWidth, value);
    }

    public MainWindowViewModel()
    {
        NavigationStore.MainWindow = this;

        var dispatcher = new ObsMCLauncher.Desktop.Services.AvaloniaDispatcher();
        ObsMCLauncher.Core.Services.Minecraft.DownloadTaskManager.Instance.SetDispatcher(dispatcher);
        ObsMCLauncher.Core.Services.Download.DownloadTaskManager.Instance.SetDispatcher(dispatcher);
        ObsMCLauncher.Core.Services.Minecraft.DownloadBridge.Initialize();

        DownloadManager = new DownloadManagerViewModel(dispatcher);

        // 初始化插件系统
        var pluginsDir = Path.Combine(VersionInfo.GetAppBaseDirectory(), "OMCL", "plugins");
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

        // 恢复上次的导航栏状态（窗口宽度自适应逻辑由 NavigationView 阈值接管）
        IsPaneOpen = !config.IsNavCollapsed;

        AccountManagement = new AccountManagementViewModel();
        VersionDownload = new VersionDownloadViewModel(dispatcher, Notifications);
        Settings = new SettingsViewModel(Notifications, _homeViewModel);

        const string iconBase = "avares://ObsMCLauncher.Desktop/Assets/SidebarIcons/";
        NavItems.Add(new NavItemViewModel("主页", Home, "🏠") { IconPath = iconBase + "dashboard.svg" });
        NavItems.Add(new NavItemViewModel("多人联机", new MultiplayerViewModel(Notifications, Dialogs), "🌐") { IconPath = iconBase + "multiplayer.svg" });
        NavItems.Add(new NavItemViewModel("账号管理", AccountManagement, "👤") { IconPath = iconBase + "accounts.svg" });
        NavItems.Add(new NavItemViewModel("版本管理", VersionDownload, "📥") { IconPath = iconBase + "versions.svg" });
        NavItems.Add(new NavItemViewModel("资源下载", new ResourcesViewModel(), "📦") { IconPath = iconBase + "resources.svg" });

        BottomNavItems.Add(new NavItemViewModel("设置", Settings, "⚙️") { IconPath = iconBase + "settings.svg" });
        BottomNavItems.Add(new NavItemViewModel("更多", _moreViewModel, "⋯") { IconPath = iconBase + "more.svg" });

        SelectedNavItem = NavItems[0];
    }

    private void LoadPluginsOnStartup()
    {
        try
        {
            var pluginsDir = Path.Combine(VersionInfo.GetAppBaseDirectory(), "OMCL", "plugins");
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

        PluginContext.OnTabRegisteredWithContent = (pluginId, title, tabId, customContent, payload) =>
        {
            DebugLogger.Info("Plugin", $"注册带自定义UI的标签页: {title} (tabId: {tabId}, plugin: {pluginId}, hasContent: {customContent != null})");

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var control = customContent as Avalonia.Controls.Control;
                _moreViewModel?.OnPluginTabRegisteredWithContent(pluginId, title, tabId, control, payload);
            });
        };

        PluginContext.OnHomeCardRegistered = (cardId, title, description, icon, commandId, payload, defaultSize) =>
        {
            DebugLogger.Info("Plugin", $"注册主页卡片: {title} (cardId: {cardId})");

            // 无条件同步组件注册表，保证与插件生命周期一致（不依赖 HomeViewModel 是否已创建）
            var dot = cardId.IndexOf('.');
            var pluginId = dot > 0 ? cardId[..dot] : cardId;
            var shortCardId = dot > 0 ? cardId[(dot + 1)..] : cardId;
            ObsMCLauncher.Core.Services.HomeComponentRegistry.RegisterPluginCard(
                pluginId, shortCardId, title, description, icon, defaultSize);

            // 分发到HomeViewModel
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _homeViewModel?.OnPluginCardRegistered(cardId, title, description, icon, commandId, payload);
            });
        };

        PluginContext.OnHomeCardUnregistered = (cardId) =>
        {
            DebugLogger.Info("Plugin", $"注销主页卡片: {cardId}");

            ObsMCLauncher.Core.Services.HomeComponentRegistry.Unregister(cardId);

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

            ObsMCLauncher.Core.Services.HomeComponentRegistry.RemovePluginComponents(pluginId);

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

            ObsMCLauncher.Core.Services.HomeComponentRegistry.RemovePluginComponents(pluginId);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _moreViewModel?.RemoveAllPluginTabs(pluginId);
                _homeViewModel?.RemoveAllPluginCards(pluginId);
            });
        };

        // 插件日志写入启动器统一日志
        PluginContext.OnLogMessage = (pluginId, level, message) =>
        {
            var tag = $"Plugin[{pluginId}]";
            switch (level)
            {
                case PluginLogLevel.Debug:
                    DebugLogger.Debug(tag, message);
                    break;
                case PluginLogLevel.Info:
                    DebugLogger.Info(tag, message);
                    break;
                case PluginLogLevel.Warning:
                    DebugLogger.Warn(tag, message);
                    break;
                case PluginLogLevel.Error:
                    DebugLogger.Error(tag, message);
                    break;
            }
        };

        // 获取已安装版本列表（只读精简信息）
        PluginContext.OnGetInstalledVersions = (pluginId) =>
        {
            try
            {
                var config = LauncherConfig.Load();
                return ObsMCLauncher.Core.Services.Minecraft.LocalVersionService.GetInstalledVersions(config.GameDirectory)
                    .Select(v => new PluginVersionInfo
                    {
                        VersionId = v.Id,
                        McVersion = string.IsNullOrEmpty(v.ActualVersionId) ? v.Id : v.ActualVersionId,
                        LoaderType = NormalizePluginLoaderType(v.LoaderType),
                        VersionDirectory = v.Path,
                        LastPlayed = v.LastPlayed > DateTime.MinValue ? v.LastPlayed : (DateTime?)null
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                DebugLogger.Error("MainWindow", $"获取已安装版本列表异常: {ex.Message}");
                return Array.Empty<PluginVersionInfo>();
            }
        };

        // 获取当前默认账户（不含任何令牌）
        PluginContext.OnGetCurrentAccount = () =>
        {
            try
            {
                var account = AccountService.Instance.GetDefaultAccount();
                if (account == null) return null;
                return new PluginAccountInfo
                {
                    AccountId = account.Id,
                    Username = account.Username,
                    AccountType = account.Type.ToString(),
                    UUID = !string.IsNullOrEmpty(account.MinecraftUUID) ? account.MinecraftUUID : account.UUID,
                    IsDefault = account.IsDefault
                };
            }
            catch (Exception ex)
            {
                DebugLogger.Error("MainWindow", $"获取当前账户异常: {ex.Message}");
                return null;
            }
        };

        // 提交下载请求到启动器下载管理器统一调度
        PluginContext.OnRequestDownload = (pluginId, request) =>
        {
            try
            {
                return TrySubmitPluginDownload(pluginId, request);
            }
            catch (Exception ex)
            {
                DebugLogger.Error("MainWindow", $"提交插件下载请求异常: {ex.Message}");
                return string.Empty;
            }
        };

        // 打开外部链接
        PluginContext.OnOpenUrl = (url) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Error("MainWindow", $"插件打开链接失败: {ex.Message}");
                return false;
            }
        };

        // 跳转到启动器内部页面
        PluginContext.OnNavigateTo = (page) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => NavToPage(page));
        };

        // 查询下载任务状态
        PluginContext.OnGetDownloadTaskStatus = (taskId) => QueryDownloadTaskStatus(taskId);
    }

    /// <summary>按页名导航到对应侧边栏页面（主页卡片等统一调用）</summary>
    public void NavToPage(string page)
    {
        if (string.IsNullOrWhiteSpace(page)) return;

        var pageMapping = new System.Collections.Generic.Dictionary<string, string>
        {
            ["home"] = "主页",
            ["multiplayer"] = "多人联机",
            ["resources"] = "资源下载",
            ["accounts"] = "账号管理",
            ["versions"] = "版本管理",
            ["settings"] = "设置",
            ["more"] = "更多"
        };

        if (!pageMapping.TryGetValue(page.ToLowerInvariant(), out var navTitle)) return;

        var targetNav = NavItems.FirstOrDefault(n => n.Title == navTitle)
            ?? BottomNavItems.FirstOrDefault(n => n.Title == navTitle);
        if (targetNav == null) return;

        if (NavItems.Contains(targetNav))
        {
            SelectedNavItem = targetNav;
        }
        else if (BottomNavItems.Contains(targetNav))
        {
            SelectedBottomNavItem = targetNav;
        }
    }

    private static PluginDownloadTaskStatus? QueryDownloadTaskStatus(string taskId)
    {
        if (string.IsNullOrEmpty(taskId)) return null;

        ObsMCLauncher.Core.Services.Download.DownloadTask? task = null;
        var tasks = ObsMCLauncher.Core.Services.Download.DownloadTaskManager.Instance.Tasks;

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            task = tasks.FirstOrDefault(t => t.Id == taskId);
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
            {
                task = tasks.FirstOrDefault(t => t.Id == taskId);
            });
        }

        if (task == null) return null;
        return new PluginDownloadTaskStatus
        {
            TaskId = task.Id,
            Status = task.Status.ToString(),
            Progress = task.Progress,
            StatusMessage = task.StatusMessage
        };
    }

    private static string NormalizePluginLoaderType(string? loader)
    {
        if (string.IsNullOrWhiteSpace(loader)) return "vanilla";
        var normalized = loader.Trim().ToLowerInvariant();
        return normalized switch
        {
            "forge" or "fabric" or "quilt" or "neoforge" or "optifine" => normalized,
            _ => "vanilla"
        };
    }

    /// <summary>
    /// 提交一个插件下载请求：校验目标目录白名单后，交由下载管理器创建任务并异步下载。
    /// </summary>
    private static string TrySubmitPluginDownload(string pluginId, PluginDownloadRequest request)
    {
        if (request == null) return string.Empty;

        var baseDir = Path.GetFullPath(VersionInfo.GetAppBaseDirectory());
        var pluginDataDir = Path.GetFullPath(Path.Combine(baseDir, "OMCL", "plugins", pluginId));
        var omclDir = Path.GetFullPath(Path.Combine(baseDir, "OMCL"));
        var gameDir = Path.GetFullPath(LauncherConfig.Load().GameDirectory);

        var fullTargetDir = Path.GetFullPath(request.TargetDirectory);
        if (!IsSubPathOf(fullTargetDir, pluginDataDir) &&
            !IsSubPathOf(fullTargetDir, omclDir) &&
            !IsSubPathOf(fullTargetDir, gameDir))
        {
            DebugLogger.Warn("MainWindow", $"插件 {pluginId} 请求的下载目录不在允许范围内: {request.TargetDirectory}");
            return string.Empty;
        }

        var savePath = Path.Combine(fullTargetDir, request.FileName);

        var manager = ObsMCLauncher.Core.Services.Download.DownloadTaskManager.Instance;
        var task = manager.AddTask(
            string.IsNullOrWhiteSpace(request.TaskName) ? request.FileName : request.TaskName,
            ObsMCLauncher.Core.Services.Download.DownloadTaskType.Mod);

        var cts = new CancellationTokenSource();
        task.CancellationTokenSource = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await ObsMCLauncher.Core.Services.Download.HttpDownloadService
                    .DownloadFileToPathAsync(request.Url, savePath, task.Id, cts.Token);

                if (!string.IsNullOrWhiteSpace(request.Sha1))
                {
                    var actual = FileHashVerifier.ComputeSha1(savePath);
                    if (!string.Equals(actual, request.Sha1, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception("文件 SHA-1 校验失败");
                    }
                }

                manager.CompleteTask(task.Id);
                DebugLogger.Info("MainWindow", $"插件 {pluginId} 下载完成: {request.FileName}");
            }
            catch (OperationCanceledException)
            {
                manager.CancelTask(task.Id);
            }
            catch (Exception ex)
            {
                manager.FailTask(task.Id, ex.Message);
                DebugLogger.Error("MainWindow", $"插件 {pluginId} 下载失败: {ex.Message}");
            }
        });

        return task.Id;
    }

    private static bool IsSubPathOf(string candidate, string parent)
    {
        var candidateDir = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var parentDir = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return candidateDir.StartsWith(parentDir, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSelectedNavItemChanged(NavItemViewModel? value)
    {
        if (value != null)
        {
            SelectedBottomNavItem = null;
        }
        
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(SelectedNavEntry));
        
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
        OnPropertyChanged(nameof(SelectedNavEntry));

        // 切换到"更多"页面时刷新更新通道显示
        if (value?.Title == "更多")
        {
            _moreViewModel?.About.RefreshChannelInfo();
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
                DownloadManager?.Dispose();
                foreach (var item in NavItems)
                {
                    if (item.Page is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                foreach (var item in BottomNavItems)
                {
                    if (item.Page is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
            _disposed = true;
        }
    }

    ~MainWindowViewModel()
    {
        Dispose(false);
    }
}
