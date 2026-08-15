using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
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
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.Services;
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
    private ObservableCollection<ShaderPackInfo> _shaderPacks = new();

    [ObservableProperty]
    private bool _hasShaderPacks;

    [ObservableProperty]
    private ObservableCollection<ResourcePackInfo> _resourcePacks = new();

    [ObservableProperty]
    private bool _hasResourcePacks;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private ObservableCollection<GroupListItem> _groupListItems = new();

    [ObservableProperty]
    private GroupListItem? _selectedGroupItem;

    /// <summary>
    /// 请求打开分组管理对话框（由视图订阅并显示 ContentDialog）。
    /// </summary>
    public event Action? GroupManagerRequested;

    [ObservableProperty]
    private string _newGroupName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<VersionGroup> _managedGroups = new();

    // 内存配置
    [ObservableProperty]
    private bool _useCustomMemory;

    [ObservableProperty]
    private int _customMaxMemory = 4096;

    [ObservableProperty]
    private int _customMinMemory = 1024;

    [ObservableProperty]
    private string _globalMemoryText = "";

    [ObservableProperty]
    private string _memoryHint = "建议: 最小内存不超过最大内存的1/4，最大内存不超过系统可用内存的70%";

    [ObservableProperty]
    private bool _isMemoryHintWarning;

    // 实例级 Java 与 JVM 参数
    [ObservableProperty]
    private bool _useCustomJava;

    [ObservableProperty]
    private string _customJavaPath = "";

    /// <summary>自定义模式下的 Java 下拉列表（已探测列表 + 自定义路径）。</summary>
    [ObservableProperty]
    private ObservableCollection<JavaOption> _javaOptions = new();

    [ObservableProperty]
    private JavaOption? _selectedJavaOption;

    [ObservableProperty]
    private string _globalJavaText = "";

    /// <summary>Java 选择下方的提示/校验信息。</summary>
    [ObservableProperty]
    private string _javaPathHint = "";

    [ObservableProperty]
    private bool _isJavaPathWarning;

    /// <summary>当前选中的是否为「自定义路径」选项。</summary>
    public bool IsCustomJavaPath => SelectedJavaOption?.Type == JavaOptionType.Custom;

    [ObservableProperty]
    private bool _useCustomJvm;

    [ObservableProperty]
    private string _globalJvmText = "";

    /// <summary>实例级 JVM 参数编辑器（chips + 预设 + 自由编辑），供「版本设置」页使用。</summary>
    public JvmArgumentsEditorViewModel JvmArgumentsEditor { get; }

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

        JvmArgumentsEditor = new JvmArgumentsEditorViewModel
        {
            ArgumentsCommit = args =>
            {
                if (_version == null || _isLoadingConfig) return;
                if (UseCustomJvm)
                {
                    Core.Services.VersionInitService.SetJvmArguments(_versionPath, args ?? "");
                }
            }
        };
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

            // 扫描 Java 列表（与设置页共用缓存，设置页扫过后此处几乎无耗时）
            List<JavaOption> javaList = new();
            try
            {
                javaList = await JavaOptionsProvider.ScanAsync().ConfigureAwait(false);
            }
            catch
            {
                // 扫描失败时仍可手动填写自定义路径
            }

            Dispatcher.UIThread.Post(() =>
            {
                ApplyVersionData(data);
                ApplyJavaOptions(javaList, data.CustomJavaPath);
                ApplyWorlds(data.Worlds);
                ApplyMods(data.Mods);
                LoadShaderPacks();
                LoadResourcePacks();
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

        // 实例级 Java 与 JVM 参数
        data.CustomJavaPath = Core.Services.VersionInitService.GetCustomJavaPath(_versionPath);
        data.UseCustomJava = !string.IsNullOrWhiteSpace(data.CustomJavaPath);
        data.GlobalJavaText = $"全局: {config.GetActualJavaPath(data.ActualVersion)}";
        data.InstanceJvmArguments = Core.Services.VersionInitService.GetJvmArguments(_versionPath);
        data.UseCustomJvm = !string.IsNullOrWhiteSpace(data.InstanceJvmArguments);
        data.GlobalJvmArguments = config.JvmArguments;

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
        UseCustomJava = data.UseCustomJava;
        CustomJavaPath = data.CustomJavaPath;
        GlobalJavaText = data.GlobalJavaText;
        UseCustomJvm = data.UseCustomJvm;
        JvmArgumentsEditor.SetArguments(data.InstanceJvmArguments);
        GlobalJvmText = $"全局: {data.GlobalJvmArguments}";
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
        UpdateMemoryHint();

        var items = new ObservableCollection<GroupListItem>();
        foreach (var g in data.Groups)
        {
            items.Add(new GroupListItem { Id = g.Id, Name = g.Name });
        }

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
                        var info = new WorldInfo
                        {
                            Name = Path.GetFileName(dir),
                            Path = dir,
                            CreationTime = Directory.GetCreationTime(dir),
                            LastModified = Directory.GetLastWriteTime(dir),
                            GameVersion = Core.Services.NbtReader.ReadWorldVersionFromLevelDat(levelDat),
                            WorldSizeBytes = CalculateDirectorySize(dir)
                        };

                        // 存档图标
                        var iconFile = Path.Combine(dir, "icon.png");
                        if (File.Exists(iconFile))
                            info.IconPath = iconFile;

                        data.Worlds.Add(info);
                    }
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// 递归计算目录大小
    /// </summary>
    private static long CalculateDirectorySize(string dirPath)
    {
        long size = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; }
                catch { }
            }
        }
        catch { }
        return size;
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

    private static readonly string ModIconCacheDir = Path.Combine(VersionInfo.GetAppBaseDirectory(), "OMCL", "cache", "mod_icons");

    /// <summary>
    /// 生成稳定的十六进制哈希（SHA256 前 4 字节），用于图标缓存文件名。
    /// 避免 string.GetHashCode 跨进程随机化导致缓存永远失效、每次打开实例都重新解压图标。
    /// </summary>
    private static string StableHash(string input)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }

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
                    var hash = StableHash(jarPath);
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
                    var hash = StableHash(jarPath);
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

        var existingGroupId = Core.Services.VersionGroupService.GetEffectiveGroupId(_version);
        if (existingGroupId == value.Id) return;

        Core.Services.VersionGroupService.SetVersionGroup(_version.Id, _versionPath, value.Id);
        _notificationService.Show("分组已更新", $"版本 {_version.Id} 已移动到 \"{value.Name}\"", NotificationType.Success, 2);
    }

    [RelayCommand]
    private void OpenGroupManager()
    {
        GroupManagerRequested?.Invoke();
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
            items.Add(new GroupListItem { Id = g.Id, Name = g.Name });
        }

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
        OpenFolderInExplorer(StoragePath);
    }

    [RelayCommand]
    private void OpenSavesFolder()
    {
        var gameDir = GetGameDirectory();
        var savesDir = Path.Combine(gameDir, "saves");
        if (!Directory.Exists(savesDir)) Directory.CreateDirectory(savesDir);
        OpenFolderInExplorer(savesDir);
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        var gameDir = GetGameDirectory();
        var modsDir = Path.Combine(gameDir, "mods");
        if (!Directory.Exists(modsDir)) Directory.CreateDirectory(modsDir);
        OpenFolderInExplorer(modsDir);
    }

    [RelayCommand]
    private void ToggleModEnabled(ModInfo mod)
    {
        if (mod == null) return;

        try
        {
            mod.IsEnabled = !mod.IsEnabled;
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
        _notificationService.Show("已刷新", "模组列表已重新加载", NotificationType.Success, 2);
    }

    [RelayCommand]
    private void RefreshShaderPacks()
    {
        LoadShaderPacks();
        _notificationService.Show("已刷新", "Shader Pack 列表已重新加载", NotificationType.Success, 2);
    }

    [RelayCommand]
    private void RefreshResourcePacks()
    {
        LoadResourcePacks();
        _notificationService.Show("已刷新", "材质包列表已重新加载", NotificationType.Success, 2);
    }

    private void LoadShaderPacks()
    {
        var gameDir = GetGameDirectory();
        var shaderDir = Path.Combine(gameDir, "shaderpacks");
        var list = new List<ShaderPackInfo>();

        if (Directory.Exists(shaderDir))
        {
            foreach (var file in Directory.GetFiles(shaderDir, "*.zip"))
            {
                try
                {
                    list.Add(new ShaderPackInfo
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        FileName = Path.GetFileName(file),
                        Path = file,
                        Size = new FileInfo(file).Length,
                        IsEnabled = true,
                        IconPath = ExtractShaderPackIcon(file)
                    });
                }
                catch { }
            }

            foreach (var file in Directory.GetFiles(shaderDir, "*.zip.disabled"))
            {
                try
                {
                    var fileName = Path.GetFileName(file);
                    list.Add(new ShaderPackInfo
                    {
                        Name = fileName,
                        FileName = fileName,
                        Path = file,
                        Size = new FileInfo(file).Length,
                        IsEnabled = false,
                        IconPath = ExtractShaderPackIcon(file)
                    });
                }
                catch { }
            }
        }

        ShaderPacks.Clear();
        foreach (var p in list) ShaderPacks.Add(p);
        HasShaderPacks = ShaderPacks.Count > 0;
    }

    private static readonly string ShaderIconCacheDir = Path.Combine(VersionInfo.GetAppBaseDirectory(), "OMCL", "cache", "shader_icons");

    /// <summary>
    /// 从光影包 zip 中提取图标，返回缓存文件路径，未找到则返回 null。
    /// 光影包图标没有强制路径规范，按以下顺序查找：
    ///   根目录 → shaders/ → textures/ → gui/
    /// 文件名候选：pack.png → logo.png → icon.png
    /// </summary>
    private static string? ExtractShaderPackIcon(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            string[] dirs = ["", "shaders/", "textures/", "gui/"];
            string[] names = ["pack.png", "logo.png", "icon.png"];

            foreach (var dir in dirs)
            {
                foreach (var name in names)
                {
                    var entry = archive.GetEntry(dir + name);
                    if (entry != null)
                    {
                        Directory.CreateDirectory(ShaderIconCacheDir);
                        var hash = StableHash(zipPath);
                        var tmpPath = Path.Combine(ShaderIconCacheDir, $"{hash}.png");
                        if (!File.Exists(tmpPath) || new FileInfo(tmpPath).Length != entry.Length)
                            entry.ExtractToFile(tmpPath, true);
                        return tmpPath;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    [RelayCommand]
    private void ToggleShaderPack(ShaderPackInfo pack)
    {
        if (pack == null) return;
        try
        {
            pack.IsEnabled = !pack.IsEnabled;
        }
        catch (Exception ex)
        {
            _notificationService.Show("操作失败", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteShaderPackAsync(ShaderPackInfo pack)
    {
        if (pack == null) return;

        var result = await _dialogService.ShowQuestion("确认删除", $"确定要删除 Shader Pack \"{pack.DisplayName}\" 吗？");
        if (result != DialogResult.Yes) return;

        try
        {
            if (File.Exists(pack.Path))
            {
                File.Delete(pack.Path);
                ShaderPacks.Remove(pack);
                HasShaderPacks = ShaderPacks.Count > 0;
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("删除失败", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private void OpenShaderPacksFolder()
    {
        var gameDir = GetGameDirectory();
        var shaderDir = Path.Combine(gameDir, "shaderpacks");
        if (!Directory.Exists(shaderDir)) Directory.CreateDirectory(shaderDir);
        OpenFolderInExplorer(shaderDir);
    }

    private void LoadResourcePacks()
    {
        var gameDir = GetGameDirectory();
        var dir = Path.Combine(gameDir, "resourcepacks");
        var list = new List<ResourcePackInfo>();

        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.GetFiles(dir, "*.zip"))
            {
                try
                {
                    list.Add(new ResourcePackInfo
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        FileName = Path.GetFileName(file),
                        Path = file,
                        Size = new FileInfo(file).Length,
                        IsEnabled = true,
                        IconPath = ExtractResourcePackIcon(file)
                    });
                }
                catch { }
            }

            foreach (var file in Directory.GetFiles(dir, "*.zip.disabled"))
            {
                try
                {
                    var fileName = Path.GetFileName(file);
                    list.Add(new ResourcePackInfo
                    {
                        Name = fileName,
                        FileName = fileName,
                        Path = file,
                        Size = new FileInfo(file).Length,
                        IsEnabled = false,
                        IconPath = ExtractResourcePackIcon(file)
                    });
                }
                catch { }
            }
        }

        ResourcePacks.Clear();
        foreach (var p in list) ResourcePacks.Add(p);
        HasResourcePacks = ResourcePacks.Count > 0;
    }

    private static readonly string ResourcePackIconCacheDir = Path.Combine(VersionInfo.GetAppBaseDirectory(), "OMCL", "cache", "resourcepack_icons");

    /// <summary>
    /// 从材质包 zip 中提取图标，返回缓存文件路径，未找到则返回 null。
    /// 材质包遵循资源包规范，图标通常位于根目录 pack.png。
    /// </summary>
    private static string? ExtractResourcePackIcon(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            string[] names = ["pack.png", "logo.png", "icon.png"];
            foreach (var name in names)
            {
                var entry = archive.GetEntry(name);
                if (entry != null)
                {
                    Directory.CreateDirectory(ResourcePackIconCacheDir);
                    var hash = StableHash(zipPath);
                    var tmpPath = Path.Combine(ResourcePackIconCacheDir, $"{hash}.png");
                    if (!File.Exists(tmpPath) || new FileInfo(tmpPath).Length != entry.Length)
                        entry.ExtractToFile(tmpPath, true);
                    return tmpPath;
                }
            }
        }
        catch { }
        return null;
    }

    [RelayCommand]
    private void ToggleResourcePack(ResourcePackInfo pack)
    {
        if (pack == null) return;
        try
        {
            pack.IsEnabled = !pack.IsEnabled;
        }
        catch (Exception ex)
        {
            _notificationService.Show("操作失败", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteResourcePackAsync(ResourcePackInfo pack)
    {
        if (pack == null) return;

        var result = await _dialogService.ShowQuestion("确认删除", $"确定要删除材质包 \"{pack.DisplayName}\" 吗？");
        if (result != DialogResult.Yes) return;

        try
        {
            if (File.Exists(pack.Path))
            {
                File.Delete(pack.Path);
                ResourcePacks.Remove(pack);
                HasResourcePacks = ResourcePacks.Count > 0;
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("删除失败", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private void OpenResourcePacksFolder()
    {
        var gameDir = GetGameDirectory();
        var dir = Path.Combine(gameDir, "resourcepacks");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        OpenFolderInExplorer(dir);
    }

    [RelayCommand]
    private void OpenConfigFolder()
    {
        var gameDir = GetGameDirectory();
        var dir = Path.Combine(gameDir, "config");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        OpenFolderInExplorer(dir);
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

        // 隔离模式变更后刷新模组列表
        LoadMods();
    }

    partial void OnUseCustomMemoryChanged(bool value)
    {
        if (_version == null || _isLoadingConfig) return;
        SaveMemoryConfig();
        UpdateMemoryHint();
    }

    partial void OnCustomMaxMemoryChanged(int value)
    {
        if (_version == null || _isLoadingConfig) return;
        // 限制最小值
        if (value < 512) CustomMaxMemory = 512;
        SaveMemoryConfig();
        UpdateMemoryHint();
    }

    partial void OnCustomMinMemoryChanged(int value)
    {
        if (_version == null || _isLoadingConfig) return;
        if (value < 256) CustomMinMemory = 256;
        SaveMemoryConfig();
        UpdateMemoryHint();
    }

    private void UpdateMemoryHint()
    {
        if (UseCustomMemory && CustomMinMemory >= CustomMaxMemory)
        {
            MemoryHint = "最小内存应小于最大内存";
            IsMemoryHintWarning = true;
            return;
        }

        if (UseCustomMemory && CustomMinMemory > CustomMaxMemory / 4)
        {
            MemoryHint = "最小内存建议不超过最大内存的 1/4";
            IsMemoryHintWarning = true;
            return;
        }

        MemoryHint = "建议: 最小内存不超过最大内存的1/4，最大内存不超过系统可用内存的70%";
        IsMemoryHintWarning = false;
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

    partial void OnUseCustomJavaChanged(bool value)
    {
        if (_version == null || _isLoadingConfig) return;

        var path = value
            ? (SelectedJavaOption?.Type == JavaOptionType.Detected ? SelectedJavaOption.Path : CustomJavaPath)
            : "";
        Core.Services.VersionInitService.SetCustomJavaPath(_versionPath, path ?? "");
        UpdateJavaPathHint();
    }

    partial void OnCustomJavaPathChanged(string value)
    {
        if (_version == null || _isLoadingConfig) return;

        if (UseCustomJava && SelectedJavaOption?.Type == JavaOptionType.Custom)
        {
            Core.Services.VersionInitService.SetCustomJavaPath(_versionPath, value ?? "");
        }
        UpdateJavaPathHint();
    }

    partial void OnSelectedJavaOptionChanged(JavaOption? value)
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsCustomJavaPath)));
        if (_version == null || _isLoadingConfig) return;

        if (value?.Type == JavaOptionType.Detected && UseCustomJava)
        {
            CustomJavaPath = value.Path;
            Core.Services.VersionInitService.SetCustomJavaPath(_versionPath, value.Path);
        }
        UpdateJavaPathHint();
    }

    partial void OnUseCustomJvmChanged(bool value)
    {
        if (_version == null || _isLoadingConfig) return;
        Core.Services.VersionInitService.SetJvmArguments(_versionPath, value ? JvmArgumentsEditor.Arguments : "");
    }

    /// <summary>加载 Java 下拉列表并恢复选中项（实例已存路径优先匹配探测结果）。</summary>
    private void ApplyJavaOptions(List<JavaOption> found, string customPath)
    {
        var list = new ObservableCollection<JavaOption>();
        foreach (var j in found)
            list.Add(j);
        list.Add(JavaOption.Custom());
        JavaOptions = list;

        JavaOption? selection = null;
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            selection = found.FirstOrDefault(j =>
                            string.Equals(j.Path, customPath, StringComparison.OrdinalIgnoreCase))
                        ?? JavaOptions.FirstOrDefault(x => x.Type == JavaOptionType.Custom);
        }

        _isLoadingConfig = true;
        SelectedJavaOption = selection;
        _isLoadingConfig = false;
        UpdateJavaPathHint();
    }

    /// <summary>刷新 Java 选择区下方的提示信息（版本详情 / 路径校验警告）。</summary>
    private void UpdateJavaPathHint()
    {
        if (!UseCustomJava)
        {
            JavaPathHint = "";
            IsJavaPathWarning = false;
            return;
        }

        if (SelectedJavaOption?.Type == JavaOptionType.Detected)
        {
            var o = SelectedJavaOption;
            JavaPathHint = $"Java {o.MajorVersion} · {o.Architecture} · {o.Source}";
            IsJavaPathWarning = false;
            return;
        }

        var path = CustomJavaPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            JavaPathHint = "请选择或输入 Java 可执行文件路径（留空则按全局设置）";
            IsJavaPathWarning = false;
            return;
        }

        if (!File.Exists(path))
        {
            JavaPathHint = "该路径不存在或不是有效的 Java 可执行文件，启动时将失败";
            IsJavaPathWarning = true;
            return;
        }

        JavaPathHint = "";
        IsJavaPathWarning = false;
    }

    [RelayCommand]
    private async Task BrowseJavaAsync()
    {
        try
        {
            var storageProvider = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.StorageProvider;
            if (storageProvider == null) return;

            var patterns = OperatingSystem.IsWindows()
                ? new[] { "javaw.exe", "java.exe" }
                : new[] { "java" };

            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择 Java 可执行文件",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Java 可执行文件") { Patterns = patterns } }
            });

            var file = files.FirstOrDefault();
            if (file == null) return;

            var path = file.Path.LocalPath;

            // 尝试读取版本信息用于展示（失败则仅按自定义路径处理）
            var info = await Task.Run(() => JavaOptionsProvider.Inspect(path)).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 若恰好命中已探测的 Java，直接选中对应条目，版本信息更完整
                var detected = JavaOptions.FirstOrDefault(x =>
                    x.Type == JavaOptionType.Detected &&
                    string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));

                UseCustomJava = true;
                CustomJavaPath = path;

                if (detected != null)
                {
                    SelectedJavaOption = detected;
                }
                else
                {
                    // 构造新的 Custom 实例（Equals 按 Type 匹配，仍会高亮列表中的「自定义路径...」条目）
                    var custom = new JavaOption
                    {
                        Type = JavaOptionType.Custom,
                        Display = info != null
                            ? $"自定义: Java {info.MajorVersion} ({info.Architecture}) - {info.Source}"
                            : "自定义路径..."
                    };
                    SelectedJavaOption = custom;
                }
            });
        }
        catch (Exception ex)
        {
            _notificationService.Show("浏览失败", ex.Message, NotificationType.Error);
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

        // 获取实例加载器类型用于判断
        var instanceLoader = _version?.LoaderType?.ToLowerInvariant() ?? "";

        if (Directory.Exists(modsDir))
        {
            foreach (var file in Directory.GetFiles(modsDir, "*.jar"))
            {
                try
                {
                    var meta = ObsMCLauncher.Core.Services.ModMetadataParser.ParseFromJar(file);
                    var effectiveLoader = DetermineEffectiveLoader(meta?.Loader ?? "", instanceLoader);
                    list.Add(new ModInfo
                    {
                        Name = meta?.Name ?? Path.GetFileNameWithoutExtension(file),
                        FileName = Path.GetFileName(file),
                        Path = file,
                        Size = new FileInfo(file).Length,
                        IsEnabled = true,
                        ModId = meta?.ModId ?? "",
                        Version = meta?.Version ?? "",
                        Loader = effectiveLoader,
                        IconPath = ExtractModIconWithMeta(file, meta)
                    });
                }
                catch { }
            }

            foreach (var file in Directory.GetFiles(modsDir, "*.jar.disabled"))
            {
                try
                {
                    var fileName = Path.GetFileName(file);
                    var meta = ObsMCLauncher.Core.Services.ModMetadataParser.ParseFromJar(file);
                    var effectiveLoader = DetermineEffectiveLoader(meta?.Loader ?? "", instanceLoader);
                    list.Add(new ModInfo
                    {
                        Name = meta?.Name ?? fileName,
                        FileName = fileName,
                        Path = file,
                        Size = new FileInfo(file).Length,
                        IsEnabled = false,
                        ModId = meta?.ModId ?? "",
                        Version = meta?.Version ?? "",
                        Loader = effectiveLoader,
                        IconPath = ExtractModIconWithMeta(file, meta)
                    });
                }
                catch { }
            }
        }

        // 冲突检测
        var conflicts = ObsMCLauncher.Core.Services.ModConflictDetector.DetectConflicts(modsDir);
        if (conflicts.Count > 0)
        {
            var conflictModIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var conflict in conflicts)
            {
                if (!string.IsNullOrEmpty(conflict.ModId1)) conflictModIds.Add(conflict.ModId1);
                if (!string.IsNullOrEmpty(conflict.ModId2)) conflictModIds.Add(conflict.ModId2);
            }

            foreach (var mod in list)
            {
                if (conflictModIds.Contains(mod.ModId))
                {
                    mod.HasConflict = true;
                    var relatedConflicts = conflicts
                        .Where(c => c.ModId1.Equals(mod.ModId, StringComparison.OrdinalIgnoreCase) ||
                                    c.ModId2.Equals(mod.ModId, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    mod.ConflictDescription = string.Join("\n", relatedConflicts.Select(c => c.Description));
                    mod.ConflictSuggestion = string.Join("\n",
                        relatedConflicts
                            .Where(c => !string.IsNullOrEmpty(c.Suggestion))
                            .Select(c => c.Suggestion));
                }
            }

            var errorCount = conflicts.Count(c => c.Severity == ConflictSeverity.Error);
            var warnCount = conflicts.Count(c => c.Severity == ConflictSeverity.Warning);
            if (errorCount > 0)
            {
                _notificationService.Show("模组冲突",
                    $"检测到 {errorCount} 个严重冲突和 {warnCount} 个警告，请查看模组列表中的标记",
                    NotificationType.Warning, 8);
            }
        }

        ApplyMods(list);
    }

    private static string DetermineEffectiveLoader(string modLoader, string instanceLoader)
    {
        if (string.IsNullOrEmpty(modLoader)) return "";

        // 某些模组（如Sodium等）同时支持 Fabric 和 Quilt，根据实例加载器显示
        var loaders = modLoader;
        if ((instanceLoader == "fabric" || instanceLoader == "quilt") &&
            (loaders.Equals("Fabric", StringComparison.OrdinalIgnoreCase) ||
             loaders.Equals("Quilt", StringComparison.OrdinalIgnoreCase)))
        {
            return char.ToUpper(instanceLoader[0]) + instanceLoader[1..];
        }

        return loaders;
    }

    private static string? ExtractModIconWithMeta(string jarPath, ModMetadata? meta)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            Directory.CreateDirectory(ModIconCacheDir);

            // 优先从元数据声明的图标路径提取
            if (meta?.IconPath != null)
            {
                var iconEntry = archive.GetEntry(meta.IconPath);
                if (iconEntry != null)
                {
                    var cacheName = $"{StableHash(jarPath)}_{meta.ModId}.png";
                    var tmpPath = Path.Combine(ModIconCacheDir, cacheName);
                    if (!File.Exists(tmpPath) || new FileInfo(tmpPath).Length != iconEntry.Length)
                        iconEntry.ExtractToFile(tmpPath, true);
                    return tmpPath;
                }
            }

            // 回退到常规图标路径
            string[] candidates = ["pack.png", "logo.png", "icon.png"];
            foreach (var candidate in candidates)
            {
                var entry = archive.GetEntry(candidate);
                if (entry != null)
                {
                    var cacheName = $"{StableHash(jarPath)}_{candidate}";
                    var tmpPath = Path.Combine(ModIconCacheDir, cacheName);
                    if (!File.Exists(tmpPath) || new FileInfo(tmpPath).Length != entry.Length)
                        entry.ExtractToFile(tmpPath, true);
                    return tmpPath;
                }
            }

            // 尝试 assets/<modid>/icon.png
            var modId = meta?.ModId;
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName;
                if (!string.IsNullOrEmpty(modId) &&
                    name.StartsWith($"assets/{modId}/", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith("/icon.png", StringComparison.OrdinalIgnoreCase))
                {
                    var cacheName = $"{StableHash(jarPath)}_{modId}.png";
                    var tmpPath = Path.Combine(ModIconCacheDir, cacheName);
                    if (!File.Exists(tmpPath) || new FileInfo(tmpPath).Length != entry.Length)
                        entry.ExtractToFile(tmpPath, true);
                    return tmpPath;
                }
            }

            // 最后兜底
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) &&
                    entry.FullName.EndsWith("/icon.png", StringComparison.OrdinalIgnoreCase))
                {
                    var cacheName = $"{StableHash(jarPath)}_asset.png";
                    var tmpPath = Path.Combine(ModIconCacheDir, cacheName);
                    if (!File.Exists(tmpPath) || new FileInfo(tmpPath).Length != entry.Length)
                        entry.ExtractToFile(tmpPath, true);
                    return tmpPath;
                }
            }
        }
        catch { }
        return null;
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

        public bool UseCustomJava { get; set; }
        public string CustomJavaPath { get; set; } = "";
        public string GlobalJavaText { get; set; } = "";
        public bool UseCustomJvm { get; set; }
        public string InstanceJvmArguments { get; set; } = "";
        public string GlobalJvmArguments { get; set; } = "";
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// 跨平台打开文件夹。Windows 用 explorer.exe，macOS 用 open，Linux 用 xdg-open。
    /// </summary>
    private static void OpenFolderInExplorer(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "explorer.exe"
                         : OperatingSystem.IsMacOS() ? "open"
                         : "xdg-open",
                Arguments = path,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
    }
}

