using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Plugins;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Services.Minecraft;
using ObsMCLauncher.Core.Services.Ui;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.Services;
using ObsMCLauncher.Desktop.ViewModels.Notifications;
using ObsMCLauncher.Desktop.ViewModels.Dialogs;
using ObsMCLauncher.Desktop.Views;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class HomeViewModel : ViewModelBase, IDisposable
{
    private readonly ObsMCLauncher.Core.Services.Ui.IDispatcher _dispatcher;
    private readonly NotificationService _notificationService;
    private readonly DialogService _dialogService;

    public ObservableCollection<ObsMCLauncher.Core.Services.Minecraft.InstalledVersion> InstalledVersions { get; } = new();

    public ObservableCollection<GameAccount> Accounts { get; } = new();

    public ObservableCollection<HomeCardInfo> HomeCards { get; } = new();

    /// <summary>主页运行时布局：行列表，由 LauncherConfig.HomeLayout 驱动；操作区固定渲染，不在行结构中</summary>
    public ObservableCollection<HomeRowViewModel> HomeRows { get; } = new();

    private bool _hasAccounts = true;
    public bool HasAccounts
    {
        get => _hasAccounts;
        private set
        {
            if (SetProperty(ref _hasAccounts, value))
            {
                OnPropertyChanged(nameof(CanLaunch));
            }
        }
    }

    /// <summary>是否存在本地版本（决定版本区显示下拉还是下载引导）</summary>
    public bool HasInstalledVersions => InstalledVersions.Count > 0;

    /// <summary>启动按钮是否可用：有账号 + 已选版本 + 未在启动中</summary>
    public bool CanLaunch => HasAccounts && SelectedInstalledVersion != null && !IsLaunching;

    /// <summary>已持久化的选中账号 Id，用于避免无变化的重复写盘</summary>
    private string _persistedAccountId = "";

    /// <summary>头像渲染结果缓存：按账号 Id 缓存，避免列表刷新时重复下载/解码</summary>
    private readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap> _avatarCache = new();

    /// <summary>正在加载头像的账号集合，同一账号并发只加载一次</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _avatarLoading = new();

    private GameAccount? _selectedAccount;
    public GameAccount? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
            {
                // 已是默认账号且持久化记录一致时无需重复写盘（如启动/刷新时的无变化选中）
                if (value != null && (!value.IsDefault || _persistedAccountId != value.Id))
                {
                    if (!value.IsDefault)
                    {
                        ObsMCLauncher.Core.Services.Accounts.AccountService.Instance.SetDefaultAccount(value.Id);
                    }

                    var config = LauncherConfig.Load();
                    config.SelectedAccountId = value.Id;
                    config.Save();
                    _persistedAccountId = value.Id;

                    if (NavigationStore.MainWindow?.AccountManagement is { } accountVm)
                    {
                        foreach (var w in accountVm.Items)
                        {
                            w.Account.IsDefault = w.Account.Id == value.Id;
                        }
                    }
                }
            }
        }
    }

    private bool _showGameLog;
    public bool ShowGameLog
    {
        get => _showGameLog;
        set
        {
            if (SetProperty(ref _showGameLog, value))
            {
                var config = LauncherConfig.Load();
                config.ShowGameLogOnLaunch = value;
                config.Save();
            }
        }
    }

    private ObsMCLauncher.Core.Services.Minecraft.InstalledVersion? _selectedInstalledVersion;
    public ObsMCLauncher.Core.Services.Minecraft.InstalledVersion? SelectedInstalledVersion
    {
        get => _selectedInstalledVersion;
        set
        {
            if (SetProperty(ref _selectedInstalledVersion, value))
            {
                OnPropertyChanged(nameof(CanLaunch));

                if (value != null)
                {
                    try
                    {
                        ObsMCLauncher.Core.Services.Minecraft.LocalVersionService.SetSelectedVersion(value.Id);

                        // 显式同步到 LauncherConfig 并保存，确保全局一致
                        var config = LauncherConfig.Load();
                        config.SelectedVersion = value.Id;
                        config.Save();

                        SelectedVersionId = value.Id;
                        OpenVersionDetailCommand.NotifyCanExecuteChanged();
                        LaunchCommand.NotifyCanExecuteChanged(); // 刷新启动按钮可用状态
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Error("Home", $"选择版本失败: {ex.Message}");
                    }
                }
            }
        }
    }

    private string? _selectedVersionId;
    public string? SelectedVersionId
    {
        get => _selectedVersionId;
        set
        {
            if (SetProperty(ref _selectedVersionId, value))
            {
                OpenVersionDetailCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _isLaunching;
    public bool IsLaunching
    {
        get => _isLaunching;
        set
        {
            if (SetProperty(ref _isLaunching, value))
            {
                LaunchCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanLaunch));
            }
        }
    }

    // ===== 启动遮罩 =====

    /// <summary>遮罩当前展示的状态</summary>
    public enum LaunchOverlayState
    {
        Running,
        Success,
        Failed,
        Canceled
    }

    private bool _isLaunchOverlayVisible;
    public bool IsLaunchOverlayVisible
    {
        get => _isLaunchOverlayVisible;
        private set => SetProperty(ref _isLaunchOverlayVisible, value);
    }

    private LaunchOverlayState _launchState = LaunchOverlayState.Running;
    public LaunchOverlayState LaunchState
    {
        get => _launchState;
        private set
        {
            if (SetProperty(ref _launchState, value))
            {
                OnPropertyChanged(nameof(IsLaunchFailed));
                OnPropertyChanged(nameof(IsLaunchRunning));
            }
        }
    }

    public bool IsLaunchFailed => _launchState == LaunchOverlayState.Failed;

    public bool IsLaunchRunning => _launchState == LaunchOverlayState.Running;

    private string _launchStatusText = "";
    public string LaunchStatusText
    {
        get => _launchStatusText;
        private set => SetProperty(ref _launchStatusText, value);
    }

    private string _launchErrorMessage = "";
    public string LaunchErrorMessage
    {
        get => _launchErrorMessage;
        private set => SetProperty(ref _launchErrorMessage, value);
    }

    private double _launchPercent;
    public double LaunchPercent
    {
        get => _launchPercent;
        private set
        {
            if (SetProperty(ref _launchPercent, value))
            {
                OnPropertyChanged(nameof(LaunchPercentText));
            }
        }
    }

    public string LaunchPercentText => $"{Math.Round(_launchPercent)}%";

    /// <summary>遮罩里滚动的最近几行</summary>
    public ObservableCollection<string> LaunchLogLines { get; } = new();

    private string _echoCaveText = "";
    public string EchoCaveText
    {
        get => _echoCaveText;
        private set => SetProperty(ref _echoCaveText, value);
    }

    public IRelayCommand CancelLaunchCommand { get; }
    public IRelayCommand RetryLaunchCommand { get; }
    public IRelayCommand DismissLaunchOverlayCommand { get; }
    public IRelayCommand NextEchoCaveCommand { get; }

    private CancellationTokenSource? _launchCts;
    private LaunchProgressTracker _progressTracker = new();
    private bool _launchCancelRequested;
    private int _launchSession;

    /// <summary>游戏输出先进队列，界面线程按帧取，免得日志洪峰把 UI 卡住</summary>
    private readonly ConcurrentQueue<string> _launchLogQueue = new();
    private readonly DispatcherTimer _overlayTimer;
    private readonly DispatcherTimer _echoTimer;
    private const int LaunchLogMaxLines = 3;
    private const int LaunchLogQueueLimit = 300;
    private const int LaunchLogMaxLength = 180;

    public IRelayCommand OpenVersionDetailCommand { get; }
    public IAsyncRelayCommand LaunchCommand { get; }

    public InstanceViewModel InstanceViewModel { get; }

    // 默认卡片图标（SVG path data），避免依赖系统 emoji 字形导致跨平台渲染为方块
    internal const string IconRocket = "M12 2C12 2 6 6 6 12C6 15.31 7.79 18.17 10.5 19.71L12 23L13.5 19.71C16.21 18.17 18 15.31 18 12C18 6 12 2 12 2M12 10C10.9 10 10 9.1 10 8C10 6.9 10.9 6 12 6C13.1 6 14 6.9 14 8C14 9.1 13.1 10 12 10M12 20C12 20 8 17.86 8 12C8 10.5 8.5 9.24 9.3 8.17C9.86 8.69 10.42 9.12 11.16 9.44C12.62 10.08 14.55 10.37 15.5 10.05C15.76 11.5 15.37 12.6 15 13.43C14.5 14.53 12 20 12 20Z";
    internal const string IconNews = "M20 2H4C2.9 2 2 2.9 2 4V22L6 18H20C21.1 18 22 17.1 22 16V4C22 2.9 21.1 2 20 2M20 16H5.17L4 17.17V4H20V16M7 9H17V7H7V9M7 13H14V11H7V13Z";
    internal const string IconGlobe = "M12 2C6.48 2 2 6.48 2 12S6.48 22 12 22 22 17.52 22 12 17.52 2 12 2M12 20C11.1 20 10.21 19.88 9.36 19.67L10 18L12 16L13.34 13.09L14.35 12H17.5C18.2 12 18.85 12.26 19.35 12.67C18.37 16.8 15.48 20 12 20M7 9L5.77 11.13L5.25 11.77C5.08 11.23 5 10.65 5 10C5 8.94 5.26 7.94 5.71 7.06L7 9M19 10.25C18.03 9.21 16.57 8.5 15 8.5H12.86L10 9.63V12.38L12.41 14.79L13.07 13.25L15 12.5L17 14.5V17.13C14.24 18.37 11 18.37 8.24 17.13L6.83 16.71L6.4 16.29L5.03 16.72C5.16 17.5 5.41 18.25 5.75 18.94C7.21 20.91 9.43 22.02 12 22C16.42 22 20 18.42 20 14C20 12.72 19.65 11.52 19.03 10.5L19 10.25Z";
    internal const string IconDownload = "M19 9H15V3H9V9H5L12 16L19 9M5 18V20H19V18H5Z";

    public HomeViewModel(ObsMCLauncher.Core.Services.Ui.IDispatcher dispatcher, NotificationService notificationService)
    {
        _dispatcher = dispatcher;
        _notificationService = notificationService;
        _dialogService = new DialogService();

        // 账号管理页新增/删除账号后刷新主页账号列表（事件解耦，不再通过导航项查找）
        AccountEvents.AccountsChanged += OnAccountsChanged;

        InstanceViewModel = new InstanceViewModel(notificationService);

        LaunchCommand = new AsyncRelayCommand(LaunchAsync, () => CanLaunch);

        OpenVersionDetailCommand = new RelayCommand(OpenVersionDetail, CanOpenVersionDetail);

        CancelLaunchCommand = new RelayCommand(CancelLaunch);
        RetryLaunchCommand = new RelayCommand(RetryLaunch);
        DismissLaunchOverlayCommand = new RelayCommand(DismissLaunchOverlay);
        NextEchoCaveCommand = new RelayCommand(() => EchoCaveText = EchoCave.Next());

        _overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _overlayTimer.Tick += OnOverlayTick;

        // 回声洞轮播：遮罩挂着的时候每 7 秒换一句，不跟着进度条走
        _echoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _echoTimer.Tick += (_, _) =>
        {
            if (IsLaunchOverlayVisible) EchoCaveText = EchoCave.Next();
        };

        var config = LauncherConfig.Load();
        SelectedVersionId = config.SelectedVersion;
        _showGameLog = config.ShowGameLogOnLaunch;
        _persistedAccountId = config.SelectedAccountId ?? "";

        InitializeHomeData();

        _ = LoadLocalAsync();
    }

    public void Dispose()
    {
        AccountEvents.AccountsChanged -= OnAccountsChanged;
        _overlayTimer.Stop();
        _echoTimer.Stop();
        GC.SuppressFinalize(this);
    }

    private void OnAccountsChanged()
    {
        RefreshAccounts();
    }

    private void InitializeHomeData()
    {
        HomeCards.Clear();

        var config = LauncherConfig.Load();
        var cardConfigs = config.HomeCards ?? new();

        var defaultCards = new List<HomeCardInfo>
        {
            new HomeCardInfo { CardId = HomeCardInfo.WelcomeCardId, Title = "欢迎使用黑曜石启动器", Description = "开始你的 Minecraft 之旅", Icon = IconRocket, Order = 0 },
            new HomeCardInfo { CardId = "news", Title = "查看最新的 Minecraft 新闻", Description = "了解游戏动态", Icon = IconNews, CommandId = "url:https://zh.minecraft.wiki/", Order = 1 },
            new HomeCardInfo { CardId = "multiplayer", Title = "多人联机", Description = "加入服务器与好友一起游戏", Icon = IconGlobe, CommandId = "navigate:multiplayer", Order = 2 },
            new HomeCardInfo { CardId = "mods", Title = "资源下载", Description = "下载Mod、材质包等资源", Icon = IconDownload, CommandId = "navigate:resources", Order = 3 }
        };

        foreach (var card in defaultCards)
        {
            var cardConfig = cardConfigs.FirstOrDefault(c => c.CardId == card.CardId);
            card.IsEnabled = cardConfig?.IsEnabled ?? true;
            card.Order = cardConfig?.Order ?? defaultCards.IndexOf(card);
        }

        foreach (var card in defaultCards.OrderBy(c => c.Order))
        {
            HomeCards.Add(card);
        }

        BuildHomeRows();

        LoadAccounts();

        // 注意：PluginContext.OnHomeCardRegistered 和 OnHomeCardUnregistered
        // 现在在 MainWindowViewModel 中设置，并通过事件分发到各个ViewModel
    }

    /// <summary>从持久化布局重建运行时行结构；没有数据的组件（如尚未注册的插件卡片）跳过渲染</summary>
    private void BuildHomeRows()
    {
        HomeRows.Clear();

        var layout = LauncherConfig.Load().GetHomeLayout();
        DebugLogger.Info("Home", $"BuildHomeRows: layout has {layout.Rows.Count} rows, {layout.Rows.Sum(r => r.Components.Count)} components total");

        foreach (var row in layout.Rows)
        {
            var rowVm = new HomeRowViewModel();
            foreach (var comp in row.Components)
            {
                var vm = CreateComponentVM(comp.Id, comp.Size);
                if (vm != null)
                {
                    rowVm.Components.Add(vm);
                }
                else
                {
                    DebugLogger.Warn("Home", $"BuildHomeRows: skipped component '{comp.Id}' (CreateComponentVM returned null)");
                }
            }
            HomeRows.Add(rowVm);
        }

        DebugLogger.Info("Home", $"BuildHomeRows: {HomeRows.Count} rows built, {HomeRows.Sum(r => r.Components.Count)} components, HomeCards has {HomeCards.Count} items");
    }

    /// <summary>按组件 ID 创建运行时组件视图模型；无法提供渲染数据的返回 null</summary>
    private HomeComponentViewModel? CreateComponentVM(string id, HomeCardSize size)
    {
        var vm = CreateDataComponentVM(id);
        if (vm == null) return null;

        vm.Id = id;
        vm.Owner = this;
        vm.Size = size;
        return vm;
    }

    /// <summary>数据驱动组件：欢迎横幅或通用卡片。只要卡片存在即可放置
    /// （是否放入主页布局由 HomeLayout 决定，禁用卡片仍可从组件库手动添加）</summary>
    private HomeComponentViewModel? CreateDataComponentVM(string id)
    {
        var card = HomeCards.FirstOrDefault(c => c.CardId == id);
        if (id == HomeComponentRegistry.WelcomeId)
        {
            return card != null ? new WelcomeComponentViewModel { Card = card } : null;
        }
        return card != null ? new CardComponentViewModel { Card = card } : null;
    }

    /// <summary>从运行时布局与持久化布局中移除组件</summary>
    private void RemoveComponentFromRows(string id)
    {
        foreach (var row in HomeRows)
        {
            var vm = row.Components.FirstOrDefault(c => c.Id == id);
            if (vm != null)
            {
                row.Components.Remove(vm);
            }
        }

        var config = LauncherConfig.Load();
        if (config.GetHomeLayout().Remove(id))
        {
            config.Save();
        }
    }

    /// <summary>把当前运行时行结构完整写回配置并保存（编辑器每次改动后调用）</summary>
    public void PersistHomeLayout()
    {
        var config = LauncherConfig.Load();
        config.HomeLayout = new HomeLayoutConfig
        {
            Rows = HomeRows.Select(r => new HomeRowConfig
            {
                Components = r.Components.Select(c => new HomeComponentConfig
                {
                    Id = c.Id,
                    Size = c.Size
                }).ToList()
            }).ToList()
        };

        // 同步旧版卡片配置（启用状态/顺序），保持与卡片开关的语义一致
        config.HomeCards = HomeCards.Select((c, i) => new HomeCardConfig
        {
            CardId = c.CardId,
            IsEnabled = c.IsEnabled,
            Order = i,
            IsPluginCard = c.IsPluginCard,
            PluginId = c.PluginId
        }).ToList();

        config.Save();
    }

    /// <summary>添加组件到指定行的指定位置（index 超出范围时追加到行尾）；行容量已满或档位互斥时返回 null，同一组件可重复添加</summary>
    public HomeComponentViewModel? AddComponentToRow(string componentId, HomeRowViewModel row, int index)
    {
        var descriptor = HomeComponentRegistry.TryGet(componentId);
        var size = descriptor?.DefaultSize ?? HomeCardSize.Medium;
        if (!row.CanAccept(size))
        {
            DebugLogger.Warn("Home", $"row cannot accept component '{componentId}' (size={size}), row is full");
            return null;
        }

        var vm = CreateComponentVM(componentId, size);
        if (vm == null) return null;

        if (index < 0 || index > row.Components.Count)
        {
            index = row.Components.Count;
        }
        row.Components.Insert(index, vm);

        PersistHomeLayout();
        return vm;
    }

    /// <summary>移除组件（布局即唯一真相，组件库可随时重新添加）</summary>
    public void RemoveComponent(HomeComponentViewModel component)
    {
        var row = HomeRows.FirstOrDefault(r => r.Components.Contains(component));
        row?.Components.Remove(component);

        PersistHomeLayout();
    }

    /// <summary>移动组件到目标行的目标位置（编辑器拖拽用）；目标行放不下时不移动</summary>
    public void MoveComponent(HomeComponentViewModel component, HomeRowViewModel targetRow, int targetIndex)
    {
        var sourceRow = HomeRows.FirstOrDefault(r => r.Components.Contains(component));
        if (sourceRow == null) return;

        // 目标行容量校验：源行即目标行时排除自身
        if (!targetRow.CanAccept(component.Size, component))
        {
            DebugLogger.Warn("Home", "move rejected: target row is full");
            return;
        }

        // 同行移动需要先移除再按剩余集合计算插入位置
        sourceRow.Components.Remove(component);
        if (ReferenceEquals(sourceRow, targetRow) && targetIndex > sourceRow.Components.Count)
        {
            targetIndex = sourceRow.Components.Count;
        }
        if (targetIndex < 0 || targetIndex > targetRow.Components.Count)
        {
            targetIndex = targetRow.Components.Count;
        }
        targetRow.Components.Insert(targetIndex, component);

        PersistHomeLayout();
    }

    /// <summary>在指定位置插入空行（index 超出范围时追加到末尾）</summary>
    public HomeRowViewModel InsertRow(int index)
    {
        var row = new HomeRowViewModel();
        if (index < 0 || index > HomeRows.Count)
        {
            index = HomeRows.Count;
        }
        HomeRows.Insert(index, row);
        PersistHomeLayout();
        return row;
    }

    /// <summary>删除行（至少保留一行）</summary>
    public bool RemoveRow(HomeRowViewModel row)
    {
        if (HomeRows.Count <= 1) return false;
        if (!HomeRows.Remove(row)) return false;
        PersistHomeLayout();
        return true;
    }

    /// <summary>调整组件尺寸档位。改为整行（Fill）时若行里有其他组件，
    /// 自动把该组件挪到新独占行；放大到更大档位而当前行放不下时，也会把组件挪到新行，
    /// 保证宽度调整生效并即时刷新；缩小档位永远可行，直接应用。</summary>
    public void SetComponentSize(HomeComponentViewModel component, HomeCardSize size)
    {
        if (component.Size == size) return;
        var row = HomeRows.FirstOrDefault(r => r.Components.Contains(component));
        if (row == null)
        {
            component.Size = size;
            PersistHomeLayout();
            return;
        }

        // 整行：行里有其他组件时把该组件拆到新行，占满整行才成立
        if (size == HomeCardSize.Fill && row.Components.Count > 1)
        {
            row.Components.Remove(component);
            var idx = HomeRows.IndexOf(row);
            var newRow = InsertRow(idx + 1);
            newRow.Components.Add(component);
            component.Size = size;
            PersistHomeLayout();
            return;
        }

        // 放大到更大档位而当前行放不下时，把组件移到新行，保证宽度调整生效并即时刷新；
        // 缩小档位永远可行，直接应用即可。
        if (!row.CanAccept(size, component))
        {
            if ((int)size > (int)component.Size)
            {
                row.Components.Remove(component);
                var idx = HomeRows.IndexOf(row);
                var newRow = InsertRow(idx + 1);
                newRow.Components.Add(component);
            }
            else
            {
                // 缩小档位理论上不会放不下，仅作防护
                DebugLogger.Warn("Home", $"size change rejected: row cannot fit size={size}");
                return;
            }
        }

        component.Size = size;
        PersistHomeLayout();
    }

    /// <summary>恢复默认布局（保留插件卡片数据，仅重置摆放）</summary>
    public void ResetHomeLayout()
    {
        var config = LauncherConfig.Load();
        config.HomeLayout = HomeLayoutConfig.CreateDefault(config.HomeCards);
        config.Save();
        BuildHomeRows();
    }

    /// <summary>强制从配置重建行（设置页编辑器兜底用）</summary>
    public void ForceRebuildRows()
    {
        BuildHomeRows();
    }

    public void OnPluginCardRegistered(string cardId, string title, string description, string? icon, string? commandId, object? payload)
    {
        _dispatcher.InvokeAsync(() =>
        {
            // 检查卡片是否在配置中被禁用
            var config = LauncherConfig.Load();
            var cardConfig = config.HomeCards.FirstOrDefault(c => c.CardId == cardId);
            var isEnabled = cardConfig?.IsEnabled ?? true;

            var existing = HomeCards.FirstOrDefault(c => c.CardId == cardId);
            if (existing != null)
            {
                existing.Title = title;
                existing.Description = description;
                existing.Icon = icon;
                existing.CommandId = commandId;
                existing.Payload = payload;
                existing.IsEnabled = isEnabled;
            }
            else
            {
                var newCard = new HomeCardInfo
                {
                    CardId = cardId,
                    Title = title,
                    Description = description,
                    Icon = icon,
                    CommandId = commandId,
                    Payload = payload,
                    IsPluginCard = true,
                    PluginId = cardId.Split('.')[0],
                    IsEnabled = isEnabled
                };

                // 无论卡片是否被启用，都添加到集合中，只是在显示时根据 IsEnabled 属性决定是否显示
                HomeCards.Add(newCard);
            }

            // 通知SettingsViewModel刷新插件卡片
            NotifySettingsViewModelRefreshPluginCards();

            // 插件注册的卡片不自动放入主页布局，由用户在组件库中手动添加
        });
    }

    private void NotifySettingsViewModelRefreshPluginCards()
    {
        // 直接引用主窗口持有的设置 ViewModel，避免按导航标题查找
        NavigationStore.MainWindow?.Settings?.SettingsHome.RefreshLibrary();
    }

    public void OnPluginCardUnregistered(string cardId)
    {
        _dispatcher.InvokeAsync(() =>
        {
            var card = HomeCards.FirstOrDefault(c => c.CardId == cardId);
            if (card != null && card.IsPluginCard)
            {
                HomeCards.Remove(card);
            }

            RemoveComponentFromRows(cardId);
        });
    }

    public void RemoveAllPluginCards(string pluginId)
    {
        _dispatcher.InvokeAsync(() =>
        {
            var cardsToRemove = HomeCards.Where(c => c.IsPluginCard && c.PluginId == pluginId).ToList();
            foreach (var card in cardsToRemove)
            {
                HomeCards.Remove(card);
            }

            // 同步移除运行时布局与持久化布局中的组件
            var prefix = pluginId + ".";
            foreach (var row in HomeRows)
            {
                foreach (var vm in row.Components.Where(c => c.Id.StartsWith(prefix)).ToList())
                {
                    row.Components.Remove(vm);
                }
            }

            var config = LauncherConfig.Load();
            var configToRemove = config.HomeCards.Where(c => c.IsPluginCard && c.PluginId == pluginId).ToList();
            foreach (var cfg in configToRemove)
            {
                config.HomeCards.Remove(cfg);
            }

            var layout = config.GetHomeLayout();
            var layoutChanged = false;
            foreach (var row in layout.Rows)
            {
                if (row.Components.RemoveAll(c => c.Id.StartsWith(prefix)) > 0)
                {
                    layoutChanged = true;
                }
            }
            if (layoutChanged)
            {
                layout.RemoveEmptyRows();
            }
            config.Save();

            DebugLogger.Info("Home", $"已移除插件 {pluginId} 的所有卡片，共 {cardsToRemove.Count} 个");
        });
    }

    [RelayCommand]
    private void CardClick(HomeCardInfo? card)
    {
        if (card == null || string.IsNullOrEmpty(card.CommandId)) return;

        if (card.CommandId.StartsWith("navigate:"))
        {
            NavigateToNavPage(card.CommandId.Substring(9));
        }
        else if (card.CommandId.StartsWith("url:"))
        {
            var url = card.CommandId.Substring(4);
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
        else if (card.CommandId.StartsWith("command:"))
        {
            var commandId = card.CommandId.Substring(8);
            PluginContext.ExecuteCommand(commandId, card.Payload);
        }
    }

    /// <summary>主页引导按钮跳转（如「添加账号」「去下载版本」）</summary>
    [RelayCommand]
    private void GoTo(string page)
    {
        if (string.IsNullOrWhiteSpace(page)) return;
        NavigateToNavPage(page);
    }

    /// <summary>按页名导航到对应侧边栏页面（统一走 MainWindow 的导航逻辑，避免映射重复）</summary>
    private void NavigateToNavPage(string page)
    {
        if (string.IsNullOrWhiteSpace(page)) return;
        NavigationStore.MainWindow?.NavToPage(page);
    }

    private void LoadAccounts()
    {
        var accounts = ObsMCLauncher.Core.Services.Accounts.AccountService.Instance.GetAllAccounts();
        var newIds = new HashSet<string>(accounts.Select(a => a.Id));

        // 清理已删除账号的头像缓存，释放对应位图
        foreach (var id in _avatarCache.Keys.ToList())
        {
            if (!newIds.Contains(id) && _avatarCache[id] is IDisposable d)
            {
                d.Dispose();
                _avatarCache.Remove(id);
            }
        }

        Accounts.Clear();
        foreach (var acc in accounts)
        {
            Accounts.Add(acc);
        }

        HasAccounts = Accounts.Count > 0;
        SelectLastAccount();
        LoadAccountAvatars();
    }

    public void RefreshAccounts()
    {
        LoadAccounts();
    }

    public void RefreshHomeCards()
    {
        // 保存当前的插件卡片
        var pluginCards = HomeCards.Where(c => c.IsPluginCard).ToList();

        // 重新初始化主页数据
        InitializeHomeData();

        // 重新添加插件卡片
        foreach (var pluginCard in pluginCards)
        {
            // 检查卡片是否已经存在
            var existingCard = HomeCards.FirstOrDefault(c => c.CardId == pluginCard.CardId);
            if (existingCard == null)
            {
                HomeCards.Add(pluginCard);
            }
            else
            {
                // 更新现有卡片的状态
                existingCard.IsEnabled = pluginCard.IsEnabled;
            }
        }

        // 卡片数据重建后同步运行时布局
        BuildHomeRows();

        // 按配置中的启用状态刷新插件卡片显示
        _dispatcher.InvokeAsync(() =>
        {
            var config = LauncherConfig.Load();
            var cardConfigs = config.HomeCards.Where(c => c.IsPluginCard).ToList();
            foreach (var cardConfig in cardConfigs)
            {
                var existingCard = HomeCards.FirstOrDefault(c => c.CardId == cardConfig.CardId);
                if (existingCard != null)
                {
                    existingCard.IsEnabled = cardConfig.IsEnabled;
                }
            }
            BuildHomeRows();
        });
    }

    private void SelectLastAccount()
    {
        if (!string.IsNullOrEmpty(_persistedAccountId))
        {
            SelectedAccount = Accounts.FirstOrDefault(a => a.Id == _persistedAccountId);
        }
        
        if (SelectedAccount == null)
        {
            SelectedAccount = Accounts.FirstOrDefault(a => a.IsDefault) ?? Accounts.FirstOrDefault();
        }
    }

    private void LoadAccountAvatars()
    {
        foreach (var acc in Accounts)
        {
            if (_avatarCache.TryGetValue(acc.Id, out var cached))
            {
                SetAvatar(acc, cached);
                continue;
            }

            // 同一账号只发起一次加载，避免列表刷新时重复下载/解码
            if (!_avatarLoading.TryAdd(acc.Id, 0)) continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    var head = await AccountAvatarService.LoadHeadAsync(acc);
                    if (head != null)
                    {
                        await _dispatcher.InvokeAsync(() =>
                        {
                            _avatarCache[acc.Id] = head;
                            SetAvatar(acc, head);
                        });
                        return;
                    }

                    // 没有皮肤或加载失败时使用「纯色背景 + 首字母」的默认头像
                    // （生成走 RenderTargetBitmap，必须在 UI 线程）
                    await _dispatcher.InvokeAsync(() =>
                    {
                        var defaultAvatar = AccountAvatarService.CreateDefaultAvatar(acc);
                        if (defaultAvatar != null)
                        {
                            _avatarCache[acc.Id] = defaultAvatar;
                            SetAvatar(acc, defaultAvatar);
                        }
                    });
                }
                catch { }
                finally
                {
                    _avatarLoading.TryRemove(acc.Id, out _);
                }
            });
        }
    }

    private void SetAvatar(GameAccount acc, object? newAvatar)
    {
        var old = acc.Avatar;
        if (!ReferenceEquals(old, newAvatar))
        {
            if (old is IDisposable oldDisposable)
            {
                oldDisposable.Dispose();
            }
            acc.Avatar = newAvatar;
        }
    }

    private bool CanOpenVersionDetail() => SelectedInstalledVersion != null;

    private void OpenVersionDetail()
    {
        if (SelectedInstalledVersion == null) return;
        InstanceViewModel.SetVersion(SelectedInstalledVersion);
    }

    public async Task LoadLocalAsync()
    {
        try
        {
            var config = LauncherConfig.Load();
            var gameDir = config.GameDirectory;
            var list = ObsMCLauncher.Core.Services.Minecraft.LocalVersionService.GetInstalledVersions(gameDir);

            await _dispatcher.InvokeAsync(() =>
            {
                InstalledVersions.Clear();
                foreach (var v in list) InstalledVersions.Add(v);
                OnPropertyChanged(nameof(HasInstalledVersions));

                var selectedId = ObsMCLauncher.Core.Services.Minecraft.LocalVersionService.GetSelectedVersion();
                SelectedVersionId = selectedId;
                SelectedInstalledVersion = InstalledVersions.FirstOrDefault(x => x.Id == selectedId);
            });
        }
        catch (Exception ex)
        {
            DebugLogger.Error("Home", $"本地版本扫描失败: {ex.Message}");
        }
    }

    private async Task LaunchAsync()
    {
        if (SelectedInstalledVersion == null || SelectedAccount == null)
        {
            _notificationService.Show("无法启动", "请先选择游戏版本和账号", NotificationType.Warning);
            return;
        }

        var launchCts = new CancellationTokenSource();
        _launchCts = launchCts;
        var session = ++_launchSession;
        var versionId = SelectedInstalledVersion.Id;
        var account = SelectedAccount;
        var tracker = new LaunchProgressTracker();
        _progressTracker = tracker;
        _launchCancelRequested = false;

        ResetLaunchOverlay();
        EchoCaveText = EchoCave.Next();
        IsLaunchOverlayVisible = true;
        _overlayTimer.Start();
        _echoTimer.Start();

        try
        {
            IsLaunching = true;
            var config = LauncherConfig.Load();

            // 1. 检查完整性。进度现在由遮罩展示，不再开通知卡片
            var integrity = await ObsMCLauncher.Core.Services.GameLauncher.CheckGameIntegrityAsync(
                versionId,
                config,
                (msg) => ReportLaunchProgress(tracker, msg),
                launchCts.Token);

            if (integrity.HasIssue && integrity.MissingLibraries.Count > 0)
            {
                var missingCount = integrity.MissingLibraries.Count;
                ReportLaunchProgress(tracker, $"正在补全 {missingCount} 个缺失依赖...",
                    LaunchProgressTracker.StageMissingDependencies, 0);

                try
                {
                    var (successCount, failedCount) = await ObsMCLauncher.Core.Services.LibraryDownloader.DownloadMissingLibrariesAsync(
                        config.GameDirectory,
                        versionId,
                        integrity.MissingLibraries,
                        (progress, current, total) => ReportLaunchProgress(tracker, progress,
                            LaunchProgressTracker.StageMissingDependencies,
                            current * 100.0 / Math.Max(1, total)),
                        launchCts.Token);

                    if (failedCount > 0)
                    {
                        SetLaunchFailure($"{failedCount} 个必需库文件下载失败，请检查网络后重试");
                        return;
                    }

                    ReportLaunchProgress(tracker, $"已成功补全 {successCount} 个依赖",
                        LaunchProgressTracker.StageMissingDependencies, 100);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception dlEx)
                {
                    SetLaunchFailure($"依赖补全失败：{dlEx.Message}");
                    return;
                }
            }

            // 1.5 模组冲突检测
            ReportLaunchProgress(tracker, "正在检测模组冲突...", LaunchProgressTracker.StageConflictCheck);
            var modsDir = Path.Combine(config.GetRunDirectory(versionId), "mods");
            var conflicts = ObsMCLauncher.Core.Services.ModConflictDetector.DetectConflicts(modsDir);
            var errors = conflicts.Where(c => c.Severity == ObsMCLauncher.Core.Services.ConflictSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                var conflictMsg = string.Join("\n", errors.Select(c => c.Description));
                var result = await _dialogService.ShowQuestion(
                    "检测到模组冲突",
                    $"发现 {errors.Count} 个严重冲突，可能导致游戏崩溃：\n\n{conflictMsg}\n\n是否仍要启动游戏？");
                if (result != DialogResult.Yes)
                {
                    LaunchState = LaunchOverlayState.Canceled;
                    LaunchStatusText = "启动已取消";
                    _ = HideLaunchOverlayAfterAsync(900, session);
                    return;
                }
            }

            // 2. 准备日志窗口（独立窗口照常保留，遮罩日志只是它的缩略）
            GameLogWindow? logWindow = null;
            if (config.ShowGameLogOnLaunch)
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    logWindow = new GameLogWindow(versionId);
                    logWindow.Show();
                });
            }

            // 记录启动时刻：崩溃检测要靠它排除历史崩溃报告
            var launchStartedAt = DateTime.Now;

            var launchResult = await ObsMCLauncher.Core.Services.GameLauncher.LaunchGameAsync(
                versionId,
                account,
                config,
                (progress) => ReportLaunchProgress(tracker, progress),
                (output) =>
                {
                    logWindow?.AppendGameOutput(output);
                    AppendLaunchLog(output);
                },
                (exitCode) =>
                {
                    logWindow?.OnGameExit(exitCode);
                    CrashAnalysisPromptService.NotifyGameExit(versionId, exitCode, launchStartedAt);
                    _dispatcher.Post(() =>
                        _notificationService.Show(
                            "游戏退出",
                            $"游戏已退出，退出代码: {exitCode}",
                            exitCode == 0 ? NotificationType.Info : NotificationType.Warning));
                },
                (stepProgress) => tracker.ReportStep(stepProgress),
                launchCts.Token);

            if (_launchCancelRequested)
            {
                LaunchState = LaunchOverlayState.Canceled;
                LaunchStatusText = "已取消启动";
                _ = HideLaunchOverlayAfterAsync(900, session);
                return;
            }

            if (launchResult.Success)
            {
                LaunchState = LaunchOverlayState.Success;
                LaunchStatusText = "启动成功";
                tracker.Complete();
                LaunchPercent = 100;
                _ = HideLaunchOverlayAfterAsync(1200, session);

                if (config.CloseAfterLaunch)
                {
                    await _dispatcher.InvokeAsync(() =>
                    {
                        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            desktop.MainWindow?.Close();
                        }
                    });
                }
            }
            else
            {
                SetLaunchFailure(string.IsNullOrEmpty(launchResult.ErrorMessage)
                    ? "请检查日志或 Java 配置"
                    : launchResult.ErrorMessage);
            }
        }
        catch (OperationCanceledException)
        {
            LaunchState = LaunchOverlayState.Canceled;
            LaunchStatusText = "已取消启动";
            _ = HideLaunchOverlayAfterAsync(900, session);
        }
        catch (Exception ex)
        {
            SetLaunchFailure(ex.Message);
        }
        finally
        {
            IsLaunching = false;
            launchCts.Dispose();
            if (_launchCts == launchCts) _launchCts = null;
        }
    }

    private void ResetLaunchOverlay()
    {
        LaunchState = LaunchOverlayState.Running;
        LaunchStatusText = "正在准备启动...";
        LaunchErrorMessage = "";
        LaunchPercent = 0;
        LaunchLogLines.Clear();
        while (_launchLogQueue.TryDequeue(out _)) { }
        _progressTracker.Reset();
    }

    private void SetLaunchFailure(string message)
    {
        LaunchState = LaunchOverlayState.Failed;
        LaunchStatusText = "启动失败";
        LaunchErrorMessage = message;
    }

    /// <summary>
    /// 进度回调的公共入口。回调大多来自后台线程，所以算进度和改状态都挪到界面线程。
    /// </summary>
    private void ReportLaunchProgress(LaunchProgressTracker tracker, string? message, int? stage = null, double? percent = null)
    {
        var clean = LaunchProgressTracker.CleanMessage(message);
        if (clean.Length == 0) return;

        // 资源下载回调把百分比塞在 "xxx (45%)|45" 里，这里解出来当真实进度用
        if (percent == null && message != null)
        {
            var barIndex = message.LastIndexOf('|');
            if (barIndex >= 0 && double.TryParse(message.Substring(barIndex + 1), out var parsed))
            {
                percent = parsed;
                if (stage == null) stage = LaunchProgressTracker.StageAssets;
            }
        }

        _dispatcher.Post(() =>
        {
            // 换了一轮启动之后，上一轮迟到的回调直接丢掉
            if (!IsLaunchOverlayVisible || tracker != _progressTracker) return;

            if (stage.HasValue)
            {
                tracker.ReportPhase(stage.Value, clean, percent);
            }
            else
            {
                tracker.Report(clean);
            }

            LaunchStatusText = clean;
            AppendLaunchLog(clean);
        });
    }

    /// <summary>游戏输出可能一秒上百行，这里只入队，界面线程定时搬</summary>
    private void AppendLaunchLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        var text = line.Trim();
        if (text.Length > LaunchLogMaxLength) text = text.Substring(0, LaunchLogMaxLength) + " ...";

        while (_launchLogQueue.Count >= LaunchLogQueueLimit && _launchLogQueue.TryDequeue(out _)) { }
        _launchLogQueue.Enqueue(text);
    }

    private void OnOverlayTick(object? sender, EventArgs e)
    {
        if (!IsLaunchOverlayVisible)
        {
            _overlayTimer.Stop();
            return;
        }

        // 一次把队列搬空，只保留最后三行
        var pending = new List<string>(LaunchLogMaxLines);
        var drained = 0;
        while (drained < LaunchLogQueueLimit && _launchLogQueue.TryDequeue(out var line))
        {
            drained++;
            pending.Add(line);
            if (pending.Count > LaunchLogMaxLines) pending.RemoveAt(0);
        }

        if (drained > 0)
        {
            foreach (var line in pending) LaunchLogLines.Add(line);
            while (LaunchLogLines.Count > LaunchLogMaxLines) LaunchLogLines.RemoveAt(0);
        }

        if (_launchState != LaunchOverlayState.Running) return;

        var target = _progressTracker.TargetPercent;
        if (target <= LaunchPercent) return;

        // 越靠近目标走得越慢，看起来像在磨进度，而不是匀速计时
        var step = (target - LaunchPercent) * 0.12;
        if (step < 0.2) step = 0.2;
        if (step > 4) step = 4;

        LaunchPercent = Math.Min(target, LaunchPercent + step);
    }

    private void CancelLaunch()
    {
        if (!IsLaunching) return;
        _launchCancelRequested = true;
        LaunchStatusText = "正在取消启动...";
        try
        {
            _launchCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void RetryLaunch()
    {
        HideLaunchOverlay();
        if (CanLaunch) LaunchCommand.Execute(null);
    }

    private void DismissLaunchOverlay()
    {
        if (IsLaunching) return;
        HideLaunchOverlay();
    }

    private void HideLaunchOverlay()
    {
        _overlayTimer.Stop();
        _echoTimer.Stop();
        IsLaunchOverlayVisible = false;
    }

    private async Task HideLaunchOverlayAfterAsync(int delayMilliseconds, int session)
    {
        await Task.Delay(delayMilliseconds);
        if (session != _launchSession) return;
        await _dispatcher.InvokeAsync(HideLaunchOverlay);
    }
}
