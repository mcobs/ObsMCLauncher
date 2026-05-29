using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
    private LauncherConfig _config;

    [ObservableProperty]
    private ObservableCollection<MinecraftVersion> _allVersions = new();

    [ObservableProperty]
    private ObservableCollection<MinecraftVersion> _filteredVersions = new();

    [ObservableProperty]
    private ObservableCollection<Core.Services.Minecraft.InstalledVersion> _installedVersions = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _selectedTypeIndex = 1;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _installedVersionsCount;

    [ObservableProperty]
    private bool _isDetailOpen;

    [ObservableProperty]
    private ViewModelBase? _detailPage;

    [ObservableProperty]
    private ObservableCollection<GameDirectoryItem> _gameDirectories = new();

    [ObservableProperty]
    private bool _isSidebarOpen;

    [ObservableProperty]
    private bool _isRefreshingVersions;

    [ObservableProperty]
    private string _currentDirectoryPath = string.Empty;

    [ObservableProperty]
    private string _currentDirectoryDisplay = string.Empty;

    [ObservableProperty]
    private bool _isCurrentDirectoryValid = true;

    public InstanceViewModel InstanceViewModel { get; }

    public VersionDownloadViewModel(ObsMCLauncher.Core.Services.Ui.IDispatcher dispatcher, NotificationService notificationService)
    {
        _dispatcher = dispatcher;
        _notificationService = notificationService;
        _dialogService = NavigationStore.MainWindow?.Dialogs ?? new DialogService();
        _config = LauncherConfig.Load();

        InstanceViewModel = new InstanceViewModel(notificationService);

        LoadGameDirectories();

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
        _config = LauncherConfig.Load();
        RefreshCurrentDirectoryDisplay();
        var list = LocalVersionService.GetInstalledVersions(_config.GameDirectory);
        InstalledVersions = new ObservableCollection<Core.Services.Minecraft.InstalledVersion>(list);
        InstalledVersionsCount = list.Count;
    }

    private void RefreshCurrentDirectoryDisplay()
    {
        CurrentDirectoryPath = _config.GameDirectory;
        CurrentDirectoryDisplay = _config.GameDirectoryLocation switch
        {
            DirectoryLocation.AppData => OperatingSystem.IsWindows() ? "%APPDATA%\\.minecraft"
                : OperatingSystem.IsMacOS() ? "~/Library/Application Support/minecraft"
                : "~/.minecraft",
            DirectoryLocation.RunningDirectory => "运行目录\\.minecraft",
            _ => _config.CustomGameDirectory
        };
        IsCurrentDirectoryValid = Directory.Exists(_config.GameDirectory);
        UpdateDirectoryItemsSelection();
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarOpen = !IsSidebarOpen;
    }

    [RelayCommand]
    private async Task AddDirectoryAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null)
                return;

#pragma warning disable CS0618
            var dlg = new OpenFolderDialog { Title = "选择游戏目录 (.minecraft)" };
            var path = await dlg.ShowAsync(desktop.MainWindow);
