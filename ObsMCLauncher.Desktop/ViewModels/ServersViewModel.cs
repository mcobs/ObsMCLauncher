using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Desktop.ViewModels.Notifications;
using ObsMCLauncher.Desktop.ViewModels.Dialogs;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class ServersViewModel : ViewModelBase
{
    private readonly NotificationService _notificationService;

    [ObservableProperty]
    private ObservableCollection<ServerInfo> _servers = new();

    [ObservableProperty]
    private ObservableCollection<string> _groups = new();

    [ObservableProperty]
    private string? _selectedGroup;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmpty;

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

    public ServersViewModel(NotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            var config = LauncherConfig.Load();

            await Task.Run(() =>
            {
                var servers = config.Servers ?? new System.Collections.Generic.List<ServerInfo>();
                var groups = ServerManager.Instance.GetServerGroups(servers);
                groups.Insert(0, "全部分组");

                Dispatcher.UIThread.Post(() =>
                {
                    Servers.Clear();
                    foreach (var s in servers) Servers.Add(s);

                    Groups.Clear();
                    foreach (var g in groups) Groups.Add(g);
                    SelectedGroup = Groups.FirstOrDefault();

                    IsEmpty = Servers.Count == 0;
                });
            });
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"加载服务器列表失败: {ex.Message}", NotificationType.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        try
        {
            _notificationService.Show("提示", "正在刷新服务器状态...", NotificationType.Info);

            var serverList = Servers.ToList();
            var updated = await ServerManager.Instance.QueryServersAsync(serverList);

            Servers.Clear();
            foreach (var s in updated) Servers.Add(s);

            var config = LauncherConfig.Load();
            config.Servers = updated;
            config.Save();

            _notificationService.Show("成功", "服务器状态已刷新", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"刷新服务器状态失败: {ex.Message}", NotificationType.Error);
        }
    }

    partial void OnSelectedGroupChanged(string? value)
    {
        _ = FilterAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = FilterAsync();
    }

    private async Task FilterAsync()
    {
        try
        {
            var config = LauncherConfig.Load();
            var servers = config.Servers ?? new System.Collections.Generic.List<ServerInfo>();

            await Task.Run(() =>
            {
                var filtered = servers;

                if (!string.IsNullOrEmpty(SelectedGroup) && SelectedGroup != "全部分组")
                {
                    filtered = ServerManager.Instance.FilterByGroup(filtered, SelectedGroup);
                }

                if (!string.IsNullOrEmpty(SearchText))
                {
                    filtered = filtered
                        .Where(s => s.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                                   s.Address.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                Dispatcher.UIThread.Post(() =>
                {
                    Servers.Clear();
                    foreach (var s in filtered) Servers.Add(s);
                    IsEmpty = Servers.Count == 0;
                });
            });
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"筛选服务器失败: {ex.Message}", NotificationType.Error);
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
        if (string.IsNullOrWhiteSpace(AddServerDialog.Name) || string.IsNullOrWhiteSpace(AddServerDialog.Address))
        {
            _notificationService.Show("错误", "服务器名称和地址不能为空", NotificationType.Error);
            return;
        }

        try
        {
            var config = LauncherConfig.Load();
            var servers = config.Servers ?? new System.Collections.Generic.List<ServerInfo>();

            var newServer = new ServerInfo
            {
                Name = AddServerDialog.Name.Trim(),
                Address = AddServerDialog.Address.Trim(),
                Port = AddServerDialog.Port,
                Group = AddServerDialog.Group?.Trim()
            };

            // 检查是否已存在
            if (servers.Any(s => s.Name == newServer.Name))
            {
                _notificationService.Show("错误", "已存在同名服务器", NotificationType.Error);
                return;
            }

            servers.Add(newServer);
            config.Servers = servers;
            config.Save();

            IsAddServerDialogOpen = false;
            _notificationService.Show("成功", "服务器已添加", NotificationType.Success);
            await LoadAsync();
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
            Group = server.Group
        };
        SelectedServer = server;
        IsEditServerDialogOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmEditServerAsync()
    {
        if (string.IsNullOrWhiteSpace(EditServerDialog.Name) || string.IsNullOrWhiteSpace(EditServerDialog.Address))
        {
            _notificationService.Show("错误", "服务器名称和地址不能为空", NotificationType.Error);
            return;
        }

        if (SelectedServer == null) return;

        try
        {
            var config = LauncherConfig.Load();
            var servers = config.Servers ?? new System.Collections.Generic.List<ServerInfo>();

            var serverToEdit = servers.FirstOrDefault(s => s.Name == SelectedServer.Name && s.Address == SelectedServer.Address);
            if (serverToEdit != null)
            {
                serverToEdit.Name = EditServerDialog.Name.Trim();
                serverToEdit.Address = EditServerDialog.Address.Trim();
                serverToEdit.Port = EditServerDialog.Port;
                serverToEdit.Group = EditServerDialog.Group?.Trim();
            }

            config.Servers = servers;
            config.Save();

            IsEditServerDialogOpen = false;
            _notificationService.Show("成功", "服务器已更新", NotificationType.Success);
            await LoadAsync();
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

        try
        {
            var config = LauncherConfig.Load();
            var servers = config.Servers ?? new System.Collections.Generic.List<ServerInfo>();
            servers.RemoveAll(s => s.Name == server.Name && s.Address == server.Address);
            config.Servers = servers;
            config.Save();

            _notificationService.Show("成功", "服务器已删除", NotificationType.Success);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"删除服务器失败: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private void ConnectServer(ServerInfo? server)
    {
        if (server == null) return;
        _notificationService.Show("提示", $"连接到服务器: {server.FullAddress}\n（此功能待实现）", NotificationType.Info);
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
    private string? _group;
}
