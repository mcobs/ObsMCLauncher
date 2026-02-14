using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Plugins;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Services.Minecraft;
using ObsMCLauncher.Core.Services.Ui;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.ViewModels.Notifications;
using ObsMCLauncher.Desktop.Views;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly ObsMCLauncher.Core.Services.Ui.IDispatcher _dispatcher;
    private readonly NotificationService _notificationService;

    public ObservableCollection<MinecraftVersion> Versions { get; } = new();

    public ObservableCollection<ObsMCLauncher.Core.Services.Minecraft.InstalledVersion> InstalledVersions { get; } = new();

    public ObservableCollection<GameAccount> Accounts { get; } = new();

    public ObservableCollection<HomeCardInfo> HomeCards { get; } = new();

    private bool _hasAccounts = true;
    public bool HasAccounts
    {
        get => _hasAccounts;
        private set => SetProperty(ref _hasAccounts, value);
    }

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
                    ObsMCLauncher.Core.Services.Accounts.AccountService.Instance.UpdateLastUsed(value.Id);
                    
                    // 保存选择的账号到配置
                    var config = LauncherConfig.Load();
                    config.SelectedAccountId = value.Id;
                    config.Save();
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
            }
        }
    }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand RefreshLocalCommand { get; }
    public IRelayCommand OpenVersionDetailCommand { get; }
    public IAsyncRelayCommand LaunchCommand { get; }

    public InstanceViewModel InstanceViewModel { get; }

    public HomeViewModel(ObsMCLauncher.Core.Services.Ui.IDispatcher dispatcher, NotificationService notificationService)
    {
        _dispatcher = dispatcher;
        _notificationService = notificationService;

        InstanceViewModel = new InstanceViewModel(notificationService);

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsLoading);
        RefreshLocalCommand = new AsyncRelayCommand(LoadLocalAsync, () => !IsLocalLoading);
        LaunchCommand = new AsyncRelayCommand(LaunchAsync, () => !IsLaunching);

        OpenVersionDetailCommand = new RelayCommand(OpenVersionDetail, CanOpenVersionDetail);

        var config = LauncherConfig.Load();
        SelectedVersionId = config.SelectedVersion;
        _showGameLog = config.ShowGameLogOnLaunch;

        InitializeHomeData();

        _ = LoadAsync();
        _ = LoadLocalAsync();
    }

    private void InitializeHomeData()
    {
        HomeCards.Clear();

        var config = LauncherConfig.Load();
        var cardConfigs = config.HomeCards ?? new();

        var defaultCards = new List<HomeCardInfo>
        {
            new HomeCardInfo { CardId = "welcome", Title = "欢迎使用黑曜石启动器！", Description = "开始你的Minecraft之旅", Icon = "🎉", Order = 0 },
            new HomeCardInfo { CardId = "news", Title = "查看最新的 Minecraft 新闻", Description = "了解游戏动态", Icon = "📰", CommandId = "url:https://zh.minecraft.wiki", Order = 1 },
            new HomeCardInfo { CardId = "multiplayer", Title = "多人联机", Description = "加入服务器与好友一起游戏", Icon = "🌐", CommandId = "navigate:multiplayer", Order = 2 },
            new HomeCardInfo { CardId = "mods", Title = "资源下载", Description = "下载Mod、材质包等资源", Icon = "📦", CommandId = "navigate:resources", Order = 3 }
        };

        foreach (var card in defaultCards)
        {
            var cardConfig = cardConfigs.FirstOrDefault(c => c.CardId == card.CardId);
            card.IsEnabled = cardConfig?.IsEnabled ?? true;
            card.Order = cardConfig?.Order ?? defaultCards.IndexOf(card);
        }

        foreach (var card in defaultCards.Where(c => c.IsEnabled).OrderBy(c => c.Order))
        {
            HomeCards.Add(card);
        }

        LoadAccounts();

        // 注意：PluginContext.OnHomeCardRegistered 和 OnHomeCardUnregistered
        // 现在在 MainWindowViewModel 中设置，并通过事件分发到各个ViewModel
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

                // 如果卡片被启用，才添加到显示列表
                if (isEnabled)
                {
                    HomeCards.Add(newCard);
                }
            }

            // 通知SettingsViewModel刷新插件卡片
            NotifySettingsViewModelRefreshPluginCards();
        });
    }

    private void NotifySettingsViewModelRefreshPluginCards()
    {
        // 通过NavigationStore找到SettingsViewModel并刷新插件卡片
        var settingsVm = NavigationStore.MainWindow?.NavItems
            .FirstOrDefault(x => x.Title == "设置")?.Page as SettingsViewModel;
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

            System.Diagnostics.Debug.WriteLine($"[HomeViewModel] 已移除插件 {pluginId} 的所有卡片，共 {cardsToRemove.Count} 个");
        });
    }

    [RelayCommand]
    private void CardClick(HomeCardInfo? card)
    {
        if (card == null || string.IsNullOrEmpty(card.CommandId)) return;

        if (card.CommandId.StartsWith("navigate:"))
        {
            var page = card.CommandId.Substring(9);
            var main = NavigationStore.MainWindow;
            if (main == null)
            {
                System.Diagnostics.Debug.WriteLine("NavigationStore.MainWindow is null");
                return;
            }

            // 映射卡片导航命令到导航项标题
            var pageMapping = new System.Collections.Generic.Dictionary<string, string>
            {
                ["multiplayer"] = "多人联机",
                ["resources"] = "资源下载",
                ["accounts"] = "账号管理",
                ["versions"] = "版本管理",
                ["settings"] = "设置",
                ["more"] = "更多"
            };

            if (pageMapping.TryGetValue(page.ToLower(), out var navTitle))
            {
                var targetNav = main.NavItems.FirstOrDefault(n => n.Title == navTitle);
                if (targetNav != null)
                {
                    main.SelectedNavItem = targetNav;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"NavItem with title '{navTitle}' not found");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Page '{page}' not in mapping");
            }
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
        // 重新初始化主页数据
        InitializeHomeData();

        // 重新触发插件卡片注册，以便根据新的启用状态更新显示
        // 这里需要通知所有已加载的插件重新注册他们的卡片
        // 由于插件系统已经加载，我们可以通过重新调用OnPluginCardRegistered来更新卡片状态
        _dispatcher.InvokeAsync(() =>
        {
            // 获取当前所有插件卡片
            var config = LauncherConfig.Load();
            var pluginCards = config.HomeCards.Where(c => c.IsPluginCard).ToList();

            // 对于每个插件卡片，重新检查其启用状态
            foreach (var cardConfig in pluginCards)
            {
                // 这里需要从插件系统获取卡片的详细信息
                // 由于插件系统没有提供获取卡片详情的方法，我们只能更新现有卡片的启用状态
                var existingCard = HomeCards.FirstOrDefault(c => c.CardId == cardConfig.CardId);
                if (existingCard != null)
                {
                    existingCard.IsEnabled = cardConfig.IsEnabled;

                    // 如果卡片被禁用，从显示列表中移除
                    if (!cardConfig.IsEnabled)
                    {
                        HomeCards.Remove(existingCard);
                    }
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
                                acc.Avatar = bitmap;
                                // 强制 UI 刷新项
                                var index = Accounts.IndexOf(acc);
                                if (index >= 0) Accounts[index] = acc;
                            });
                            return;
                        }
                    }

                    // 如果没有皮肤或加载失败，加载默认头像
                    await _dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            // 使用默认头像资源 (假设已存在，若不存在则使用 logo 代替)
                            var defaultAvatar = AssetLoader.Open(new Uri("avares://ObsMCLauncher.Desktop/Assets/logo.png"));
                            if (defaultAvatar != null)
                            {
                                acc.Avatar = new Avalonia.Media.Imaging.Bitmap(defaultAvatar);
                                var index = Accounts.IndexOf(acc);
                                if (index >= 0) Accounts[index] = acc;
                        }
                    }
                        catch { }
                    });
                }
                catch { }
            });
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
            bool hasIssue = await ObsMCLauncher.Core.Services.GameLauncher.CheckGameIntegrityAsync(
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

            if (hasIssue && ObsMCLauncher.Core.Services.GameLauncher.MissingLibraries.Count > 0)
            {
                _notificationService.Update(notifId, $"正在补全 {ObsMCLauncher.Core.Services.GameLauncher.MissingLibraries.Count} 个依赖...");
                _notificationService.Show("缺少依赖", "检测到必需库文件缺失，请先在版本管理中修复", NotificationType.Error);
                _notificationService.Remove(notifId);
                return;
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

            // 3. 正式启动
            bool success = await ObsMCLauncher.Core.Services.GameLauncher.LaunchGameAsync(
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

            if (success)
            {
                _notificationService.Show("启动成功", $"Minecraft {versionId} 已成功拉起", NotificationType.Success);
            }
            else
            {
                _notificationService.Show("启动失败", ObsMCLauncher.Core.Services.GameLauncher.LastError ?? "请检查日志或Java配置", NotificationType.Error);
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
        }
    }
}
