using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
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
            // 在后台线程收集 I/O 数据，避免阻塞 UI
            var data = await Task.Run(() => CollectLoadData());

            // 在 UI 线程上更新 ObservableCollection，避免布局期间并发修改
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
        var versionConfig = config.VersionIsolationSettings?.FirstOrDefault(v => v.VersionId == _version.Id);
        if (versionConfig != null)
        {
            data.IsolationMode = versionConfig.IsolationMode switch
            {
                "enabled" => 1,
                "disabled" => 2,
                _ => 0
            };
        }
        else
        {
            data.IsolationMode = 0;
        }

        data.GameDir = config.GetRunDirectory(_version.Id);
        data.StoragePath = Path.Combine(config.GameDirectory, "versions", _version.Id);

        // 收集分组数据
        data.Groups = Core.Services.VersionGroupService.GetAllGroups();
        data.CurrentGroupId = Core.Services.VersionGroupService.GetEffectiveGroupId(_version);

        // 收集世界数据
        CollectWorlds(data);

        // 收集 Mod 数据
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
        _isLoadingConfig = false;

        // 应用分组信息
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
                    data.Mods.Add(new ModInfo
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
            // 恢复之前的选择
            var currentGroupId = Core.Services.VersionGroupService.GetEffectiveGroupId(_version);
            SelectedGroupItem = GroupListItems.FirstOrDefault(g => g.Id == currentGroupId);
            return;
        }

        if (value.IsSeparator) return;

        // 避免初始化时触发保存
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

        Core.Services.VersionGroupService.DeleteGroup(group.Id);
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

    private string GetVersionDirectory()
    {
        if (_version == null) return "-";

        var config = LauncherConfig.Load();
        return Path.Combine(config.GameDirectory, "versions", _version.Id);
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

    private void LoadWorlds()
    {
        var gameDir = GetGameDirectory();
        var savesDir = Path.Combine(gameDir, "saves");
        var list = new List<WorldInfo>();

        if (Directory.Exists(savesDir))
        {
            foreach (var dir in Directory.GetDirectories(savesDir))
            {
                try
                {
                    var levelDat = Path.Combine(dir, "level.dat");
                    if (File.Exists(levelDat))
                    {
                        list.Add(new WorldInfo
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

        ApplyWorlds(list);
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
        if (_version == null) return;

        var config = LauncherConfig.Load();
        config.VersionIsolationSettings ??= new();

        var existing = config.VersionIsolationSettings.FirstOrDefault(v => v.VersionId == _version.Id);
        if (existing == null)
        {
            existing = new VersionIsolationSetting { VersionId = _version.Id };
            config.VersionIsolationSettings.Add(existing);
        }

        existing.IsolationMode = value switch
        {
            1 => "enabled",
            2 => "disabled",
            _ => "global"
        };

        config.Save();

        if (!_isLoadingConfig)
        {
            _notificationService.Show("已保存", "版本隔离设置已更新", NotificationType.Success, 2);
        }
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
}
