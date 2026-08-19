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

    public ObservableCollection<MinecraftVersion> Versions { get; } = new();

    public ObservableCollection<ObsMCLauncher.Core.Services.Minecraft.InstalledVersion> InstalledVersions { get; } = new();

    public ObservableCollection<GameAccount> Accounts { get; } = new();

    public ObservableCollection<HomeCardInfo> HomeCards { get; } = new();

    public HomeCardInfo? WelcomeCard => HomeCards.FirstOrDefault(c => c.CardId == "welcome");

    public IEnumerable<HomeCardInfo> OtherCards => HomeCards.Where(c => c.CardId != "welcome");

    public bool IsWelcomeCardEnabled => WelcomeCard?.IsEnabled ?? false;

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

    private GameAccount? _selectedAccount;
    public GameAccount? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
            {
                if (value != null)
                {
                    if (!value.IsDefault)
                    {
                        ObsMCLauncher.Core.Services.Accounts.AccountService.Instance.SetDefaultAccount(value.Id);
                    }

                    var config = LauncherConfig.Load();
                    config.SelectedAccountId = value.Id;
                    config.Save();

                    if (NavigationStore.MainWindow?.NavItems.FirstOrDefault(x => x.Title == "账号管理")?.Page is AccountManagementViewModel accountVm)
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
                        LocalStatus = $"已选择版本: {value.Id}";
                        OpenVersionDetailCommand.NotifyCanExecuteChanged();
                        LaunchCommand.NotifyCanExecuteChanged(); // 刷新启动按钮可用状态
                    }
                    catch (Exception ex)
                    {
                        LocalStatus = $"选择版本失败: {ex.Message}";
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

    private string _status = "";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    private string _localStatus = "";
    public string LocalStatus
    {
        get => _localStatus;
        set => SetProperty(ref _localStatus, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _isLocalLoading;
    public bool IsLocalLoading
    {
        get => _isLocalLoading;
        set
        {
            if (SetProperty(ref _isLocalLoading, value))
            {
                RefreshLocalCommand.NotifyCanExecuteChanged();
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

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand RefreshLocalCommand { get; }
    public IRelayCommand OpenVersionDetailCommand { get; }
    public IAsyncRelayCommand LaunchCommand { get; }

    public InstanceViewModel InstanceViewModel { get; }

    private readonly System.Collections.Specialized.NotifyCollectionChangedEventHandler _homeCardsChangedHandler;

    // 默认卡片图标（SVG path data），避免依赖系统 emoji 字形导致跨平台渲染为方块
    private const string IconRocket = "M12 2C12 2 6 6 6 12C6 15.31 7.79 18.17 10.5 19.71L12 23L13.5 19.71C16.21 18.17 18 15.31 18 12C18 6 12 2 12 2M12 10C10.9 10 10 9.1 10 8C10 6.9 10.9 6 12 6C13.1 6 14 6.9 14 8C14 9.1 13.1 10 12 10M12 20C12 20 8 17.86 8 12C8 10.5 8.5 9.24 9.3 8.17C9.86 8.69 10.42 9.12 11.16 9.44C12.62 10.08 14.55 10.37 15.5 10.05C15.76 11.5 15.37 12.6 15 13.43C14.5 14.53 12 20 12 20Z";
    private const string IconNews = "M20 2H4C2.9 2 2 2.9 2 4V22L6 18H20C21.1 18 22 17.1 22 16V4C22 2.9 21.1 2 20 2M20 16H5.17L4 17.17V4H20V16M7 9H17V7H7V9M7 13H14V11H7V13Z";
    private const string IconGlobe = "M12 2C6.48 2 2 6.48 2 12S6.48 22 12 22 22 17.52 22 12 17.52 2 12 2M12 20C11.1 20 10.21 19.88 9.36 19.67L10 18L12 16L13.34 13.09L14.35 12H17.5C18.2 12 18.85 12.26 19.35 12.67C18.37 16.8 15.48 20 12 20M7 9L5.77 11.13L5.25 11.77C5.08 11.23 5 10.65 5 10C5 8.94 5.26 7.94 5.71 7.06L7 9M19 10.25C18.03 9.21 16.57 8.5 15 8.5H12.86L10 9.63V12.38L12.41 14.79L13.07 13.25L15 12.5L17 14.5V17.13C14.24 18.37 11 18.37 8.24 17.13L6.83 16.71L6.4 16.29L5.03 16.72C5.16 17.5 5.41 18.25 5.75 18.94C7.21 20.91 9.43 22.02 12 22C16.42 22 20 18.42 20 14C20 12.72 19.65 11.52 19.03 10.5L19 10.25Z";
    private const string IconDownload = "M19 9H15V3H9V9H5L12 16L19 9M5 18V20H19V18H5Z";

    public HomeViewModel(ObsMCLauncher.Core.Services.Ui.IDispatcher dispatcher, NotificationService notificationService)
    {
        _dispatcher = dispatcher;
        _notificationService = notificationService;
        _dialogService = new DialogService();

        // 账号管理页新增/删除账号后刷新主页账号列表（事件解耦，不再通过导航项查找）
        AccountEvents.AccountsChanged += OnAccountsChanged;

        InstanceViewModel = new InstanceViewModel(notificationService);

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsLoading);
        RefreshLocalCommand = new AsyncRelayCommand(LoadLocalAsync, () => !IsLocalLoading);
        LaunchCommand = new AsyncRelayCommand(LaunchAsync, () => CanLaunch);

        OpenVersionDetailCommand = new RelayCommand(OpenVersionDetail, CanOpenVersionDetail);

        var config = LauncherConfig.Load();
        SelectedVersionId = config.SelectedVersion;
        _showGameLog = config.ShowGameLogOnLaunch;

        _homeCardsChangedHandler = (s, e) =>
        {
            OnPropertyChanged(nameof(WelcomeCard));
            OnPropertyChanged(nameof(IsWelcomeCardEnabled));
            OnPropertyChanged(nameof(OtherCards));

            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add || 
                e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace ||
                e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                SubscribeToWelcomeCardChanges();
            }
        };
        HomeCards.CollectionChanged += _homeCardsChangedHandler;

        InitializeHomeData();

        _ = LoadAsync();
        _ = LoadLocalAsync();
    }

    public void Dispose()
    {
        AccountEvents.AccountsChanged -= OnAccountsChanged;
        HomeCards.CollectionChanged -= _homeCardsChangedHandler;
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
            new HomeCardInfo { CardId = "welcome", Title = "欢迎使用黑曜石启动器", Description = "开始你的 Minecraft 之旅", Icon = IconRocket, Order = 0 },
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

        LoadAccounts();

        // 注意：PluginContext.OnHomeCardRegistered 和 OnHomeCardUnregistered
        // 现在在 MainWindowViewModel 中设置，并通过事件分发到各个ViewModel
    }

    private void SubscribeToWelcomeCardChanges()
    {
        if (WelcomeCard != null)
        {
            WelcomeCard.PropertyChanged -= OnWelcomeCardPropertyChanged;
            WelcomeCard.PropertyChanged += OnWelcomeCardPropertyChanged;
        }
    }

    private void OnWelcomeCardPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeCardInfo.IsEnabled))
        {
            OnPropertyChanged(nameof(IsWelcomeCardEnabled));
        }
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
        });
    }

    private void NotifySettingsViewModelRefreshPluginCards()
    {
        // 通过NavigationStore找到SettingsViewModel并刷新插件卡片
        var mainWindow = NavigationStore.MainWindow;
        if (mainWindow == null) return;
        
        // 先检查NavItems
        var settingsVm = mainWindow.NavItems
            .FirstOrDefault(x => x.Title == "设置")?.Page as SettingsViewModel;
        
        // 如果在NavItems中找不到，检查BottomNavItems
        if (settingsVm == null)
        {
            settingsVm = mainWindow.BottomNavItems
                .FirstOrDefault(x => x.Title == "设置")?.Page as SettingsViewModel;
        }
        
        settingsVm?.RefreshPluginCards();
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

            var config = LauncherConfig.Load();
            var configToRemove = config.HomeCards.Where(c => c.IsPluginCard && c.PluginId == pluginId).ToList();
            foreach (var cfg in configToRemove)
            {
                config.HomeCards.Remove(cfg);
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

    /// <summary>按页名导航到对应侧边栏页面（与 MainWindow 的导航映射保持一致）</summary>
    private void NavigateToNavPage(string page)
    {
        var main = NavigationStore.MainWindow;
        if (main == null)
        {
            DebugLogger.Warn("Home", "NavigationStore.MainWindow is null");
            return;
        }

        // 映射页面名到导航项标题
        var pageMapping = new System.Collections.Generic.Dictionary<string, string>
        {
            ["multiplayer"] = "多人联机",
            ["resources"] = "资源下载",
            ["accounts"] = "账号管理",
            ["versions"] = "版本管理",
            ["settings"] = "设置",
            ["more"] = "更多"
        };

        if (!pageMapping.TryGetValue(page.ToLowerInvariant(), out var navTitle))
        {
            DebugLogger.Warn("Home", $"Page '{page}' not in mapping");
            return;
        }

        var targetNav = main.NavItems.FirstOrDefault(n => n.Title == navTitle)
            ?? main.BottomNavItems.FirstOrDefault(n => n.Title == navTitle);
        if (targetNav == null)
        {
            DebugLogger.Warn("Home", $"NavItem with title '{navTitle}' not found");
            return;
        }

        if (main.NavItems.Contains(targetNav))
        {
            main.SelectedNavItem = targetNav;
        }
        else if (main.BottomNavItems.Contains(targetNav))
        {
            main.SelectedBottomNavItem = targetNav;
        }
    }

    private void LoadAccounts()
    {
        Accounts.Clear();
        var accounts = ObsMCLauncher.Core.Services.Accounts.AccountService.Instance.GetAllAccounts();
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

        // 重新触发插件卡片注册，以便根据新的启用状态更新显示
        // 这里需要通知所有已加载的插件重新注册他们的卡片
        // 由于插件系统已经加载，我们可以通过重新调用OnPluginCardRegistered来更新卡片状态
        _dispatcher.InvokeAsync(() =>
        {
            // 获取当前所有插件卡片
            var config = LauncherConfig.Load();
            var cardConfigs = config.HomeCards.Where(c => c.IsPluginCard).ToList();

            // 对于每个插件卡片，重新检查其启用状态
            foreach (var cardConfig in cardConfigs)
            {
                // 这里需要从插件系统获取卡片的详细信息
                // 由于插件系统没有提供获取卡片详情的方法，我们只能更新现有卡片的启用状态
                var existingCard = HomeCards.FirstOrDefault(c => c.CardId == cardConfig.CardId);
                if (existingCard != null)
                {
                    existingCard.IsEnabled = cardConfig.IsEnabled;

                    // 不再从集合中移除禁用的卡片，而是保留它们，只是在显示时根据 IsEnabled 属性决定是否显示
                }
                else if (cardConfig.IsEnabled)
                {
                    // 如果卡片被启用但不在显示列表中，需要插件重新注册
                    // 这里无法处理，因为需要插件重新调用RegisterHomeCard
                }
            }
        });
    }

    private void SelectLastAccount()
    {
        var config = LauncherConfig.Load();
        var lastAccountId = config.SelectedAccountId;
        
        if (!string.IsNullOrEmpty(lastAccountId))
        {
            SelectedAccount = Accounts.FirstOrDefault(a => a.Id == lastAccountId);
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
                                SetAvatar(acc, bitmap);
                            });
                            return;
                        }
                    }

                    // 如果没有皮肤或加载失败，加载默认头像
                    await _dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            using var defaultAvatar = AssetLoader.Open(new Uri("avares://ObsMCLauncher.Desktop/Assets/logo.png"));
                            if (defaultAvatar != null)
                            {
                                SetAvatar(acc, new Avalonia.Media.Imaging.Bitmap(defaultAvatar));
                            }
                        }
                        catch { }
                    });
                }
                catch { }
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

    public async Task LoadAsync()
    {
        try
        {
            await _dispatcher.InvokeAsync(() => { IsLoading = true; Status = "正在获取版本列表..."; });
            var manifest = await ObsMCLauncher.Core.Services.Minecraft.MinecraftVersionService.GetVersionListAsync().ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() =>
            {
                Versions.Clear();
                if (manifest?.Versions != null)
                {
                    foreach (var v in manifest.Versions) Versions.Add(v);
                    Status = $"已加载 {Versions.Count} 个版本";
                }
                else Status = "版本清单为空";
            });
        }
        catch (Exception ex)
        {
            await _dispatcher.InvokeAsync(() => Status = $"加载失败: {ex.Message}");
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => IsLoading = false);
        }
    }

    public async Task LoadLocalAsync()
    {
        try
        {
            await _dispatcher.InvokeAsync(() => { IsLocalLoading = true; LocalStatus = "正在扫描本地版本..."; });
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

                LocalStatus = $"已发现 {InstalledVersions.Count} 个本地版本";
            });
        }
        catch (Exception ex)
        {
            await _dispatcher.InvokeAsync(() => LocalStatus = $"本地版本扫描失败: {ex.Message}");
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => IsLocalLoading = false);
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