public class WorldInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
    public DateTime LastModified { get; set; }
    public string GameVersion { get; set; } = "";
    public long WorldSizeBytes { get; set; }
    public string? IconPath { get; set; }

    public string WorldSizeDisplay => WorldSizeBytes switch
    {
        < 1024 => $"{WorldSizeBytes} B",
        < 1024 * 1024 => $"{WorldSizeBytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{WorldSizeBytes / (1024.0 * 1024):F1} MB",
        _ => $"{WorldSizeBytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}

public class ModInfo : ObservableObject
{
    private string _name = string.Empty;
    private string _fileName = string.Empty;
    private string _path = string.Empty;
    private long _size;
    private bool _isEnabled;
    private string? _iconPath;
    private string _modId = string.Empty;
    private string _version = string.Empty;
    private string _loader = string.Empty;
    private bool _hasConflict;
    private string _conflictDescription = string.Empty;
    private string _conflictSuggestion = string.Empty;

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

    /// <summary>
    /// 是否启用。setter 会同步重命名文件（追加/移除 .disabled 后缀）。
    /// 文件操作失败时抛出异常，由调用方处理；此时字段不更新，双向绑定会自动回滚 UI。
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;

            // 判断文件当前状态是否已与目标状态一致（初始化场景：加载时直接设置字段）
            bool fileAlreadyAtTarget = value
                ? !_path.EndsWith(".disabled")
                : _path.EndsWith(".disabled");

            if (fileAlreadyAtTarget)
            {
                // 文件已是目标状态，仅同步字段（如加载时设置已启用的 .jar 模组）
                _isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(DisplayName));
                return;
            }

            // 需要重命名文件
            string newPath = value
                ? _path[..^".disabled".Length]
                : _path + ".disabled";

            System.IO.File.Move(_path, newPath);

            _path = newPath;
            _isEnabled = value;
            _fileName = System.IO.Path.GetFileName(newPath);
            _name = value
                ? System.IO.Path.GetFileNameWithoutExtension(newPath)
                : System.IO.Path.GetFileName(newPath);

            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(Path));
        }
    }

    public string DisplayName => IsEnabled ? Name : Name.Replace(".jar.disabled", "").Replace(".disabled", "");

    public string? IconPath
    {
        get => _iconPath;
        set => SetProperty(ref _iconPath, value);
    }

    public string ModId
    {
        get => _modId;
        set => SetProperty(ref _modId, value);
    }

    public string Version
    {
        get => _version;
        set => SetProperty(ref _version, value);
    }

    public string Loader
    {
        get => _loader;
        set => SetProperty(ref _loader, value);
    }

    public bool HasConflict
    {
        get => _hasConflict;
        set => SetProperty(ref _hasConflict, value);
    }

    public string ConflictDescription
    {
        get => _conflictDescription;
        set => SetProperty(ref _conflictDescription, value);
    }

    public string ConflictSuggestion
    {
        get => _conflictSuggestion;
        set => SetProperty(ref _conflictSuggestion, value);
    }
}

