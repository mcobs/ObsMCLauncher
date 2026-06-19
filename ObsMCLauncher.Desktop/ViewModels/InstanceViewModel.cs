using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Services.Accounts;
using ObsMCLauncher.Core.Services.Minecraft;
using ObsMCLauncher.Desktop.ViewModels.Notifications;
using ObsMCLauncher.Desktop.ViewModels.Dialogs;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class InstanceViewModel : ViewModelBase
{
    private readonly NotificationService _notificationService;
    private readonly DialogService _dialogService;
    private ObsMCLauncher.Core.Services.Minecraft.InstalledVersion? _version;
    private string _versionPath = string.Empty;
    private bool _isLoadingConfig;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _versionId = "-";

    [ObservableProperty]
    private string _actualVersion = "-";

    [ObservableProperty]
    private string _versionType = "-";

    [ObservableProperty]
    private string _lastPlayed = "-";

    [ObservableProperty]
    private string _storagePath = "-";

    [ObservableProperty]
    private int _isolationMode;

    [ObservableProperty]
    private ObservableCollection<WorldInfo> _worlds = new();

    [ObservableProperty]
    private ObservableCollection<ModInfo> _mods = new();

    [ObservableProperty]
    private bool _hasWorlds;

    [ObservableProperty]
    private bool _hasMods;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private ObservableCollection<GroupListItem> _groupListItems = new();

    [ObservableProperty]
    private GroupListItem? _selectedGroupItem;

    [ObservableProperty]
    private bool _isGroupManagerOpen;

    [ObservableProperty]
    private string _newGroupName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<VersionGroup> _managedGroups = new();

    // 导航栏相关
    [ObservableProperty]
    private int _selectedNavIndex = 0;

    public bool IsBasicTab => SelectedNavIndex == 0;
    public bool IsSettingsTab => SelectedNavIndex == 1;
    public bool IsWorldsTab => SelectedNavIndex == 2;
    public bool IsModsTab => SelectedNavIndex == 3;

    partial void OnSelectedNavIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsBasicTab));
        OnPropertyChanged(nameof(IsSettingsTab));
        OnPropertyChanged(nameof(IsWorldsTab));
        OnPropertyChanged(nameof(IsModsTab));
    }

    [RelayCommand]
    private void SelectNav(object? parameter)
    {
        if (parameter is int index)
            SelectedNavIndex = index;
        else if (parameter is string s && int.TryParse(s, out var parsed))
            SelectedNavIndex = parsed;
    }

    // 内存配置
    [ObservableProperty]
    private bool _useCustomMemory;

    [ObservableProperty]
    private int _customMaxMemory = 4096;

    [ObservableProperty]
    private int _customMinMemory = 1024;

    [ObservableProperty]
    private string _globalMemoryText = "";

    // 描述
    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _editingDescription = "";

    [ObservableProperty]
    private bool _isEditingDescription;

    public Action? OnCloseRequested { get; set; }

    public InstanceViewModel(NotificationService notificationService)
    {
        _notificationService = notificationService;
        _dialogService = NavigationStore.MainWindow?.Dialogs ?? new DialogService();
    }

    public void SetVersion(ObsMCLauncher.Core.Services.Minecraft.InstalledVersion version)
    {
        _version = version;
        _versionPath = version.Path;
        IsVisible = true;
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (_version == null) return;

        IsLoading = true;

        try
        {
            var data = await Task.Run(() => CollectLoadData());

            Dispatcher.UIThread.Post(() =>
            {
                ApplyVersionData(data);
                ApplyWorlds(data.Worlds);
                ApplyMods(data.Mods);
                IsLoading = false;
            });
        }
        catch
        {
            Dispatcher.UIThread.Post(() => IsLoading = false);
        }
    }

    private LoadData CollectLoadData()
    {
        var data = new LoadData();

        if (_version == null) return data;

        data.VersionId = _version.Id;
        data.ActualVersion = _version.ActualVersionId ?? _version.Id;
        data.VersionType = _version.Type ?? "未知";

        var lastPlayedFile = Path.Combine(_versionPath, ".lastplayed");
        if (File.Exists(lastPlayedFile))
        {
            try
            {
                var lastPlayed = File.GetLastWriteTime(lastPlayedFile);
                data.LastPlayed = lastPlayed.ToString("yyyy-MM-dd HH:mm");
            }
            catch { data.LastPlayed = "从未"; }
        }
        else
        {
            data.LastPlayed = "从未";
        }

        var config = LauncherConfig.Load();

        // 从 init.json 读取隔离模式
        var initIsolation = Core.Services.VersionInitService.GetIsolationMode(_versionPath);
        data.IsolationMode = initIsolation switch
        {
            "enabled" => 1,
            "disabled" => 2,
            _ => 0
        };

        // 兼容旧的全局隔离配置
        if (initIsolation == "global")
        {
            var legacySetting = config.VersionIsolationSettings?.FirstOrDefault(v => v.VersionId == _version.Id);
            if (legacySetting != null)
            {
                data.IsolationMode = legacySetting.IsolationMode switch
                {
                    "enabled" => 1,
                    "disabled" => 2,
                    _ => 0
                };
                // 迁移到 init.json
                if (data.IsolationMode != 0)
                {
                    Core.Services.VersionInitService.SetIsolationMode(_versionPath, legacySetting.IsolationMode);
                }
            }
        }

        data.GameDir = config.GetRunDirectory(_version.Id);
        data.StoragePath = Path.Combine(config.GameDirectory, "versions", _version.Id);

        // 收集分组数据
        data.Groups = Core.Services.VersionGroupService.GetAllGroups();
        data.CurrentGroupId = Core.Services.VersionGroupService.GetEffectiveGroupId(_version);

        // 内存配置
        var (max, min) = Core.Services.VersionInitService.GetMemory(_versionPath);
        data.UseCustomMemory = max.HasValue || min.HasValue;
        data.CustomMaxMemory = max ?? config.MaxMemory;
        data.CustomMinMemory = min ?? config.MinMemory;
        data.GlobalMaxMemory = config.MaxMemory;

        // 描述
        data.Description = Core.Services.VersionInitService.GetDescription(_versionPath);

        CollectWorlds(data);
        CollectMods(data);

        return data;
    }

    private void ApplyVersionData(LoadData data)
    {
        if (_version == null) return;

        VersionId = data.VersionId;
        ActualVersion = data.ActualVersion;
        VersionType = data.VersionType;
        LastPlayed = data.LastPlayed;
        StoragePath = data.StoragePath;

        _isLoadingConfig = true;
        IsolationMode = data.IsolationMode;
        UseCustomMemory = data.UseCustomMemory;
        CustomMaxMemory = data.CustomMaxMemory;
        CustomMinMemory = data.CustomMinMemory;
        GlobalMemoryText = $"全局: {data.GlobalMaxMemory} MB";
        Description = data.Description;
        EditingDescription = data.Description;

        // 如果描述为空，用版本信息重新生成默认描述
        if (string.IsNullOrEmpty(Description) && _version != null)
        {
            var defaultDesc = Core.Services.VersionInitService.GenerateDefaultDescription(
                _version.Type,
                _version.ActualVersionId,
                _version.LoaderType ?? "vanilla");
            Core.Services.VersionInitService.SetDescription(_versionPath, defaultDesc);
            Description = defaultDesc;
            EditingDescription = defaultDesc;
        }
        _isLoadingConfig = false;

        var items = new ObservableCollection<GroupListItem>();
        foreach (var g in data.Groups)
        {
            items.Add(new GroupListItem { Id = g.Id, Name = g.Name, IsSystem = g.IsSystem, IsDeletable = g.IsDeletable });
        }
        items.Add(new GroupListItem { Id = "__separator__", Name = "", IsSeparator = true });
        items.Add(new GroupListItem { Id = "__manage__", Name = "管理分组...", IsManageEntry = true });

        GroupListItems = items;
        SelectedGroupItem = items.FirstOrDefault(g => g.Id == data.CurrentGroupId);
        ManagedGroups = new ObservableCollection<VersionGroup>(data.Groups);
    }

    private void CollectWorlds(LoadData data)
    {
        var savesDir = Path.Combine(data.GameDir, "saves");
        if (Directory.Exists(savesDir))
        {
            foreach (var dir in Directory.GetDirectories(savesDir))
            {
                try
                {
                    var levelDat = Path.Combine(dir, "level.dat");
                    if (File.Exists(levelDat))
                    {
                        data.Worlds.Add(new WorldInfo
                        {
                            Name = Path.GetFileName(dir),
                            Path = dir,
                            LastModified = File.GetLastWriteTime(dir)
                        });
                    }
                }
                catch { }
            }
        }
    }

    private void ApplyWorlds(List<WorldInfo> worlds)
    {
        Worlds.Clear();
        foreach (var w in worlds) Worlds.Add(w);
        HasWorlds = Worlds.Count > 0;
    }

    private void CollectMods(LoadData data)
    {
        var modsDir = Path.Combine(data.GameDir, "mods");
        if (Directory.Exists(modsDir))
        {
            foreach (var file in Directory.GetFiles(modsDir, "*.jar"))
            {
                try
                {
                    data.Mods.Add(new ModInfo
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        FileName = Path.GetFileName(file),
                        Path = file,
                        Size = new FileInfo(file).Length,
                        IsEnabled = true,
                        IconPath = ExtractModIcon(file)
                    });
                }
                catch { }
            }

            foreach (var file in Directory.GetFiles(modsDir, "*.jar.disabled"))
            {
                try
                {
                    var fileName = Path.GetFileName(file);
                    data.Mods.Add(new ModInfo
                    {
                        Name = fileName,
                        FileName = fileName,
                        Path = file,
                        Size = new FileInfo(file).Length,
                        IsEnabled = false,
                        IconPath = ExtractModIcon(file)
                    });
                }
                catch { }
            }
        }
    }

    private static readonly string ModIconCacheDir = Path.Combine(Path.GetTempPath(), "OMCL", "mod_icons");

    /// <summary>
    /// 从 JAR 中提取 Mod 图标，返回缓存文件路径，未找到则返回 null
    /// </summary>
    private static string? ExtractModIcon(string jarPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            // 按优先级查找图标
            string[] candidates = ["pack.png", "logo.png", "icon.png"];
            foreach (var candidate in candidates)
            {
                var entry = archive.GetEntry(candidate);
                if (entry != null)
                {
                    Directory.CreateDirectory(ModIconCacheDir);
                    var hash = Math.Abs(jarPath.GetHashCode()).ToString("x8");
                    var tmpPath = Path.Combine(ModIconCacheDir, $"{hash}.png");
                    if (!File.Exists(tmpPath) || new FileInfo(tmpPath).Length != entry.Length)
                        entry.ExtractToFile(tmpPath, true);
                    return tmpPath;
                }
            }

            // 尝试 assets/<modid>/icon.png
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName;
                if (name.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith("/icon.png", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(ModIconCacheDir);
                    var hash = Math.Abs(jarPath.GetHashCode()).ToString("x8");
                    var tmpPath = Path.Combine(ModIconCacheDir, $"{hash}.png");
                    if (!File.Exists(tmpPath) || new FileInfo(tmpPath).Length != entry.Length)
                        entry.ExtractToFile(tmpPath, true);
                    return tmpPath;
                }
            }
        }
        catch { }
        return null;
    }

    private void ApplyMods(List<ModInfo> mods)
    {
        Mods.Clear();
        foreach (var m in mods) Mods.Add(m);
        HasMods = Mods.Count > 0;
    }

    partial void OnSelectedGroupItemChanged(GroupListItem? value)
    {
        if (_version == null || value == null) return;

        if (value.IsManageEntry)
        {
            IsGroupManagerOpen = true;
            var currentGroupId = Core.Services.VersionGroupService.GetEffectiveGroupId(_version);
            SelectedGroupItem = GroupListItems.FirstOrDefault(g => g.Id == currentGroupId);
            return;
        }

        if (value.IsSeparator) return;

        var existingGroupId = Core.Services.VersionGroupService.GetEffectiveGroupId(_version);
        if (existingGroupId == value.Id) return;

        Core.Services.VersionGroupService.SetVersionGroup(_version.Id, _versionPath, value.Id);
        _notificationService.Show("分组已更新", $"版本 {_version.Id} 已移动到 \"{value.Name}\"", NotificationType.Success, 2);
    }

    [RelayCommand]
    private void CreateGroup()
    {
        if (string.IsNullOrWhiteSpace(NewGroupName)) return;

        var group = Core.Services.VersionGroupService.CreateGroup(NewGroupName.Trim());
        NewGroupName = string.Empty;
        LoadGroupInfo();
        SelectedGroupItem = GroupListItems.FirstOrDefault(g => g.Id == group.Id);
        _notificationService.Show("分组已创建", $"分组 \"{group.Name}\" 已创建", NotificationType.Success);
    }

    [RelayCommand]
    private async Task RenameGroupAsync(VersionGroup? group)
    {
        if (group == null || group.IsSystem) return;

        var (result, newName) = await _dialogService.ShowInputAsync("重命名分组", "请输入新的分组名称", group.Name);
        if (result != DialogResult.OK || string.IsNullOrWhiteSpace(newName)) return;

        Core.Services.VersionGroupService.RenameGroup(group.Id, newName.Trim());
        LoadGroupInfo();
        SelectedGroupItem = GroupListItems.FirstOrDefault(g => g.Id == group.Id);
        _notificationService.Show("分组已重命名", $"分组已重命名为 \"{newName.Trim()}\"", NotificationType.Success);
    }

    [RelayCommand]
    private async Task DeleteGroupAsync(VersionGroup? group)
    {
        if (group == null || !group.IsDeletable) return;

        var result = await _dialogService.ShowQuestion("确认删除", $"确定要删除分组 \"{group.Name}\" 吗？\n组内版本将归入\"自动\"分组。");
        if (result != DialogResult.Yes) return;

        var config = LauncherConfig.Load();
        Core.Services.VersionGroupService.DeleteGroup(group.Id, config.GameDirectory);
        LoadGroupInfo();
        SelectedGroupItem = GroupListItems.FirstOrDefault();
        _notificationService.Show("分组已删除", $"分组 \"{group.Name}\" 已删除", NotificationType.Success);
    }

    [RelayCommand]
    private void CloseGroupManager()
    {
        IsGroupManagerOpen = false;
    }

    private string GetGameDirectory()
    {
        if (_version == null) return "-";

        var config = LauncherConfig.Load();
        return config.GetRunDirectory(_version.Id);
    }

    private void LoadGroupInfo()
    {
        if (_version == null) return;

        var groups = Core.Services.VersionGroupService.GetAllGroups();
        var currentGroupId = Core.Services.VersionGroupService.GetEffectiveGroupId(_version);

        var items = new ObservableCollection<GroupListItem>();
        foreach (var g in groups)
        {
            items.Add(new GroupListItem { Id = g.Id, Name = g.Name, IsSystem = g.IsSystem, IsDeletable = g.IsDeletable });
        }
        items.Add(new GroupListItem { Id = "__separator__", Name = "", IsSeparator = true });
        items.Add(new GroupListItem { Id = "__manage__", Name = "管理分组...", IsManageEntry = true });

        GroupListItems = items;
        SelectedGroupItem = items.FirstOrDefault(g => g.Id == currentGroupId);
        ManagedGroups = new ObservableCollection<VersionGroup>(groups);
    }

    [RelayCommand]
    private void Close()
    {
        IsVisible = false;
        OnCloseRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (string.IsNullOrEmpty(StoragePath) || !Directory.Exists(StoragePath)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = StoragePath,
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private void OpenSavesFolder()
    {
        var gameDir = GetGameDirectory();
        var savesDir = Path.Combine(gameDir, "saves");
        if (!Directory.Exists(savesDir)) Directory.CreateDirectory(savesDir);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = savesDir,
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        var gameDir = GetGameDirectory();
        var modsDir = Path.Combine(gameDir, "mods");
        if (!Directory.Exists(modsDir)) Directory.CreateDirectory(modsDir);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = modsDir,
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private void ToggleModEnabled(ModInfo mod)
    {
        if (mod == null) return;

        try
        {
            var currentPath = mod.Path;
            string newPath;

            if (mod.IsEnabled)
            {
                newPath = currentPath + ".disabled";
            }
            else
            {
                newPath = currentPath.Replace(".disabled", "");
            }

            if (File.Exists(currentPath))
            {
                File.Move(currentPath, newPath);
                mod.Path = newPath;
                mod.IsEnabled = !mod.IsEnabled;
                mod.FileName = Path.GetFileName(newPath);
                mod.Name = mod.IsEnabled ? Path.GetFileNameWithoutExtension(newPath) : Path.GetFileName(newPath);
                OnPropertyChanged(nameof(Mods));
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("操作失败", $"无法更改Mod状态: {ex.Message}", NotificationType.Error, 3);
        }
    }

    [RelayCommand]
    private void RefreshMods()
    {
        LoadMods();
    }

    partial void OnIsolationModeChanged(int value)
    {
        if (_version == null || _isLoadingConfig) return;

        var mode = value switch
        {
            1 => "enabled",
            2 => "disabled",
            _ => "global"
        };

        Core.Services.VersionInitService.SetIsolationMode(_versionPath, mode);
        _notificationService.Show("已保存", "版本隔离设置已更新", NotificationType.Success, 2);
    }

    partial void OnUseCustomMemoryChanged(bool value)
    {
        if (_version == null || _isLoadingConfig) return;
        SaveMemoryConfig();
    }

    partial void OnCustomMaxMemoryChanged(int value)
    {
        if (_version == null || _isLoadingConfig) return;
        // 限制最小值
        if (value < 512) CustomMaxMemory = 512;
        SaveMemoryConfig();
    }

    partial void OnCustomMinMemoryChanged(int value)
    {
        if (_version == null || _isLoadingConfig) return;
        if (value < 256) CustomMinMemory = 256;
        SaveMemoryConfig();
    }

    private void SaveMemoryConfig()
    {
        if (_version == null) return;

        if (UseCustomMemory)
        {
            // 保证 min <= max
            var min = Math.Min(CustomMinMemory, CustomMaxMemory);
            var max = Math.Max(CustomMinMemory, CustomMaxMemory);
            Core.Services.VersionInitService.SetMemory(_versionPath, max, min);
        }
        else
        {
            // 清除自定义配置，回退到全局
            Core.Services.VersionInitService.SetMemory(_versionPath, null, null);
        }
    }

    [RelayCommand]
    private void StartEditDescription()
    {
        EditingDescription = Description;
        IsEditingDescription = true;
    }

    [RelayCommand]
    private void SaveDescription()
    {
        if (_version == null) return;

        var text = EditingDescription?.Trim() ?? "";
        Core.Services.VersionInitService.SetDescription(_versionPath, text);
        Description = text;
        IsEditingDescription = false;
        _notificationService.Show("已保存", "版本描述已更新", NotificationType.Success, 2);
    }

    [RelayCommand]
    private void CancelEditDescription()
    {
        IsEditingDescription = false;
        EditingDescription = Description;
    }

    [RelayCommand]
    private async Task ExportLaunchScript()
    {
        try
        {
            if (_version == null) return;

            var config = LauncherConfig.Load();
            var account = AccountService.Instance.GetDefaultAccount();
            if (account == null)
            {
                _notificationService.Show("导出失败", "请先登录账号", NotificationType.Error);
                return;
            }

            var arguments = GameLauncher.BuildLaunchScriptContent(_version.Id, config, account);

            var storageProvider = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.StorageProvider;
            if (storageProvider == null) return;

            var defaultName = $"启动_{_version.Id}.bat";
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出启动脚本",
                DefaultExtension = ".bat",
                SuggestedFileName = defaultName,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Windows 批处理") { Patterns = new[] { "*.bat" } },
                    new FilePickerFileType("Shell 脚本") { Patterns = new[] { "*.sh" } }
                }
            });

            if (file == null) return;

            var isWindows = file.Name.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
            var gameDir = config.GetRunDirectory(_version.Id);

            var script = isWindows
                ? $"@echo off{Environment.NewLine}cd /d \"{gameDir}\"{Environment.NewLine}java {arguments}{Environment.NewLine}pause"
                : $"#!/bin/bash{Environment.NewLine}cd \"{gameDir}\"{Environment.NewLine}java {arguments}";

            await File.WriteAllTextAsync(file.Path.LocalPath, script);
            _notificationService.Show("导出成功", $"启动脚本已导出到 {file.Name}", NotificationType.Success, 3);
        }
        catch (Exception ex)
        {
            _notificationService.Show("导出失败", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task CompleteFiles()
    {
        try
        {
            if (_version == null) return;

            var config = LauncherConfig.Load();
            var (missingLibs, missingAssets) = GameLauncher.CheckVersionIntegrity(config.GameDirectory, _version.Id);

            if (missingLibs == -1)
            {
                _notificationService.Show("补全文件", "未找到版本信息文件，无法检测", NotificationType.Error);
                return;
            }

            if (missingLibs == 0 && missingAssets == 0)
            {
                _notificationService.Show("补全文件", "所有文件已完整，无需补全", NotificationType.Success);
                return;
            }

            var msg = new System.Text.StringBuilder();
            if (missingLibs > 0) msg.AppendLine($"缺失 {missingLibs} 个库文件");
            if (missingAssets > 0) msg.AppendLine($"缺失 {missingAssets} 个资源文件");
            if (missingAssets == -1) msg.AppendLine("资源索引文件缺失，将重新下载");

            var result = await _dialogService.ShowQuestion("补全文件", $"{msg}\n是否自动下载补全这些文件？");
            if (result != DialogResult.Yes) return;

            _notificationService.Show("补全文件", "正在下载缺失文件...", NotificationType.Info);

            var (downloaded, failed, assetsOk) = await GameLauncher.CompleteVersionFilesAsync(
                config.GameDirectory, _version.Id);

            if (failed > 0)
            {
                _notificationService.Show("补全文件", $"库文件下载完成: {downloaded} 成功, {failed} 失败。请检查网络后重试", NotificationType.Warning);
            }
            else if (!assetsOk)
            {
                _notificationService.Show("补全文件", "库文件下载完成，资源文件下载失败，请检查网络后重试", NotificationType.Warning);
            }
            else
            {
                _notificationService.Show("补全文件", $"补全完成，已下载 {downloaded} 个文件", NotificationType.Success);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("补全失败", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteVersion()
    {
        try
        {
            if (_version == null) return;

            var result = await _dialogService.ShowQuestion("确认删除", $"确定要删除版本 {_version.Id} 吗？\n此操作将永久删除版本文件，不可恢复。");
            if (result != DialogResult.Yes) return;

            var config = LauncherConfig.Load();
            var versionDir = Path.Combine(config.GameDirectory, "versions", _version.Id);

            if (Directory.Exists(versionDir))
            {
                Directory.Delete(versionDir, true);
            }

            _notificationService.Show("删除成功", $"版本 {_version.Id} 已删除", NotificationType.Success);

            OnCloseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            _notificationService.Show("删除失败", ex.Message, NotificationType.Error);
        }
    }

    private void LoadMods()
    {
        var gameDir = GetGameDirectory();
        var modsDir = Path.Combine(gameDir, "mods");
        var list = new List<ModInfo>();

        if (Directory.Exists(modsDir))
        {
            foreach (var file in Directory.GetFiles(modsDir, "*.jar"))
            {
                try
                {
                    list.Add(new ModInfo
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        FileName = Path.GetFileName(file),
                        Path = file,
                        Size = new FileInfo(file).Length,
                        IsEnabled = true
                    });
                }
                catch { }
            }

            foreach (var file in Directory.GetFiles(modsDir, "*.jar.disabled"))
            {
                try
                {
                    var fileName = Path.GetFileName(file);
                    list.Add(new ModInfo
                    {
                        Name = fileName,
                        FileName = fileName,
                        Path = file,
                        Size = new FileInfo(file).Length,
                        IsEnabled = false
                    });
                }
                catch { }
            }
        }

        ApplyMods(list);
    }

    private class LoadData
    {
        public string VersionId { get; set; } = "-";
        public string ActualVersion { get; set; } = "-";
        public string VersionType { get; set; } = "-";
        public string LastPlayed { get; set; } = "-";
        public string GameDir { get; set; } = "-";
        public string StoragePath { get; set; } = "-";
        public int IsolationMode { get; set; }
        public List<VersionGroup> Groups { get; set; } = new();
        public string CurrentGroupId { get; set; } = "";
        public List<WorldInfo> Worlds { get; set; } = new();
        public List<ModInfo> Mods { get; set; } = new();
        public bool UseCustomMemory { get; set; }
        public int CustomMaxMemory { get; set; }
        public int CustomMinMemory { get; set; }
        public int GlobalMaxMemory { get; set; }
        public string Description { get; set; } = "";
    }
}

public class WorldInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}

public class ModInfo : ObservableObject
{
    private string _name = string.Empty;
    private string _fileName = string.Empty;
    private string _path = string.Empty;
    private long _size;
    private bool _isEnabled;
    private string? _iconPath;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string FileName
    {
        get => _fileName;
        set => SetProperty(ref _fileName, value);
    }

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public long Size
    {
        get => _size;
        set => SetProperty(ref _size, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string DisplayName => IsEnabled ? Name : Name.Replace(".disabled", "");

    public string? IconPath
    {
        get => _iconPath;
        set => SetProperty(ref _iconPath, value);
    }
}

/// <summary>
/// 分组下拉框列表项模型
/// </summary>
public class GroupListItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsSystem { get; set; }
    public bool IsDeletable { get; set; }
    public bool IsSeparator { get; set; }
    public bool IsManageEntry { get; set; }

    /// <summary>
    /// 是否为普通分组项（非分隔线、非管理入口）
    /// </summary>
    public bool IsNormalItem => !IsSeparator && !IsManageEntry;
}
