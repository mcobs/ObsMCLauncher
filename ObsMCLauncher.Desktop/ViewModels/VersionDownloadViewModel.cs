using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Minecraft;
using ObsMCLauncher.Core.Services.Installers;
using ObsMCLauncher.Core.Services.Ui;
using ObsMCLauncher.Desktop.ViewModels.Notifications;
using ObsMCLauncher.Desktop.ViewModels.Dialogs;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class VersionDownloadViewModel : ViewModelBase
{
    private readonly ObsMCLauncher.Core.Services.Ui.IDispatcher _dispatcher;
    private readonly NotificationService _notificationService;
    private readonly DialogService _dialogService;

    [ObservableProperty]
    private ObservableCollection<MinecraftVersion> _allVersions = new();

    [ObservableProperty]
    private ObservableCollection<MinecraftVersion> _filteredVersions = new();

    [ObservableProperty]
    private ObservableCollection<Core.Services.Minecraft.InstalledVersion> _installedVersions = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _selectedTypeIndex = 1; // 默认正式版

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _installedVersionsCount;

    [ObservableProperty]
    private bool _isDetailOpen;

    [ObservableProperty]
    private ViewModelBase? _detailPage;

    public InstanceViewModel InstanceViewModel { get; }

    public VersionDownloadViewModel(ObsMCLauncher.Core.Services.Ui.IDispatcher dispatcher, NotificationService notificationService)
    {
        _dispatcher = dispatcher;
        _notificationService = notificationService;
        _dialogService = NavigationStore.MainWindow?.Dialogs ?? new DialogService();

        InstanceViewModel = new InstanceViewModel(notificationService);

        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        await LoadOnlineVersionsAsync();
        RefreshInstalled();
    }

    [RelayCommand]
    private async Task LoadOnlineVersionsAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            var manifest = await MinecraftVersionService.GetVersionListAsync();
            if (manifest != null)
            {
                AllVersions = new ObservableCollection<MinecraftVersion>(manifest.Versions);
                ApplyFilters();
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("加载失败", $"无法获取版本列表: {ex.Message}", NotificationType.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void RefreshInstalled()
    {
        var config = LauncherConfig.Load();
        var list = LocalVersionService.GetInstalledVersions(config.GameDirectory);
        InstalledVersions = new ObservableCollection<Core.Services.Minecraft.InstalledVersion>(list);
        InstalledVersionsCount = list.Count;
    }

    [RelayCommand]
    private void OpenInstance(ObsMCLauncher.Core.Services.Minecraft.InstalledVersion? version)
    {
        if (version == null) return;
        InstanceViewModel.SetVersion(version);
    }

    [RelayCommand]
    private async Task RefreshOnline()
    {
        await LoadOnlineVersionsAsync();
    }

    [RelayCommand]
    private void OpenDetail(MinecraftVersion version)
    {
        var main = NavigationStore.MainWindow;
        if (main == null) return;

        // 创建详情页 ViewModel
        var detailVm = new VersionDetailViewModel(version, _dispatcher, _notificationService);
        
        // 查找是否已存在“版本详情”导航项
        var existing = main.NavItems.FirstOrDefault(x => x.Title == "版本详情");
        if (existing != null)
        {
            // 如果已存在，从集合中移除（为了更新为新版本的详情）
            main.NavItems.Remove(existing);
        }

        // 创建新导航项并加入集合（必须加入集合，否则会被 ListBox 重置为 null）
        var newItem = new NavItemViewModel("版本详情", detailVm);
        main.NavItems.Add(newItem);
        main.SelectedNavItem = newItem;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedTypeIndexChanged(int value) => ApplyFilters();

    private void ApplyFilters()
    {
        if (AllVersions == null) return;

        var filtered = AllVersions.Where(v =>
        {
            // 搜索过滤
            if (!string.IsNullOrEmpty(SearchText) && !v.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                return false;

            // 类型过滤
            return SelectedTypeIndex switch
            {
                1 => v.Type == "release",
                2 => v.Type == "snapshot",
                3 => v.Type != "release" && v.Type != "snapshot",
                _ => true
            };
        }).Take(50).ToList();

        FilteredVersions = new ObservableCollection<MinecraftVersion>(filtered);
    }

    [RelayCommand]
    private void SelectInstalled(Core.Services.Minecraft.InstalledVersion version)
    {
        try
        {
            LocalVersionService.SetSelectedVersion(version.Id);
            
            var config = LauncherConfig.Load();
            config.SelectedVersion = version.Id;
            config.Save();

            RefreshInstalled();
            _notificationService.Show("版本选择", $"已选择版本: {version.Id}", NotificationType.Success);
            
            if (NavigationStore.MainWindow?.NavItems.FirstOrDefault(x => x.Title == "主页")?.Page is HomeViewModel homeVm)
            {
                _ = homeVm.LoadLocalAsync();
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("选择失败", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private void ManageInstance(Core.Services.Minecraft.InstalledVersion version)
    {
        _notificationService.Show("版本管理", $"正在打开 {version.Id} 的管理页面（迁移中）...", NotificationType.Info);
    }

    [RelayCommand]
    private async Task QuickLaunch(Core.Services.Minecraft.InstalledVersion version)
    {
        var launchCts = new System.Threading.CancellationTokenSource();
        var account = ObsMCLauncher.Core.Services.Accounts.AccountService.Instance.GetAllAccounts()
            .FirstOrDefault(a => a.IsDefault) ?? ObsMCLauncher.Core.Services.Accounts.AccountService.Instance.GetAllAccounts().FirstOrDefault();

        if (account == null)
        {
            _notificationService.Show("无法启动", "请先在账号管理中添加账号", NotificationType.Warning);
            return;
        }

        try
        {
            var config = LauncherConfig.Load();
            // 将 launchCts 绑定到通知，实现点击关闭即取消
            var notifId = _notificationService.Show("正在启动", $"正在检查 {version.Id} 完整性...", NotificationType.Progress, cts: launchCts);

            // 1. 检查完整性
            bool hasIssue = await ObsMCLauncher.Core.Services.GameLauncher.CheckGameIntegrityAsync(
                version.Id,
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
                _notificationService.Show("缺少依赖", "请先在版本详情中修复依赖", NotificationType.Error);
                _notificationService.Remove(notifId);
                return;
            }

            _notificationService.Update(notifId, "正在启动 Minecraft...");

            // 2. 正式启动
            bool success = await ObsMCLauncher.Core.Services.GameLauncher.LaunchGameAsync(
                version.Id,
                account,
                config,
                (progress) => _notificationService.Update(notifId, progress),
                null, // 快速启动暂不显示日志窗口
                (exitCode) =>
                {
                    _dispatcher.Post(() =>
                        _notificationService.Show(
                            "游戏退出",
                            $"版本 {version.Id} 已退出 ({exitCode})",
                            exitCode == 0 ? NotificationType.Info : NotificationType.Warning));
                },
                launchCts.Token);

            _notificationService.Remove(notifId);

            if (!success)
            {
                _notificationService.Show("启动失败", ObsMCLauncher.Core.Services.GameLauncher.LastError ?? "请检查 Java 配置", NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("启动异常", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteInstalled(Core.Services.Minecraft.InstalledVersion version)
    {
        var result = await _dialogService.ShowQuestion("确认删除", $"确定要删除版本 {version.Id}吗？\n此操作不可恢复。");
        if (result != DialogResult.Yes) return;

        try
        {
            if (LocalVersionService.DeleteVersion(version.Path))
            {
                RefreshInstalled();
                _notificationService.Show("删除成功", $"版本 {version.Id} 已删除", NotificationType.Success);
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowError("删除失败", ex.Message);
        }
    }
}
