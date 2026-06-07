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
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenWebsiteCommand))]
    private string _websiteUrl = "";
    [ObservableProperty] private string _websiteButtonText = "";
    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVersionsVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmptyVisible))]
    private bool _isLoading = true;

    public bool IsVersionsVisible => !IsLoading && HasAnyGroup;
    public bool IsEmptyVisible => !IsLoading && !HasAnyGroup;

    public ObservableCollection<VersionGroupViewModel> VersionGroups { get; } = new();
    public bool HasAnyGroup => VersionGroups.Count > 0;

    public ObservableCollection<LoaderFilterItem> AvailableLoaders { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredVersionGroups))]
    private LoaderFilterItem? _selectedLoaderFilter;

    /// <summary>
    /// 当前筛选的加载器名称，供 VersionGroupViewModel 内部判断
    /// </summary>
    public string CurrentLoaderFilter =>
        SelectedLoaderFilter?.LoaderName == "全部" ? "" : (SelectedLoaderFilter?.LoaderName ?? "");

    public IEnumerable<VersionGroupViewModel> FilteredVersionGroups
    {
        get
        {
            // 同步筛选条件到各版本组
            VersionGroupViewModel.SharedLoaderFilter = CurrentLoaderFilter;
            foreach (var g in VersionGroups)
                g.NotifyLoaderFilterChanged();

            if (string.IsNullOrEmpty(CurrentLoaderFilter))
                return VersionGroups;

            return VersionGroups.Where(g => g.LoaderGroups.Any(lg => lg.LoaderName == CurrentLoaderFilter));
        }
    }

    public IRelayCommand BackCommand { get; }
    public IRelayCommand OpenWebsiteCommand { get; }
    public IAsyncRelayCommand<VersionEntryViewModel> DownloadVersionCommand { get; }
    public IRelayCommand<DependencyItemViewModel> NavigateToDependencyCommand { get; }

    public event Action<object>? DependencyNavigationRequested;

    private static bool IsIncompleteCurseForgeData(CurseForgeMod cf)
    {
        return cf.Logo == null || cf.Authors.Count == 0 || string.IsNullOrEmpty(cf.Summary);
    }

    private static bool IsIncompleteModrinthData(ModrinthSearchHit hit)
    {
        return string.IsNullOrEmpty(hit.IconUrl) || string.IsNullOrEmpty(hit.Author);
    }

    public ModDetailViewModel(object rawData, string selectedVersionId, string resourceType, Action? onBack = null)
    {
        RawData = rawData;
        SelectedVersionId = selectedVersionId;
        ResourceType = resourceType;
        _onBack = onBack;

        BackCommand = new RelayCommand(Back);
        OpenWebsiteCommand = new RelayCommand(OpenWebsite, () => !string.IsNullOrEmpty(WebsiteUrl));
        DownloadVersionCommand = new AsyncRelayCommand<VersionEntryViewModel>(DownloadVersionAsync);
        NavigateToDependencyCommand = new RelayCommand<DependencyItemViewModel>(NavigateToDependency);

        LoadHeader();
        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        try
        {
            if (RawData is CurseForgeMod cf && IsIncompleteCurseForgeData(cf))
            {
                await LoadHeaderFromFullCurseForgeDataAsync(cf);
            }
            else if (RawData is ModrinthSearchHit hit && IsIncompleteModrinthData(hit))
            {
                await LoadHeaderFromFullModrinthDataAsync(hit);
            }

            await LoadVersionsAsync();
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasAnyGroup));
        }
    }

    private async Task LoadHeaderFromFullCurseForgeDataAsync(CurseForgeMod cf)
    {
        try
        {
            var response = await CurseForgeService.GetModAsync(cf.Id);
            if (response?.Data is { } fullMod)
            {
                var translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(fullMod.Slug)
                                  ?? ModTranslationService.Instance.GetTranslationByCurseForgeId(fullMod.Id);
                DisplayName = ModTranslationService.Instance.GetDisplayName(fullMod.Name, translation);
                Summary = fullMod.Summary;
                AuthorDisplay = fullMod.Authors.Count > 0
                    ? $"作者: {string.Join(", ", fullMod.Authors.Select(a => a.Name))}"
                    : "作者: 未知";
                DownloadsDisplay = $"下载量: {CurseForgeService.FormatDownloadCount(fullMod.DownloadCount)}";
                LastUpdateDisplay = $"更新: {fullMod.DateModified:yyyy-MM-dd}";
                WebsiteUrl = fullMod.Links?.WebsiteUrl ?? "";
                WebsiteButtonText = "访问curseforge";
                await LoadIconAsync(fullMod.Logo?.Url);
            }
        }
        catch { }
    }

    private async Task LoadHeaderFromFullModrinthDataAsync(ModrinthSearchHit hit)
    {
        try
        {
            var project = await _modrinth.GetProjectAsync(hit.ProjectId, _cts.Token);
            if (project != null)
            {
                var translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(project.Id);
                DisplayName = ModTranslationService.Instance.GetDisplayName(project.Title, translation);
                Summary = project.Description ?? string.Empty;
                AuthorDisplay = !string.IsNullOrEmpty(project.Author) ? $"作者: {project.Author}" : "作者: 未知";
                DownloadsDisplay = $"下载量: {CurseForgeService.FormatDownloadCount(project.Downloads)}";
                LastUpdateDisplay = project.DateModified != default ? $"更新: {project.DateModified:yyyy-MM-dd}" : string.Empty;
                WebsiteUrl = $"https://modrinth.com/project/{project.Id}";
                WebsiteButtonText = "访问modrinth";
                await LoadIconAsync(project.IconUrl);
            }
        }
        catch { }
    }

    private void Back()
    {
        _cts.Cancel();
        _onBack?.Invoke();
    }

    private void OpenWebsite()
    {
        if (string.IsNullOrEmpty(WebsiteUrl)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = WebsiteUrl,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void NavigateToDependency(DependencyItemViewModel? dep)
    {
        if (dep == null) return;

        if (dep.BackendType == VersionBackendType.CurseForge && dep.CurseForgeModId > 0)
        {
            var fakeMod = new CurseForgeMod { Id = dep.CurseForgeModId, Name = dep.Name, Slug = "" };
            DependencyNavigationRequested?.Invoke(fakeMod);
        }
        else if (dep.BackendType == VersionBackendType.Modrinth && !string.IsNullOrEmpty(dep.ProjectId))
        {
            var fakeHit = new ModrinthSearchHit { ProjectId = dep.ProjectId, Title = dep.Name };
            DependencyNavigationRequested?.Invoke(fakeHit);
        }
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
            WebsiteUrl = cf.Links?.WebsiteUrl ?? "";
            WebsiteButtonText = "访问curseforge";

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
            WebsiteUrl = $"https://modrinth.com/project/{hit.ProjectId}";
            WebsiteButtonText = "访问modrinth";

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

        await LoadDependenciesAsync();
    }

    private async Task LoadDependenciesAsync()
    {
        var cfDepModIds = new HashSet<int>();
        var modrinthDepProjectIds = new HashSet<string>();

        foreach (var group in VersionGroups)
        {
            foreach (var entry in group.Files)
            {
                if (entry.BackendType == VersionBackendType.CurseForge && entry.CurseForgeFile?.Dependencies != null)
                {
                    foreach (var dep in entry.CurseForgeFile.Dependencies)
                    {
                        if (dep.ModId > 0 && dep.RelationTypeKind is CurseForgeDependencyType.Required or CurseForgeDependencyType.Optional)
                            cfDepModIds.Add(dep.ModId);
                    }
                }
                else if (entry.BackendType == VersionBackendType.Modrinth && entry.ModrinthVersion?.Dependencies != null)
                {
                    foreach (var dep in entry.ModrinthVersion.Dependencies)
                    {
                        if (!string.IsNullOrEmpty(dep.ProjectId) && dep is { IsRequired: true } or { IsOptional: true })
                            modrinthDepProjectIds.Add(dep.ProjectId);
                    }
                }
            }
        }

        Dictionary<int, CurseForgeMod>? cfMods = null;
        Dictionary<string, ModrinthProject>? modrinthProjects = null;

        var tasks = new List<Task>();
        if (cfDepModIds.Count > 0)
        {
            tasks.Add(Task.Run(async () =>
            {
                try { cfMods = await CurseForgeService.GetModsAsync(cfDepModIds).ConfigureAwait(false); }
                catch { }

                // 批量获取失败的 mod，逐个回退获取
                if (cfMods != null)
                {
                    var missingIds = cfDepModIds.Where(id => !cfMods.ContainsKey(id)).ToList();
                    foreach (var id in missingIds)
                    {
                        try
                        {
                            var resp = await CurseForgeService.GetModAsync(id).ConfigureAwait(false);
                            if (resp?.Data != null)
                                cfMods[id] = resp.Data;
                        }
                        catch { }
                    }
                }
                else
                {
                    cfMods = new Dictionary<int, CurseForgeMod>();
                    foreach (var id in cfDepModIds)
                    {
                        try
                        {
                            var resp = await CurseForgeService.GetModAsync(id).ConfigureAwait(false);
                            if (resp?.Data != null)
                                cfMods[id] = resp.Data;
                        }
                        catch { }
                    }
                }
            }));
        }
        if (modrinthDepProjectIds.Count > 0)
        {
            tasks.Add(Task.Run(async () =>
            {
                try { modrinthProjects = await _modrinth.GetProjectsAsync(modrinthDepProjectIds, _cts.Token).ConfigureAwait(false); }
                catch { }

                // 批量获取失败的 project，逐个回退获取
                if (modrinthProjects != null)
                {
                    var missingIds = modrinthDepProjectIds.Where(id => !modrinthProjects.ContainsKey(id)).ToList();
                    foreach (var id in missingIds)
                    {
                        try
                        {
                            var proj = await _modrinth.GetProjectAsync(id, _cts.Token).ConfigureAwait(false);
                            if (proj != null)
                                modrinthProjects[id] = proj;
                        }
                        catch { }
                    }
                }
                else
                {
                    modrinthProjects = new Dictionary<string, ModrinthProject>();
                    foreach (var id in modrinthDepProjectIds)
                    {
                        try
                        {
                            var proj = await _modrinth.GetProjectAsync(id, _cts.Token).ConfigureAwait(false);
                            if (proj != null)
                                modrinthProjects[id] = proj;
                        }
                        catch { }
                    }
                }
            }));
        }
        if (tasks.Count > 0)
            await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var group in VersionGroups)
        {
            foreach (var entry in group.Files)
            {
                if (entry.BackendType == VersionBackendType.CurseForge && entry.CurseForgeFile?.Dependencies != null)
                {
                    foreach (var dep in entry.CurseForgeFile.Dependencies)
                    {
                        if (dep.ModId <= 0) continue;
                        var isRequired = dep.RelationTypeKind is CurseForgeDependencyType.Required;
                        var isOptional = dep.RelationTypeKind is CurseForgeDependencyType.Optional;
                        if (!isRequired && !isOptional) continue;

                        var depName = cfMods?.TryGetValue(dep.ModId, out var mod) == true ? mod.Name : $"Mod #{dep.ModId}";
                        var translation = cfMods?.TryGetValue(dep.ModId, out var tmod) == true
                            ? ModTranslationService.Instance.GetTranslationByCurseForgeId(tmod.Slug)
                              ?? ModTranslationService.Instance.GetTranslationByCurseForgeId(tmod.Id)
                            : null;
                        if (translation != null)
                            depName = ModTranslationService.Instance.GetDisplayName(depName, translation);

                        entry.Dependencies.Add(new DependencyItemViewModel
                        {
                            BackendType = VersionBackendType.CurseForge,
                            Name = depName,
                            DependencyType = isRequired ? "必需" : "可选",
                            IsRequired = isRequired,
                            CurseForgeModId = dep.ModId
                        });
                    }
                }
                else if (entry.BackendType == VersionBackendType.Modrinth && entry.ModrinthVersion?.Dependencies != null)
                {
                    foreach (var dep in entry.ModrinthVersion.Dependencies)
                    {
                        if (string.IsNullOrEmpty(dep.ProjectId)) continue;
                        if (!dep.IsRequired && !dep.IsOptional) continue;

                        var depName = modrinthProjects?.TryGetValue(dep.ProjectId, out var proj) == true ? proj.Title : dep.ProjectId;
                        var translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(dep.ProjectId);
                        if (translation != null)
                            depName = ModTranslationService.Instance.GetDisplayName(depName, translation);

                        entry.Dependencies.Add(new DependencyItemViewModel
                        {
                            BackendType = VersionBackendType.Modrinth,
                            Name = depName,
                            DependencyType = dep.IsRequired ? "必需" : "可选",
                            IsRequired = dep.IsRequired,
                            ProjectId = dep.ProjectId
                        });
                    }
                }

                if (entry.Dependencies.Count > 0)
                {
                    entry.HasDependencies = true;
                    var requiredCount = entry.Dependencies.Count(d => d.IsRequired);
                    var optionalCount = entry.Dependencies.Count(d => !d.IsRequired);
                    var parts = new List<string>();
                    if (requiredCount > 0) parts.Add($"{requiredCount} 个必需");
                    if (optionalCount > 0) parts.Add($"{optionalCount} 个可选");
                    entry.DependenciesDisplay = $"前置: {string.Join(", ", parts)}";
                }
            }
        }

        // 计算每个版本组的公共前置资源
        foreach (var group in VersionGroups)
        {
            if (group.Files.Count == 0) continue;

            var filesWithDeps = group.Files.Where(f => f.HasDependencies).ToList();
            if (filesWithDeps.Count == 0) continue;

            // 取所有有依赖的条目的依赖交集
            var intersection = new HashSet<string>(
                filesWithDeps[0].Dependencies.Select(d => d.UniqueKey));

            for (int i = 1; i < filesWithDeps.Count; i++)
            {
                var currentKeys = new HashSet<string>(
                    filesWithDeps[i].Dependencies.Select(d => d.UniqueKey));
                intersection.IntersectWith(currentKeys);
            }

            if (intersection.Count == 0) continue;

            // 从第一个条目中提取公共依赖（保留名称和类型信息）
            var seenKeys = new HashSet<string>();
            foreach (var entry in filesWithDeps)
            {
                foreach (var dep in entry.Dependencies)
                {
                    if (intersection.Contains(dep.UniqueKey) && seenKeys.Add(dep.UniqueKey))
                    {
                        group.CommonDependencies.Add(new DependencyItemViewModel
                        {
                            BackendType = dep.BackendType,
                            Name = dep.Name,
                            DependencyType = dep.DependencyType,
                            IsRequired = dep.IsRequired,
                            CurseForgeModId = dep.CurseForgeModId,
                            ProjectId = dep.ProjectId
                        });
                    }
                }
            }

            // 从各条目中移除已提升为公共前置的依赖，避免重复显示
            foreach (var entry in group.Files)
            {
                var toRemove = entry.Dependencies
                    .Where(d => intersection.Contains(d.UniqueKey))
                    .ToList();
                foreach (var d in toRemove)
                    entry.Dependencies.Remove(d);

                if (entry.Dependencies.Count == 0)
                {
                    entry.HasDependencies = false;
                    entry.DependenciesDisplay = "";
                }
                else
                {
                    var requiredCount = entry.Dependencies.Count(d => d.IsRequired);
                    var optionalCount = entry.Dependencies.Count(d => !d.IsRequired);
                    var parts = new List<string>();
                    if (requiredCount > 0) parts.Add($"{requiredCount} 个必需");
                    if (optionalCount > 0) parts.Add($"{optionalCount} 个可选");
                    entry.DependenciesDisplay = $"前置: {string.Join(", ", parts)}";
                }
            }

            group.NotifyCommonDependenciesChanged();
        }

        // 按加载器分子组
        foreach (var group in VersionGroups)
        {
            var loaderGroups = group.Files
                .GroupBy(f => f.Loader)
                .OrderByDescending(g => g.Key, new LoaderComparer())
                .ToList();

            if (loaderGroups.Count <= 1)
            {
                // 只有一种加载器，不需要分子组
                var singleGroup = new LoaderSubGroupViewModel
                {
                    LoaderName = loaderGroups.Count == 1 ? loaderGroups[0].Key : "通用",
                    LoaderIcon = GetLoaderIcon(loaderGroups.Count == 1 ? loaderGroups[0].Key : "通用")
                };
                foreach (var f in loaderGroups.FirstOrDefault() ?? Enumerable.Empty<VersionEntryViewModel>())
                    singleGroup.Files.Add(f);

                // 将版本组的公共前置移到唯一的加载器子组
                foreach (var dep in group.CommonDependencies)
                    singleGroup.CommonDependencies.Add(new DependencyItemViewModel
                    {
                        BackendType = dep.BackendType,
                        Name = dep.Name,
                        DependencyType = dep.DependencyType,
                        IsRequired = dep.IsRequired,
                        CurseForgeModId = dep.CurseForgeModId,
                        ProjectId = dep.ProjectId
                    });
                singleGroup.NotifyCommonDependenciesChanged();

                group.CommonDependencies.Clear();
                group.NotifyCommonDependenciesChanged();

                group.LoaderGroups.Add(singleGroup);
            }
            else
            {
                // 多种加载器，分别建子组并计算各子组的公共前置
                foreach (var lg in loaderGroups)
                {
                    var subGroup = new LoaderSubGroupViewModel
                    {
                        LoaderName = lg.Key,
                        LoaderIcon = GetLoaderIcon(lg.Key)
                    };
                    foreach (var f in lg)
                        subGroup.Files.Add(f);

                    group.LoaderGroups.Add(subGroup);
                }

                // 重新分配公共前置到各加载器子组
                if (group.CommonDependencies.Count > 0)
                {
                    var commonKeys = group.CommonDependencies.Select(d => d.UniqueKey).ToHashSet();

                    foreach (var subGroup in group.LoaderGroups)
                    {
                        // 检查该子组中哪些条目有依赖
                        var filesWithDeps = subGroup.Files.Where(f => f.Dependencies.Count > 0 || f.HasDependencies).ToList();

                        // 计算该子组内的依赖交集
                        var subGroupDepKeys = new HashSet<string>();
                        if (filesWithDeps.Count > 0)
                        {
                            subGroupDepKeys = filesWithDeps[0].Dependencies.Select(d => d.UniqueKey).ToHashSet();
                            for (int i = 1; i < filesWithDeps.Count; i++)
                            {
                                var currentKeys = filesWithDeps[i].Dependencies.Select(d => d.UniqueKey).ToHashSet();
                                subGroupDepKeys.IntersectWith(currentKeys);
                            }
                        }

                        // 将属于该子组的公共前置添加进去
                        foreach (var dep in group.CommonDependencies)
                        {
                            if (commonKeys.Contains(dep.UniqueKey))
                            {
                                // 检查该子组是否有条目依赖此项
                                bool anyEntryDependsOnThis = subGroup.Files.Any(f =>
                                    f.Dependencies.Any(d => d.UniqueKey == dep.UniqueKey) ||
                                    (f.HasDependencies == false && commonKeys.Contains(dep.UniqueKey)));

                                // 如果该子组所有有依赖的条目都依赖此项，或者该子组只有一个条目
                                if (filesWithDeps.Count == 0 || subGroupDepKeys.Contains(dep.UniqueKey) || anyEntryDependsOnThis)
                                {
                                    subGroup.CommonDependencies.Add(new DependencyItemViewModel
                                    {
                                        BackendType = dep.BackendType,
                                        Name = dep.Name,
                                        DependencyType = dep.DependencyType,
                                        IsRequired = dep.IsRequired,
                                        CurseForgeModId = dep.CurseForgeModId,
                                        ProjectId = dep.ProjectId
                                    });
                                }
                            }
                        }

                        subGroup.NotifyCommonDependenciesChanged();
                    }

                    group.CommonDependencies.Clear();
                    group.NotifyCommonDependenciesChanged();
                }
            }

            group.NotifyHasLoaderGroupsChanged();
        }

        // 构建加载器筛选列表
        var allLoaders = VersionGroups
            .SelectMany(g => g.LoaderGroups)
            .GroupBy(lg => lg.LoaderName)
            .Select(g => new LoaderFilterItem
            {
                LoaderName = g.Key,
                IconUri = GetLoaderIcon(g.Key),
                Count = g.Sum(lg => lg.Files.Count)
            })
            .OrderBy(l => l.LoaderName, new LoaderComparer())
            .ToList();

        AvailableLoaders.Clear();
        AvailableLoaders.Add(new LoaderFilterItem { LoaderName = "全部", IconUri = "", Count = allLoaders.Sum(l => l.Count) });
        foreach (var loader in allLoaders)
            AvailableLoaders.Add(loader);

        SelectedLoaderFilter = AvailableLoaders.FirstOrDefault();
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
                    SizeDisplay = FormatFileSize(f.FileLength),
                    Loader = ExtractLoaderName(f, null)
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
                    SizeDisplay = FormatFileSize(file.Size),
                    Loader = ExtractLoaderName(null, v)
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

    private static string ExtractLoaderName(CurseForgeFile? cfFile, ModrinthVersion? mrVersion)
    {
        // Modrinth：直接使用 loaders 字段
        if (mrVersion?.Loaders != null && mrVersion.Loaders.Count > 0)
        {
            var loader = mrVersion.Loaders.FirstOrDefault(l =>
                !string.IsNullOrEmpty(l) && !l.Equals("minecraft", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(loader))
            {
                return NormalizeLoaderName(loader);
            }
        }

        // CurseForge：优先从 SortableGameVersions 的 GameVersionTypeId 判断
        if (cfFile?.SortableGameVersions != null)
        {
            foreach (var sgv in cfFile.SortableGameVersions)
            {
                // CurseForge GameVersionTypeId: 1=MC版本, 2=Forge, 3=Fabric, 4=Quilt, 5=NeoForge
                switch (sgv.GameVersionTypeId)
                {
                    case 5: return "NeoForge";
                    case 2: return "Forge";
                    case 3: return "Fabric";
                    case 4: return "Quilt";
                }
            }
        }

        // 回退：从 GameVersions 字符串中提取
        if (cfFile?.GameVersions != null)
        {
            foreach (var gv in cfFile.GameVersions)
            {
                var lower = gv.ToLowerInvariant();
                if (lower.Contains("neoforge")) return "NeoForge";
                if (lower.Contains("forge")) return "Forge";
                if (lower.Contains("fabric")) return "Fabric";
                if (lower.Contains("quilt")) return "Quilt";
            }
        }

        // 最后回退：从文件名推断
        if (cfFile?.FileName != null)
        {
            var lower = cfFile.FileName.ToLowerInvariant();
            if (lower.Contains("neoforge")) return "NeoForge";
            if (lower.Contains("forge")) return "Forge";
            if (lower.Contains("fabric")) return "Fabric";
            if (lower.Contains("quilt")) return "Quilt";
        }

        return "其他";
    }

    private static string NormalizeLoaderName(string loader)
    {
        var lower = loader.ToLowerInvariant();
        if (lower.Contains("neoforge")) return "NeoForge";
        if (lower.Contains("forge")) return "Forge";
        if (lower.Contains("fabric")) return "Fabric";
        if (lower.Contains("quilt")) return "Quilt";
        if (lower.Contains("bukkit") || lower.Contains("spigot") || lower.Contains("paper")) return "Bukkit";
        if (lower.Contains("rift")) return "Rift";
        // 首字母大写
        return char.ToUpper(loader[0]) + loader[1..].ToLowerInvariant();
    }

    private static readonly Dictionary<string, string> LoaderIconMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Forge"] = "avares://ObsMCLauncher.Desktop/Assets/LoaderIcons/forge.png",
        ["NeoForge"] = "avares://ObsMCLauncher.Desktop/Assets/LoaderIcons/neoforged.png",
        ["Fabric"] = "avares://ObsMCLauncher.Desktop/Assets/LoaderIcons/fabric.png",
        ["Quilt"] = "avares://ObsMCLauncher.Desktop/Assets/LoaderIcons/quilt.png",
        ["其他"] = "avares://ObsMCLauncher.Desktop/Assets/LoaderIcons/vanilla.png",
        ["Bukkit"] = "avares://ObsMCLauncher.Desktop/Assets/LoaderIcons/vanilla.png",
        ["Rift"] = "avares://ObsMCLauncher.Desktop/Assets/LoaderIcons/vanilla.png"
    };

    private static string GetLoaderIcon(string loaderName)
    {
        return LoaderIconMap.TryGetValue(loaderName, out var icon) ? icon : "avares://ObsMCLauncher.Desktop/Assets/LoaderIcons/vanilla.png";
    }

    private class LoaderComparer : IComparer<string>
    {
        private static readonly string[] Order = ["Forge", "NeoForge", "Fabric", "Quilt", "其他", "Bukkit", "Rift"];

        public int Compare(string? x, string? y)
        {
            var xi = Array.IndexOf(Order, x ?? "其他");
            var yi = Array.IndexOf(Order, y ?? "其他");
            if (xi < 0) xi = Order.Length;
            if (yi < 0) yi = Order.Length;
            return xi.CompareTo(yi);
        }
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

    public string FileCountDisplay
    {
        get
        {
            int count = FilteredLoaderGroups.Sum(lg => lg.Files.Count);
            if (count == 0) count = Files.Count;
            return $"{count} 个文件";
        }
    }

    public ObservableCollection<DependencyItemViewModel> CommonDependencies { get; } = new();

    public bool HasCommonDependencies => CommonDependencies.Count > 0;

    public string CommonDependenciesDisplay
    {
        get
        {
            if (CommonDependencies.Count == 0) return "";
            var required = CommonDependencies.Count(d => d.IsRequired);
            var optional = CommonDependencies.Count(d => !d.IsRequired);
            var parts = new List<string>();
            if (required > 0) parts.Add($"{required} 个必需");
            if (optional > 0) parts.Add($"{optional} 个可选");
            return $"公共前置: {string.Join(", ", parts)}";
        }
    }

    public void NotifyCommonDependenciesChanged()
    {
        OnPropertyChanged(nameof(HasCommonDependencies));
        OnPropertyChanged(nameof(CommonDependenciesDisplay));
    }

    public ObservableCollection<LoaderSubGroupViewModel> LoaderGroups { get; } = new();

    public bool HasLoaderGroups => LoaderGroups.Count > 1;

    /// <summary>
    /// 当前筛选的加载器名称（空字符串表示不筛选），各版本组共享此值
    /// </summary>
    public static string SharedLoaderFilter { get; set; } = "";

    /// <summary>
    /// 根据当前筛选条件返回可见的加载器子组
    /// </summary>
    public IEnumerable<LoaderSubGroupViewModel> FilteredLoaderGroups =>
        string.IsNullOrEmpty(SharedLoaderFilter)
            ? LoaderGroups
            : LoaderGroups.Where(lg => lg.LoaderName == SharedLoaderFilter);

    public void NotifyLoaderFilterChanged()
    {
        OnPropertyChanged(nameof(FilteredLoaderGroups));
        OnPropertyChanged(nameof(FileCountDisplay));
    }

    public void NotifyHasLoaderGroupsChanged()
    {
        OnPropertyChanged(nameof(HasLoaderGroups));
    }
}

public partial class LoaderSubGroupViewModel : ObservableObject
{
    [ObservableProperty] private string _loaderName = string.Empty;
    [ObservableProperty] private string _loaderIcon = string.Empty;

    public ObservableCollection<VersionEntryViewModel> Files { get; } = new();

    public ObservableCollection<DependencyItemViewModel> CommonDependencies { get; } = new();

    public bool HasCommonDependencies => CommonDependencies.Count > 0;

    public string CommonDependenciesDisplay
    {
        get
        {
            if (CommonDependencies.Count == 0) return "";
            var required = CommonDependencies.Count(d => d.IsRequired);
            var optional = CommonDependencies.Count(d => !d.IsRequired);
            var parts = new List<string>();
            if (required > 0) parts.Add($"{required} 个必需");
            if (optional > 0) parts.Add($"{optional} 个可选");
            return $"前置: {string.Join(", ", parts)}";
        }
    }

    public void NotifyCommonDependenciesChanged()
    {
        OnPropertyChanged(nameof(HasCommonDependencies));
        OnPropertyChanged(nameof(CommonDependenciesDisplay));
    }
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
    [ObservableProperty] private bool _hasDependencies;
    [ObservableProperty] private string _dependenciesDisplay = string.Empty;
    [ObservableProperty] private string _loader = string.Empty;

    public ObservableCollection<DependencyItemViewModel> Dependencies { get; } = new();
}

public partial class DependencyItemViewModel : ObservableObject
{
    public VersionBackendType BackendType { get; set; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _dependencyType = string.Empty;
    [ObservableProperty] private bool _isRequired;
    [ObservableProperty] private string _projectId = string.Empty;
    [ObservableProperty] private int _curseForgeModId;

    public string TypeTag => IsRequired ? "必需" : "可选";
    public string TypeTagColor => IsRequired ? "#E74C3C" : "#95A5A6";

    // 用于去重比较：同一后端 + 同一 ID 视为同一依赖
    public string UniqueKey => BackendType == VersionBackendType.CurseForge
        ? $"cf_{CurseForgeModId}"
        : $"mr_{ProjectId}";
}

public class LoaderFilterItem
{
    public string LoaderName { get; init; } = "";
    public string IconUri { get; init; } = "";
    public int Count { get; init; }
}
