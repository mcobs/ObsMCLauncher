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
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Core.Services.Installers;
using ObsMCLauncher.Core.Services.Ui;
using ObsMCLauncher.Desktop.Services;
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

    private int _displayCount;
    private const int VersionPageSize = 50;
    private bool _isLoadingMore;
    private List<MinecraftVersion> _fullFilteredVersions = new();

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

    [ObservableProperty]
    private ObservableCollection<object> _flatGroupItems = new();

    /// <summary>
    /// 已安装版本 Id 集合（用于在线版本列表标记"已安装"）
    /// </summary>
    [ObservableProperty]
    private HashSet<string> _installedVersionIds = new();

    public InstanceViewModel InstanceViewModel { get; }

    public VersionDownloadViewModel(ObsMCLauncher.Core.Services.Ui.IDispatcher dispatcher, NotificationService notificationService)
    {
        _dispatcher = dispatcher;
        _notificationService = notificationService;
        _dialogService = NavigationStore.MainWindow?.Dialogs ?? new DialogService();
        _config = LauncherConfig.Load();

        InstanceViewModel = new InstanceViewModel(notificationService);

        LoadGameDirectories();

        _ = InitializeAsync();
    }

    private void BuildGroupSections()
    {
        var groups = Core.Services.VersionGroupService.GetAllGroups();
        var allVersions = InstalledVersions.ToList();
        // 平铺列表：分组头 + 版本项混合，支持虚拟化
        var items = new ObservableCollection<object>();

        foreach (var group in groups)
        {
            // "自动"分组不在界面显示，其下版本自动归类到其他分组
            if (group.Id == VersionGroup.AutoGroupId) continue;

            var versions = allVersions.Where(v =>
            {
                var displayGroup = Core.Services.VersionGroupService.GetDisplayGroupId(v);
                return displayGroup == group.Id;
            }).ToList();

            // 空分组不显示
            if (versions.Count == 0) continue;

            var description = group.Id switch
            {
                VersionGroup.ModdableGroupId => "安装了Mod加载器的版本",
                VersionGroup.CommonGroupId => "30天内游玩过的版本",
                VersionGroup.UncommonGroupId => "超过30天未游玩或从未游玩的版本",
                _ => ""
            };

            items.Add(new GroupSectionHeader
            {
                GroupId = group.Id,
                GroupName = group.Name,
                IsSystem = group.IsSystem,
                Description = description,
                HasDescription = !string.IsNullOrEmpty(description),
                VersionCount = versions.Count
            });

            foreach (var version in versions)
            {
                items.Add(version);
            }
        }

        FlatGroupItems = items;
    }

    private async Task InitializeAsync()
    {
        try
        {
            await LoadOnlineVersionsAsync();
            RefreshInstalled();
        }
        catch (Exception ex)
        {
            DebugLogger.Error("VersionDownloadVM", $"初始化失败: {ex.Message}");
        }
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
        InstalledVersionIds = new HashSet<string>(list.Select(v => v.Id));
        BuildGroupSections();
    }

    private void RefreshCurrentDirectoryDisplay()
    {
        CurrentDirectoryPath = _config.GameDirectory;
        CurrentDirectoryDisplay = _config.GameDirectoryLocation switch
        {
            DirectoryLocation.AppData => OperatingSystem.IsWindows() ? "%APPDATA%\\.minecraft"
                : OperatingSystem.IsMacOS() ? "~/Library/Application Support/minecraft"
                : "~/.minecraft",
            DirectoryLocation.RunningDirectory => "启动器目录\\.minecraft",
            _ => _config.CustomGameDirectory
        };

        if (!Directory.Exists(_config.GameDirectory))
        {
            Directory.CreateDirectory(_config.GameDirectory);
        }

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

                string officialDir = LauncherConfig.GetDefaultAppdataGameDirectory();
                string runningDir = Path.Combine(VersionInfo.GetAppBaseDirectory(), ".minecraft");

                if (string.Equals(path, officialDir, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(path, runningDir, StringComparison.OrdinalIgnoreCase))
                {
                    _notificationService.Show("无需添加", "该目录已是默认游戏目录，可以直接在列表中切换使用", NotificationType.Info);
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

        if (!item.IsDeletable)
        {
            _notificationService.Show("无法移除", "默认目录不可移除。如需使用其他目录，请添加自定义目录后切换。", NotificationType.Warning);
            return;
        }

        _config = LauncherConfig.Load();

        bool isCurrent = string.Equals(item.Path, _config.GameDirectory, StringComparison.OrdinalIgnoreCase);

        // 只是"从列表里去掉"：磁盘上的文件夹原样保留，存档 / mods 不会被误删。
        var confirmMsg = isCurrent
            ? $"要把该目录从列表中移除吗？\n\n{item.Path}\n\n这是当前正在使用的目录，移除后系统将自动切换到下一个可用目录。\n\n仅从列表移除，不会删除磁盘上的文件夹。"
            : $"要把该目录从列表中移除吗？\n\n{item.Path}\n\n仅从列表移除，不会删除磁盘上的文件夹。";

        var result = await _dialogService.ShowQuestion("从列表移除目录", confirmMsg);
        if (result != DialogResult.Yes) return;

        try
        {
            IsRefreshingVersions = true;
            RemoveDirectoryFromList(item, isCurrent);
        }
        catch (Exception ex)
        {
            Core.Utils.DebugLogger.Error("Directory", $"移除目录失败: {item.Path} - {ex.Message}");
            _notificationService.Show("移除失败", ex.Message, NotificationType.Error);
        }
        finally
        {
            IsRefreshingVersions = false;
        }
    }

    /// <summary>
    /// 仅从目录列表中移除，不删除实际文件夹
    /// </summary>
    private void RemoveDirectoryFromList(GameDirectoryItem item, bool isCurrent)
    {
        _config = LauncherConfig.Load();

        // ⚠️ nextDir 必须在同一份副本上算出来：FindNextAvailableDirectory 早先会自己
        // LauncherConfig.Load() 并写回 _config 字段，把下面这行 RemoveAll 的结果整个丢掉 ——
        // 于是"移除当前正在使用的目录"这一项永远删不掉（列表里那行原地复活、当前目录却已经切走）。
        var nextDir = isCurrent ? FindNextAvailableDirectory(_config.CustomGameDirectories, item.Path) : null;

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
            RefreshInstalled();
            var homeVm = NavigationStore.MainWindow?.Home;
            if (homeVm != null)
            {
                _ = homeVm.LoadLocalAsync();
            }
        }

        _notificationService.Show("已移除", "目录已从列表中移除（磁盘上的文件夹未删除）", NotificationType.Success);
    }

    /// <summary>
    /// 从给定目录列表里挑一个仍然可用的（排除 excludingPath）。
    /// 刻意做成静态、只读传入列表：调用方可能刚在同一份配置副本上做过修改，
    /// 这里绝不能再去 Load 一份新配置写回 <see cref="_config"/>（那会把改动覆盖掉）。
    /// </summary>
    private static string? FindNextAvailableDirectory(IReadOnlyList<string> customDirectories, string excludingPath)
    {
        var validDirs = customDirectories
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

    [RelayCommand]
    private async Task SwitchDirectoryAsync(GameDirectoryItem? item)
    {
        if (item == null) return;

        if (item.IsCurrent && !IsRefreshingVersions) return;

        try
        {
            if (!Directory.Exists(item.Path))
            {
                Directory.CreateDirectory(item.Path);
            }

            IsRefreshingVersions = true;

            _config = LauncherConfig.Load();
            if (item.IsDefault)
            {
                string officialDir = LauncherConfig.GetDefaultAppdataGameDirectory();

                if (string.Equals(item.Path, officialDir, StringComparison.OrdinalIgnoreCase))
                {
                    _config.GameDirectoryLocation = DirectoryLocation.AppData;
                }
                else
                {
                    _config.GameDirectoryLocation = DirectoryLocation.RunningDirectory;
                }
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

            var homeVm = NavigationStore.MainWindow?.Home;
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

    /// <summary>
    /// 导入本地整合包（.zip / .mrpack）。与「导出整合包」构成闭环：
    /// 先嗅探格式 → 让用户确认安装后的版本名 → 复制到 versions 下 → 交给 <see cref="ModpackInstallService"/>。
    /// </summary>
    [RelayCommand]
    private async Task ImportModpackAsync()
    {
        var storageProvider = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow?.StorageProvider;
        if (storageProvider == null)
            return;

        IReadOnlyList<Avalonia.Platform.Storage.IStorageFile> files;
        try
        {
            files = await storageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择整合包文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("整合包")
                    {
                        Patterns = new[] { "*.zip", "*.mrpack" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("所有文件")
                    {
                        Patterns = new[] { "*" }
                    }
                }
            });
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("VersionDownloadVM", $"选择整合包文件失败: {ex.Message}");
            return;
        }

        if (files.Count == 0)
            return;

        var sourcePath = files[0].Path.LocalPath;
        if (!File.Exists(sourcePath))
            return;

        // 先嗅探格式，别让用户填完名字才失败
        var archiveType = ModpackInstallService.DetectArchiveType(sourcePath);
        if (archiveType == ModpackArchiveType.Unknown)
        {
            await _dialogService.ShowWarning("无法导入",
                "这个压缩包里既没有 CurseForge 的 manifest.json，也没有 Modrinth 的 modrinth.index.json，"
                + "目录结构也不像手工整合包（没有 .minecraft/ 或 versions/ 前缀）。\n\n"
                + "请确认选择的是整合包文件本身，而不是解压后的文件夹或其它类型的压缩包。");
            return;
        }

        var formatLabel = archiveType switch
        {
            ModpackArchiveType.CurseForge => "CurseForge",
            ModpackArchiveType.Modrinth => "Modrinth",
            _ => "手工"
        };

        var defaultName = Path.GetFileNameWithoutExtension(sourcePath);
        var (dialogResult, versionName) = await _dialogService.ShowInputAsync(
            "导入整合包",
            $"已识别为 {formatLabel} 整合包。\n请输入安装后的版本名称：",
            defaultName,
            "版本名称");

        if (dialogResult != DialogResult.OK || string.IsNullOrWhiteSpace(versionName))
            return;

        versionName = versionName.Trim();

        CancellationTokenSource? cts = null;
        string? taskId = null;
        try
        {
            var config = LauncherConfig.Load();
            var versionsDir = Path.Combine(config.GameDirectory, "versions");
            Directory.CreateDirectory(versionsDir);

            // 复制到 versions 下再安装：原文件可能来自只读位置（U 盘、下载目录外的路径），
            // 也可能与安装流程共用同名文件，复制一份最省心
            var stagedPath = Path.Combine(versionsDir, Path.GetFileName(sourcePath));
            cts = new CancellationTokenSource();

            var task = Core.Services.Download.DownloadTaskManager.Instance.AddTask(
                $"导入整合包: {versionName}",
                Core.Services.Download.DownloadTaskType.Version,
                cts);
            taskId = task.Id;

            Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 0, "正在读取整合包...");
            await Task.Run(() => File.Copy(sourcePath, stagedPath, overwrite: true), cts.Token);

            Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 5, "正在安装整合包...");

            await ModpackInstallService.InstallModpackAsync(
                stagedPath,
                versionName,
                config.GameDirectory,
                (msg, progress) =>
                {
                    var total = 5 + (progress * 0.95);
                    Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(taskId, total, msg);
                });

            Core.Services.Download.DownloadTaskManager.Instance.CompleteTask(taskId);
            _notificationService.Show("导入完成", $"整合包「{versionName}」已安装成功", NotificationType.Success, 3);

            _config = LauncherConfig.Load();
            RefreshInstalled();
        }
        catch (OperationCanceledException)
        {
            if (taskId != null)
                Core.Services.Download.DownloadTaskManager.Instance.FailTask(taskId, "已取消");
            _notificationService.Show("已取消", "整合包导入已取消", NotificationType.Info);
        }
        catch (Exception ex)
        {
            if (taskId != null)
                Core.Services.Download.DownloadTaskManager.Instance.FailTask(taskId, ex.Message);
            _notificationService.Show("导入失败", ex.Message, NotificationType.Error);
            DebugLogger.Error("VersionDownloadVM", $"导入整合包失败: {ex}");
        }
        finally
        {
            cts?.Dispose();
        }
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

        _fullFilteredVersions = AllVersions.Where(v =>
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
        }).ToList();

        _displayCount = Math.Min(VersionPageSize, _fullFilteredVersions.Count);
        FilteredVersions = new ObservableCollection<MinecraftVersion>(_fullFilteredVersions.Take(_displayCount));
        OnPropertyChanged(nameof(ShowFilteredEmpty));
    }

    /// <summary>
    /// 在线版本列表空状态（非加载中且无匹配结果）
    /// </summary>
    public bool ShowFilteredEmpty => !IsLoading && _fullFilteredVersions.Count == 0;

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowFilteredEmpty));

    [RelayCommand]
    private void LoadMoreVersions()
    {
        if (_isLoadingMore || _displayCount >= _fullFilteredVersions.Count) return;
        _isLoadingMore = true;

        var newCount = Math.Min(_displayCount + VersionPageSize, _fullFilteredVersions.Count);
        var newItems = _fullFilteredVersions.Skip(_displayCount).Take(newCount - _displayCount);
        foreach (var item in newItems)
            FilteredVersions.Add(item);
        _displayCount = newCount;

        _isLoadingMore = false;
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
            
            if (NavigationStore.MainWindow?.Home is { } homeVm)
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

            var integrity = await ObsMCLauncher.Core.Services.GameLauncher.CheckGameIntegrityAsync(
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

            if (integrity.HasIssue && integrity.MissingLibraries.Count > 0)
            {
                var missingCount = integrity.MissingLibraries.Count;
                _notificationService.Update(notifId, $"正在补全 {missingCount} 个缺失依赖...", 0);

                try
                {
                    var (successCount, failedCount) = await ObsMCLauncher.Core.Services.LibraryDownloader.DownloadMissingLibrariesAsync(
                        config.GameDirectory,
                        version.Id,
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

            _notificationService.Update(notifId, "正在启动 Minecraft...");

            // 记录启动时刻：崩溃检测要靠它排除历史崩溃报告
            var launchStartedAt = DateTime.Now;

            var launchResult = await ObsMCLauncher.Core.Services.GameLauncher.LaunchGameAsync(
                version.Id,
                account,
                config,
                (progress) => _notificationService.Update(notifId, progress),
                null,
                (exitCode) =>
                {
                    CrashAnalysisPromptService.NotifyGameExit(version.Id, exitCode, launchStartedAt);
                    _dispatcher.Post(() =>
                        _notificationService.Show(
                            "游戏退出",
                            $"版本 {version.Id} 已退出 ({exitCode})",
                            exitCode == 0 ? NotificationType.Info : NotificationType.Warning));
                },
                launchCts.Token);

            _notificationService.Remove(notifId);

            if (!launchResult.Success)
            {
                _notificationService.Show("启动失败", string.IsNullOrEmpty(launchResult.ErrorMessage) ? "请检查 Java 配置" : launchResult.ErrorMessage, NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("启动异常", ex.Message, NotificationType.Error);
        }
        finally
        {
            launchCts.Dispose();
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

        string officialDir = LauncherConfig.GetDefaultAppdataGameDirectory();
        string runningDir = Path.Combine(VersionInfo.GetAppBaseDirectory(), ".minecraft");

        // 默认目录一：官方目录（平台自适应）
        string officialDisplay;
        if (OperatingSystem.IsWindows())
            officialDisplay = ".minecraft（官方目录）";
        else if (OperatingSystem.IsMacOS())
            officialDisplay = "minecraft（官方目录）";
        else
            officialDisplay = ".minecraft（官方目录）";

        if (!Directory.Exists(officialDir))
        {
            Directory.CreateDirectory(officialDir);
        }

        items.Add(new GameDirectoryItem
        {
            Path = officialDir,
            DisplayName = officialDisplay,
            IsDefault = true,
            IsDeletable = false,
            IsCurrent = _config.GameDirectoryLocation == DirectoryLocation.AppData
                || string.Equals(officialDir, _config.GameDirectory, StringComparison.OrdinalIgnoreCase),
            IsValid = Directory.Exists(officialDir)
        });

        // 默认目录二：启动器根目录下的 .minecraft
        if (!Directory.Exists(runningDir))
        {
            Directory.CreateDirectory(runningDir);
        }

        items.Add(new GameDirectoryItem
        {
            Path = runningDir,
            DisplayName = ".minecraft（启动器目录）",
            IsDefault = true,
            IsDeletable = false,
            IsCurrent = _config.GameDirectoryLocation == DirectoryLocation.RunningDirectory
                || string.Equals(runningDir, _config.GameDirectory, StringComparison.OrdinalIgnoreCase),
            IsValid = Directory.Exists(runningDir)
        });

        // 用户自定义目录
        foreach (var dir in _config.CustomGameDirectories)
        {
            if (string.Equals(dir, officialDir, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(dir, runningDir, StringComparison.OrdinalIgnoreCase)) continue;

            items.Add(new GameDirectoryItem
            {
                Path = dir,
                DisplayName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                IsDefault = false,
                IsDeletable = true,
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

    [ObservableProperty]
    private bool _isDeletable = true;
}

/// <summary>
/// 版本分组头（平铺列表中的分组标题项）
/// </summary>
public class GroupSectionHeader
{
    public string GroupId { get; set; } = "";
    public string GroupName { get; set; } = "";
    public bool IsSystem { get; set; }
    public string Description { get; set; } = "";
    public bool HasDescription { get; set; }
    public int VersionCount { get; set; }
}
