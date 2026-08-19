using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Plugins;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Services.Minecraft;
using ObsMCLauncher.Core.Services.Ui;
using ObsMCLauncher.Core.Utils;
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

    /// <summary>主页运行时布局：行列表，由 LauncherConfig.HomeLayout 驱动</summary>
    public ObservableCollection<HomeRowViewModel> HomeRows { get; } = new();

    /// <summary>随卡片区滚动的行（供主页渲染绑定）</summary>
    public ObservableCollection<HomeRowViewModel> ScrollableRows { get; } = new();

    /// <summary>固定在主页底部的行（供主页渲染绑定）</summary>
    public ObservableCollection<HomeRowViewModel> PinnedRows { get; } = new();

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
            new HomeCardInfo { CardId = "news", Title = "查看最新的 Minecraft 新闻", Description = "了解游戏动态", Icon = IconNews, Order = 1 },
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
            var rowVm = new HomeRowViewModel { IsPinnedToBottom = row.IsPinnedToBottom };
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

        SyncRenderRows();
    }

    /// <summary>按固定属性把行分发到滚动区/底部区两个渲染集合</summary>
    private void SyncRenderRows()
    {
        ScrollableRows.Clear();
        PinnedRows.Clear();
        foreach (var row in HomeRows)
        {
            (row.IsPinnedToBottom ? PinnedRows : ScrollableRows).Add(row);
        }
    }

    /// <summary>按组件 ID 创建运行时组件视图模型；无法提供渲染数据的返回 null</summary>
    private HomeComponentViewModel? CreateComponentVM(string id, HomeCardSize size)
    {
        var descriptor = HomeComponentRegistry.TryGet(id);

        HomeComponentViewModel? vm = id switch
        {
            HomeComponentRegistry.SeparatorId => new SeparatorComponentViewModel(),
            HomeComponentRegistry.AccountPickerId => new AccountPickerComponentViewModel(),
            HomeComponentRegistry.VersionPickerId => new VersionPickerComponentViewModel(),
            HomeComponentRegistry.LaunchButtonId => new LaunchButtonComponentViewModel(),
            HomeComponentRegistry.LogToggleId => new LogToggleComponentViewModel(),
            _ => CreateDataComponentVM(id, descriptor)
        };
        if (vm == null) return null;

        vm.Id = id;
        vm.Owner = this;
        vm.Size = size;
        return vm;
    }

    /// <summary>数据驱动组件：插件自定义内容优先，其次为通用卡片（要求启用状态）</summary>
    private HomeComponentViewModel? CreateDataComponentVM(string id, HomeComponentDescriptor? descriptor)
    {
        if (descriptor?.HasCustomContent == true)
        {
            return new CustomContentComponentViewModel { Content = descriptor.ContentFactory!() };
        }

        var card = HomeCards.FirstOrDefault(c => c.CardId == id && c.IsEnabled);
        if (id == HomeComponentRegistry.WelcomeId)
        {
            return card != null ? new WelcomeComponentViewModel { Card = card } : null;
        }
        return card != null ? new CardComponentViewModel { Card = card } : null;
    }

    /// <summary>确保组件出现在运行时布局中：已存在则刷新数据引用，不存在则追加到最后一个滚动行并持久化</summary>
    private void EnsureComponentInRows(string id)
    {
        var existing = HomeRows.SelectMany(r => r.Components).FirstOrDefault(c => c.Id == id);
        if (existing != null)
        {
            existing.Card = HomeCards.FirstOrDefault(c => c.CardId == id);
            return;
        }

        var descriptor = HomeComponentRegistry.TryGet(id);
        var isCardEnabled = HomeCards.Any(c => c.CardId == id && c.IsEnabled);
        if (!isCardEnabled && descriptor?.HasCustomContent != true) return;

        var vm = CreateComponentVM(id, descriptor?.DefaultSize ?? HomeCardSize.Medium);
        if (vm == null) return;

        var targetRow = HomeRows.LastOrDefault(r => !r.IsPinnedToBottom);
        if (targetRow == null)
        {
            targetRow = new HomeRowViewModel();
            HomeRows.Add(targetRow);
            ScrollableRows.Add(targetRow);
        }
        targetRow.Components.Add(vm);

        var config = LauncherConfig.Load();
        config.GetHomeLayout().Append(id, vm.Size);
        config.Save();
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
                IsPinnedToBottom = r.IsPinnedToBottom,
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

    /// <summary>添加组件到指定行的指定位置（index 超出范围时追加到行尾），同一组件可重复添加</summary>
    public HomeComponentViewModel? AddComponentToRow(string componentId, HomeRowViewModel row, int index)
    {
        var descriptor = HomeComponentRegistry.TryGet(componentId);
        var vm = CreateComponentVM(componentId, descriptor?.DefaultSize ?? HomeCardSize.Medium);
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

    /// <summary>移动组件到目标行的目标位置（编辑器拖拽用）</summary>
    public void MoveComponent(HomeComponentViewModel component, HomeRowViewModel targetRow, int targetIndex)
    {
        var sourceRow = HomeRows.FirstOrDefault(r => r.Components.Contains(component));
        if (sourceRow == null) return;

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
        SyncRenderRows();
        PersistHomeLayout();
        return row;
    }

    /// <summary>删除行（至少保留一行）</summary>
    public bool RemoveRow(HomeRowViewModel row)
    {
        if (HomeRows.Count <= 1) return false;
        if (!HomeRows.Remove(row)) return false;
        SyncRenderRows();
        PersistHomeLayout();
        return true;
    }

    /// <summary>切换行的底部固定状态</summary>
    public void SetRowPinned(HomeRowViewModel row, bool pinned)
    {
        if (row.IsPinnedToBottom == pinned) return;
        row.IsPinnedToBottom = pinned;
        SyncRenderRows();
        PersistHomeLayout();
    }

    /// <summary>调整组件尺寸档位</summary>
    public void SetComponentSize(HomeComponentViewModel component, HomeCardSize size)
    {
        if (component.Size == size) return;
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

            // 新卡片自动进入主页布局（与旧版"注册即显示"行为一致）
            EnsureComponentInRows(cardId);
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

    /// <summary>
    /// 插件注册自定义主页组件（内容由组件注册表中的工厂提供，UI 线程调用工厂创建控件实例）
    /// </summary>
    public void OnPluginComponentRegistered(string componentId, string title, string description, string? icon)
    {
        _dispatcher.InvokeAsync(() =>
        {
            var existing = HomeCards.FirstOrDefault(c => c.CardId == componentId);
            if (existing != null)
            {
                existing.Title = title;
                existing.Description = description;
                existing.Icon = icon;
            }
            else
            {
                HomeCards.Add(new HomeCardInfo
                {
                    CardId = componentId,
                    Title = title,
                    Description = description,
                    Icon = icon,
                    IsPluginCard = true,
                    PluginId = componentId.Split('.')[0]
                });
            }

            NotifySettingsViewModelRefreshPluginCards();

            EnsureComponentInRows(componentId);
        });
    }

    public void OnPluginComponentUnregistered(string componentId)
    {
        _dispatcher.InvokeAsync(() =>
        {
            var card = HomeCards.FirstOrDefault(c => c.CardId == componentId);
            if (card != null && card.IsPluginCard)
            {
                HomeCards.Remove(card);
            }

            RemoveComponentFromRows(componentId);
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
                    var skinPath = await SkinService.Instance.GetSkinPathAsync(acc);
                    if (!string.IsNullOrEmpty(skinPath) && File.Exists(skinPath))
                    {
                        var bitmap = SkinHeadRenderer.GetHeadFromSkin(skinPath);
                        if (bitmap != null)
                        {
                            await _dispatcher.InvokeAsync(() =>
                            {
                                _avatarCache[acc.Id] = bitmap;
                                SetAvatar(acc, bitmap);
                            });
                            return;
                        }
                    }

                    // 没有皮肤或加载失败时使用默认头像
                    await _dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            using var defaultAvatar = AssetLoader.Open(new Uri("avares://ObsMCLauncher.Desktop/Assets/logo.png"));
                            if (defaultAvatar != null)
                            {
                                var bitmap = new Avalonia.Media.Imaging.Bitmap(defaultAvatar);
                                _avatarCache[acc.Id] = bitmap;
                                SetAvatar(acc, bitmap);
                            }
                        }
                        catch { }
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

        var launchCts = new System.Threading.CancellationTokenSource();
        var versionId = SelectedInstalledVersion.Id;
        var account = SelectedAccount;
        
        try
        {
            IsLaunching = true;
            var config = LauncherConfig.Load();

            // 将 launchCts 绑定到通知，实现点击关闭即取消
            var notifId = _notificationService.Show("正在启动", "正在检查游戏完整性...", NotificationType.Progress, cts: launchCts);

            // 1. 检查完整性
            var integrity = await ObsMCLauncher.Core.Services.GameLauncher.CheckGameIntegrityAsync(
                versionId,
                config,
                (msg) => 
                {
                    if (msg.Contains("|"))
                    {
                        var parts = msg.Split('|');
                        if (double.TryParse(parts[1], out double p))
                        {
                            _notificationService.Update(notifId, parts[0], p);
                            return;
                        }
                    }
                    _notificationService.Update(notifId, msg);
                },
                launchCts.Token);

            if (integrity.HasIssue && integrity.MissingLibraries.Count > 0)
            {
                var missingCount = integrity.MissingLibraries.Count;
                _notificationService.Update(notifId, $"正在补全 {missingCount} 个缺失依赖...", 0);

                try
                {
                    var (successCount, failedCount) = await ObsMCLauncher.Core.Services.LibraryDownloader.DownloadMissingLibrariesAsync(
                        config.GameDirectory,
                        versionId,
                        integrity.MissingLibraries,
                        (progress, current, total) =>
                        {
                            _notificationService.Update(notifId, progress, current * 100.0 / Math.Max(1, total));
                        },
                        launchCts.Token);

                    if (failedCount > 0)
                    {
                        _notificationService.Show("依赖补全失败", $"{failedCount} 个必需库文件下载失败，请检查网络后重试", NotificationType.Error);
                        _notificationService.Remove(notifId);
                        return;
                    }

                    _notificationService.Update(notifId, $"已成功补全 {successCount} 个依赖", 100);
                }
                catch (Exception dlEx)
                {
                    _notificationService.Show("依赖补全失败", dlEx.Message, NotificationType.Error);
                    _notificationService.Remove(notifId);
                    return;
                }
            }

            // 1.5 模组冲突检测
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
                    _notificationService.Remove(notifId);
                    return;
                }
            }

            // 2. 准备日志窗口
            GameLogWindow? logWindow = null;
            if (config.ShowGameLogOnLaunch)
            {
                await _dispatcher.InvokeAsync(() => 
                {
                    logWindow = new GameLogWindow(versionId);
                    logWindow.Show();
                });
            }

            _notificationService.Update(notifId, "正在启动 Minecraft...");

            var launchResult = await ObsMCLauncher.Core.Services.GameLauncher.LaunchGameAsync(
                versionId,
                account,
                config,
                (progress) => _notificationService.Update(notifId, progress),
                (output) => logWindow?.AppendGameOutput(output),
                (exitCode) =>
                {
                    logWindow?.OnGameExit(exitCode);
                    _dispatcher.InvokeAsync(() =>
                        _notificationService.Show(
                            "游戏退出",
                            $"游戏已退出，退出代码: {exitCode}",
                            exitCode == 0 ? NotificationType.Info : NotificationType.Warning));
                },
                launchCts.Token);

            _notificationService.Remove(notifId);

            if (launchResult.Success)
            {
                _notificationService.Show("启动成功", $"Minecraft {versionId} 已成功拉起", NotificationType.Success);

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
                _notificationService.Show("启动失败", string.IsNullOrEmpty(launchResult.ErrorMessage) ? "请检查日志或Java配置" : launchResult.ErrorMessage, NotificationType.Error);
            }
        }
        catch (OperationCanceledException)
        {
            _notificationService.Show("已取消", "启动流程已取消", NotificationType.Info);
        }
        catch (Exception ex)
        {
            _notificationService.Show("启动异常", ex.Message, NotificationType.Error);
        }
        finally
        {
            IsLaunching = false;
            launchCts.Dispose();
        }
    }
}