public class ShaderPackInfo : ObservableObject
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

    public string? IconPath
    {
        get => _iconPath;
        set => SetProperty(ref _iconPath, value);
    }

    /// <summary>
    /// 是否启用。setter 会同步重命名文件（追加/移除 .disabled 后缀）。
    /// 文件操作失败时抛出异常，由调用方处理；此时字段不更新，双向绑定会自动回滚 UI。
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;

            bool fileAlreadyAtTarget = value
                ? !_path.EndsWith(".disabled")
                : _path.EndsWith(".disabled");

            if (fileAlreadyAtTarget)
            {
                _isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(DisplayName));
                return;
            }

            string newPath = value
                ? _path[..^".disabled".Length]
                : _path + ".disabled";

            System.IO.File.Move(_path, newPath);

            _path = newPath;
            _isEnabled = value;
            _fileName = System.IO.Path.GetFileName(newPath);
            _name = value
                ? System.IO.Path.GetFileNameWithoutExtension(newPath)
                : System.IO.Path.GetFileName(newPath);

            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(Path));
        }
    }

    public string DisplayName => IsEnabled ? Name : Name.Replace(".zip.disabled", "").Replace(".disabled", "");
}

/// <summary>
/// 材质包信息。启用/禁用通过追加/移除 .disabled 后缀实现。
/// </summary>
public class ResourcePackInfo : ObservableObject
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

    public string? IconPath
    {
        get => _iconPath;
        set => SetProperty(ref _iconPath, value);
    }

    /// <summary>
    /// 是否启用。setter 会同步重命名文件（追加/移除 .disabled 后缀）。
    /// 文件操作失败时抛出异常，由调用方处理；此时字段不更新，双向绑定会自动回滚 UI。
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;

            bool fileAlreadyAtTarget = value
                ? !_path.EndsWith(".disabled")
                : _path.EndsWith(".disabled");

            if (fileAlreadyAtTarget)
            {
                _isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(DisplayName));
                return;
            }

            string newPath = value
                ? _path[..^".disabled".Length]
                : _path + ".disabled";

            System.IO.File.Move(_path, newPath);

            _path = newPath;
            _isEnabled = value;
            _fileName = System.IO.Path.GetFileName(newPath);
            _name = value
                ? System.IO.Path.GetFileNameWithoutExtension(newPath)
                : System.IO.Path.GetFileName(newPath);

            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(Path));
        }
    }

    public string DisplayName => IsEnabled ? Name : Name.Replace(".zip.disabled", "").Replace(".disabled", "");
}

/// <summary>
/// 分组下拉框列表项模型
/// </summary>
public class GroupListItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
