using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Plugins;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.ViewModels.Dialogs;
using ObsMCLauncher.Desktop.ViewModels.Notifications;

namespace ObsMCLauncher.Desktop.ViewModels;

public enum PluginSubTab
{
    Market,
    Installed
}

public enum PlatformFilter
{
    All,
    Windows,
    Linux,
    macOS,
    Android
}

public partial class PluginsViewModel : ViewModelBase
{
    private readonly PluginLoader _pluginLoader;
    private readonly NotificationService _notificationService;
    private readonly DialogService _dialogService;

    [ObservableProperty]
    private PluginSubTab _currentTab = PluginSubTab.Market;

    public bool IsMarket => CurrentTab == PluginSubTab.Market;
    public bool IsInstalled => CurrentTab == PluginSubTab.Installed;

    [ObservableProperty]
    private int _currentTabIndex;

    partial void OnCurrentTabIndexChanged(int value)
    {
        if (value < 0) value = 0;
        if ((int)CurrentTab != value)
        {
            CurrentTab = (PluginSubTab)value;
        }
    }

    [ObservableProperty]
    private ObservableCollection<PluginListItemViewModel> _leftItems = new();

    [ObservableProperty]
    private PluginListItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isMarketLoading;

    [ObservableProperty]
    private string? _marketError;

    [ObservableProperty]
    private bool _isEmptyHintVisible;

    [ObservableProperty]
    private PluginDetailViewModel _detail = new();

    [ObservableProperty]
    private int _selectedDetailTabIndex;

    [ObservableProperty]
    private ObservableCollection<PluginCategory> _categories = new();

    [ObservableProperty]
    private PluginCategory? _selectedCategory;

    [ObservableProperty]
    private ObservableCollection<PlatformFilterItem> _platformFilters = new();

    [ObservableProperty]
    private PlatformFilterItem? _selectedPlatformFilter;

    [ObservableProperty]
    private string _emptyStateText = string.Empty;

    private ObservableCollection<MarketPlugin>? _allMarketPlugins;

    /// <summary>插件 id → 更新状态（由本地版本与市场版本比对得出）</summary>
    private Dictionary<string, PluginUpdateInfo> _updateInfos = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>可更新且尚未下载暂存的插件数量</summary>
    [ObservableProperty]
    private int _updateCount;

    /// <summary>已下载暂存、等待重启生效的插件数量</summary>
    [ObservableProperty]
    private int _pendingRestartCount;

    /// <summary>更新/检查进行中（用于禁用按钮，避免重复点击）</summary>
    [ObservableProperty]
    private bool _isUpdateBusy;

    public bool HasUpdates => UpdateCount > 0;

    public bool HasPendingRestart => PendingRestartCount > 0;

    public string UpdateAllButtonText => UpdateCount > 0 ? $"全部更新 ({UpdateCount})" : "全部更新";

    public bool UpdateAllEnabled => UpdateCount > 0 && !IsUpdateBusy;

    public string PendingRestartText => $"有 {PendingRestartCount} 个更新已就绪，重启启动器后生效";

