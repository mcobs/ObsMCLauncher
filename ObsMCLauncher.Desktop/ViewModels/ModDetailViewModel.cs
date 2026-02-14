using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Services.Minecraft;
using ObsMCLauncher.Core.Services.Modrinth;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.ViewModels.Dialogs;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class ModDetailViewModel : ViewModelBase
{
    private readonly ModrinthService _modrinth = new();
    private readonly CancellationTokenSource _cts = new();

    public object RawData { get; }
    public string SelectedVersionId { get; }
    public string ResourceType { get; }
    private readonly Action? _onBack;

    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _authorDisplay = "";
    [ObservableProperty] private string _downloadsDisplay = "";
    [ObservableProperty] private string _lastUpdateDisplay = "";
    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVersionsVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmptyVisible))]
    private bool _isLoading = true;

    public bool IsVersionsVisible => !IsLoading && HasAnyGroup;
    public bool IsEmptyVisible => !IsLoading && !HasAnyGroup;

    public ObservableCollection<VersionGroupViewModel> VersionGroups { get; } = new();
    public bool HasAnyGroup => VersionGroups.Count > 0;

    public IRelayCommand BackCommand { get; }
    public IAsyncRelayCommand<VersionEntryViewModel> DownloadVersionCommand { get; }

    public ModDetailViewModel(object rawData, string selectedVersionId, string resourceType, Action? onBack = null)
    {
        RawData = rawData;
        SelectedVersionId = selectedVersionId;
        ResourceType = resourceType;
        _onBack = onBack;

        BackCommand = new RelayCommand(Back);
        DownloadVersionCommand = new AsyncRelayCommand<VersionEntryViewModel>(DownloadVersionAsync);

        LoadHeader();
        _ = LoadVersionsAsync();
    }

    private void Back()
    {
        _cts.Cancel();
        _onBack?.Invoke();
    }

    private void LoadHeader()
    {
        if (RawData is CurseForgeMod cf)
        {
            var translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(cf.Slug)
                              ?? ModTranslationService.Instance.GetTranslationByCurseForgeId(cf.Id);
            DisplayName = ModTranslationService.Instance.GetDisplayName(cf.Name, translation);
            Summary = cf.Summary;
            AuthorDisplay = cf.Authors.Count > 0
                ? $"作者: {string.Join(", ", cf.Authors.Select(a => a.Name))}"
                : "作者: 未知";
            DownloadsDisplay = $"下载量: {CurseForgeService.FormatDownloadCount(cf.DownloadCount)}";
            LastUpdateDisplay = $"更新: {cf.DateModified:yyyy-MM-dd}";

            _ = LoadIconAsync(cf.Logo?.Url);
        }
        else if (RawData is ModrinthSearchHit hit)
        {
            var translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(hit.ProjectId);
            DisplayName = ModTranslationService.Instance.GetDisplayName(hit.Title, translation);
            Summary = hit.Description ?? string.Empty;
            AuthorDisplay = $"作者: {hit.Author ?? "未知"}";
            DownloadsDisplay = $"下载量: {CurseForgeService.FormatDownloadCount(hit.Downloads)}";
            LastUpdateDisplay = string.Empty;

            _ = LoadIconAsync(hit.IconUrl);
        }
    }

    private async Task LoadIconAsync(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        var path = await ImageCacheService.GetImagePathAsync(url);
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                Icon = new Bitmap(path);
            }
            catch
            {
            }
        }
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            IsLoading = true;
            VersionGroups.Clear();

            if (RawData is CurseForgeMod cf)
            {
                await LoadCurseForgeVersionsAsync(cf, _cts.Token);
            }
            else if (RawData is ModrinthSearchHit hit)
            {
                await LoadModrinthVersionsAsync(hit, _cts.Token);
            }
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasAnyGroup));
        }
    }

    private async Task LoadCurseForgeVersionsAsync(CurseForgeMod mod, CancellationToken cancellationToken)
    {
        int pageIndex = 0;
        const int pageSize = 50;
        int totalCount = 0;
        bool isFirst = true;
        var allFiles = new List<CurseForgeFile>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await CurseForgeService.GetModFilesAsync(mod.Id, pageIndex: pageIndex, pageSize: pageSize)
                .ConfigureAwait(false);
            if (result?.Data == null || result.Data.Count == 0) break;

            allFiles.AddRange(result.Data);

            if (isFirst && result.Pagination != null)
            {
                totalCount = result.Pagination.TotalCount;
                isFirst = false;
            }

            pageIndex++;
            if (totalCount > 0 && allFiles.Count >= totalCount) break;
        }

        if (allFiles.Count == 0) return;

        var sorted = allFiles.OrderByDescending(f => f.FileDate).ToList();
        var groups = GroupCurseForgeFiles(sorted);
        foreach (var g in groups)
        {
            VersionGroups.Add(g);
        }
    }

    private List<VersionGroupViewModel> GroupCurseForgeFiles(List<CurseForgeFile> files)
    {
        var grouped = new Dictionary<string, List<CurseForgeFile>>();

        foreach (var file in files)
        {
            if (file.GameVersions == null || file.GameVersions.Count == 0)
            {
                if (!grouped.ContainsKey("未知版本")) grouped["未知版本"] = new List<CurseForgeFile>();
                grouped["未知版本"].Add(file);
                continue;
            }

            var mcVersions = VersionUtils.ExtractAllMinecraftVersions(file.GameVersions);
            if (mcVersions.Count == 0)
            {
                if (!grouped.ContainsKey("其他版本")) grouped["其他版本"] = new List<CurseForgeFile>();
                grouped["其他版本"].Add(file);
                continue;
            }

            foreach (var v in mcVersions)
            {
                if (!grouped.ContainsKey(v)) grouped[v] = new List<CurseForgeFile>();
                grouped[v].Add(file);
            }
        }

        var sortedKeys = grouped.Keys.OrderByDescending(v => v, new MinecraftVersionComparer()).ToList();
        var result = new List<VersionGroupViewModel>();
        for (int i = 0; i < sortedKeys.Count; i++)
        {
            var key = sortedKeys[i];
            var filesInGroup = grouped[key];
            var isLatest = i == 0 && key != "未知版本" && key != "其他版本";

            var groupVm = new VersionGroupViewModel
            {
                McVersion = key,
                IsLatest = isLatest
            };

            foreach (var f in filesInGroup)
            {
                var mcVersions = VersionUtils.ExtractAllMinecraftVersions(f.GameVersions ?? new List<string>());
                var mcDisplay = mcVersions.Count <= 3
                    ? string.Join(", ", mcVersions)
                    : $"{mcVersions[0]} ~ {mcVersions[^1]}";

                groupVm.Files.Add(new VersionEntryViewModel
                {
                    BackendType = VersionBackendType.CurseForge,
                    CurseForgeFile = f,
                    Name = f.DisplayName,
                    DateDisplay = f.FileDate.ToString("yyyy-MM-dd HH:mm"),
                    McVersionsDisplay = string.IsNullOrEmpty(mcDisplay) ? string.Empty : $"适用: {mcDisplay}",
                    SizeDisplay = FormatFileSize(f.FileLength)
                });
            }

            result.Add(groupVm);
        }

        return result;
    }

    private async Task LoadModrinthVersionsAsync(ModrinthSearchHit hit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var versions = await _modrinth.GetProjectVersionsAsync(hit.ProjectId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (versions == null || versions.Count == 0) return;

        var sorted = versions.ToList();

        var grouped = new Dictionary<string, List<ModrinthVersion>>();
        foreach (var v in sorted)
        {
            if (v.GameVersions == null || v.GameVersions.Count == 0)
            {
                if (!grouped.ContainsKey("未知版本")) grouped["未知版本"] = new List<ModrinthVersion>();
                grouped["未知版本"].Add(v);
                continue;
            }

            var mcVersions = VersionUtils.ExtractAllMinecraftVersions(v.GameVersions);
            if (mcVersions.Count == 0)
            {
                if (!grouped.ContainsKey("其他版本")) grouped["其他版本"] = new List<ModrinthVersion>();
                grouped["其他版本"].Add(v);
                continue;
            }

            foreach (var mc in mcVersions)
            {
                if (!grouped.ContainsKey(mc)) grouped[mc] = new List<ModrinthVersion>();
                grouped[mc].Add(v);
            }
        }

        var sortedKeys = grouped.Keys.OrderByDescending(v => v, new MinecraftVersionComparer()).ToList();
        for (int i = 0; i < sortedKeys.Count; i++)
        {
            var key = sortedKeys[i];
            var list = grouped[key];
            var isLatest = i == 0 && key != "未知版本" && key != "其他版本";

            var groupVm = new VersionGroupViewModel
            {
                McVersion = key,
                IsLatest = isLatest
            };

            foreach (var v in list)
            {
                if (v.Files == null || v.Files.Count == 0) continue;
                var file = v.Files[0];

                var mcVersions = VersionUtils.ExtractAllMinecraftVersions(v.GameVersions ?? new List<string>());
                var mcDisplay = mcVersions.Count <= 3
                    ? string.Join(", ", mcVersions)
                    : $"{mcVersions[0]} ~ {mcVersions[^1]}";

                groupVm.Files.Add(new VersionEntryViewModel
                {
                    BackendType = VersionBackendType.Modrinth,
                    ModrinthVersion = v,
                    ModrinthFile = file,
                    Name = v.Name,
                    DateDisplay = string.Empty,
                    McVersionsDisplay = string.IsNullOrEmpty(mcDisplay) ? string.Empty : $"适用: {mcDisplay}",
                    SizeDisplay = FormatFileSize(file.Size)
                });
            }

            VersionGroups.Add(groupVm);
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private async Task DownloadVersionAsync(VersionEntryViewModel? entry)
    {
        if (entry == null) return;

        if (ResourceType == "Modpacks")
        {
            await HandleModpackInstallation(entry);
            return;
        }

        if (entry.BackendType == VersionBackendType.CurseForge && entry.CurseForgeFile != null)
        {
            await DownloadCurseForgeAsync(entry.CurseForgeFile);
        }
        else if (entry.BackendType == VersionBackendType.Modrinth && entry.ModrinthVersion != null && entry.ModrinthFile != null)
        {
            await DownloadModrinthAsync(entry.ModrinthVersion, entry.ModrinthFile);
        }
    }

    private async Task HandleModpackInstallation(VersionEntryViewModel entry)
    {
        var defaultName = entry.Name;
        var mainWindow = NavigationStore.MainWindow;
        if (mainWindow == null) return;

        // 使用扩展后的 DialogSystem 获取版本名
        var (dialogResult, versionName) = await mainWindow.Dialogs.ShowInputAsync(
            "安装整合包",
            "请输入安装后的版本名称：",
            defaultName,
            "版本名称");

        if (dialogResult != DialogResult.OK || string.IsNullOrWhiteSpace(versionName)) return;

        CancellationTokenSource? cts = null;
        string? taskId = null;
        try
        {
            var config = LauncherConfig.Load();
            
            // 修正下载路径：下载到 versions 根目录下
            var versionsDir = Path.Combine(config.GameDirectory, "versions");
            if (!Directory.Exists(versionsDir)) Directory.CreateDirectory(versionsDir);

            string fileName = "";
            string? downloadUrl = null;

            if (entry.BackendType == VersionBackendType.CurseForge && entry.CurseForgeFile != null)
            {
                fileName = entry.CurseForgeFile.FileName;
                downloadUrl = entry.CurseForgeFile.DownloadUrl;
            }
            else if (entry.BackendType == VersionBackendType.Modrinth && entry.ModrinthFile != null)
            {
                fileName = entry.ModrinthFile.Filename;
                downloadUrl = entry.ModrinthFile.Url;
            }

            if (string.IsNullOrEmpty(fileName)) return;
            
            // zip 文件直接放在 versions/xxx.zip
            var savePath = Path.Combine(versionsDir, fileName);

            cts = new CancellationTokenSource();
            var task = Core.Services.Download.DownloadTaskManager.Instance.AddTask(
                $"安装整合包: {versionName}",
                Core.Services.Download.DownloadTaskType.Version,
                cts);
            taskId = task.Id;

            // 1. 下载阶段 (占 0-50% 进度)
            Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 0, $"正在下载整合包...");
            
            bool downloadOk = false;
            if (entry.BackendType == VersionBackendType.CurseForge && entry.CurseForgeFile != null)
            {
                var progress = new Progress<int>(p => 
                    Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(taskId, p * 0.5, $"正在下载: {fileName} ({p}%)"));
                downloadOk = await CurseForgeService.DownloadModFileAsync(entry.CurseForgeFile, savePath, progress, cts.Token);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(downloadUrl))
                    throw new Exception("无法获取整合包下载地址");

                var progress = new Progress<int>(p =>
                    Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(taskId, p * 0.5, $"正在下载: {fileName} ({p}%)"));
                downloadOk = await DownloadByUrlAsync(downloadUrl, savePath, fileName, cts.Token);
            }

            if (!downloadOk || cts.Token.IsCancellationRequested)
            {
                if (File.Exists(savePath)) try { File.Delete(savePath); } catch { }
                Core.Services.Download.DownloadTaskManager.Instance.FailTask(taskId, "下载中断或失败");
                return;
            }

            // 2. 安装阶段 (占 50-100% 进度)
            Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 50, "正在安装整合包...");
            
            await ModpackInstallService.InstallModpackAsync(
                savePath,
                versionName,
                config.GameDirectory,
                (msg, progress) =>
                {
                    // 映射安装进度 0-100 到 50-100
                    var totalProgress = 50 + (progress * 0.5);
                    Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(taskId, totalProgress, msg);
                }
            );

            Core.Services.Download.DownloadTaskManager.Instance.CompleteTask(taskId);
        }
        catch (Exception ex)
        {
            if (taskId != null)
                Core.Services.Download.DownloadTaskManager.Instance.FailTask(taskId, ex.Message);
        }
        finally
        {
            cts?.Dispose();
        }
    }

    private static async Task<string?> ShowModpackInstallDialogAsync(string defaultName)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new ModpackInstallDialogViewModel(defaultName);
            var dialog = new Views.ModpackInstallDialog { DataContext = viewModel };

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

    private string GetTargetDirectory(LauncherConfig config)
    {
        var version = SelectedVersionId ?? "";
        var runDir = config.GetRunDirectory(version);

        return ResourceType switch
        {
            "Mods" => config.GetModsDirectory(version),
            "Textures" => config.GetResourcePacksDirectory(version),
            "Shaders" => config.GetShaderPacksDirectory(version),
            "Datapacks" => Path.Combine(runDir, "saves"),
            "Modpacks" => Path.Combine(config.GetDataDirectory(), "downloads", "modpacks"),
            _ => Path.Combine(config.GetDataDirectory(), "downloads")
        };
    }

    private async Task<string?> ShowSaveFileDialogAsync(string defaultDir, string defaultFileName)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var storage = desktop.MainWindow?.StorageProvider;
            if (storage == null) return null;

            if (!Directory.Exists(defaultDir))
            {
                Directory.CreateDirectory(defaultDir);
            }

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

    private async Task DownloadCurseForgeAsync(CurseForgeFile file)
    {
        CancellationTokenSource? cts = null;
        string? taskId = null;
        try
        {
            var config = LauncherConfig.Load();
            var defaultDir = GetTargetDirectory(config);
            
            var savePath = await ShowSaveFileDialogAsync(defaultDir, file.FileName);
            if (string.IsNullOrEmpty(savePath)) return;

            var finalDir = Path.GetDirectoryName(savePath)!;

            var resourceName = DisplayName;
            cts = new CancellationTokenSource();
            var downloadTask = Core.Services.Download.DownloadTaskManager.Instance.AddTask(resourceName, Core.Services.Download.DownloadTaskType.Resource, cts);
            taskId = downloadTask.Id;

            int lastReported = -1;
            var progress = new Progress<int>(p =>
            {
                if (p >= 100 || p - lastReported >= 5)
                {
                    if (taskId != null)
                    {
                        Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(taskId, p, file.FileName);
                    }
                    lastReported = p;
                }
            });

            var success = await CurseForgeService.DownloadModFileAsync(file, savePath, progress, cts.Token).ConfigureAwait(false);

            if (cts.Token.IsCancellationRequested)
            {
                if (taskId != null)
                    Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 0, "已取消");
                if (File.Exists(savePath))
                {
                    try { File.Delete(savePath); } catch { }
                }
                return;
            }

            if (success)
            {
                if (taskId != null)
                    Core.Services.Download.DownloadTaskManager.Instance.CompleteTask(taskId);
            }
            else
            {
                if (taskId != null)
                    Core.Services.Download.DownloadTaskManager.Instance.FailTask(taskId, "下载失败");
            }
        }
        catch
        {
            if (taskId != null)
                Core.Services.Download.DownloadTaskManager.Instance.FailTask(taskId, "下载失败");
        }
        finally
        {
            cts?.Dispose();
        }
    }

    private async Task<bool> DownloadByUrlAsync(string url, string savePath, string fileName, CancellationToken token)
    {
        var resourceName = DisplayName;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var task = Core.Services.Download.DownloadTaskManager.Instance.AddTask(resourceName, Core.Services.Download.DownloadTaskType.Resource, cts);

        try
        {
            await Core.Services.Download.HttpDownloadService.DownloadFileToPathAsync(url, savePath, task.Id, cts.Token).ConfigureAwait(false);
            Core.Services.Download.DownloadTaskManager.Instance.CompleteTask(task.Id);
            return true;
        }
        catch (OperationCanceledException)
        {
            Core.Services.Download.DownloadTaskManager.Instance.CancelTask(task.Id);
            return false;
        }
        catch (Exception ex)
        {
            Core.Services.Download.DownloadTaskManager.Instance.FailTask(task.Id, ex.Message);
            return false;
        }
    }

    private async Task DownloadModrinthAsync(ModrinthVersion version, ModrinthVersionFile file)
    {
        CancellationTokenSource? cts = null;
        string? taskId = null;
        try
        {
            var config = LauncherConfig.Load();
            var defaultDir = GetTargetDirectory(config);

            var savePath = await ShowSaveFileDialogAsync(defaultDir, file.Filename);
            if (string.IsNullOrEmpty(savePath)) return;

            var finalDir = Path.GetDirectoryName(savePath)!;

            var resourceName = DisplayName;
            cts = new CancellationTokenSource();
            var downloadTask = Core.Services.Download.DownloadTaskManager.Instance.AddTask(resourceName, Core.Services.Download.DownloadTaskType.Resource, cts);
            taskId = downloadTask.Id;

            int lastReported = -1;
            var progress = new Progress<int>(p =>
            {
                if (p >= 100 || p - lastReported >= 5)
                {
                    if (taskId != null)
                    {
                        Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(taskId, p, file.Filename);
                    }
                    lastReported = p;
                }
            });

            var success = await DownloadByUrlAsync(file.Url, savePath, file.Filename, cts.Token).ConfigureAwait(false);

            if (cts.Token.IsCancellationRequested)
            {
                if (taskId != null)
                    Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 0, "已取消");
                if (File.Exists(savePath))
                {
                    try { File.Delete(savePath); } catch { }
                }
                return;
            }

            if (success)
            {
                if (taskId != null)
                    Core.Services.Download.DownloadTaskManager.Instance.CompleteTask(taskId);
            }
            else
            {
                if (taskId != null)
                    Core.Services.Download.DownloadTaskManager.Instance.FailTask(taskId, "下载失败");
            }
        }
        catch
        {
            if (taskId != null)
                Core.Services.Download.DownloadTaskManager.Instance.FailTask(taskId, "下载失败");
        }
        finally
        {
            cts?.Dispose();
        }
    }
}

public enum VersionBackendType
{
    CurseForge,
    Modrinth
}

public partial class VersionGroupViewModel : ObservableObject
{
    [ObservableProperty] private string _mcVersion = string.Empty;
    [ObservableProperty] private bool _isLatest;

    public ObservableCollection<VersionEntryViewModel> Files { get; } = new();

    public string FileCountDisplay => $"{Files.Count} 个文件";
}

public partial class VersionEntryViewModel : ObservableObject
{
    public VersionBackendType BackendType { get; set; }
    public CurseForgeFile? CurseForgeFile { get; set; }
    public ModrinthVersion? ModrinthVersion { get; set; }
    public ModrinthVersionFile? ModrinthFile { get; set; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _dateDisplay = string.Empty;
    [ObservableProperty] private string _mcVersionsDisplay = string.Empty;
    [ObservableProperty] private string _sizeDisplay = string.Empty;
}
