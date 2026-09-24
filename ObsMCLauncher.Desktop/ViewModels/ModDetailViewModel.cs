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
using FluentAvalonia.UI.Controls;
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
    private CancellationTokenSource? _cts = new();
    private CancellationToken OperationToken => _cts?.Token ?? CancellationToken.None;

    public object RawData { get; }
    public string SelectedVersionId { get; }
    public string ResourceType { get; }
    private readonly Action? _onBack;

    /// <summary>资源来源显示名（CurseForge / Modrinth）</summary>
    public string SourceDisplay { get; }

    [ObservableProperty] private string _displayName = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSummaryToggle))]
    private string _summary = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryMaxLines))]
    [NotifyPropertyChangedFor(nameof(SummaryToggleText))]
    private bool _isSummaryExpanded;
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

    // 顶部提示条（下载 / 安装结果反馈）
    [ObservableProperty] private bool _isInfoBarOpen;
    [ObservableProperty] private InfoBarSeverity _infoBarSeverity = InfoBarSeverity.Informational;
    [ObservableProperty] private string _infoBarTitle = "";
    [ObservableProperty] private string _infoBarMessage = "";

    public bool IsVersionsVisible => !IsLoading && HasAnyGroup;
    public bool IsEmptyVisible => !IsLoading && !HasAnyGroup;

    /// <summary>摘要较长时才显示"展开/收起"入口</summary>
    public bool ShowSummaryToggle => Summary.Length > 80;
    public int SummaryMaxLines => IsSummaryExpanded ? int.MaxValue : 2;
    public string SummaryToggleText => IsSummaryExpanded ? "收起" : "展开";

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
    public IRelayCommand ToggleSummaryCommand { get; }

    /// <summary>
    /// 依赖跳转请求。<c>RawData</c> 优先携带批量拉取时留下的完整工程对象，详情页据此不再二次请求；
    /// <c>ResourceType</c> 是前置自身的资源类型，避免把父资源的类型套到前置上。
    /// </summary>
    public event Action<DependencyNavigationRequest>? DependencyNavigationRequested;

    public ModDetailViewModel(object rawData, string selectedVersionId, string resourceType, Action? onBack = null)
    {
        RawData = rawData;
        SelectedVersionId = selectedVersionId;
        ResourceType = resourceType;
        _onBack = onBack;
        SourceDisplay = rawData is CurseForgeMod ? "CurseForge" : "Modrinth";

        BackCommand = new RelayCommand(Back);
        OpenWebsiteCommand = new RelayCommand(OpenWebsite, () => !string.IsNullOrEmpty(WebsiteUrl));
        DownloadVersionCommand = new AsyncRelayCommand<VersionEntryViewModel>(DownloadVersionAsync);
        NavigateToDependencyCommand = new RelayCommand<DependencyItemViewModel>(NavigateToDependency);
        ToggleSummaryCommand = new RelayCommand(ToggleSummary);

        LoadHeader();
        _ = LoadDataAsync();
    }

    private void ToggleSummary()
    {
        IsSummaryExpanded = !IsSummaryExpanded;
    }

    private void ShowInfo(InfoBarSeverity severity, string title, string message)
    {
        InfoBarSeverity = severity;
        InfoBarTitle = title;
        InfoBarMessage = message;
        IsInfoBarOpen = true;
    }

    private static bool IsIncompleteCurseForgeData(CurseForgeMod cf)
    {
        return cf.Logo == null || cf.Authors.Count == 0 || string.IsNullOrEmpty(cf.Summary);
    }

    private static bool IsIncompleteModrinthData(ModrinthSearchHit hit)
    {
        return string.IsNullOrEmpty(hit.IconUrl) || string.IsNullOrEmpty(hit.Author);
    }

    private async Task LoadDataAsync()
    {
        // 头部补全和版本列表互不依赖，同时发起，省掉一次串行的网络往返
        var headerTask = LoadHeaderIfIncompleteAsync();

        try
        {
            await LoadVersionsAsync();
        }
        finally
        {
            // 版本列表一到就解除加载态。前置资源、图标都不参与首屏，放到后面慢慢补
            IsLoading = false;
            OnPropertyChanged(nameof(HasAnyGroup));
        }

        await headerTask;

        _ = LoadDependenciesAsync();
    }

    /// <summary>搜索页带过来的数据不全时，再拉一次完整工程信息补齐头部</summary>
    private async Task LoadHeaderIfIncompleteAsync()
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
        }
        catch { }
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
                    ? string.Join(", ", fullMod.Authors.Select(a => a.Name))
                    : "未知";
                DownloadsDisplay = CurseForgeService.FormatDownloadCount(fullMod.DownloadCount);
                LastUpdateDisplay = fullMod.DateModified.ToString("yyyy-MM-dd");
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
            var project = await _modrinth.GetProjectAsync(hit.ProjectId, OperationToken);
            if (project != null)
            {
                var translation = ModTranslationService.Instance.GetTranslationById(project.Id);
                DisplayName = ModTranslationService.Instance.GetDisplayName(project.Title, translation);
                Summary = project.Description ?? string.Empty;
                AuthorDisplay = !string.IsNullOrEmpty(project.Author) ? project.Author : "未知";
                DownloadsDisplay = CurseForgeService.FormatDownloadCount(project.Downloads);
                LastUpdateDisplay = project.DateModified != default ? project.DateModified.ToString("yyyy-MM-dd") : string.Empty;
                WebsiteUrl = $"https://modrinth.com/project/{project.Id}";
                WebsiteButtonText = "访问modrinth";
                await LoadIconAsync(project.IconUrl);
            }
        }
        catch { }
    }

    private void Back()
    {
        var cts = _cts;
        _cts = null;
        cts?.Cancel();
        cts?.Dispose();
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
        // 直接用批量拉取阶段留下的完整工程对象：目标详情页头部数据齐全，不会再发一次请求
        if (dep?.RawProject is not { } rawProject) return;

        DependencyNavigationRequested?.Invoke(new DependencyNavigationRequest(rawProject, dep.ResourceType));
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
                ? string.Join(", ", cf.Authors.Select(a => a.Name))
                : "未知";
            DownloadsDisplay = CurseForgeService.FormatDownloadCount(cf.DownloadCount);
            LastUpdateDisplay = cf.DateModified.ToString("yyyy-MM-dd");
            WebsiteUrl = cf.Links?.WebsiteUrl ?? "";
            WebsiteButtonText = "访问curseforge";

            _ = LoadIconAsync(cf.Logo?.Url);
        }
        else if (RawData is ModrinthSearchHit hit)
        {
            var translation = ModTranslationService.Instance.GetTranslationById(hit.ProjectId);
            DisplayName = ModTranslationService.Instance.GetDisplayName(hit.Title, translation);
            Summary = hit.Description ?? string.Empty;
            AuthorDisplay = hit.Author ?? "未知";
            DownloadsDisplay = CurseForgeService.FormatDownloadCount(hit.Downloads);
            LastUpdateDisplay = hit.DateModified != default ? hit.DateModified.ToString("yyyy-MM-dd") : string.Empty;
            WebsiteUrl = $"https://modrinth.com/project/{hit.ProjectId}";
            WebsiteButtonText = "访问modrinth";

            _ = LoadIconAsync(hit.IconUrl);
        }
    }

    private async Task LoadIconAsync(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        var path = await ImageCacheService.GetImagePathAsync(url);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                var newIcon = new Bitmap(path);
                var old = Icon;
                Icon = newIcon;
                if (old is IDisposable oldDisposable)
                {
                    oldDisposable.Dispose();
                }
            }
            catch
            {
            }
        });
    }

    private async Task LoadVersionsAsync()
    {
        IsLoading = true;
        VersionGroups.Clear();

        if (RawData is CurseForgeMod cf)
        {
            await LoadCurseForgeVersionsAsync(cf, OperationToken);
        }
        else if (RawData is ModrinthSearchHit hit)
        {
            await LoadModrinthVersionsAsync(hit, OperationToken);
        }

        // 加载器分组和筛选栏是纯本地装配，必须赶在版本列表显示前做完，
        // 否则列表会先以「空分组」的形态闪一下
        await BuildLoaderGroupsAsync();
    }

    /// <summary>
    /// 按加载器给各版本组分子组，并重建顶部筛选栏。纯本地装配、不涉及网络，
    /// 但会改 ObservableCollection，统一回 UI 线程。
    /// </summary>
    private async Task BuildLoaderGroupsAsync()
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            // 按加载器分子组（仅用于渲染，前置已提升到版本组层级）
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

                    group.LoaderGroups.Add(singleGroup);
                }
                else
                {
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
                }

                group.NotifyHasLoaderGroupsChanged();
            }

            // 构建加载器筛选列表（大小写不敏感去重）
            var allLoaders = VersionGroups
                .SelectMany(g => g.LoaderGroups)
                .GroupBy(lg => lg.LoaderName, StringComparer.OrdinalIgnoreCase)
                .Select(g => new LoaderFilterItem
                {
                    LoaderName = g.First().LoaderName,
                    IconUri = GetLoaderIcon(g.First().LoaderName),
                    Count = g.Sum(lg => lg.Files.Count)
                })
                .OrderBy(l => l.LoaderName, new LoaderComparer())
                .ToList();

            // 记住当前选中的加载器名称，重建后恢复
            var previousSelection = SelectedLoaderFilter?.LoaderName;

            AvailableLoaders.Clear();
            AvailableLoaders.Add(new LoaderFilterItem { LoaderName = "全部", IconUri = "", Count = allLoaders.Sum(l => l.Count) });
            foreach (var loader in allLoaders)
                AvailableLoaders.Add(loader);

            // 恢复选中状态：优先匹配之前的选中项，否则默认选"全部"
            SelectedLoaderFilter = string.IsNullOrEmpty(previousSelection)
                ? AvailableLoaders.FirstOrDefault()
                : AvailableLoaders.FirstOrDefault(l => string.Equals(l.LoaderName, previousSelection, StringComparison.OrdinalIgnoreCase))
                  ?? AvailableLoaders.FirstOrDefault();
        });
    }

    /// <summary>前置图标的并行下载上限。单个版本组可能有几十个前置，不限制会瞬间打满连接</summary>
    private const int MaxConcurrentIconLoads = 4;

    /// <summary>
    /// 装配"前置资源"：每个 MC 版本组一份列表，组内所有加载器、所有文件的依赖合并去重（PCL 口径）。
    /// 元数据来自本方法内已经批量拉取的 cfMods / modrinthProjects，不再额外发请求；
    /// 只有图标图片走 ImageCacheService（磁盘缓存，二次进入不放请求）。
    /// </summary>
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
                try { modrinthProjects = await _modrinth.GetProjectsAsync(modrinthDepProjectIds, OperationToken).ConfigureAwait(false); }
                catch { }

                // 批量获取失败的 project，逐个回退获取
                if (modrinthProjects != null)
                {
                    var missingIds = modrinthDepProjectIds.Where(id => !modrinthProjects.ContainsKey(id)).ToList();
                    foreach (var id in missingIds)
                    {
                        try
                        {
                            var proj = await _modrinth.GetProjectAsync(id, OperationToken).ConfigureAwait(false);
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
                            var proj = await _modrinth.GetProjectAsync(id, OperationToken).ConfigureAwait(false);
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

        // 每个版本组合并去重（纯数据装配，可放在 UI 线程外）
        var perGroup = new List<(VersionGroupViewModel Group, List<DependencyItemViewModel> Deps)>();
        foreach (var group in VersionGroups)
        {
            var merged = new Dictionary<string, DependencyItemViewModel>(StringComparer.Ordinal);
            foreach (var entry in group.Files)
            {
                foreach (var dep in BuildEntryDependencies(entry, cfMods, modrinthProjects))
                {
                    if (merged.TryGetValue(dep.UniqueKey, out var existing))
                        existing.MergeFrom(dep);
                    else
                        merged[dep.UniqueKey] = dep;
                }
            }

            var ordered = merged.Values
                .OrderBy(d => d.IsRequired ? 0 : 1)   // 必需在前
                .ThenBy(d => d.IsResolved ? 0 : 1)    // 拿到详细信息的在前
                .ThenBy(d => d.Name, StringComparer.CurrentCulture)
                .ToList();

            perGroup.Add((group, ordered));
        }

        // 集合更新统一回到 UI 线程，避免跨线程修改 ObservableCollection
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var (group, deps) in perGroup)
            {
                group.Dependencies.Clear();
                foreach (var dep in deps)
                    group.Dependencies.Add(dep);
                group.NotifyDependenciesChanged();
            }
        });

        // 图标最后补，且不等它：列表早已可见，图标慢慢填就行
        _ = LoadDependencyIconsAsync(perGroup.SelectMany(p => p.Deps).ToList(), OperationToken);
    }

    /// <summary>
    /// 把单个文件的原始依赖转成带完整工程信息的前置项。信息全部来自本次批量拉取的
    /// cfMods / modrinthProjects；取不到的项保留为可见但不可跳转（IsResolved=false）。
    /// </summary>
    private List<DependencyItemViewModel> BuildEntryDependencies(
        VersionEntryViewModel entry,
        Dictionary<int, CurseForgeMod>? cfMods,
        Dictionary<string, ModrinthProject>? modrinthProjects)
    {
        var result = new List<DependencyItemViewModel>();

        if (entry.BackendType == VersionBackendType.CurseForge && entry.CurseForgeFile?.Dependencies != null)
        {
            foreach (var dep in entry.CurseForgeFile.Dependencies)
            {
                if (dep.ModId <= 0) continue;
                var isRequired = dep.RelationTypeKind is CurseForgeDependencyType.Required;
                var isOptional = dep.RelationTypeKind is CurseForgeDependencyType.Optional;
                if (!isRequired && !isOptional) continue;

                CurseForgeMod? mod = null;
                cfMods?.TryGetValue(dep.ModId, out mod);

                var item = new DependencyItemViewModel
                {
                    BackendType = VersionBackendType.CurseForge,
                    CurseForgeModId = dep.ModId,
                    IsRequired = isRequired,
                    DependencyType = isRequired ? "必需" : "可选",
                    Name = $"Mod #{dep.ModId}",
                    ResourceType = MapCurseForgeClassId(mod?.ClassId),
                    RawProject = mod,
                    IconUrl = mod?.Logo?.ThumbnailUrl ?? mod?.Logo?.Url ?? "",
                    Description = (mod?.Summary ?? "").ReplaceLineEndings(" "),
                    AuthorDisplay = mod?.Authors.FirstOrDefault()?.Name ?? "",
                    DownloadsDisplay = mod != null ? CurseForgeService.FormatDownloadCount(mod.DownloadCount) : "",
                    LastUpdateDisplay = mod is { DateModified: var date } && date != default ? date.ToString("yyyy-MM-dd") : ""
                };

                if (mod != null)
                {
                    var translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(mod.Slug)
                                      ?? ModTranslationService.Instance.GetTranslationByCurseForgeId(mod.Id);
                    item.Name = ModTranslationService.Instance.GetDisplayName(mod.Name, translation);
                }

                result.Add(item);
            }
        }
        else if (entry.BackendType == VersionBackendType.Modrinth && entry.ModrinthVersion?.Dependencies != null)
        {
            foreach (var dep in entry.ModrinthVersion.Dependencies)
            {
                if (string.IsNullOrEmpty(dep.ProjectId)) continue;
                if (!dep.IsRequired && !dep.IsOptional) continue;

                ModrinthProject? project = null;
                modrinthProjects?.TryGetValue(dep.ProjectId, out project);

                var item = new DependencyItemViewModel
                {
                    BackendType = VersionBackendType.Modrinth,
                    ProjectId = dep.ProjectId,
                    IsRequired = dep.IsRequired,
                    DependencyType = dep.IsRequired ? "必需" : "可选",
                    Name = project?.Title ?? dep.ProjectId,
                    ResourceType = MapModrinthProjectType(project?.ProjectType),
                    // 用完整命中对象当导航载荷：字段齐全，目标详情页不会再请求一次
                    RawProject = project == null ? null : new ModrinthSearchHit
                    {
                        ProjectId = project.Id,
                        Title = project.Title,
                        Description = project.Description,
                        Downloads = project.Downloads,
                        Author = project.Author,
                        IconUrl = project.IconUrl,
                        DateModified = project.DateModified
                    },
                    IconUrl = project?.IconUrl ?? "",
                    Description = (project?.Description ?? "").ReplaceLineEndings(" "),
                    AuthorDisplay = project?.Author ?? "",
                    DownloadsDisplay = project != null ? CurseForgeService.FormatDownloadCount(project.Downloads) : "",
                    LastUpdateDisplay = project is { DateModified: var date } && date != default ? date.ToString("yyyy-MM-dd") : ""
                };

                if (project != null)
                {
                    // Modrinth 工程必须用 Modrinth 的 ID 查翻译表（原来这里误用了 CurseForge 的查询）
                    var translation = ModTranslationService.Instance.GetTranslationById(project.Id);
                    item.Name = ModTranslationService.Instance.GetDisplayName(project.Title, translation);
                }

                result.Add(item);
            }
        }

        return result;
    }

    /// <summary>CurseForge classId → 本项目资源类型；未知回退 "Any"（下载时落到通用目录）</summary>
    private static string MapCurseForgeClassId(int? classId) => classId switch
    {
        6 => "Mods",
        12 => "Textures",
        6945 => "Datapacks",
        6552 => "Shaders",
        4471 => "Modpacks",
        _ => "Any"
    };

    /// <summary>Modrinth project_type → 本项目资源类型；未知回退 "Any"</summary>
    private static string MapModrinthProjectType(string? projectType) => projectType switch
    {
        "mod" => "Mods",
        "resourcepack" => "Textures",
        "shader" => "Shaders",
        "datapack" => "Datapacks",
        "modpack" => "Modpacks",
        _ => "Any"
    };

    /// <summary>
    /// 为前置项异步补齐图标。同一 URL 只下载一次并复用到所有引用它的项上；
    /// 并发受 MaxConcurrentIconLoads 限制，因为一个版本组可能有几十个前置。
    /// </summary>
    private async Task LoadDependencyIconsAsync(List<DependencyItemViewModel> items, CancellationToken cancellationToken)
    {
        var byUrl = items
            .Where(i => !string.IsNullOrEmpty(i.IconUrl))
            .GroupBy(i => i.IconUrl, StringComparer.Ordinal)
            .ToList();
        if (byUrl.Count == 0) return;

        using var gate = new SemaphoreSlim(MaxConcurrentIconLoads);
        var pending = byUrl.Select(async group =>
        {
            try
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = await ImageCacheService.GetImagePathAsync(group.Key).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Bitmap bitmap;
                        try { bitmap = new Bitmap(path); }
                        catch { return; }

                        foreach (var item in group)
                            item.Icon = bitmap;
                    });
                }
                finally
                {
                    gate.Release();
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }).ToList();

        await Task.WhenAll(pending).ConfigureAwait(false);
    }

    /// <summary>剩余分页的并发上限。几百个文件的模组原本要一页一页串行等十来次往返</summary>
    private const int MaxConcurrentVersionPages = 4;

    private async Task LoadCurseForgeVersionsAsync(CurseForgeMod mod, CancellationToken cancellationToken)
    {
        const int pageSize = 50;

        var first = await CurseForgeService.GetModFilesAsync(mod.Id, pageIndex: 0, pageSize: pageSize)
            .ConfigureAwait(false);
        if (first?.Data == null || first.Data.Count == 0) return;

        var allFiles = new List<CurseForgeFile>(first.Data);
        var totalCount = first.Pagination?.TotalCount ?? 0;

        if (totalCount > allFiles.Count)
        {
            // 总数已知：剩下的页并行拉，再按页序拼回去
            var pageCount = (totalCount + pageSize - 1) / pageSize;
            var pages = new List<CurseForgeFile>?[pageCount];

            using var gate = new SemaphoreSlim(MaxConcurrentVersionPages);
            var pending = new List<Task>();
            for (int p = 1; p < pageCount; p++)
            {
                var pageIndex = p;
                pending.Add(Task.Run(async () =>
                {
                    try
                    {
                        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                        try
                        {
                            var result = await CurseForgeService.GetModFilesAsync(mod.Id, pageIndex: pageIndex, pageSize: pageSize)
                                .ConfigureAwait(false);
                            pages[pageIndex] = result?.Data;
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch { }
                }, cancellationToken));
            }
            await Task.WhenAll(pending).ConfigureAwait(false);

            for (int p = 1; p < pageCount; p++)
            {
                if (pages[p] is { Count: > 0 } page)
                    allFiles.AddRange(page);
            }
        }
        else if (totalCount <= 0)
        {
            // 拿不到总数（分页信息缺失）时退回逐页串行，避免漏掉后面的文件
            int pageIndex = 1;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await CurseForgeService.GetModFilesAsync(mod.Id, pageIndex: pageIndex, pageSize: pageSize)
                    .ConfigureAwait(false);
                if (result?.Data == null || result.Data.Count == 0) break;

                allFiles.AddRange(result.Data);
                pageIndex++;
            }
        }

        if (allFiles.Count == 0) return;

        var sorted = allFiles.OrderByDescending(f => f.FileDate).ToList();
        var groups = GroupCurseForgeFiles(sorted);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var g in groups)
            {
                VersionGroups.Add(g);
            }
        });
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

        var sorted = versions.OrderByDescending(v => v.DatePublished).ToList();

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
        var groups = new List<VersionGroupViewModel>();
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

            groups.Add(groupVm);
        }

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var groupVm in groups)
            {
                VersionGroups.Add(groupVm);
            }
        });
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
        if (entry == null || entry.IsDownloading) return;

        entry.IsDownloading = true;
        entry.IsDownloaded = false;
        try
        {
            if (ResourceType == "Modpacks")
            {
                await HandleModpackInstallation(entry);
                return;
            }

            if (entry.BackendType == VersionBackendType.CurseForge && entry.CurseForgeFile != null)
            {
                await DownloadCurseForgeAsync(entry.CurseForgeFile, entry);
            }
            else if (entry.BackendType == VersionBackendType.Modrinth && entry.ModrinthVersion != null && entry.ModrinthFile != null)
            {
                await DownloadModrinthAsync(entry.ModrinthVersion, entry.ModrinthFile, entry);
            }
        }
        finally
        {
            entry.IsDownloading = false;
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

        if (dialogResult != DialogResult.OK || string.IsNullOrWhiteSpace(versionName))
        {
            ShowInfo(InfoBarSeverity.Informational, "已取消", "未安装整合包");
            return;
        }

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
                ShowInfo(InfoBarSeverity.Error, "整合包下载失败", "下载中断或失败，请检查网络后重试");
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
            entry.IsDownloaded = true;
            ShowInfo(InfoBarSeverity.Success, "安装完成", $"整合包「{versionName}」已安装成功");
        }
        catch (Exception ex)
        {
            if (taskId != null)
                Core.Services.Download.DownloadTaskManager.Instance.FailTask(taskId, ex.Message);
            ShowInfo(InfoBarSeverity.Error, "整合包安装失败", ex.Message);
        }
        finally
        {
            cts?.Dispose();
        }
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

    private async Task<bool> DownloadCurseForgeAsync(CurseForgeFile file, VersionEntryViewModel entry)
    {
        CancellationTokenSource? cts = null;
        string? taskId = null;
        try
        {
            var config = LauncherConfig.Load();
            var defaultDir = GetTargetDirectory(config);
            
            var savePath = await ShowSaveFileDialogAsync(defaultDir, file.FileName);
            if (string.IsNullOrEmpty(savePath))
            {
                ShowInfo(InfoBarSeverity.Informational, "已取消下载", "未选择保存位置");
                return false;
            }

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
                ShowInfo(InfoBarSeverity.Informational, "已取消下载", $"{file.FileName} 已取消");
                return false;
            }

            if (success)
            {
                if (taskId != null)
                    Core.Services.Download.DownloadTaskManager.Instance.CompleteTask(taskId);
                entry.IsDownloaded = true;
                ShowInfo(InfoBarSeverity.Success, "下载完成", $"{file.FileName} 已保存到 {finalDir}");
                return true;
            }
            else
            {
                if (taskId != null)
                    Core.Services.Download.DownloadTaskManager.Instance.FailTask(taskId, "下载失败");
                ShowInfo(InfoBarSeverity.Error, "下载失败", "下载未完成，请检查网络后重试");
                return false;
            }
        }
        catch (Exception ex)
        {
            if (taskId != null)
                Core.Services.Download.DownloadTaskManager.Instance.FailTask(taskId, "下载失败");
            ShowInfo(InfoBarSeverity.Error, "下载失败", ex.Message);
            return false;
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

    private async Task<bool> DownloadModrinthAsync(ModrinthVersion version, ModrinthVersionFile file, VersionEntryViewModel entry)
    {
        CancellationTokenSource? cts = null;
        string? taskId = null;
        try
        {
            var config = LauncherConfig.Load();
            var defaultDir = GetTargetDirectory(config);

            var savePath = await ShowSaveFileDialogAsync(defaultDir, file.Filename);
            if (string.IsNullOrEmpty(savePath))
            {
                ShowInfo(InfoBarSeverity.Informational, "已取消下载", "未选择保存位置");
                return false;
            }

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
                ShowInfo(InfoBarSeverity.Informational, "已取消下载", $"{file.Filename} 已取消");
                return false;
            }

            if (success)
            {
                if (taskId != null)
                    Core.Services.Download.DownloadTaskManager.Instance.CompleteTask(taskId);
                entry.IsDownloaded = true;
                ShowInfo(InfoBarSeverity.Success, "下载完成", $"{file.Filename} 已保存到 {finalDir}");
                return true;
            }
            else
            {
                if (taskId != null)
                    Core.Services.Download.DownloadTaskManager.Instance.FailTask(taskId, "下载失败");
                ShowInfo(InfoBarSeverity.Error, "下载失败", "下载未完成，请检查网络后重试");
                return false;
            }
        }
        catch (Exception ex)
        {
            if (taskId != null)
                Core.Services.Download.DownloadTaskManager.Instance.FailTask(taskId, "下载失败");
            ShowInfo(InfoBarSeverity.Error, "下载失败", ex.Message);
            return false;
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

/// <summary>
/// 前置资源的跳转请求。带上目标资源自身的类型，避免父资源的类型被套到前置上
/// （例如从整合包详情页点进一个 Mod 前置时，不能按"整合包"去走安装流程）。
/// </summary>
public sealed record DependencyNavigationRequest(object RawData, string ResourceType);

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

    /// <summary>前置多于这个数量时默认折叠，避免单组挂几十个前置把页面撑爆</summary>
    public const int DependencyPreviewLimit = 8;

    /// <summary>本 MC 版本组内所有文件、所有加载器的前置（已去重，必需在前）</summary>
    public ObservableCollection<DependencyItemViewModel> Dependencies { get; } = new();

    public bool HasDependencies => Dependencies.Count > 0;

    public string DependenciesDisplay
    {
        get
        {
            if (Dependencies.Count == 0) return "";
            var required = Dependencies.Count(d => d.IsRequired);
            var optional = Dependencies.Count(d => !d.IsRequired);
            var parts = new List<string>();
            if (required > 0) parts.Add($"{required} 个必需");
            if (optional > 0) parts.Add($"{optional} 个可选");
            return string.Join(" · ", parts);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleDependencies))]
    [NotifyPropertyChangedFor(nameof(DependenciesToggleText))]
    private bool _isDependenciesExpanded;

    public IEnumerable<DependencyItemViewModel> VisibleDependencies =>
        IsDependenciesExpanded ? Dependencies : Dependencies.Take(DependencyPreviewLimit);

    public bool HasMoreDependencies => Dependencies.Count > DependencyPreviewLimit;

    public string DependenciesToggleText => IsDependenciesExpanded ? "收起" : $"展开全部 {Dependencies.Count} 项";

    [RelayCommand]
    private void ToggleDependencies()
    {
        IsDependenciesExpanded = !IsDependenciesExpanded;
    }

    public void NotifyDependenciesChanged()
    {
        OnPropertyChanged(nameof(HasDependencies));
        OnPropertyChanged(nameof(DependenciesDisplay));
        OnPropertyChanged(nameof(HasMoreDependencies));
        OnPropertyChanged(nameof(VisibleDependencies));
        OnPropertyChanged(nameof(DependenciesToggleText));
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
    [ObservableProperty] private string _loader = string.Empty;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private bool _isDownloaded;
}

public partial class DependencyItemViewModel : ObservableObject
{
    public VersionBackendType BackendType { get; set; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _dependencyType = string.Empty;
    [ObservableProperty] private bool _isRequired;
    [ObservableProperty] private string _projectId = string.Empty;
    [ObservableProperty] private int _curseForgeModId;

    /// <summary>图标地址。只用于去重与加载，不直接绑定</summary>
    public string IconUrl { get; set; } = "";

    /// <summary>加载好的图标；同一 URL 的多个前置共享同一个实例</summary>
    [ObservableProperty] private Bitmap? _icon;

    /// <summary>工程简介（单行显示）</summary>
    public string Description { get; set; } = "";

    public string AuthorDisplay { get; set; } = "";

    public string DownloadsDisplay { get; set; } = "";

    public string LastUpdateDisplay { get; set; } = "";

    /// <summary>该前置自身的资源类型（Mods / Textures / Shaders / Datapacks / Modpacks），未知为 "Any"</summary>
    public string ResourceType { get; set; } = "Any";

    /// <summary>批量拉取阶段留下的完整工程对象，跳转时直接传下去以免二次请求；为 null 表示没取到</summary>
    public object? RawProject { get; set; }

    public bool IsResolved => RawProject != null;

    /// <summary>拿不到详细信息的项仍然显示（提示不可跳转），但不响应点击</summary>
    public bool CanNavigate => IsResolved;

    public string TypeTag => IsRequired ? "必需" : "可选";
    public string TypeTagColor => IsRequired ? "#E74C3C" : "#95A5A6";

    public string SourceTag => BackendType == VersionBackendType.CurseForge ? "CurseForge" : "Modrinth";
    public string SourceTagColor => BackendType == VersionBackendType.CurseForge ? "#F16436" : "#1BD96A";

    public string StatusText => IsResolved ? "" : "未获取到详细信息，暂不能跳转";

    // 用于去重比较：同一后端 + 同一 ID 视为同一依赖
    public string UniqueKey => BackendType == VersionBackendType.CurseForge
        ? $"cf_{CurseForgeModId}"
        : $"mr_{ProjectId}";

    /// <summary>同一前置出现在多个文件/多个加载器下时合并：必需优先，并补齐缺失的元信息</summary>
    public void MergeFrom(DependencyItemViewModel other)
    {
        if (other.IsRequired && !IsRequired)
        {
            IsRequired = true;
            DependencyType = "必需";
        }

        if (string.IsNullOrEmpty(IconUrl)) IconUrl = other.IconUrl;
        if (string.IsNullOrEmpty(Description)) Description = other.Description;
        if (string.IsNullOrEmpty(AuthorDisplay)) AuthorDisplay = other.AuthorDisplay;
        if (string.IsNullOrEmpty(DownloadsDisplay)) DownloadsDisplay = other.DownloadsDisplay;
        if (string.IsNullOrEmpty(LastUpdateDisplay)) LastUpdateDisplay = other.LastUpdateDisplay;
        if (ResourceType == "Any") ResourceType = other.ResourceType;

        RawProject ??= other.RawProject;
    }
}

public class LoaderFilterItem
{
    public string LoaderName { get; init; } = "";
    public string IconUri { get; init; } = "";
    public int Count { get; init; }

    // 基于名称做值相等，确保 ListBox SelectedItem 能正确匹配重建后的对象
    public override bool Equals(object? obj) =>
        obj is LoaderFilterItem other && string.Equals(LoaderName, other.LoaderName, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(LoaderName);
}
