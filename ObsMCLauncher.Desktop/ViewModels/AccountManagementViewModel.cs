using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Accounts;
using ObsMCLauncher.Desktop.Services;
using ObsMCLauncher.Desktop.ViewModels.Dialogs;
using ObsMCLauncher.Desktop.ViewModels.Notifications;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class AccountManagementViewModel : ViewModelBase
{
    /// <summary>账号列表（每项为包装视图模型，承载独立的刷新/操作状态）</summary>
    public ObservableCollection<AccountItemViewModel> Items { get; } = new();

    private ObsMCLauncher.Core.Models.GameAccount? _selectedAccount;
    public ObsMCLauncher.Core.Models.GameAccount? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (!ReferenceEquals(_selectedAccount, value))
            {
                _selectedAccount = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(SelectedAccount)));
            }
        }
    }

    private string _usernameInput = "";
    public string UsernameInput
    {
        get => _usernameInput;
        set
        {
            if (_usernameInput != value)
            {
                _usernameInput = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(UsernameInput)));
                AddOfflineCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private string _status = "";
    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Status)));
            }
        }
    }

    private bool _isMicrosoftLoginRunning;
    public bool IsMicrosoftLoginRunning
    {
        get => _isMicrosoftLoginRunning;
        set
        {
            if (_isMicrosoftLoginRunning != value)
            {
                _isMicrosoftLoginRunning = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsMicrosoftLoginRunning)));
                StartMicrosoftLoginCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _isYggdrasilLoginRunning;
    public bool IsYggdrasilLoginRunning
    {
        get => _isYggdrasilLoginRunning;
        set
        {
            if (_isYggdrasilLoginRunning != value)
            {
                _isYggdrasilLoginRunning = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsYggdrasilLoginRunning)));
                AddYggdrasilAccountCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _isAddOfflinePanelVisible;
    public bool IsAddOfflinePanelVisible
    {
        get => _isAddOfflinePanelVisible;
        set
        {
            if (_isAddOfflinePanelVisible != value)
            {
                _isAddOfflinePanelVisible = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsAddOfflinePanelVisible)));
            }
        }
    }

    [ObservableProperty]
    private bool _isYggdrasilLoginDialogOpen;

    [ObservableProperty]
    private YggdrasilLoginViewModel _yggdrasilLoginDialog = new();

    /// <summary>状态消息分级（InfoBar 展示）</summary>
    [ObservableProperty]
    private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;

    /// <summary>账号搜索关键词</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>是否存在账号（区分"无账号"与"搜索无结果"两种空状态）</summary>
    [ObservableProperty]
    private bool _hasAccounts;

    [ObservableProperty]
    private bool _showNoAccountsEmpty;

    [ObservableProperty]
    private bool _showNoSearchResultsEmpty;

    /// <summary>过滤后的账号列表（绑定到界面）</summary>
    public ObservableCollection<AccountItemViewModel> FilteredItems { get; } = new();

    /// <summary>请求视图滚动到指定账号（新增账号后定位用）</summary>
    public event Action<AccountItemViewModel>? ScrollToAccountRequested;

    private readonly HashSet<string> _autoRefreshAttempted = new();
    private bool _autoRefreshing;

    public IRelayCommand LoadCommand { get; }

    public IRelayCommand ShowAddOfflinePanelCommand { get; }
    public IRelayCommand CancelAddOfflineCommand { get; }
    public IRelayCommand AddOfflineCommand { get; }

    public IAsyncRelayCommand<AccountItemViewModel> DeleteSelectedCommand { get; }

    public IRelayCommand<AccountItemViewModel> SetDefaultSelectedCommand { get; }

    public IAsyncRelayCommand<AccountItemViewModel> RefreshAccountCommand { get; }

    public IAsyncRelayCommand StartMicrosoftLoginCommand { get; }
    public IAsyncRelayCommand AddYggdrasilAccountCommand { get; }

    private CancellationTokenSource? _msLoginCts;

    public AccountManagementViewModel()
    {
        LoadCommand = new RelayCommand(Load);

        ShowAddOfflinePanelCommand = new RelayCommand(() => IsAddOfflinePanelVisible = true);
        CancelAddOfflineCommand = new RelayCommand(() => { IsAddOfflinePanelVisible = false; UsernameInput = ""; });

        AddOfflineCommand = new RelayCommand(() =>
        {
            var username = UsernameInput.Trim();
            if (username.Length < 3 || username.Length > 16)
            {
                SetStatus("用户名长度必须在 3-16 个字符之间", InfoBarSeverity.Warning);
                return;
            }

            try
            {
                var acc = AccountService.Instance.AddOfflineAccount(username);
                UsernameInput = "";
                IsAddOfflinePanelVisible = false;
                Load();
                SetStatus("已添加离线账号", InfoBarSeverity.Success);
                
                // 通知主页等订阅方刷新账号列表
                AccountEvents.NotifyAccountsChanged();
                
                // 添加后自动刷新头像
                var newItem = Items.FirstOrDefault(w => w.Account.Id == acc.Id);
                if (newItem != null) _ = RefreshAccountAsync(newItem);
                HighlightNewAccount(acc); // 滚动到新账号并短暂高亮
            }
            catch (InvalidOperationException ex)
            {
                // 服务层业务校验（如用户名重复）直接展示其消息
                SetStatus(ex.Message, InfoBarSeverity.Warning);
            }
            catch (Exception ex)
            {
                SetStatus($"添加失败: {ex.Message}", InfoBarSeverity.Error);
            }
        }, () => !string.IsNullOrWhiteSpace(UsernameInput));

        DeleteSelectedCommand = new AsyncRelayCommand<AccountItemViewModel>(async item =>
        {
            if (item == null || item.IsBusy || item.IsRefreshing) return;
            var acc = item.Account;

            var main = NavigationStore.MainWindow;
            if (main != null)
            {
                var downloadConsent = await main.Dialogs.ShowQuestion("确认删除", $"确定要删除账号 '{acc.Username}' 吗？");
                if (downloadConsent != DialogResult.Yes) return;
            }

            item.IsBusy = true;
            try
            {
                var wasDefault = acc.IsDefault;
                AccountService.Instance.DeleteAccount(acc.Id);
                Load();

                if (wasDefault)
                {
                    // 服务层会自动把第一个账号设为默认，这里向用户说明
                    var newDefault = Items.FirstOrDefault(w => w.Account.IsDefault)?.Account;
                    SetStatus(newDefault != null
                        ? $"已删除默认账号，已自动将「{newDefault.Username}」设为默认账号"
                        : "已删除默认账号，当前没有可用账号", InfoBarSeverity.Success);
                }
                else
                {
                    SetStatus("已删除账号", InfoBarSeverity.Success);
                }
                
                // 通知主页等订阅方刷新账号列表
                AccountEvents.NotifyAccountsChanged();
            }
            catch (Exception ex)
            {
                SetStatus($"删除失败: {ex.Message}", InfoBarSeverity.Error);
            }
            finally
            {
                item.IsBusy = false;
            }
        });

        SetDefaultSelectedCommand = new RelayCommand<AccountItemViewModel>(item =>
        {
            if (item == null) return;
            var acc = item.Account;

            try
            {
                AccountService.Instance.SetDefaultAccount(acc.Id);

                foreach (var w in Items)
                {
                    w.Account.IsDefault = w.Account.Id == acc.Id;
                }

                SetStatus("已设置为默认账号", InfoBarSeverity.Success);

                if (NavigationStore.MainWindow?.Home is { } homeVm)
                {
                    foreach (var a in homeVm.Accounts)
                    {
                        a.IsDefault = a.Id == acc.Id;
                    }
                    var homeAcc = homeVm.Accounts.FirstOrDefault(a => a.Id == acc.Id);
                    if (homeAcc != null)
                    {
                        homeVm.SelectedAccount = homeAcc;
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus($"设置失败: {ex.Message}", InfoBarSeverity.Error);
            }
        });

        RefreshAccountCommand = new AsyncRelayCommand<AccountItemViewModel>(RefreshAccountAsync);
        StartMicrosoftLoginCommand = new AsyncRelayCommand(StartMicrosoftLoginAsync, () => !IsMicrosoftLoginRunning);
        AddYggdrasilAccountCommand = new AsyncRelayCommand(AddYggdrasilAccountAsync, () => !IsYggdrasilLoginRunning);

        Load();
    }

    private async Task RefreshAccountAsync(AccountItemViewModel? item)
    {
        if (item == null || item.IsBusy) return;
        var acc = item.Account;

        item.IsRefreshing = true;
        SetStatus($"正在刷新账号: {acc.Username}", InfoBarSeverity.Informational);
        try
        {
            if (acc.Type == AccountType.Microsoft)
            {
                await AccountService.Instance.RefreshMicrosoftAccountAsync(acc.Id);
            }
            else if (acc.Type == AccountType.Yggdrasil)
            {
                await AccountService.Instance.RefreshYggdrasilAccountAsync(acc.Id);
            }

            var bitmap = await AccountAvatarService.LoadHeadAsync(acc, true);
            if (bitmap != null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    acc.Avatar = bitmap;
                });
            }
            SetStatus($"账号 {acc.Username} 刷新成功", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"刷新失败: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            item.IsRefreshing = false;
        }
    }

    private async Task AddYggdrasilAccountAsync()
    {
        if (IsYggdrasilLoginRunning) return;
        IsYggdrasilLoginRunning = true;

        var main = NavigationStore.MainWindow;
        if (main == null) return;

        try
        {
            if (!AuthlibInjectorService.IsAuthlibInjectorExists())
            {
                var result = await main.Dialogs.ShowQuestion(
                    "缺少必需文件",
                    "外置登录需要 authlib-injector.jar 文件。\n\n是否立即下载？");

                if (result != DialogResult.Yes) return;

                var config = LauncherConfig.Load();
                var useBMCLAPI = config.DownloadSource == DownloadSource.BMCLAPI;
                var notifId = main.Notifications.Show("下载中", "正在下载 authlib-injector.jar...", NotificationType.Progress);

                try
                {
                    var svc = new AuthlibInjectorService();
                    svc.OnProgressUpdate = (done, total) =>
                    {
                        var pct = total > 0 ? (int)(done * 100 / total) : 0;
                        main.Notifications.Update(notifId, $"正在下载 authlib-injector.jar... {pct}%");
                    };

                    await svc.DownloadAuthlibInjectorAsync(useBMCLAPI);
                    main.Notifications.Remove(notifId);

                    main.Notifications.Show("下载完成", "authlib-injector.jar 已下载完成", NotificationType.Success, 3);
                }
                catch (Exception ex)
                {
                    main.Notifications.Remove(notifId);
                    await main.Dialogs.ShowError("下载失败", $"下载失败：{ex.Message}");
                    return;
                }
            }

            // 使用Dialog模式
            YggdrasilLoginDialog = new YggdrasilLoginViewModel();
            YggdrasilLoginDialog.OnLoginCompleted = async account =>
            {
                if (account != null)
                {
                    // 方案A：先判重（同服务器+同用户名视为重复）
                    var existing = AccountService.Instance.GetAllAccounts().FirstOrDefault(a =>
                        a.Type == AccountType.Yggdrasil &&
                        a.YggdrasilServerId == account.YggdrasilServerId &&
                        a.Username.Equals(account.Username, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        await main.Dialogs.ShowError(
                            "账号已存在",
                            $"已存在外置登录账号：{account.Username}\n\n请勿重复添加相同服务器的同名账号。"
                        );
                        IsYggdrasilLoginDialogOpen = false;
                        return;
                    }

                    AccountService.Instance.AddYggdrasilAccount(account);
                    Load();
                    HighlightNewAccount(account);

                    // 通知主页等订阅方刷新账号列表
                    AccountEvents.NotifyAccountsChanged();

                    main.Notifications.Show(
                        "登录成功",
                        $"成功添加外置登录账号：{account.Username}",
                        NotificationType.Success,
                        3
                    );
                }
                IsYggdrasilLoginDialogOpen = false;
            };
            
            IsYggdrasilLoginDialogOpen = true;
        }
        catch (Exception ex)
        {
            await main.Dialogs.ShowError("错误", ex.Message);
        }
        finally
        {
            IsYggdrasilLoginRunning = false;
        }
    }

    [RelayCommand]
    private void CancelYggdrasilLogin()
    {
        // 关闭对话框时清空密码，避免敏感信息残留在 ViewModel 中
        YggdrasilLoginDialog.Password = string.Empty;
        IsYggdrasilLoginDialogOpen = false;
    }

    private async Task StartMicrosoftLoginAsync()
    {
        if (IsMicrosoftLoginRunning)
            return;

        var main = NavigationStore.MainWindow;
        if (main == null)
        {
            SetStatus("MainWindow 未就绪", InfoBarSeverity.Error);
            return;
        }

        IsMicrosoftLoginRunning = true;
        _msLoginCts?.Dispose();
        _msLoginCts = new CancellationTokenSource();

        string? progressId = null;
        bool authDialogClosed = false;

        try
        {
            var auth = new MicrosoftAuthService();

            auth.OnProgressUpdate = msg =>
            {
                SetStatus(msg, InfoBarSeverity.Informational);

                if (progressId == null)
                {
                    progressId = main.Notifications.Show("微软账户登录", msg, NotificationType.Progress, durationSeconds: null);
                }
                else
                {
                    main.Notifications.Update(progressId, msg);
                }
            };

            auth.OnAuthUrlGenerated = url =>
            {
                // 显示授权URL对话框
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await main.Dialogs.ShowAuthUrlAsync(url, "微软账户登录");
                        authDialogClosed = true;
                        
                        // 如果用户关闭了对话框（result == false），取消登录
                        if (!result)
                        {
                            try { _msLoginCts?.Cancel(); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Error("MSLogin", $"AuthUrl dialog error: {ex.Message}");
                        authDialogClosed = true;
                    }
                });
            };

            var account = await auth.LoginAsync(_msLoginCts.Token);

            if (account == null)
            {
                SetStatus("微软登录失败或已取消", InfoBarSeverity.Warning);
                main.Notifications.Show("微软账户登录", Status, NotificationType.Warning, 3);
                return;
            }

            AccountService.Instance.AddOrUpdateMicrosoftAccount(account);
            Load();
            HighlightNewAccount(account);

            // 通知主页等订阅方刷新账号列表
            AccountEvents.NotifyAccountsChanged();

            // 确保关闭授权对话框
            try
            {
                if (!authDialogClosed)
                {
                    main.Dialogs.CloseAuthUrlCommand.Execute(false);
                }
            }
            catch { }

            SetStatus($"已添加微软账号: {account.Username}", InfoBarSeverity.Success);
            main.Notifications.Show("微软账户登录", $"已添加微软账号: {account.Username}", NotificationType.Success, 3);
        }
        catch (OperationCanceledException)
        {
            SetStatus("微软登录已取消", InfoBarSeverity.Warning);
            main.Notifications.Show("微软账户登录", Status, NotificationType.Warning, 3);
        }
        catch (Exception ex)
        {
            SetStatus($"微软登录失败: {ex.Message}", InfoBarSeverity.Error);
            main.Notifications.Show("微软账户登录", Status, NotificationType.Error, 5);
        }
        finally
        {
            // 确保关闭授权对话框
            try
            {
                if (!authDialogClosed)
                {
                    main.Dialogs.CloseAuthUrlCommand.Execute(true);
                }
            }
            catch { }

            if (progressId != null)
            {
                main.Notifications.Remove(progressId);
            }

            _msLoginCts?.Dispose();
            _msLoginCts = null;
            IsMicrosoftLoginRunning = false;
        }
    }

    public void Load()
    {
        try
        {
            AccountService.Instance.ReloadAccountsPath();
            var list = AccountService.Instance.GetAllAccounts();

            // 填充外置登录服务器显示名（用于详情行展示）
            var servers = YggdrasilServerService.Instance.GetAllServers();
            foreach (var a in list)
            {
                if (a.Type == AccountType.Yggdrasil && !string.IsNullOrEmpty(a.YggdrasilServerId))
                {
                    a.ServerName = servers.FirstOrDefault(s => s.Id == a.YggdrasilServerId)?.Name;
                }
            }

            // 同步包装列表：按 Id 复用包装器（保留每项的刷新/操作状态），并始终使用服务端最新实例
            var listById = list.ToDictionary(l => l.Id);
            var existingById = Items.ToDictionary(w => w.Account.Id);

            foreach (var w in Items.Where(w => !listById.ContainsKey(w.Account.Id)).ToList())
            {
                Items.Remove(w);
            }

            for (var i = 0; i < list.Count; i++)
            {
                var model = list[i];
                if (!existingById.TryGetValue(model.Id, out var existing))
                {
                    var insertIndex = Items.Count > i ? i : Items.Count;
                    Items.Insert(insertIndex, new AccountItemViewModel(model));
                    LoadSingleAccountAvatar(model);
                    continue;
                }

                // 复用包装器但替换为最新模型实例（令牌等字段以服务端为准）
                model.Avatar ??= existing.Account.Avatar;
                existing.Account = model;
                if (existing.Account.Avatar == null) LoadSingleAccountAvatar(existing.Account);

                var currentIndex = Items.IndexOf(existing);
                if (currentIndex != i)
                {
                    Items.Remove(existing);
                    Items.Insert(i, existing);
                }
            }

            ApplyFilter();
            SetStatus($"已加载 {Items.Count} 个账号", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            SetStatus($"加载失败: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    /// <summary>正在加载头像的账号 Id（避免重复触发加载任务）</summary>
    private readonly HashSet<string> _avatarLoadingIds = new();

    private void LoadSingleAccountAvatar(GameAccount acc)
    {
        if (!_avatarLoadingIds.Add(acc.Id)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var bitmap = await AccountAvatarService.LoadHeadAsync(acc);
                if (bitmap != null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => acc.Avatar = bitmap);
                    return;
                }

                // 离线/默认头像回退
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    acc.Avatar = AccountAvatarService.LoadFallbackAvatar();
                });
            }
            catch
            {
            }
            finally
            {
                _avatarLoadingIds.Remove(acc.Id);
            }
        });
    }

    /// <summary>
    /// 按搜索关键词过滤账号列表，并更新空状态显示。
    /// </summary>
    private void ApplyFilter()
    {
        FilteredItems.Clear();
        var query = SearchText.Trim();
        foreach (var item in Items)
        {
            if (string.IsNullOrEmpty(query) ||
                item.Account.Username.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredItems.Add(item);
            }
        }

        HasAccounts = Items.Count > 0;
        ShowNoAccountsEmpty = FilteredItems.Count == 0 && !HasAccounts;
        ShowNoSearchResultsEmpty = FilteredItems.Count == 0 && HasAccounts;
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    /// <summary>
    /// 滚动到新添加的账号并短暂高亮，引导用户注意。
    /// </summary>
    private void HighlightNewAccount(GameAccount? account)
    {
        if (account == null) return;

        // Load() 会从服务重新加载账号实例，需按 Id 找到对应的列表项包装器
        var item = Items.FirstOrDefault(w => w.Account.Id == account.Id);
        if (item == null) return;

        item.Account.IsHighlighted = true;
        ScrollToAccountRequested?.Invoke(item);
        _ = ClearHighlightAsync(item.Account);
    }

    private static async Task ClearHighlightAsync(GameAccount acc)
    {
        try
        {
            await Task.Delay(2500);
            acc.IsHighlighted = false;
        }
        catch
        {
        }
    }

    /// <summary>
    /// 页面可见时自动刷新已过期/状态未知的登录令牌（每个账号每会话只尝试一次）。
    /// </summary>
    public async Task AutoRefreshExpiredTokensAsync()
    {
        if (_autoRefreshing) return;
        _autoRefreshing = true;

        try
        {
            var expired = Items
                .Where(w => w.Account.Type != AccountType.Offline &&
                            w.Account.IsTokenExpired() &&
                            !_autoRefreshAttempted.Contains(w.Account.Id))
                .ToList();

            if (expired.Count == 0) return;

            _autoRefreshAttempted.UnionWith(expired.Select(w => w.Account.Id));

            // 自动刷新为后台维护操作，不弹状态提示（账号卡片上的刷新转圈即为反馈）
            foreach (var item in expired)
            {
                item.IsRefreshing = true;
                try
                {
                    if (item.Account.Type == AccountType.Microsoft)
                    {
                        await AccountService.Instance.RefreshMicrosoftAccountAsync(item.Account.Id);
                    }
                    else if (item.Account.Type == AccountType.Yggdrasil)
                    {
                        await AccountService.Instance.RefreshYggdrasilAccountAsync(item.Account.Id);
                    }
                }
                catch
                {
                    // 单个账号刷新失败不阻断其余账号
                }
                finally
                {
                    item.IsRefreshing = false;
                }
            }
        }
        finally
        {
            _autoRefreshing = false;
        }
    }

    /// <summary>
    /// 设置状态消息及其分级（InfoBar 按分级着色）。
    /// </summary>
    private void SetStatus(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        Status = message;
        StatusSeverity = severity;
    }
}
