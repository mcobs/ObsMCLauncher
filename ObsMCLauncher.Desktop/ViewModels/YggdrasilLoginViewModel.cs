using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Services.Accounts;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class YggdrasilLoginViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isViewLoading = true;

    [ObservableProperty]
    private bool _isLoggingIn;

    [ObservableProperty]
    private string _statusMessage = "正在准备...";

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _showServerManagement;

    [ObservableProperty]
    private bool _showProfileSelection;

    [ObservableProperty]
    private ObservableCollection<ProfileItem> _availableProfiles = new();

    [ObservableProperty]
    private ProfileItem? _selectedProfile;

    [ObservableProperty]
    private ObservableCollection<ObsMCLauncher.Core.Models.YggdrasilServer> _servers = new();

    [ObservableProperty]
    private ObsMCLauncher.Core.Models.YggdrasilServer? _selectedServer;

    [ObservableProperty]
    private string _newServerName = string.Empty;

    [ObservableProperty]
    private string _newServerUrl = string.Empty;

    private string? _pendingAccessToken;
    private string? _pendingClientToken;

    public Action<ObsMCLauncher.Core.Models.GameAccount?>? OnLoginCompleted { get; set; }

    public YggdrasilLoginViewModel()
    {
        LoadServers();
    }

    [RelayCommand]
    private void LoadServers()
    {
        Servers.Clear();

        foreach (var s in YggdrasilServerService.Instance.GetAllServers())
        {
            Servers.Add(s);
        }

        SelectedServer = Servers.FirstOrDefault();
        IsViewLoading = false;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (SelectedServer == null) return;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password)) return;

        IsLoggingIn = true;
        StatusMessage = "正在登录...";

        try
        {
            var auth = new YggdrasilAuthService();
            auth.OnProgressUpdate = msg => StatusMessage = msg;

            var account = await auth.LoginAsync(SelectedServer, Username, Password);
            if (account == null)
            {
                StatusMessage = "登录失败";
                return;
            }

            OnLoginCompleted?.Invoke(account);
            StatusMessage = "登录成功";
        }
        catch (MultipleProfilesException ex)
        {
            _pendingAccessToken = ex.AccessToken;
            _pendingClientToken = ex.ClientToken;

            AvailableProfiles.Clear();
            foreach (var profile in ex.Profiles)
            {
                AvailableProfiles.Add(new ProfileItem { Id = profile.id, Name = profile.name });
            }

            SelectedProfile = AvailableProfiles.FirstOrDefault();
            ShowProfileSelection = true;
            StatusMessage = $"请选择角色 ({ex.Profiles.Count} 个可选)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"登录失败: {ex.Message}";
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmProfileAsync()
    {
        var selected = AvailableProfiles.FirstOrDefault(p => p.IsSelected);
        if (selected == null || SelectedServer == null) return;
        if (string.IsNullOrEmpty(_pendingAccessToken) || string.IsNullOrEmpty(_pendingClientToken)) return;

        IsLoggingIn = true;
        StatusMessage = "正在完成登录...";

        try
        {
            await Task.Run(() =>
            {
                var auth = new YggdrasilAuthService();
                var account = auth.CreateAccountFromProfile(
                    SelectedServer,
                    selected.Id,
                    selected.Name,
                    _pendingAccessToken,
                    _pendingClientToken);

                ShowProfileSelection = false;
                OnLoginCompleted?.Invoke(account);
                StatusMessage = "登录成功";
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"创建账号失败: {ex.Message}";
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    [RelayCommand]
    private void CancelProfileSelection()
    {
        ShowProfileSelection = false;
        AvailableProfiles.Clear();
        SelectedProfile = null;
        _pendingAccessToken = null;
        _pendingClientToken = null;
        StatusMessage = "已取消选择";
    }

    [RelayCommand]
    private void SelectProfile(ProfileItem? profile)
    {
        if (profile == null) return;
        SelectedProfile = profile;
    }

    [RelayCommand]
    private void ToggleServerManagement()
    {
        ShowServerManagement = !ShowServerManagement;
        if (!ShowServerManagement)
        {
            LoadServers();
        }
    }

    [RelayCommand]
    private void AddServer()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(NewServerName) || string.IsNullOrWhiteSpace(NewServerUrl))
            {
                StatusMessage = "名称和地址不能为空";
                return;
            }

            YggdrasilServerService.Instance.AddServer(NewServerName, NewServerUrl);
            NewServerName = string.Empty;
            NewServerUrl = string.Empty;
            LoadServers();
            StatusMessage = "添加成功";
        }
        catch (Exception ex)
        {
            StatusMessage = $"添加失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void DeleteServer(ObsMCLauncher.Core.Models.YggdrasilServer server)
    {
        if (server.IsBuiltIn) return;
        try
        {
            YggdrasilServerService.Instance.DeleteServer(server.Id);
            LoadServers();
            StatusMessage = "已删除服务器";
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除失败: {ex.Message}";
        }
    }
}

public partial class ProfileItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}
