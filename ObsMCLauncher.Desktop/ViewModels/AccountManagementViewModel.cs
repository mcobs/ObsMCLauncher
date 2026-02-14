using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Accounts;
using ObsMCLauncher.Desktop.ViewModels.Dialogs;
using ObsMCLauncher.Desktop.ViewModels.Notifications;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class AccountManagementViewModel : ViewModelBase
{
    public ObservableCollection<ObsMCLauncher.Core.Models.GameAccount> Accounts { get; } = new();

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

    [ObservableProperty]
    private bool _isRefreshing;

    public IRelayCommand LoadCommand { get; }

    public IRelayCommand ShowAddOfflinePanelCommand { get; }
    public IRelayCommand CancelAddOfflineCommand { get; }
    public IRelayCommand AddOfflineCommand { get; }

    public IRelayCommand<GameAccount> DeleteSelectedCommand { get; }

    public IRelayCommand<GameAccount> SetDefaultSelectedCommand { get; }

    public IAsyncRelayCommand<GameAccount> RefreshAccountCommand { get; }

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
                Status = "用户名长度必须在 3-16 个字符之间";
                return;
            }

            // 方案A：显式检查重复并提示
            var existing = AccountService.Instance.GetAllAccounts()
                .FirstOrDefault(a => a.Type == AccountType.Offline && a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            
            if (existing != null)
            {
                Status = $"已存在名为 '{username}' 的离线账号";
                return;
            }

            try
            {
                var acc = AccountService.Instance.AddOfflineAccount(username);
                UsernameInput = "";
                IsAddOfflinePanelVisible = false;
                Load();
                Status = "已添加离线账号";
                _ = RefreshAccountAsync(acc); // 添加后自动刷新头像
            }
            catch (Exception ex)
            {
                Status = $"添加失败: {ex.Message}";
            }
        }, () => !string.IsNullOrWhiteSpace(UsernameInput));

        DeleteSelectedCommand = new RelayCommand<GameAccount>(async acc =>
        {
            if (acc == null) return;

            var main = NavigationStore.MainWindow;
            if (main != null)
            {
                var downloadConsent = await main.Dialogs.ShowQuestion("确认删除", $"确定要删除账号 '{acc.Username}' 吗？");
                if (downloadConsent != DialogResult.Yes) return;
            }

            try
            {
                AccountService.Instance.DeleteAccount(acc.Id);
                Load();
                Status = "已删除账号";
            }
            catch (Exception ex)
            {
                Status = $"删除失败: {ex.Message}";
            }
        });

        SetDefaultSelectedCommand = new RelayCommand<GameAccount>(acc =>
        {
            if (acc == null) return;

            try
            {
                AccountService.Instance.SetDefaultAccount(acc.Id);
                Load();
                Status = "已设置为默认账号";
            }
            catch (Exception ex)
            {
                Status = $"设置失败: {ex.Message}";
            }
        });

        RefreshAccountCommand = new AsyncRelayCommand<GameAccount>(RefreshAccountAsync);
        StartMicrosoftLoginCommand = new AsyncRelayCommand(StartMicrosoftLoginAsync, () => !IsMicrosoftLoginRunning);
        AddYggdrasilAccountCommand = new AsyncRelayCommand(AddYggdrasilAccountAsync);

        Load();
    }

    private async Task RefreshAccountAsync(GameAccount? acc)
    {
        if (acc == null) return;

        IsRefreshing = true;
        Status = $"正在刷新账号: {acc.Username}";
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

            var skinPath = await SkinService.Instance.GetSkinPathAsync(acc, true);
            if (!string.IsNullOrEmpty(skinPath) && File.Exists(skinPath))
            {
                var bitmap = SkinHeadRenderer.GetHeadFromSkin(skinPath);
                if (bitmap != null)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        acc.Avatar = bitmap;
                        var index = Accounts.IndexOf(acc);
                        if (index >= 0) Accounts[index] = acc;
                    });
                }
            }
            Status = $"账号 {acc.Username} 刷新成功";
        }
        catch (Exception ex)
        {
            Status = $"刷新失败: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task AddYggdrasilAccountAsync()
    {
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

                    if (NavigationStore.MainWindow?.NavItems.FirstOrDefault(x => x.Title == "主页")?.Page is HomeViewModel homeVm)
                    {
                        homeVm.RefreshAccounts();
                    }

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
    }

    [RelayCommand]
    private void CancelYggdrasilLogin()
    {
        IsYggdrasilLoginDialogOpen = false;
    }

    private async Task StartMicrosoftLoginAsync()
    {
        if (IsMicrosoftLoginRunning)
            return;

        var main = NavigationStore.MainWindow;
        if (main == null)
        {
            Status = "MainWindow 未就绪";
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
                Status = msg;

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
                        Debug.WriteLine($"[MSLogin] AuthUrl dialog error: {ex.Message}");
                        authDialogClosed = true;
                    }
                });
            };

            var account = await auth.LoginAsync(_msLoginCts.Token);

            if (account == null)
            {
                Status = "微软登录失败或已取消";
                main.Notifications.Show("微软账户登录", Status, NotificationType.Warning, 3);
                return;
            }

            AccountService.Instance.AddOrUpdateMicrosoftAccount(account);
            Load();

            // 确保关闭授权对话框
            try
            {
                if (!authDialogClosed)
                {
                    main.Dialogs.CloseAuthUrlCommand.Execute(false);
                }
            }
            catch { }

            Status = $"已添加微软账号: {account.Username}";
            main.Notifications.Show("微软账户登录", $"已添加微软账号: {account.Username}", NotificationType.Success, 3);
        }
        catch (OperationCanceledException)
        {
            Status = "微软登录已取消";
            main.Notifications.Show("微软账户登录", Status, NotificationType.Warning, 3);
        }
        catch (Exception ex)
        {
            Status = $"微软登录失败: {ex.Message}";
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

            // 修复：使用更稳健的同步逻辑，避免清空列表导致的 UI 闪烁或状态丢失
            var toRemove = Accounts.Where(a => !list.Any(l => l.Id == a.Id)).ToList();
            foreach (var a in toRemove) Accounts.Remove(a);

            foreach (var a in list)
            {
                var existing = Accounts.FirstOrDefault(acc => acc.Id == a.Id);
                if (existing == null)
                {
                    Accounts.Add(a);
                    LoadSingleAccountAvatar(a);
                }
                else
                {
                    // 更新现有账号属性
                    existing.Username = a.Username;
                    existing.IsDefault = a.IsDefault;
                    if (existing.Avatar == null) LoadSingleAccountAvatar(existing);
                }
            }

            Status = $"已加载 {Accounts.Count} 个账号";
        }
        catch (Exception ex)
        {
            Status = $"加载失败: {ex.Message}";
        }
    }

    private void LoadSingleAccountAvatar(GameAccount acc)
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
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            acc.Avatar = bitmap;
                            var index = Accounts.IndexOf(acc);
                            if (index >= 0) Accounts[index] = acc;
                        });
                        return;
                    }
                }

                // 离线/默认头像回退
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        var defaultAvatar = AssetLoader.Open(new Uri("avares://ObsMCLauncher.Desktop/Assets/logo.png"));
                        acc.Avatar = new Avalonia.Media.Imaging.Bitmap(defaultAvatar);
                        var index = Accounts.IndexOf(acc);
                        if (index >= 0) Accounts[index] = acc;
                    }
                    catch { }
                });
            }
            catch { }
        });
    }
}
