using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Services.Minecraft;
using ObsMCLauncher.Core.Services.Modrinth;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.Views;

using ObsMCLauncher.Desktop.ViewModels.Dialogs;

namespace ObsMCLauncher.Desktop.ViewModels;

public class SortOption
{
    public string Name { get; set; } = "";
    public object Value { get; set; } = null!;
}

public partial class ResourcesViewModel : ViewModelBase
{
    private readonly ModrinthService _modrinth = new();
    private readonly ModTranslationService _translation = ModTranslationService.Instance;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<string>> _modrinthVersionCache = new();
    private readonly SemaphoreSlim _modrinthVersionsSemaphore = new(6, 6);

    // --- 状态定义 ---
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string _status = "输入关键词开始搜索";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private ResourceSource _currentSource = ResourceSource.CurseForge;
    [ObservableProperty] private string _currentResourceType = "Mods";
    [ObservableProperty] private string? _selectedVersionId;
    [ObservableProperty] private string _versionFilter = "全部版本";
    
    // 实际用于 API 的排序值
    private object _sortValue = 2; 

    [ObservableProperty] private int _currentSourceIndex = 0;
    [ObservableProperty] private int _sortFieldIndex = 0;

    [ObservableProperty] private bool _isViewReady;

    public ObservableCollection<ResourceItemViewModel> Results { get; } = new();
    public ObservableCollection<string> InstalledVersions { get; } = new();
    public ObservableCollection<string> VersionFilters { get; } = new() { "全部版本", "1.21", "1.20", "1.19", "1.18", "1.16", "1.12" };
    public ObservableCollection<SortOption> AvailableSortOptions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListVisible))]
    private ViewModelBase? _detailViewModel;

    public bool IsListVisible => DetailViewModel == null;

    public bool HasInstalledVersions => InstalledVersions.Count > 0;
    public string VersionHintText => HasInstalledVersions ? "" : "未检测到游戏版本，请先下载版本";

    private class ResourceTypeState
    {
        public string Query { get; set; } = "";
        public List<ResourceItemViewModel> CachedResults { get; set; } = new();
    }

    private readonly Dictionary<string, ResourceTypeState> _typeStates = new();

    public IAsyncRelayCommand SearchCommand { get; }
    public IRelayCommand<string> ChangeTypeCommand { get; }

    public IRelayCommand<ResourceItemViewModel> OpenDetailCommand { get; }

    public ResourcesViewModel()
    {
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsLoading);
        ChangeTypeCommand = new RelayCommand<string>(ChangeType);
        OpenDetailCommand = new RelayCommand<ResourceItemViewModel>(OpenDetail);

        // 初始化源索引
        CurrentSourceIndex = CurrentSource == ResourceSource.Modrinth ? 1 : 0;
        
        UpdateAvailableSortOptions();
        SortFieldIndex = 0;

        LoadInstalledVersions();
        _ = SearchAsync();
    }

    private void UpdateAvailableSortOptions()
    {
        AvailableSortOptions.Clear();
        if (CurrentSource == ResourceSource.CurseForge)
        {
            AvailableSortOptions.Add(new SortOption { Name = "最热门", Value = 2 });
            AvailableSortOptions.Add(new SortOption { Name = "下载量", Value = 6 });
            AvailableSortOptions.Add(new SortOption { Name = "最新更新", Value = 3 });
            AvailableSortOptions.Add(new SortOption { Name = "名称", Value = 4 });
        }
        else
        {
            AvailableSortOptions.Add(new SortOption { Name = "相关度", Value = "relevance" });
            AvailableSortOptions.Add(new SortOption { Name = "下载量", Value = "downloads" });
            AvailableSortOptions.Add(new SortOption { Name = "最新发布", Value = "newest" });
            AvailableSortOptions.Add(new SortOption { Name = "最近更新", Value = "updated" });
        }
    }

    partial void OnCurrentSourceIndexChanged(int value)
    {
        CurrentSource = value == 1 ? ResourceSource.Modrinth : ResourceSource.CurseForge;
        
        UpdateAvailableSortOptions();
        // 切换源时重置排序到第一个（通常是默认/最热门）
        SortFieldIndex = 0;
        
        if (IsViewReady && !IsLoading)
            _ = SearchAsync();
    }

    partial void OnSortFieldIndexChanged(int value)
    {
        if (value >= 0 && value < AvailableSortOptions.Count)
        {
            _sortValue = AvailableSortOptions[value].Value;
        }

        if (IsViewReady && !IsLoading)
        {
            _ = SearchAsync();
        }
    }

    private void LoadInstalledVersions()
    {
        try
        {
            InstalledVersions.Clear();
            var config = LauncherConfig.Load();
            var versionsDir = Path.Combine(config.GameDirectory, "versions");
            if (!Directory.Exists(versionsDir)) return;

            foreach (var dir in Directory.GetDirectories(versionsDir))
            {
                var name = Path.GetFileName(dir);
                if (File.Exists(Path.Combine(dir, $"{name}.json")))
                    InstalledVersions.Add(name);
            }

            if (!string.IsNullOrEmpty(config.SelectedVersion) && InstalledVersions.Contains(config.SelectedVersion))
                SelectedVersionId = config.SelectedVersion;
            else if (InstalledVersions.Count > 0)
                SelectedVersionId = InstalledVersions[0];

            OnPropertyChanged(nameof(HasInstalledVersions));
            OnPropertyChanged(nameof(VersionHintText));
        }
        catch (Exception ex)
        {
            Status = $"加载本地版本失败: {ex.Message}";
        }
    }

    private void ChangeType(string? type)
    {
        if (string.IsNullOrEmpty(type) || type == CurrentResourceType) return;

        SaveCurrentState();
        CurrentResourceType = type;

        if (_typeStates.TryGetValue(type, out var state))
        {
            Query = state.Query;
            Results.Clear();
            foreach (var item in state.CachedResults) Results.Add(item);
        }
        else
        {
            Query = "";
            Results.Clear();
            _ = SearchAsync();
        }
    }

    private void SaveCurrentState()
    {
        if (!_typeStates.ContainsKey(CurrentResourceType))
            _typeStates[CurrentResourceType] = new ResourceTypeState();

        var state = _typeStates[CurrentResourceType];
        state.Query = Query;
        state.CachedResults = Results.ToList();
    }

    public void OpenDetail(ResourceItemViewModel? item)
    {
        if (item?.RawData == null) return;
        var selectedVersion = SelectedVersionId ?? LauncherConfig.Load().SelectedVersion ?? string.Empty;
        DetailViewModel = new ModDetailViewModel(item.RawData, selectedVersion, CurrentResourceType, () => DetailViewModel = null);
    }

    private async Task SearchAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            Status = "正在从云端获取资源...";
            Results.Clear();

            string gameVersion = "";
            if (VersionFilter != "全部版本") gameVersion = VersionFilter;
            else if (!string.IsNullOrEmpty(SelectedVersionId)) gameVersion = ExtractGameVersion(SelectedVersionId);

            if (CurrentSource == ResourceSource.CurseForge)
                await SearchCurseForge(gameVersion).ConfigureAwait(false);
            else
                await SearchModrinth(gameVersion).ConfigureAwait(false);

            Status = Results.Count > 0 ? $"找到 {Results.Count} 个资源" : "未找到匹配资源";
        }
        catch (Exception ex)
        {
            Status = $"搜索异常: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string ExtractGameVersion(string versionId)
    {
        var match = System.Text.RegularExpressions.Regex.Match(versionId, @"^(\d+\.\d+(?:\.\d+)?)");
        return match.Success ? match.Groups[1].Value : "";
    }

    private async Task SearchCurseForge(string gameVersion)
    {
        int classId = CurrentResourceType switch
        {
            "Mods" => 6,
            "Textures" => 12,
            "Shaders" => 6552,
            "Modpacks" => 4471,
            "Datapacks" => 6945,
            _ => 6
        };

        var response = await CurseForgeService.SearchModsAsync(
            searchFilter: Query,
            gameVersion: gameVersion,
            classId: classId,
            sortField: _sortValue is int i ? i : 2
        ).ConfigureAwait(false);

        if (response?.Data == null) return;

        foreach (var mod in response.Data)
        {
            var translation = _translation.GetTranslationByCurseForgeId(mod.Slug)
                           ?? _translation.GetTranslationByCurseForgeId(mod.Id);

            var item = new ResourceItemViewModel(mod, translation);
            Results.Add(item);
            _ = item.LoadIconAsync();
        }
    }

    private async Task SearchModrinth(string gameVersion)
    {
        string projectType = CurrentResourceType switch
        {
            "Mods" => "mod",
            "Textures" => "resourcepack",
            "Shaders" => "shader",
            "Modpacks" => "modpack",
            "Datapacks" => "datapack",
            _ => "mod"
        };

        var response = await _modrinth.SearchModsAsync(
            searchQuery: Query,
            gameVersion: gameVersion,
            projectType: projectType,
            sortBy: _sortValue is string s ? s : "relevance"
        ).ConfigureAwait(false);

        if (response?.Hits == null) return;

        foreach (var hit in response.Hits)
        {
            var translation = _translation.GetTranslationByCurseForgeId(hit.ProjectId);

            var item = new ResourceItemViewModel(hit, translation);
            Results.Add(item);

            _ = item.LoadIconAsync();
            _ = LoadModrinthVersionsAsync(item, hit.ProjectId);
        }
    }

    private async Task LoadModrinthVersionsAsync(ResourceItemViewModel item, string projectId)
    {
        try
        {
            if (_modrinthVersionCache.TryGetValue(projectId, out var cached))
            {
                item.UpdateVersions(cached);
                return;
            }

            await _modrinthVersionsSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_modrinthVersionCache.TryGetValue(projectId, out cached))
                {
                    item.UpdateVersions(cached);
                    return;
                }

                var versions = await _modrinth.GetProjectVersionsAsync(projectId).ConfigureAwait(false);
                if (versions == null || versions.Count == 0)
                {
                    _modrinthVersionCache.TryAdd(projectId, new List<string>());
                    return;
                }

                var mcVersions = versions
                    .SelectMany(v => v.GameVersions ?? new List<string>())
                    .Distinct()
                    .ToList();

                var sorted = VersionUtils.FilterAndSortVersions(mcVersions);

                _modrinthVersionCache.TryAdd(projectId, sorted);
                item.UpdateVersions(sorted);
            }
            finally
            {
                _modrinthVersionsSemaphore.Release();
            }
        }
        catch
        {
        }
    }

    private static string DetectVersionType(string versionName)
    {
        var lower = (versionName ?? string.Empty).ToLowerInvariant();

        if (lower.Contains("forge") && !lower.Contains("neoforge")) return "forge";
        if (lower.Contains("neoforge")) return "neoforge";
        if (lower.Contains("fabric")) return "fabric";
        if (lower.Contains("quilt")) return "quilt";
        if (lower.Contains("optifine")) return "optifine";

        return "vanilla";
    }

    private bool ValidateModsTargetVersion(out string versionName, out string? error)
    {
        versionName = SelectedVersionId ?? LauncherConfig.Load().SelectedVersion ?? string.Empty;

        if (string.IsNullOrWhiteSpace(versionName))
        {
            error = "请先选择要安装到的游戏版本";
            return false;
        }

        var type = DetectVersionType(versionName);
        if (type == "vanilla" || type == "optifine")
        {
            error = "所选版本不支持安装MOD，请选择Forge、Fabric、NeoForge或Quilt版本";
            return false;
        }

        error = null;
        return true;
    }

    private string GetTargetDirectory(LauncherConfig config)
    {
        var version = SelectedVersionId ?? config.SelectedVersion ?? "";
        
        // 获取实际运行目录（处理版本隔离）
        var runDir = config.GetRunDirectory(version);

        return CurrentResourceType switch
        {
            "Mods" => config.GetModsDirectory(version),
            "Textures" => config.GetResourcePacksDirectory(version),
            "Shaders" => config.GetShaderPacksDirectory(version),
            "Datapacks" => Path.Combine(runDir, "saves"), // 默认打开 saves 目录供选择具体存档
            "Modpacks" => Path.Combine(config.GetDataDirectory(), "downloads", "modpacks"),
            _ => Path.Combine(config.GetDataDirectory(), "downloads")
        };
    }

    [RelayCommand]
    private async Task DownloadAsync(ResourceItemViewModel item)
    {
        try
        {
            var config = LauncherConfig.Load();
            var gameVersion = VersionFilter != "全部版本" ? VersionFilter : (ExtractGameVersion(SelectedVersionId ?? config.SelectedVersion ?? ""));

            if (CurrentResourceType == "Modpacks")
            {
                var mainWindow = NavigationStore.MainWindow;
                if (mainWindow == null) return;

                // 统一整合包下载逻辑：确认版本名 -> 下载到 versions 根目录 -> 安装
                var (dialogResult, versionName) = await mainWindow.Dialogs.ShowInputAsync(
                    "安装整合包",
                    "请输入安装后的版本名称：",
                    item.Title,
                    "版本名称");

                if (dialogResult != DialogResult.OK || string.IsNullOrWhiteSpace(versionName))
                {
                    Status = "已取消整合包安装";
                    return;
                }

                // 获取整合包文件信息
                string fileName = "";
                if (item.RawData is CurseForgeMod cf)
                {
                    var fileInfo = await GetCfLatestFile(cf, gameVersion);
                    fileName = fileInfo?.FileName ?? $"{item.Title}.zip";
                }
                else if (item.RawData is ModrinthSearchHit mh)
                {
                    var versions = await _modrinth.GetProjectVersionsAsync(mh.ProjectId);
                    fileName = versions?.FirstOrDefault()?.Files?.FirstOrDefault()?.Filename ?? $"{item.Title}.mrpack";
                }

                if (string.IsNullOrEmpty(fileName))
                {
                    Status = "无法获取整合包文件信息";
                    return;
                }

                // 下载到 versions 根目录
                var versionsDir = Path.Combine(config.GameDirectory, "versions");
                if (!Directory.Exists(versionsDir)) Directory.CreateDirectory(versionsDir);
                var savePath = Path.Combine(versionsDir, fileName);

                Status = $"正在下载整合包: {fileName}";
                
                // 先下载zip到 versions 根目录，再安装
                await DownloadAndInstallModpackAsync(item.RawData, savePath, gameVersion, versionName);
                return;
            }

            // 原有的 MOD/资源包/光影 下载逻辑 (保持另存为)
            var defaultDir = GetTargetDirectory(config);
            var defaultFileName = "";

            // 1. 预获取文件名
            if (item.RawData is CurseForgeMod cfModForName)
            {
                var fileInfo = await GetCfLatestFile(cfModForName, gameVersion);
                defaultFileName = fileInfo?.FileName ?? $"{item.Title}.jar";
            }
            else if (item.RawData is ModrinthSearchHit mhForName)
            {
                var versions = await _modrinth.GetProjectVersionsAsync(mhForName.ProjectId);
                defaultFileName = versions?.FirstOrDefault()?.Files?.FirstOrDefault()?.Filename ?? $"{item.Title}.jar";
            }

            // 2. 弹出保存文件对话框
            var savePathNormal = await ShowSaveFileDialogAsync(defaultDir, defaultFileName);
            if (string.IsNullOrEmpty(savePathNormal))
            {
                Status = "已取消下载";
                return;
            }

            var finalDir = Path.GetDirectoryName(savePathNormal)!;
            var finalFileName = Path.GetFileName(savePathNormal);

            // 3. 执行下载
            if (item.RawData is CurseForgeMod cfMod)
            {
                Status = $"正在下载: {finalFileName}";
                await CurseForgeDownloadService.DownloadLatestAsync(cfMod, finalDir, gameVersion).ConfigureAwait(false);
                Status = $"下载完成: {finalFileName}";
            }
            else if (item.RawData is ModrinthSearchHit mhHit)
            {
                Status = $"正在下载: {finalFileName}";
                await ModrinthDownloadService.DownloadLatestModAsync(mhHit.ProjectId, finalDir, CancellationToken.None).ConfigureAwait(false);
                Status = $"下载完成: {finalFileName}";
            }
        }
        catch (Exception ex)
        {
            Status = $"下载失败: {ex.Message}";
        }
    }

    private async Task<CurseForgeFile?> GetCfLatestFile(CurseForgeMod mod, string? gameVersion)
    {
        if (!string.IsNullOrEmpty(gameVersion))
        {
            var index = mod.LatestFilesIndexes
                .Where(i => string.Equals(i.GameVersion, gameVersion, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(i => i.ReleaseType)
                .FirstOrDefault();
            if (index != null)
                return await CurseForgeService.GetModFileInfoAsync(mod.Id, index.FileId).ConfigureAwait(false);
        }
        return mod.LatestFiles.FirstOrDefault();
    }

    private async Task<string?> ShowSaveFileDialogAsync(string defaultDir, string defaultFileName)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var storage = desktop.MainWindow?.StorageProvider;
            if (storage == null) return null;

            var options = new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "保存资源",
                SuggestedFileName = defaultFileName,
                SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(new Uri(Path.GetFullPath(defaultDir)))
            };

            var file = await storage.SaveFilePickerAsync(options);
            return file?.Path.LocalPath;
        }
        return null;
    }

    private async Task DownloadAndInstallModpackAsync(object rawData, string savePath, string? gameVersion, string versionName)
    {
        string? taskId = null;
        CancellationTokenSource cts = new();
        try
        {
            var config = LauncherConfig.Load();
            var fileName = Path.GetFileName(savePath);
            
            var task = Core.Services.Download.DownloadTaskManager.Instance.AddTask(
                $"安装整合包: {versionName}",
                Core.Services.Download.DownloadTaskType.Version,
                cts);
            taskId = task.Id;

            // 1. 下载阶段 (0-50%)
            Status = $"正在下载整合包...";
            bool downloadOk = false;

            if (rawData is CurseForgeMod cf)
            {
                var fileInfo = await GetCfLatestFile(cf, gameVersion);
                if (fileInfo == null) throw new Exception("无法获取整合包文件信息");
                
                var progress = new Progress<int>(p => 
                    Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(taskId, p * 0.5, $"正在下载: {fileName} ({p}%)"));
                
                downloadOk = await CurseForgeService.DownloadModFileAsync(fileInfo, savePath, progress, cts.Token);
            }
            else if (rawData is ModrinthSearchHit mh)
            {
                var versions = await _modrinth.GetProjectVersionsAsync(mh.ProjectId);
                var file = versions?.FirstOrDefault()?.Files?.FirstOrDefault();
                if (file == null) throw new Exception("无法获取整合包下载地址");

                var progress = new Progress<int>(p => 
                    Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(taskId, p * 0.5, $"正在下载: {fileName} ({p}%)"));
                
                // 这里复用核心下载服务
                await Core.Services.Download.HttpDownloadService.DownloadFileToPathAsync(file.Url, savePath, taskId, cts.Token);
                downloadOk = true;
            }

            if (!downloadOk || cts.Token.IsCancellationRequested)
            {
                if (File.Exists(savePath)) try { File.Delete(savePath); } catch { }
                Core.Services.Download.DownloadTaskManager.Instance.FailTask(taskId, "下载中断或失败");
                Status = "整合包下载失败";
                return;
            }

            // 2. 安装阶段 (50-100%)
            Status = $"正在安装整合包: {versionName}";
            Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 50, "正在解压并安装...");

            await ModpackInstallService.InstallModpackAsync(
                savePath,
                versionName,
                config.GameDirectory,
                (msg, progress) =>
                {
                    var totalProgress = 50 + (progress * 0.5);
                    Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(taskId, totalProgress, msg);
                }
            );

            Core.Services.Download.DownloadTaskManager.Instance.CompleteTask(taskId);
            Status = $"整合包 {versionName} 安装成功！";
        }
        catch (Exception ex)
        {
            if (taskId != null)
                Core.Services.Download.DownloadTaskManager.Instance.FailTask(taskId, ex.Message);
            Status = $"安装失败: {ex.Message}";
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task HandleModpackInstallation(string zipPath, string versionName)
    {
        // 此方法已由 DownloadAndInstallModpackAsync 替代，保留签名以防万一但内部留空或删除
        await Task.CompletedTask;
    }

    private static async Task<string?> ShowModpackInstallDialogAsync(string defaultName)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new ModpackInstallDialogViewModel(defaultName);
            var dialog = new ModpackInstallDialog { DataContext = viewModel };

            var tcs = new TaskCompletionSource<string?>();
            viewModel.CloseRequested = name =>
            {
                dialog.Close();
                tcs.TrySetResult(name);
            };

            await dialog.ShowDialog(desktop.MainWindow!);
            return await tcs.Task;
        }
        return null;
    }

    private static async Task<string?> ShowWorldSelectDialogAsync(string savesDir)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new WorldSelectDialogViewModel(savesDir);
            var dialog = new WorldSelectDialog { DataContext = viewModel };

            var tcs = new TaskCompletionSource<string?>();
            viewModel.CloseRequested = world =>
            {
                dialog.Close();
                tcs.TrySetResult(world?.Path);
            };

            await dialog.ShowDialog(desktop.MainWindow!);
            return await tcs.Task;
        }
        return null;
    }
}

