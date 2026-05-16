using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Services.Accounts;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.ViewModels.Dialogs;
using ObsMCLauncher.Desktop.ViewModels.Notifications;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class ServersViewModel : ViewModelBase, IDisposable
{
    private readonly NotificationService _notificationService;
    private readonly DialogService _dialogService;
    private CancellationTokenSource? _autoRefreshCts;
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<ServerInfo> _servers = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private ServerInfo? _selectedServer;

    [ObservableProperty]
    private bool _isAddServerDialogOpen;

    [ObservableProperty]
    private ServerEditViewModel _addServerDialog = new();

    [ObservableProperty]
    private bool _isEditServerDialogOpen;

    [ObservableProperty]
    private ServerEditViewModel _editServerDialog = new();

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages = 1;

    [ObservableProperty]
    private int _pageSize = 12;

    public ServersViewModel(NotificationService notificationService)
    {
        _notificationService = notificationService;
        _dialogService = NavigationStore.MainWindow?.Dialogs ?? new DialogService();
    }

    public void Load()
    {
        try
        {
            IsLoading = true;
            var config = LauncherConfig.Load();

            var servers = config.Servers ?? new List<ServerInfo>();
            DebugLogger.Info("ServersVM", $"Load: 从配置加载 {servers.Count} 个服务器");

            _allFilteredServers = servers.ToList();

            CurrentPage = 1;
            UpdatePagination();
            ApplyPaging();
            IsEmpty = servers.Count == 0;
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"加载服务器列表失败: {ex.Message}", NotificationType.Error);
            DebugLogger.Error("ServersVM", $"加载服务器列表失败: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdatePagination()
    {
        var total = _allFilteredServers.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)total / PageSize));
        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;
    }

    private List<ServerInfo> _allFilteredServers = new();

    private void ApplyPaging()
    {
        var skip = (CurrentPage - 1) * PageSize;
        var page = _allFilteredServers.Skip(skip).Take(PageSize).ToList();
        Servers.Clear();
        foreach (var s in page) Servers.Add(s);
        DebugLogger.Info("ServersVM", $"分页渲染: 第{CurrentPage}/{TotalPages}页, {page.Count}个服务器加入 ObservableCollection");
        foreach (var s in page)
        {
            DebugLogger.Info("ServersVM", $"  绑定数据: [{s.Name}] 在线={s.IsOnline}, Ping={s.Ping}, 版本={s.Version ?? "null"}, 玩家={s.OnlinePlayers}/{s.MaxPlayers}, MOTD={s.Motd?.Length ?? 0}字符, 图标={s.IconPath ?? "null"}, StatusText={s.StatusText}, FormattedPing={s.FormattedPing}, FormattedPlayers={s.FormattedPlayers}, FormattedVersion={s.FormattedVersion}");
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
            ApplyPaging();
        }
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
            ApplyPaging();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Load();
    }

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        try
        {
            IsRefreshing = true;
            _notificationService.Show("提示", "正在检测服务器状态...", NotificationType.Info);

            var allServers = LauncherConfig.Load().Servers ?? new List<ServerInfo>();
            DebugLogger.Info("ServersVM", $"开始查询 {allServers.Count} 个服务器状态");
            var updated = await ServerManager.Instance.QueryServersAsync(allServers);
            DebugLogger.Info("ServersVM", $"查询完成，{updated.Count} 个服务器返回结果");

            var config = LauncherConfig.Load();
            foreach (var updatedServer in updated)
            {
                var existing = config.Servers?.FirstOrDefault(s =>
                    (!string.IsNullOrEmpty(s.Id) && s.Id == updatedServer.Id) ||
                    (s.Name == updatedServer.Name && s.Address == updatedServer.Address));
                if (existing != null)
                {
                    existing.IconPath = updatedServer.IconPath;
                }
            }
            config.Save();

            foreach (var updatedServer in updated)
            {
                var inMemory = _allFilteredServers.FirstOrDefault(s =>
                    (!string.IsNullOrEmpty(s.Id) && s.Id == updatedServer.Id) ||
                    (s.Name == updatedServer.Name && s.Address == updatedServer.Address));
                if (inMemory != null)
                {
                    inMemory.IsOnline = updatedServer.IsOnline;
                    inMemory.Ping = updatedServer.Ping;
                    inMemory.LastPingTime = updatedServer.LastPingTime;
                    inMemory.Version = updatedServer.Version;
                    inMemory.OnlinePlayers = updatedServer.OnlinePlayers;
                    inMemory.MaxPlayers = updatedServer.MaxPlayers;
                    inMemory.Motd = updatedServer.Motd;
                    inMemory.IconPath = updatedServer.IconPath;
                    DebugLogger.Info("ServersVM", $"更新内存对象: {inMemory.Name} - 在线:{inMemory.IsOnline}, Ping:{inMemory.Ping}, 版本:{inMemory.Version ?? "null"}, 玩家:{inMemory.OnlinePlayers}/{inMemory.MaxPlayers}, MOTD:{inMemory.Motd?.Length ?? 0}字符, 图标:{inMemory.IconPath ?? "null"}");
                }
                else
                {
                    DebugLogger.Warn("ServersVM", $"内存中未找到匹配: {updatedServer.Name} ({updatedServer.Address})");
                }
            }

            ApplyPaging();

            var online = updated.Count(s => s.IsOnline);
            _notificationService.Show("检测完成", $"共 {updated.Count} 个服务器，{online} 个在线", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"刷新服务器状态失败: {ex.Message}", NotificationType.Error);
            DebugLogger.Error("ServersVM", $"刷新状态失败: {ex}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public async Task ActivateAsync()
    {
        if (!IsRefreshing && !IsConnecting)
        {
            await RefreshStatusAsync();
        }
        StartAutoRefresh();
    }

    public void StartAutoRefresh()
    {
        StopAutoRefresh();
        _autoRefreshCts = new CancellationTokenSource();
        _ = AutoRefreshLoopAsync(_autoRefreshCts.Token);
    }

    public void StopAutoRefresh()
    {
        _autoRefreshCts?.Cancel();
        _autoRefreshCts?.Dispose();
        _autoRefreshCts = null;
    }

    private async Task AutoRefreshLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), ct);
                if (!ct.IsCancellationRequested && !IsRefreshing && !IsConnecting)
                {
                    await RefreshStatusAsync();
                }
            }
            catch (TaskCanceledException) { break; }
            catch (Exception ex)
            {
                DebugLogger.Error("ServersVM", $"自动刷新异常: {ex.Message}");
            }
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        CurrentPage = 1;
        _ = FilterAsync();
    }

    private async Task FilterAsync()
    {
        try
        {
            var config = LauncherConfig.Load();
            var servers = config.Servers ?? new List<ServerInfo>();

            await Task.Run(() =>
            {
                var filtered = servers.AsEnumerable();

                if (!string.IsNullOrEmpty(SearchText))
                {
                    filtered = filtered.Where(s =>
                        s.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                        s.Address.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                        (s.Motd != null && s.Motd.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                        (s.Version != null && s.Version.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));
                }

                Dispatcher.UIThread.Post(() =>
                {
                    _allFilteredServers = filtered.ToList();
                    DebugLogger.Info("ServersVM", $"筛选完成: {_allFilteredServers.Count} 个服务器" + (string.IsNullOrEmpty(SearchText) ? "" : $" (搜索: {SearchText})"));
                    UpdatePagination();
                    ApplyPaging();
                    IsEmpty = _allFilteredServers.Count == 0;
                });
            });
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ServersVM", $"筛选失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void AddServer()
    {
        AddServerDialog = new ServerEditViewModel();
        IsAddServerDialogOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmAddServerAsync()
    {
        if (!ValidateServerInput(AddServerDialog)) return;

        try
        {
            var config = LauncherConfig.Load();
            var servers = config.Servers ?? new List<ServerInfo>();

            if (servers.Any(s => s.Name == AddServerDialog.Name.Trim() && s.Address == AddServerDialog.Address.Trim()))
            {
                _notificationService.Show("错误", "已存在相同名称和地址的服务器", NotificationType.Error);
                return;
            }

            var newServer = new ServerInfo
            {
                Name = AddServerDialog.Name.Trim(),
                Address = AddServerDialog.Address.Trim(),
                Port = AddServerDialog.Port,
                Notes = string.IsNullOrWhiteSpace(AddServerDialog.Notes) ? null : AddServerDialog.Notes.Trim()
            };

            servers.Add(newServer);
            config.Servers = servers;
            config.Save();

            IsAddServerDialogOpen = false;
            _notificationService.Show("成功", "服务器已添加", NotificationType.Success);
            DebugLogger.Info("ServersVM", $"添加服务器: {newServer.Name} ({newServer.FullAddress})");
            Load();
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"添加服务器失败: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private void CancelAddServer()
    {
        IsAddServerDialogOpen = false;
    }

    [RelayCommand]
    private void EditServer(ServerInfo? server)
    {
        if (server == null) return;

        EditServerDialog = new ServerEditViewModel
        {
            Name = server.Name,
            Address = server.Address,
            Port = server.Port,
            Notes = server.Notes
        };
        SelectedServer = server;
        IsEditServerDialogOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmEditServerAsync()
    {
        if (!ValidateServerInput(EditServerDialog)) return;
        if (SelectedServer == null) return;

        try
        {
            var config = LauncherConfig.Load();
            var servers = config.Servers ?? new List<ServerInfo>();

            var serverToEdit = FindServer(servers, SelectedServer);
            if (serverToEdit != null)
            {
                serverToEdit.Name = EditServerDialog.Name.Trim();
                serverToEdit.Address = EditServerDialog.Address.Trim();
                serverToEdit.Port = EditServerDialog.Port;
                serverToEdit.Notes = string.IsNullOrWhiteSpace(EditServerDialog.Notes) ? null : EditServerDialog.Notes.Trim();
            }

            config.Servers = servers;
            config.Save();

            IsEditServerDialogOpen = false;
            SelectedServer = null;
            _notificationService.Show("成功", "服务器已更新", NotificationType.Success);
            DebugLogger.Info("ServersVM", $"编辑服务器: {serverToEdit?.Name}");
            Load();
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"更新服务器失败: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private void CancelEditServer()
    {
        IsEditServerDialogOpen = false;
        SelectedServer = null;
    }

    [RelayCommand]
    private async Task DeleteServerAsync(ServerInfo? server)
    {
        if (server == null) return;

        var result = await _dialogService.ShowQuestion("确认删除",
            $"确定要删除服务器「{server.Name}」吗？\n\n此操作不可恢复。",
            DialogButtons.YesNo);

        if (result != DialogResult.Yes) return;

        try
        {
            var config = LauncherConfig.Load();
            var servers = config.Servers ?? new List<ServerInfo>();
            servers.RemoveAll(s => MatchServer(s, server));
            config.Servers = servers;
            config.Save();

            _notificationService.Show("成功", "服务器已删除", NotificationType.Success);
            DebugLogger.Info("ServersVM", $"删除服务器: {server.Name}");
            Load();
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"删除服务器失败: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task ConnectServerAsync(ServerInfo? server)
    {
        if (server == null) return;

        var config = LauncherConfig.Load();

        if (string.IsNullOrEmpty(config.SelectedVersion))
        {
            _notificationService.Show("无法连接", "请先在主页选择一个游戏版本", NotificationType.Warning);
            return;
        }

        if (string.IsNullOrEmpty(config.SelectedAccountId))
        {
            _notificationService.Show("无法连接", "请先在主页选择一个账号", NotificationType.Warning);
            return;
        }

        var accounts = AccountService.Instance.GetAllAccounts();
        if (accounts == null)
        {
            _notificationService.Show("无法连接", "无法获取账号列表", NotificationType.Error);
            return;
        }

        var account = accounts.FirstOrDefault(a => a.Id == config.SelectedAccountId);
        if (account == null)
        {
            _notificationService.Show("无法连接", "所选账号不存在，请重新选择", NotificationType.Error);
            return;
        }

        if (account.IsTokenExpired() && account.Type != AccountType.Offline)
        {
            var refreshResult = await _dialogService.ShowQuestion("令牌已过期",
                $"账号 {account.Username} 的登录令牌已过期。\n\n需要重新登录后才能连接服务器。",
                DialogButtons.OKCancel);
            if (refreshResult != DialogResult.OK) return;
        }

        try
        {
            IsConnecting = true;
            var launchCts = new CancellationTokenSource();

            var notifId = _notificationService.Show("正在连接",
                $"正在启动 Minecraft 并连接到 {server.FullAddress}...",
                NotificationType.Progress, cts: launchCts);

            var success = await GameLauncher.LaunchAndConnectServerAsync(
                config.SelectedVersion,
                account,
                config,
                server.Address,
                server.Port,
                (progress) => _notificationService.Update(notifId, progress),
                null,
                (exitCode) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _notificationService.Show("游戏退出",
                            $"游戏已退出，退出代码: {exitCode}",
                            exitCode == 0 ? NotificationType.Info : NotificationType.Warning);
                    });
                },
                launchCts.Token);

            _notificationService.Remove(notifId);

            if (success)
            {
                _notificationService.Show("连接成功",
                    $"已启动 Minecraft 并连接到 {server.Name}",
                    NotificationType.Success);
                DebugLogger.Info("ServersVM", $"连接服务器成功: {server.FullAddress}");

                if (config.CloseAfterLaunch)
                {
                    Environment.Exit(0);
                }
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("连接失败", $"无法连接到服务器: {ex.Message}", NotificationType.Error);
            DebugLogger.Error("ServersVM", $"连接服务器失败 [{server.FullAddress}]: {ex}");
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private async Task ExportServersAsync()
    {
        try
        {
            var config = LauncherConfig.Load();
            var servers = config.Servers ?? new List<ServerInfo>();

            if (servers.Count == 0)
            {
                _notificationService.Show("提示", "没有可导出的服务器", NotificationType.Info);
                return;
            }

            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;
            var storage = desktop.MainWindow?.StorageProvider;
            if (storage == null) return;

            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出服务器列表",
                SuggestedFileName = "servers_export.json",
                DefaultExtension = ".json",
                FileTypeChoices = new[] { new FilePickerFileType("JSON文件") { Patterns = new[] { "*.json" } } }
            });

            if (file == null) return;

            await using var stream = await file.OpenWriteAsync();
            var options = new JsonSerializerOptions { WriteIndented = true };
            await JsonSerializer.SerializeAsync(stream, servers, options);

            _notificationService.Show("导出成功", $"已导出 {servers.Count} 个服务器", NotificationType.Success);
            DebugLogger.Info("ServersVM", $"导出 {servers.Count} 个服务器");
        }
        catch (Exception ex)
        {
            _notificationService.Show("导出失败", ex.Message, NotificationType.Error);
            DebugLogger.Error("ServersVM", $"导出失败: {ex}");
        }
    }

    [RelayCommand]
    private async Task ImportServersAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;
            var storage = desktop.MainWindow?.StorageProvider;
            if (storage == null) return;

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入服务器列表",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("JSON文件") { Patterns = new[] { "*.json" } } }
            });

            if (files.Count == 0) return;

            var jsonText = await File.ReadAllTextAsync(files[0].Path.LocalPath);
            List<ServerInfo>? imported;

            try
            {
                imported = JsonSerializer.Deserialize<List<ServerInfo>>(jsonText);
            }
            catch
            {
                _notificationService.Show("导入失败", "文件格式不正确，请选择有效的服务器列表JSON文件", NotificationType.Error);
                return;
            }

            if (imported == null || imported.Count == 0)
            {
                _notificationService.Show("导入失败", "文件中没有找到有效的服务器数据", NotificationType.Error);
                return;
            }

            var config = LauncherConfig.Load();
            var existing = config.Servers ?? new List<ServerInfo>();
            var addedCount = 0;
            var skippedCount = 0;

            foreach (var server in imported)
            {
                if (existing.Any(s => s.Name == server.Name && s.Address == server.Address))
                {
                    skippedCount++;
                    continue;
                }
                server.Id = Guid.NewGuid().ToString("N")[..8];
                existing.Add(server);
                addedCount++;
            }

            config.Servers = existing;
            config.Save();

            var msg = $"成功导入 {addedCount} 个服务器";
            if (skippedCount > 0) msg += $"，跳过 {skippedCount} 个重复";
            _notificationService.Show("导入完成", msg, NotificationType.Success);
            DebugLogger.Info("ServersVM", $"导入 {addedCount} 个服务器, 跳过 {skippedCount}");
            Load();
        }
        catch (Exception ex)
        {
            _notificationService.Show("导入失败", ex.Message, NotificationType.Error);
            DebugLogger.Error("ServersVM", $"导入失败: {ex}");
        }
    }

    [RelayCommand]
    private async Task QuickPingAsync(ServerInfo? server)
    {
        if (server == null) return;

        try
        {
            var (ping, _, _, _, _, _) = await ServerManager.Instance.QueryServerInfoAsync(server.Address, server.Port);

            server.Ping = ping;
            server.IsOnline = ping > 0;
            server.LastPingTime = DateTime.Now;

            var status = ping > 0 ? $"在线，延迟 {ping}ms" : "离线";
            _notificationService.Show(server.Name, status,
                ping > 0 ? NotificationType.Success : NotificationType.Warning);
        }
        catch (Exception ex)
        {
            _notificationService.Show("检测失败", ex.Message, NotificationType.Error);
        }
    }

    private bool ValidateServerInput(ServerEditViewModel dialog)
    {
        if (string.IsNullOrWhiteSpace(dialog.Name))
        {
            _notificationService.Show("验证失败", "服务器名称不能为空", NotificationType.Error);
            return false;
        }

        if (string.IsNullOrWhiteSpace(dialog.Address))
        {
            _notificationService.Show("验证失败", "服务器地址不能为空", NotificationType.Error);
            return false;
        }

        if (dialog.Address.Trim().Length > 255)
        {
            _notificationService.Show("验证失败", "服务器地址过长", NotificationType.Error);
            return false;
        }

        if (dialog.Port < 1 || dialog.Port > 65535)
        {
            _notificationService.Show("验证失败", "端口号必须在1-65535之间", NotificationType.Error);
            return false;
        }

        return true;
    }

    private static bool MatchServer(ServerInfo a, ServerInfo b)
    {
        if (!string.IsNullOrEmpty(a.Id) && !string.IsNullOrEmpty(b.Id) && a.Id == b.Id)
            return true;
        return a.Name == b.Name && a.Address == b.Address && a.Port == b.Port;
    }

    private static ServerInfo? FindServer(List<ServerInfo> list, ServerInfo target)
    {
        if (!string.IsNullOrEmpty(target.Id))
        {
            var byId = list.FirstOrDefault(s => s.Id == target.Id);
            if (byId != null) return byId;
        }
        return list.FirstOrDefault(s => s.Name == target.Name && s.Address == target.Address && s.Port == target.Port);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            StopAutoRefresh();
        }
        _disposed = true;
    }
}

public partial class ServerEditViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private int _port = 25565;

    [ObservableProperty]
    private string? _notes;
}