    partial void OnUpdateCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUpdates));
        OnPropertyChanged(nameof(UpdateAllButtonText));
        OnPropertyChanged(nameof(UpdateAllEnabled));
    }

    partial void OnPendingRestartCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasPendingRestart));
        OnPropertyChanged(nameof(PendingRestartText));
    }

    partial void OnIsUpdateBusyChanged(bool value) => OnPropertyChanged(nameof(UpdateAllEnabled));

    /// <summary>请求宿主关掉当前进程以完成"重启生效"（由 MainWindowViewModel 订阅并执行）</summary>
    public event Action? RestartRequested;

    public PluginsViewModel(PluginLoader pluginLoader, NotificationService notificationService, DialogService dialogService)
    {
        _pluginLoader = pluginLoader;
        _notificationService = notificationService;
        _dialogService = dialogService;

        PlatformFilters = new ObservableCollection<PlatformFilterItem>
        {
            new PlatformFilterItem(PlatformFilter.All, "全部平台"),
            new PlatformFilterItem(PlatformFilter.Windows, "Windows"),
            new PlatformFilterItem(PlatformFilter.Linux, "Linux"),
            new PlatformFilterItem(PlatformFilter.macOS, "macOS"),
            new PlatformFilterItem(PlatformFilter.Android, "Android")
        };

        SelectedPlatformFilter = PlatformFilters.FirstOrDefault(p => p.Filter == GetCurrentPlatformFilter());

        // 远程图标下载完成后刷新列表项图标绑定
        Converters.PluginIconConverter.IconDownloaded += OnIconDownloaded;
    }

    private void OnIconDownloaded(string url)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (var item in LeftItems)
            {
                if (item.IconUrl == url)
                {
                    item.NotifyIconUrlChanged();
                }
            }
        });
    }

    private PlatformFilter GetCurrentPlatformFilter()
    {
        if (OperatingSystem.IsWindows()) return PlatformFilter.Windows;
        if (OperatingSystem.IsLinux()) return PlatformFilter.Linux;
        if (OperatingSystem.IsMacOS()) return PlatformFilter.macOS;
        if (OperatingSystem.IsAndroid()) return PlatformFilter.Android;
        return PlatformFilter.All;
    }

    private int _readmeRequestId;
    private CancellationTokenSource? _filterCts;
    private const int FILTER_DEBOUNCE_MS = 200;

    private static readonly System.Net.Http.HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>description 解析结果缓存（key = 索引里的原始 description）</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Text, PluginDescriptionKind Kind)> DescriptionCache = new();

    partial void OnSelectedItemChanged(PluginListItemViewModel? value)
    {
        Detail = value?.ToDetail() ?? new PluginDetailViewModel();
        SelectedDetailTabIndex = 0;
        _ = LoadReadmeForDetailAsync(value);
    }

    partial void OnCurrentTabChanged(PluginSubTab value)
    {
        OnPropertyChanged(nameof(IsMarket));
        OnPropertyChanged(nameof(IsInstalled));
        if (CurrentTabIndex != (int)value)
        {
            CurrentTabIndex = (int)value;
        }
        _ = RefreshLeftAsync();
    }

    partial void OnSelectedCategoryChanged(PluginCategory? value)
    {
        _ = FilterMarketPluginsAsync();
    }

    partial void OnSelectedPlatformFilterChanged(PlatformFilterItem? value)
    {
        _ = FilterMarketPluginsAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = FilterMarketPluginsAsync();
    }

    private async Task LoadReadmeForDetailAsync(PluginListItemViewModel? item)
    {
        var requestId = ++_readmeRequestId;

        try
        {
            if (item == null) return;

            if (item.Source == PluginItemSource.Installed && item.Installed != null)
            {
                var readmePath = item.Installed.ReadmePath;
                if (!string.IsNullOrWhiteSpace(readmePath) && System.IO.File.Exists(readmePath))
                {
                    var markdown = await System.IO.File.ReadAllTextAsync(readmePath);
                    if (requestId != _readmeRequestId) return;
                    ApplyDescription(Detail, markdown, PluginDescriptionKind.Markdown);
                }
                else
                {
                    var (text, kind) = await ResolveDescriptionAsync(item.Installed.Description ?? string.Empty);
                    if (requestId != _readmeRequestId) return;
                    ApplyDescription(Detail, text, kind);
                }
            }
            else if (item.Source == PluginItemSource.Market && item.MarketPlugin != null)
            {
                var (text, kind) = await ResolveDescriptionAsync(item.MarketPlugin.Description ?? string.Empty);
                if (requestId != _readmeRequestId) return;
                ApplyDescription(Detail, text, kind);
            }
        }
        catch
        {
            if (requestId != _readmeRequestId) return;
            ApplyDescription(Detail, string.Empty, PluginDescriptionKind.Empty);
        }
    }

    /// <summary>
    /// 解析 description：是链接就下载（GitHub 链接走镜像源），否则用原文；
    /// 结果按原始字符串缓存，避免重复请求。
    /// </summary>
    private static async Task<(string Text, PluginDescriptionKind Kind)> ResolveDescriptionAsync(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return (string.Empty, PluginDescriptionKind.Empty);

        if (DescriptionCache.TryGetValue(description, out var cached))
            return cached;

        var resolved = await PluginDescriptionResolver.ResolveAsync(description, _httpClient);
        DescriptionCache[description] = resolved;
        return resolved;
    }

    /// <summary>按解析结果呈现描述：Markdown 渲染 / 纯文本直显 / 空</summary>
    private static void ApplyDescription(PluginDetailViewModel detail, string text, PluginDescriptionKind kind)
    {
        switch (kind)
        {
            case PluginDescriptionKind.Markdown:
                detail.Markdown = text;
                detail.MarkdownVisible = !string.IsNullOrWhiteSpace(text);
                detail.PlainText = string.Empty;
                detail.PlainTextVisible = false;
                break;

            case PluginDescriptionKind.PlainText:
                detail.PlainText = text;
                detail.PlainTextVisible = !string.IsNullOrWhiteSpace(text);
                detail.Markdown = string.Empty;
                detail.MarkdownVisible = false;
                break;

            default:
                detail.Markdown = string.Empty;
                detail.MarkdownVisible = false;
                detail.PlainText = string.Empty;
                detail.PlainTextVisible = false;
                break;
        }
    }

    public async Task InitializeAsync()
    {
        // 插件已经在启动时加载，这里只需要刷新UI
        await LoadCategoriesAsync();
        await RefreshLeftAsync();
    }

    private async Task LoadCategoriesAsync()
    {
        try
        {
            var categories = await PluginMarketService.GetCategoriesAsync();
            if (categories != null)
            {
                Categories.Clear();
                Categories.Add(new PluginCategory { Id = "", Name = "全部分类" });
                foreach (var cat in categories)
                {
                    Categories.Add(cat);
                }
                SelectedCategory = Categories.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginsVM", $"加载分类失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RefreshLeftAsync();
    }

    private Task RefreshInstalledAsync()
    {
        return RefreshLeftAsync();
    }

    /// <summary>
    /// 检查更新：拉一次市场索引，再对声明了 releaseUrl 的已安装插件并发查 GitHub Release，
    /// 结果落盘缓存（TTL 6 小时）。比打开列表时的本地版本比对更准。
    /// </summary>
    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (IsUpdateBusy) return;

        string? notificationId = null;
        try
        {
            IsUpdateBusy = true;
            notificationId = _notificationService.Show("检查更新", "正在检查插件更新...", NotificationType.Progress);

            var index = await PluginMarketService.GetMarketIndexAsync(forceRefresh: true);
            var market = index?.Plugins ?? new List<MarketPlugin>();

            var infos = await PluginUpdateService.CheckForUpdatesAsync(
                _pluginLoader.PluginsDirectory,
                _pluginLoader.LoadedPlugins,
                market,
                deep: true,
                forceRefresh: true);

            _updateInfos = infos.ToDictionary(i => i.PluginId, i => i, StringComparer.OrdinalIgnoreCase);
            UpdateCount = infos.Count(i => i.HasUpdate && !i.IsStaged);
            PendingRestartCount = infos.Count(i => i.IsStaged);

            PluginUpdateService.MarkChecked(_pluginLoader.PluginsDirectory);

            _notificationService.Remove(notificationId);
            notificationId = null;

            var actionable = infos.Where(i => i.HasUpdate && !i.IsStaged).ToList();
            if (actionable.Count == 0)
            {
                _notificationService.Show("检查更新", "所有插件都是最新版本", NotificationType.Success);
            }
            else
            {
                _notificationService.Show("检查更新",
                    $"发现 {actionable.Count} 个插件可更新：{DescribeUpdates(actionable)}",
                    NotificationType.Info, 6);
            }

            // 刷新当前列表，让角标立刻反映检查结果
            await RefreshLeftAsync();
        }
        catch (Exception ex)
        {
            _notificationService.Show("检查更新", $"检查失败: {ex.Message}", NotificationType.Error);
        }
        finally
        {
            if (notificationId != null) _notificationService.Remove(notificationId);
            IsUpdateBusy = false;
        }
    }

    /// <summary>更新选中的插件：下载并暂存，重启启动器后生效</summary>
    [RelayCommand]
    private async Task UpdatePluginAsync()
    {
        var installed = SelectedItem?.Installed;
        if (installed == null) return;

        if (!_updateInfos.TryGetValue(installed.Id, out var info) || !info.HasUpdate)
        {
            _notificationService.Show("插件更新", "该插件没有可用更新", NotificationType.Info);
            return;
        }

        if (info.Market == null)
        {
            _notificationService.Show("插件更新", "市场中找不到该插件，无法获取下载地址", NotificationType.Error);
            return;
        }

        if (IsUpdateBusy) return;

        try
        {
            IsUpdateBusy = true;
            var (success, message) = await StageOneAsync(info);
            _notificationService.Show(success ? "更新已就绪" : "更新失败", message,
                success ? NotificationType.Success : NotificationType.Error);
        }
        finally
        {
            IsUpdateBusy = false;
        }

        await RefreshInstalledAsync();
    }

    /// <summary>取消选中插件已下载但尚未生效的更新</summary>
    [RelayCommand]
    private async Task CancelUpdateAsync()
    {
        var installed = SelectedItem?.Installed;
        if (installed == null) return;

        if (!PluginUpdateService.CancelPendingUpdate(_pluginLoader.PluginsDirectory, installed.Id))
        {
            _notificationService.Show("插件更新", "没有可取消的更新", NotificationType.Info);
            return;
        }

        _notificationService.Show("插件更新", $"{installed.Name} 的更新已取消", NotificationType.Success);
        await RefreshInstalledAsync();
    }

    /// <summary>一键更新全部有更新的插件</summary>
    [RelayCommand]
    private async Task UpdateAllAsync()
    {
        if (IsUpdateBusy) return;

        var targets = _updateInfos.Values
            .Where(i => i.HasUpdate && !i.IsStaged && i.Market != null)
            .ToList();

        if (targets.Count == 0)
        {
            _notificationService.Show("插件更新", "没有可更新的插件", NotificationType.Info);
            return;
        }

        try
        {
            IsUpdateBusy = true;

            var succeeded = new List<string>();
            var failed = new List<string>();

            foreach (var info in targets)
            {
                var (success, message) = await StageOneAsync(info);
                if (success) succeeded.Add(info.Name);
                else failed.Add(message);
            }

            if (failed.Count == 0)
            {
                _notificationService.Show("更新已就绪",
                    $"{succeeded.Count} 个插件已下载完成，重启启动器后生效", NotificationType.Success, 6);
            }
            else if (succeeded.Count == 0)
            {
                _notificationService.Show("更新失败", string.Join("；", failed), NotificationType.Error, 8);
            }
            else
            {
                _notificationService.Show("部分更新成功",
                    $"{succeeded.Count} 个成功、{failed.Count} 个失败：{string.Join("；", failed)}",
                    NotificationType.Warning, 8);
            }
        }
        finally
        {
            IsUpdateBusy = false;
        }

        await RefreshInstalledAsync();
    }

    /// <summary>重启启动器，让已下载的更新生效</summary>
    [RelayCommand]
    private void RestartLauncher()
    {
        if (!AppRestarter.TryStartNewInstance(out var error))
        {
            _notificationService.Show("重启失败",
                $"{error ?? "未知错误"}，请手动关闭并重新打开启动器", NotificationType.Error);
            return;
        }

        RestartRequested?.Invoke();
    }

    /// <summary>下载并暂存单个插件更新，返回 (是否成功, 展示文案)</summary>
    private async Task<(bool Success, string Message)> StageOneAsync(PluginUpdateInfo info)
    {
        var market = info.Market!;
        var notificationId = _notificationService.Show("下载中",
            $"正在下载 {market.Name}... 0%", NotificationType.Progress);

        var progress = new Progress<double>(p =>
            _notificationService.Update(notificationId, $"正在下载 {market.Name}... {(int)p}%", p));

        try
        {
            var (success, error, stagedVersion) = await PluginUpdateService.StageUpdateAsync(
                market,
                _pluginLoader.PluginsDirectory,
                info.InstalledVersion,
                progress);

            return success
                ? (true, $"{market.Name} {stagedVersion} 已下载，重启启动器后生效")
                : (false, $"{market.Name}: {error ?? "未知错误"}");
        }
        catch (Exception ex)
        {
            return (false, $"{market.Name}: {ex.Message}");
        }
        finally
        {
            _notificationService.Remove(notificationId);
        }
    }

    private static string DescribeUpdates(IEnumerable<PluginUpdateInfo> infos)
    {
        const int maxNames = 3;
        var names = infos.Select(i => i.Name).ToList();

        return names.Count <= maxNames
            ? string.Join("、", names)
            : string.Join("、", names.Take(maxNames)) + $" 等 {names.Count} 个";
    }

    private async Task RefreshLeftAsync()
    {
        var previousSelected = SelectedItem?.Title;

        LeftItems.Clear();
        SelectedItem = null;

        if (CurrentTab == PluginSubTab.Installed)
        {
            await LoadInstalledItemsAsync();
        }
        else
        {
            IsMarketLoading = true;
            MarketError = null;
            IsEmptyHintVisible = false;

            try
            {
                var index = await PluginMarketService.GetMarketIndexAsync();
                if (index?.Plugins != null)
                {
                    _allMarketPlugins = new ObservableCollection<MarketPlugin>(index.Plugins);

                    // 市场索引已在手，顺带算一遍更新状态（纯本地比对，不再发请求）
                    RefreshUpdateInfos(index.Plugins);

                    await FilterMarketPluginsAsync();
                }
                else
                {
                    MarketError = "无法加载插件市场数据";
                    IsEmptyHintVisible = true;
                    EmptyStateText = "无法加载插件市场数据";
                }
            }
            catch (Exception ex)
            {
                MarketError = $"加载失败: {ex.Message}";
                IsEmptyHintVisible = true;
                EmptyStateText = MarketError;
            }
            finally
            {
                IsMarketLoading = false;
            }
        }

        TryRestoreSelection(previousSelected);
    }

    private async Task LoadInstalledItemsAsync()
    {
        var installed = _pluginLoader.LoadedPlugins;

        // 已安装列表要显示"可更新"角标，需要市场索引；命中 10 分钟内存缓存时零网络开销
        try
        {
            var index = await PluginMarketService.GetMarketIndexAsync();
            RefreshUpdateInfos(index?.Plugins);
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("PluginsVM", $"获取市场索引失败，本次不显示更新状态: {ex.Message}");
            RefreshUpdateInfos(null);
        }

        LeftItems.Clear();
        foreach (var p in installed)
        {
            _updateInfos.TryGetValue(p.Id, out var updateInfo);
            LeftItems.Add(PluginListItemViewModel.FromInstalled(p, updateInfo));
        }

        IsEmptyHintVisible = LeftItems.Count == 0;
        if (IsEmptyHintVisible)
        {
            EmptyStateText = "还没有安装任何插件，去插件市场看看吧";
        }
    }

    /// <summary>
    /// 重算全部插件的更新状态。纯本地版本比对（市场索引已在手），不发网络请求。
    /// </summary>
    private void RefreshUpdateInfos(IEnumerable<MarketPlugin>? market)
    {
        var marketList = market?.ToList() ?? new List<MarketPlugin>();

        var infos = PluginUpdateService.BuildUpdateInfo(
            _pluginLoader.PluginsDirectory, _pluginLoader.LoadedPlugins, marketList);

        _updateInfos = infos.ToDictionary(i => i.PluginId, i => i, StringComparer.OrdinalIgnoreCase);

        UpdateCount = infos.Count(i => i.HasUpdate && !i.IsStaged);
        PendingRestartCount = infos.Count(i => i.IsStaged);
    }

    private void TryRestoreSelection(string? title)
    {
        if (string.IsNullOrEmpty(title)) return;

        var match = LeftItems.FirstOrDefault(i => i.Title == title);
        if (match != null)
        {
            SelectedItem = match;
        }
    }

    private async Task FilterMarketPluginsAsync()
    {
        if (_allMarketPlugins == null) return;

        _filterCts?.Cancel();
        var cts = new CancellationTokenSource();
        _filterCts = cts;
        var token = cts.Token;

        try
        {
            await Task.Delay(FILTER_DEBOUNCE_MS, token);
            if (token.IsCancellationRequested) return;

            var previousSelected = SelectedItem?.Title;

            var filtered = _allMarketPlugins.AsEnumerable();

            // 平台过滤
            if (SelectedPlatformFilter != null && SelectedPlatformFilter.Filter != PlatformFilter.All)
            {
                var targetPlatform = SelectedPlatformFilter.Filter.ToString();
                filtered = filtered.Where(p => p.Platforms.Count == 0 || p.Platforms.Contains(targetPlatform));
            }

            // 分类过滤
            if (SelectedCategory != null && !string.IsNullOrEmpty(SelectedCategory.Id))
            {
                filtered = filtered.Where(p => p.Category == SelectedCategory.Id);
            }

            // 搜索过滤
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var search = SearchText.ToLowerInvariant();
                filtered = filtered.Where(p =>
                    p.Name.ToLowerInvariant().Contains(search) ||
                    p.Description.ToLowerInvariant().Contains(search) ||
                    p.Author.ToLowerInvariant().Contains(search));
            }

            if (token.IsCancellationRequested) return;

            LeftItems.Clear();
            foreach (var p in filtered)
            {
                LeftItems.Add(PluginListItemViewModel.FromMarket(p));
            }

            IsEmptyHintVisible = LeftItems.Count == 0;
            if (IsEmptyHintVisible)
            {
                EmptyStateText = "找不到符合条件的插件，请调整筛选条件后重试";
            }
            TryRestoreSelection(previousSelected);
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand]
    private async Task PrimaryActionAsync()
    {
        try
        {
            var item = SelectedItem;
            if (item == null) return;

            if (item.Source == PluginItemSource.Installed && item.Installed != null)
            {
                await ToggleInstalledAsync(item.Installed);
                await RefreshInstalledAsync();
            }
            else if (item.Source == PluginItemSource.Market && item.MarketPlugin != null)
            {
                if (!item.MarketPlugin.IsApiVersionCompatible)
                {
                    _notificationService.Show("无法安装",
                        item.MarketPlugin.IncompatibleReason ?? "该插件与当前启动器不兼容",
                        NotificationType.Error);
                    return;
                }

                await InstallMarketPluginAsync(item.MarketPlugin);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("操作失败", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task SecondaryActionAsync()
    {
        if (SelectedItem?.Source != PluginItemSource.Installed) return;
        if (SelectedItem.Installed == null) return;

        await UninstallInstalledAsync(SelectedItem.Installed);
        await RefreshInstalledAsync();
    }

    [RelayCommand]
    private void OpenHome()
    {
        if (string.IsNullOrWhiteSpace(Detail.HomeUrl)) return;
        TryOpenUrl(Detail.HomeUrl);
    }

    [RelayCommand]
    private void OpenRepo()
    {
        if (string.IsNullOrWhiteSpace(Detail.RepoUrl)) return;
        TryOpenUrl(Detail.RepoUrl);
    }

    private static void TryOpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private async Task ToggleInstalledAsync(LoadedPlugin plugin)
    {
        try
        {
            var disabledMarkerPath = System.IO.Path.Combine(plugin.DirectoryPath, ".disabled");
            var willDisable = plugin.IsLoaded || !System.IO.File.Exists(disabledMarkerPath);

            bool success;
            string? errorMessage = null;

            if (willDisable)
            {
                success = await Task.Run(() => _pluginLoader.DisablePluginImmediately(plugin.Id));
            }
            else
            {
                success = _pluginLoader.EnablePlugin(plugin.Id, out errorMessage);
            }

            if (success)
            {
                _notificationService.Show("插件管理",
                    willDisable ? $"插件 {plugin.Name} 已禁用" : $"插件 {plugin.Name} 已启用",
                    NotificationType.Success);
            }
            else
            {
                _notificationService.Show("插件管理", $"操作失败: {errorMessage ?? "未知错误"}", NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("插件管理", $"操作失败: {ex.Message}", NotificationType.Error);
        }
    }

    private async Task UninstallInstalledAsync(LoadedPlugin plugin)
    {
        try
        {
            // 插件数据目录已与插件本体分离，卸载不会再顺手删掉配置——
            // 所以有数据时先问一句，默认保留。
            var dataDir = PluginContext.GetPluginDataDirectory(plugin.Id);
            var hasPluginData = System.IO.Directory.Exists(dataDir);

            var deleteData = false;
            if (hasPluginData)
            {
                var choice = await _dialogService.ShowAsync(
                    "卸载插件",
                    $"确定要卸载「{plugin.Name}」吗？\n\n" +
                    $"是：连同配置与数据一起删除（不可恢复）\n" +
                    $"否：保留配置与数据，只移除插件本体\n" +
                    $"取消：不卸载",
                    DialogType.Warning,
                    DialogButtons.YesNoCancel);

                if (choice != DialogResult.Yes && choice != DialogResult.No) return;

                deleteData = choice == DialogResult.Yes;
            }

            string? errorMessage = null;
            bool success = await Task.Run(() => _pluginLoader.RemovePlugin(plugin.Id, out errorMessage));

            if (success)
            {
                _notificationService.Show("插件卸载",
                    string.IsNullOrEmpty(errorMessage) ? $"插件 {plugin.Name} 已卸载" : errorMessage,
                    string.IsNullOrEmpty(errorMessage) ? NotificationType.Success : NotificationType.Warning);

                if (deleteData)
                {
                    TryDeletePluginData(plugin, dataDir);
                }
            }
            else
            {
                _notificationService.Show("插件卸载", $"卸载失败: {errorMessage ?? "未知错误"}", NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("插件卸载", $"卸载失败: {ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>删除插件的配置与数据目录；失败只提示不抛出（插件本体已经卸载成功）</summary>
    private void TryDeletePluginData(LoadedPlugin plugin, string dataDir)
    {
        try
        {
            if (System.IO.Directory.Exists(dataDir))
            {
                System.IO.Directory.Delete(dataDir, true);
            }

            DebugLogger.Info("PluginsVM", $"已删除插件数据目录: {dataDir}");
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("PluginsVM", $"删除插件数据目录失败 [{dataDir}]: {ex.Message}");
            _notificationService.Show("插件数据未删除",
                $"{plugin.Name} 的配置目录被占用，未能删除：{dataDir}",
                NotificationType.Warning);
        }
    }

    private async Task InstallMarketPluginAsync(MarketPlugin plugin)
    {
        try
        {
            var pluginsDir = Path.Combine(VersionInfo.GetAppBaseDirectory(), "OMCL", "plugins");
            
            var notificationId = _notificationService.Show("下载中", $"正在下载 {plugin.Name}... 0%", NotificationType.Progress);

            var progress = new Progress<double>(p =>
            {
                _notificationService.Update(notificationId, $"正在下载 {plugin.Name}... {(int)p}%", p);
            });

            var success = await PluginMarketService.DownloadAndInstallPluginAsync(plugin, pluginsDir, progress);

            _notificationService.Remove(notificationId);

            if (success)
            {
                _notificationService.ShowCountdown("安装成功", $"插件 {plugin.Name} 已安装，重启启动器生效", 3);
                _pluginLoader.LoadPluginById(plugin.Id);
                await RefreshInstalledAsync();
                CurrentTab = PluginSubTab.Installed;
            }
            else
            {
                _notificationService.Show("安装失败", $"插件 {plugin.Name} 安装失败", NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("安装失败", ex.Message, NotificationType.Error);
        }
    }
}

public enum PluginItemSource
{
    Market,
    Installed
}

public partial class PluginListItemViewModel : ObservableObject
{
    public PluginItemSource Source { get; }
    public LoadedPlugin? Installed { get; }
    public MarketPlugin? MarketPlugin { get; }

    /// <summary>本地插件的更新状态；市场条目没有（用 MarketPlugin.HasUpdate）</summary>
    public PluginUpdateInfo? Update { get; }

    public string Title { get; }
    public string Meta { get; }
    public bool HasError { get; }
    public bool IsMarket => Source == PluginItemSource.Market;
    public bool IsInstalled => Source == PluginItemSource.Installed;
    public string? IconUrl { get; }

    /// <summary>插件声明的 API 版本区间不覆盖当前启动器</summary>
    public bool IsIncompatible { get; }

    /// <summary>不兼容原因（兼容时为 null）</summary>
    public string? IncompatibleReason { get; }

    /// <summary>有可用的新版本</summary>
    public bool HasUpdate => Update?.HasUpdate ?? MarketPlugin?.HasUpdate ?? false;

    /// <summary>新版本已下载，等待重启生效</summary>
    public bool IsUpdateStaged => Update?.IsStaged ?? false;

    /// <summary>列表右侧角标文案（无更新时为 null）</summary>
    public string? UpdateBadgeText =>
        IsUpdateStaged ? "待重启" :
        HasUpdate ? "可更新" : null;

    public string? UpdateTooltip =>
        IsUpdateStaged
            ? "新版本已下载，重启启动器后生效"
            : Update is { HasUpdate: true } info
                ? $"可更新 {info.InstalledVersion} → {info.AvailableVersion}"
                : HasUpdate ? "有可用的新版本" : null;

    private PluginListItemViewModel(PluginItemSource source, string title, string meta, bool hasError, LoadedPlugin? installed, MarketPlugin? marketPlugin, string? iconUrl = null, bool isIncompatible = false, string? incompatibleReason = null, PluginUpdateInfo? update = null)
    {
        Source = source;
        Title = title;
        Meta = meta;
        HasError = hasError;
        Installed = installed;
        MarketPlugin = marketPlugin;
        IconUrl = iconUrl;
        IsIncompatible = isIncompatible;
        IncompatibleReason = incompatibleReason;
        Update = update;
    }

    /// <summary>远程图标下载完成后通知重新求值图标绑定</summary>
    public void NotifyIconUrlChanged() => OnPropertyChanged(nameof(IconUrl));

    public static PluginListItemViewModel FromInstalled(LoadedPlugin p, PluginUpdateInfo? update = null)
    {
        var meta = update is { HasUpdate: true }
            ? $"{p.Version} → {update.AvailableVersion} | {p.Author}"
            : $"{p.Version} | {p.Author}";

        return new PluginListItemViewModel(PluginItemSource.Installed, p.Name, meta,
            !string.IsNullOrEmpty(p.ErrorOutput), p, null, p.IconPath, update: update);
    }

    public static PluginListItemViewModel FromMarket(MarketPlugin p)
    {
        var platforms = string.Join(", ", p.Platforms);
        var meta = string.IsNullOrEmpty(platforms)
            ? $"{p.Version} | {p.Author}"
            : $"{p.Version} | {p.Author} | {platforms}";
        return new PluginListItemViewModel(PluginItemSource.Market, p.Name, meta, false, null, p, p.Icon,
            !p.IsApiVersionCompatible, p.IncompatibleReason);
    }

    public PluginDetailViewModel ToDetail()
    {
        if (Source == PluginItemSource.Installed && Installed != null)
        {
            var outputText = string.IsNullOrWhiteSpace(Installed.ErrorOutput) ? "运行正常" : Installed.ErrorOutput;

            var hasError = !string.IsNullOrEmpty(Installed.ErrorMessage) && !Installed.IsLoaded;

            var detail = new PluginDetailViewModel
            {
                Title = Installed.Name,
                Meta = $"v{Installed.Version} | {Installed.Author}",
                IconUrl = Installed.IconPath,
                Description = Installed.Description ?? string.Empty,
                Output = outputText,
                OutputVisible = true,
                IsEnabledStatus = Installed.IsLoaded,
                IsErrorStatus = hasError,
                IsDisabledStatus = !Installed.IsLoaded && !hasError,
                PrimaryActionText = Installed.IsLoaded ? "禁用" : "启用",
                PrimaryActionEnabled = true,
                PrimaryActionVisible = true,
                SecondaryActionVisible = true,
                SecondaryActionText = "卸载"
            };

            if (Update is { HasUpdate: true } info)
            {
                detail.NewVersionText = $"{info.InstalledVersion} → {info.AvailableVersion}";
                detail.NewVersionVisible = true;
                detail.UpdateText = $"更新到 {info.AvailableVersion}";
                detail.UpdateVisible = !info.IsStaged;
                detail.UpdateEnabled = info.Market != null;
                detail.StagedHintVisible = info.IsStaged;
                detail.StagedHintText = "新版本已下载，重启启动器后生效";
                detail.CancelUpdateVisible = info.IsStaged;
            }
            else if (Update is { IsStaged: true } staged)
            {
                detail.NewVersionText = $"{staged.InstalledVersion} → {staged.AvailableVersion}";
                detail.NewVersionVisible = true;
                detail.StagedHintVisible = true;
                detail.StagedHintText = "新版本已下载，重启启动器后生效";
                detail.CancelUpdateVisible = true;
            }

            detail.HomeUrl = Installed.Metadata?.Homepage;
            detail.RepoUrl = Installed.Metadata?.Repository;
            detail.HomeVisible = !string.IsNullOrWhiteSpace(detail.HomeUrl);
            detail.RepoVisible = !string.IsNullOrWhiteSpace(detail.RepoUrl);

            return detail;
        }

        if (Source == PluginItemSource.Market && MarketPlugin != null)
        {
            var platforms = string.Join(", ", MarketPlugin.Platforms);
            var meta = string.IsNullOrEmpty(platforms)
                ? $"v{MarketPlugin.Version} | {MarketPlugin.Author}"
                : $"v{MarketPlugin.Version} | {MarketPlugin.Author} | {platforms}";

            var compatible = MarketPlugin.IsApiVersionCompatible;

            var detail = new PluginDetailViewModel
            {
                Title = MarketPlugin.Name,
                Meta = meta,
                IconUrl = MarketPlugin.Icon,
                Description = MarketPlugin.Description,
                OutputVisible = false,
                PrimaryActionText = "安装",
                PrimaryActionEnabled = compatible,
                PrimaryActionVisible = true,
                SecondaryActionVisible = false,
                SecondaryActionText = "",
                IsIncompatibleStatus = !compatible,
                IncompatibleReason = MarketPlugin.IncompatibleReason
            };

            detail.HomeUrl = MarketPlugin.Repository;
            detail.RepoUrl = MarketPlugin.Repository;
            detail.HomeVisible = !string.IsNullOrWhiteSpace(detail.HomeUrl);
            detail.RepoVisible = !string.IsNullOrWhiteSpace(detail.RepoUrl);

            return detail;
        }

        return new PluginDetailViewModel();
    }
}

public partial class PluginDetailViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "选择一个插件";
    [ObservableProperty] private string _meta = string.Empty;
    [ObservableProperty] private string _description = string.Empty;

    [ObservableProperty] private string _primaryActionText = string.Empty;
    [ObservableProperty] private bool _primaryActionEnabled;
    [ObservableProperty] private bool _primaryActionVisible;

    [ObservableProperty] private string _secondaryActionText = string.Empty;
    [ObservableProperty] private bool _secondaryActionVisible;

    [ObservableProperty] private string? _homeUrl;
    [ObservableProperty] private bool _homeVisible;

    [ObservableProperty] private string? _repoUrl;
    [ObservableProperty] private bool _repoVisible;

    [ObservableProperty] private string _output = string.Empty;
    [ObservableProperty] private bool _outputVisible;

    [ObservableProperty] private string _markdown = string.Empty;
    [ObservableProperty] private bool _markdownVisible;

    /// <summary>纯文本描述（description 未被识别为 Markdown 时直接显示）</summary>
    [ObservableProperty] private string _plainText = string.Empty;
    [ObservableProperty] private bool _plainTextVisible;

    [ObservableProperty] private string? _iconUrl;

    // 状态胶囊（用于详情头部的彩色状态标签）
    [ObservableProperty] private bool _isEnabledStatus;
    [ObservableProperty] private bool _isErrorStatus;
    [ObservableProperty] private bool _isDisabledStatus;

    /// <summary>插件 API 版本区间不覆盖当前启动器</summary>
    [ObservableProperty] private bool _isIncompatibleStatus;

    /// <summary>不兼容原因，用于按钮提示</summary>
    [ObservableProperty] private string? _incompatibleReason;

    // ---- 插件更新 ----

    /// <summary>版本变化描述，如 "1.2.0 → 1.3.0"</summary>
    [ObservableProperty] private string _newVersionText = string.Empty;

    [ObservableProperty] private bool _newVersionVisible;

    /// <summary>「更新」按钮文案</summary>
    [ObservableProperty] private string _updateText = "更新";

    [ObservableProperty] private bool _updateVisible;

    [ObservableProperty] private bool _updateEnabled = true;

    /// <summary>新版本已下载、等待重启（显示提示与「取消更新」）</summary>
    [ObservableProperty] private bool _stagedHintVisible;

    [ObservableProperty] private string _stagedHintText = string.Empty;

    [ObservableProperty] private bool _cancelUpdateVisible;

    public PluginDetailViewModel()
    {
        PrimaryActionVisible = false;
    }
}

public class PlatformFilterItem
{
    public PlatformFilter Filter { get; }
    public string DisplayName { get; }

    public PlatformFilterItem(PlatformFilter filter, string displayName)
    {
        Filter = filter;
        DisplayName = displayName;
    }
}