public partial class ResourceItemViewModel : ObservableObject
{
    public string Id { get; }
    public string Title { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string Author { get; }
    public string IconUrl { get; }
    public string Downloads { get; }

    [ObservableProperty]
    private List<string> versions = new();

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? icon;

    public string VersionDisplay
    {
        get
        {
            if (Versions.Count == 0) return string.Empty;
            if (Versions.Count <= 5) return string.Join(", ", Versions);
            return string.Join(", ", Versions.Take(3).Concat(new[] { "...", Versions[^1] }));
        }
    }

    public object RawData { get; }

    public ResourceItemViewModel(CurseForgeMod mod, ModTranslation? translation)
    {
        Id = mod.Id.ToString();
        Title = mod.Name;
        DisplayName = ModTranslationService.Instance.GetDisplayName(mod.Name, translation);
        Description = mod.Summary;
        Author = mod.Authors.FirstOrDefault()?.Name ?? "未知";
        IconUrl = mod.Logo?.ThumbnailUrl ?? "";
        Downloads = CurseForgeService.FormatDownloadCount(mod.DownloadCount);
        Versions = VersionUtils.FilterAndSortVersions(mod.LatestFilesIndexes.Select(f => f.GameVersion));
        RawData = mod;
        OnPropertyChanged(nameof(VersionDisplay));
    }

    public ResourceItemViewModel(ModrinthSearchHit hit, ModTranslation? translation)
    {
        Id = hit.ProjectId;
        Title = hit.Title;
        DisplayName = ModTranslationService.Instance.GetDisplayName(hit.Title, translation);
        Description = hit.Description ?? string.Empty;
        Author = hit.Author ?? "未知";
        IconUrl = hit.IconUrl ?? "";
        Downloads = hit.Downloads.ToString();
        Versions = new List<string>();
        RawData = hit;
        OnPropertyChanged(nameof(VersionDisplay));
    }

    public void UpdateVersions(List<string> v)
    {
        Versions = v;
        OnPropertyChanged(nameof(VersionDisplay));
    }

    public async Task LoadIconAsync()
    {
        if (Icon != null) return;
        var path = await ImageCacheService.GetImagePathAsync(IconUrl);
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                Icon = new Avalonia.Media.Imaging.Bitmap(path);
            }
            catch
            {
                // 加载失败逻辑
            }
        }
    }
}