#pragma warning restore CS0618

            if (!string.IsNullOrWhiteSpace(path))
            {
                if (!Directory.Exists(path))
                {
                    _notificationService.Show("目录无效", $"目录不存在: {path}", NotificationType.Warning);
                    return;
                }

                _config = LauncherConfig.Load();

                if (_config.CustomGameDirectories.Any(d => string.Equals(d, path, StringComparison.OrdinalIgnoreCase)))
                {
                    _notificationService.Show("重复添加", "该目录已在列表中", NotificationType.Info);
                    return;
                }

                string defaultDir = _config.GameDirectoryLocation switch
                {
                    DirectoryLocation.AppData => LauncherConfig.GetDefaultAppdataGameDirectory(),
                    _ => _config.CustomGameDirectory
                };

                if (string.Equals(path, defaultDir, StringComparison.OrdinalIgnoreCase))
                {
                    _notificationService.Show("无需添加", "该目录已是默认游戏目录", NotificationType.Info);
                    return;
                }

                _config.CustomGameDirectories.Add(path);
                _config.Save();

                LoadGameDirectories();
                _notificationService.Show("添加成功", $"游戏目录已添加: {Path.GetFileName(path)}", NotificationType.Success);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("添加失败", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task RemoveDirectoryAsync(GameDirectoryItem? item)
    {
        if (item == null) return;

        _config = LauncherConfig.Load();

        bool isCurrent = string.Equals(item.Path, _config.GameDirectory, StringComparison.OrdinalIgnoreCase);

        var confirmMsg = isCurrent && item.IsDefault
            ? $"要永久删除默认游戏目录吗？\n\n{item.Path}\n\n此目录是当前正在使用的默认目录，删除后系统将自动切换到下一个可用目录。此操作不可撤销！"
            : isCurrent
                ? $"要永久删除当前游戏目录吗？\n\n{item.Path}\n\n删除后系统将自动切换到下一个可用目录。此操作不可撤销！"
                : $"要永久删除此游戏目录吗？\n\n{item.Path}\n\n该目录及其所有内容将被永久删除。此操作不可撤销！";

        var result = await _dialogService.ShowQuestion("确认删除目录", confirmMsg);
        if (result != DialogResult.Yes) return;

        try
        {
            IsRefreshingVersions = true;

            string? nextDir = null;
            if (isCurrent)
            {
                nextDir = FindNextAvailableDirectory(item.Path);
            }

            if (Directory.Exists(item.Path))
            {
                var dirSize = await Task.Run(() => GetDirectorySize(item.Path));
                await Task.Run(() => Directory.Delete(item.Path, true));
                _notificationService.Show("目录已删除", $"释放 {FormatFileSize(dirSize)} 空间", NotificationType.Success);
            }

            _config = LauncherConfig.Load();
            _config.CustomGameDirectories.RemoveAll(d => string.Equals(d, item.Path, StringComparison.OrdinalIgnoreCase));

            if (isCurrent)
            {
                if (!string.IsNullOrEmpty(nextDir))
                {
                    _config.GameDirectoryLocation = DirectoryLocation.Custom;
                    _config.CustomGameDirectory = nextDir;
                }
                else
                {
                    _config.GameDirectoryLocation = DirectoryLocation.AppData;
                    _config.CustomGameDirectory = "";
                }
            }

            _config.Save();
            LoadGameDirectories();
            RefreshCurrentDirectoryDisplay();

            if (isCurrent)
            {
                await Task.Run(() =>
                {
                    var list = LocalVersionService.GetInstalledVersions(_config.GameDirectory);
                    _dispatcher.Post(() =>
                    {
                        InstalledVersions = new ObservableCollection<Core.Services.Minecraft.InstalledVersion>(list);
                        InstalledVersionsCount = list.Count;
                    });
                });

                var homeVm = NavigationStore.MainWindow?.NavItems
                    .FirstOrDefault(x => x.Title == "主页")?.Page as HomeViewModel;
                if (homeVm != null)
                {
                    _ = homeVm.LoadLocalAsync();
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            _notificationService.Show("权限不足", "无法删除该目录，请检查文件夹权限或以管理员身份运行", NotificationType.Error);
        }
        catch (IOException ex)
        {
            _notificationService.Show("删除失败", $"文件被占用或无法访问: {ex.Message}", NotificationType.Error);
        }
        catch (Exception ex)
        {
            _notificationService.Show("删除失败", ex.Message, NotificationType.Error);
        }
        finally
        {
            IsRefreshingVersions = false;
        }
    }

    private string? FindNextAvailableDirectory(string excludingPath)
    {
        _config = LauncherConfig.Load();
        var validDirs = _config.CustomGameDirectories
            .Where(d => !string.Equals(d, excludingPath, StringComparison.OrdinalIgnoreCase))
            .Where(Directory.Exists)
            .ToList();

        if (validDirs.Count > 0)
            return validDirs[0];

        var defaultDir = LauncherConfig.GetDefaultAppdataGameDirectory();
        if (Directory.Exists(defaultDir) && !string.Equals(defaultDir, excludingPath, StringComparison.OrdinalIgnoreCase))
            return defaultDir;

        return null;
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            var dirInfo = new DirectoryInfo(path);
            return dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f =>
            {
                try { return f.Length; }
                catch { return 0; }
            });
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            >= 1073741824 => $"{bytes / 1073741824.0:F2} GB",
            >= 1048576 => $"{bytes / 1048576.0:F1} MB",
            >= 1024 => $"{bytes / 1024.0:F0} KB",
            > 0 => $"{bytes} B",
            _ => "0 B"
        };
    }

    [RelayCommand]
    private async Task SwitchDirectoryAsync(GameDirectoryItem? item)
    {
        if (item == null) return;

        if (item.IsCurrent && !IsRefreshingVersions) return;

        try
        {
            if (!Directory.Exists(item.Path))
            {
                _notificationService.Show("目录无效", $"目录不存在: {item.Path}", NotificationType.Warning);
                item.IsValid = false;
                return;
            }

            IsRefreshingVersions = true;

            _config = LauncherConfig.Load();
            if (item.IsDefault)
            {
                _config.GameDirectoryLocation = DirectoryLocation.AppData;
                _config.CustomGameDirectory = "";
            }
            else
            {
                _config.GameDirectoryLocation = DirectoryLocation.Custom;
                _config.CustomGameDirectory = item.Path;
            }
            _config.Save();

            RefreshCurrentDirectoryDisplay();

            await Task.Run(() =>
            {
                var list = LocalVersionService.GetInstalledVersions(_config.GameDirectory);
                _dispatcher.Post(() =>
                {
                    InstalledVersions = new ObservableCollection<Core.Services.Minecraft.InstalledVersion>(list);
                    InstalledVersionsCount = list.Count;
                });
            });

            UpdateDirectoryItemsSelection();
            LoadGameDirectories();

            _notificationService.Show("目录已切换", $"当前游戏目录: {Path.GetFileName(item.Path)}", NotificationType.Success);

            var homeVm = NavigationStore.MainWindow?.NavItems
                .FirstOrDefault(x => x.Title == "主页")?.Page as HomeViewModel;
            if (homeVm != null)
            {
                _ = homeVm.LoadLocalAsync();
            }
        }
        catch (UnauthorizedAccessException)
        {
            _notificationService.Show("权限不足", "无法访问所选目录，请检查文件夹权限", NotificationType.Error);
        }
        catch (Exception ex)
        {
            _notificationService.Show("切换失败", ex.Message, NotificationType.Error);
        }
        finally
        {
            IsRefreshingVersions = false;
        }
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
        var detailVm = new VersionDetailViewModel(version, _dispatcher, _notificationService);
        detailVm.CloseRequested += () =>
        {
            IsDetailOpen = false;
            DetailPage = null;
        };
        DetailPage = detailVm;
        IsDetailOpen = true;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedTypeIndexChanged(int value) => ApplyFilters();

    private void ApplyFilters()
    {
        if (AllVersions == null) return;

        var filtered = AllVersions.Where(v =>
        {
            if (!string.IsNullOrEmpty(SearchText) && !v.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                return false;

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
            var notifId = _notificationService.Show("正在启动", $"正在检查 {version.Id} 完整性...", NotificationType.Progress, cts: launchCts);

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
                var missingCount = ObsMCLauncher.Core.Services.GameLauncher.MissingLibraries.Count;
                _notificationService.Update(notifId, $"正在补全 {missingCount} 个缺失依赖...", 0);

                try
                {
                    var (successCount, failedCount) = await ObsMCLauncher.Core.Services.LibraryDownloader.DownloadMissingLibrariesAsync(
                        config.GameDirectory,
                        version.Id,
                        ObsMCLauncher.Core.Services.GameLauncher.MissingLibraries,
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

            _notificationService.Update(notifId, "正在启动 Minecraft...");

            bool success = await ObsMCLauncher.Core.Services.GameLauncher.LaunchGameAsync(
                version.Id,
                account,
                config,
                (progress) => _notificationService.Update(notifId, progress),
                null,
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

    private void LoadGameDirectories()
    {
        _config = LauncherConfig.Load();
        var items = new ObservableCollection<GameDirectoryItem>();

        string defaultPath = _config.GameDirectoryLocation switch
        {
            DirectoryLocation.AppData => LauncherConfig.GetDefaultAppdataGameDirectory(),
            _ => _config.CustomGameDirectory
        };

        bool isUsingDefault = _config.GameDirectoryLocation == DirectoryLocation.AppData
            || _config.GameDirectoryLocation == DirectoryLocation.RunningDirectory;

        items.Add(new GameDirectoryItem
        {
            Path = defaultPath,
            DisplayName = isUsingDefault ? $".minecraft（默认）" : $".minecraft",
            IsDefault = true,
            IsCurrent = _config.GameDirectoryLocation == DirectoryLocation.AppData
                || _config.GameDirectoryLocation == DirectoryLocation.RunningDirectory
                || string.Equals(defaultPath, _config.CustomGameDirectory, StringComparison.OrdinalIgnoreCase),
            IsValid = Directory.Exists(defaultPath)
        });

        foreach (var dir in _config.CustomGameDirectories)
        {
            if (string.Equals(dir, defaultPath, StringComparison.OrdinalIgnoreCase)) continue;

            items.Add(new GameDirectoryItem
            {
                Path = dir,
                DisplayName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                IsDefault = false,
                IsCurrent = string.Equals(dir, _config.GameDirectory, StringComparison.OrdinalIgnoreCase),
                IsValid = Directory.Exists(dir)
            });
        }

        GameDirectories = items;
        RefreshCurrentDirectoryDisplay();
    }

    private void UpdateDirectoryItemsSelection()
    {
        foreach (var item in GameDirectories)
        {
            item.IsCurrent = string.Equals(item.Path, CurrentDirectoryPath, StringComparison.OrdinalIgnoreCase);
            item.IsValid = Directory.Exists(item.Path);
        }
    }
}

public partial class GameDirectoryItem : ObservableObject
{
    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private bool _isDefault;

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private bool _isValid = true;
}
