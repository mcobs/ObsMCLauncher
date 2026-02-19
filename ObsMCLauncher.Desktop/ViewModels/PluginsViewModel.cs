using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Plugins;
using ObsMCLauncher.Core.Utils;
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

    [ObservableProperty]
    private PluginSubTab _currentTab = PluginSubTab.Market;

    public bool IsMarket => CurrentTab == PluginSubTab.Market;
    public bool IsInstalled => CurrentTab == PluginSubTab.Installed;

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
    private ObservableCollection<PluginCategory> _categories = new();

    [ObservableProperty]
    private PluginCategory? _selectedCategory;

    [ObservableProperty]
    private ObservableCollection<PlatformFilterItem> _platformFilters = new();

    [ObservableProperty]
    private PlatformFilterItem? _selectedPlatformFilter;

    private ObservableCollection<MarketPlugin>? _allMarketPlugins;

    public PluginsViewModel(PluginLoader pluginLoader, NotificationService notificationService)
    {
        _pluginLoader = pluginLoader;
        _notificationService = notificationService;

        PlatformFilters = new ObservableCollection<PlatformFilterItem>
        {
            new PlatformFilterItem(PlatformFilter.All, "全部平台"),
            new PlatformFilterItem(PlatformFilter.Windows, "Windows"),
            new PlatformFilterItem(PlatformFilter.Linux, "Linux"),
            new PlatformFilterItem(PlatformFilter.macOS, "macOS"),
            new PlatformFilterItem(PlatformFilter.Android, "Android")
        };

        SelectedPlatformFilter = PlatformFilters.FirstOrDefault(p => p.Filter == GetCurrentPlatformFilter());
    }

    private PlatformFilter GetCurrentPlatformFilter()
    {
        if (OperatingSystem.IsWindows()) return PlatformFilter.Windows;
        if (OperatingSystem.IsLinux()) return PlatformFilter.Linux;
        if (OperatingSystem.IsMacOS()) return PlatformFilter.macOS;
        if (OperatingSystem.IsAndroid()) return PlatformFilter.Android;
        return PlatformFilter.All;
    }

    partial void OnSelectedItemChanged(PluginListItemViewModel? value)
    {
        Detail = value?.ToDetail(_pluginLoader) ?? new PluginDetailViewModel();
        _ = LoadReadmeForDetailAsync(value);
    }

    partial void OnCurrentTabChanged(PluginSubTab value)
    {
        OnPropertyChanged(nameof(IsMarket));
        OnPropertyChanged(nameof(IsInstalled));
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
        try
        {
            if (item == null) return;

            if (item.Source == PluginItemSource.Installed && item.Installed != null)
            {
                var readmePath = item.Installed.ReadmePath;
                if (!string.IsNullOrWhiteSpace(readmePath) && System.IO.File.Exists(readmePath))
                {
                    Detail.Markdown = await System.IO.File.ReadAllTextAsync(readmePath);
                    Detail.MarkdownVisible = !string.IsNullOrWhiteSpace(Detail.Markdown);
                }
                else
                {
                    Detail.Markdown = "";
                    Detail.MarkdownVisible = false;
                }
            }
            else if (item.Source == PluginItemSource.Market && item.MarketPlugin != null)
            {
                if (!string.IsNullOrWhiteSpace(item.MarketPlugin.Readme))
                {
                    try
                    {
                        var url = ObsMCLauncher.Core.Utils.GitHubProxyHelper.WithProxy(item.MarketPlugin.Readme);
                        using var client = new System.Net.Http.HttpClient();
                        client.Timeout = TimeSpan.FromSeconds(10);
                        Detail.Markdown = await client.GetStringAsync(url);
                        Detail.MarkdownVisible = !string.IsNullOrWhiteSpace(Detail.Markdown);
                    }
                    catch
                    {
                        Detail.Markdown = item.MarketPlugin.Description;
                        Detail.MarkdownVisible = !string.IsNullOrWhiteSpace(Detail.Markdown);
                    }
                }
                else
                {
                    Detail.Markdown = item.MarketPlugin.Description;
                    Detail.MarkdownVisible = !string.IsNullOrWhiteSpace(Detail.Markdown);
                }
            }
        }
        catch
        {
            Detail.Markdown = "";
            Detail.MarkdownVisible = false;
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
    private async Task RefreshMarketAsync()
    {
        await RefreshLeftAsync();
    }

    [RelayCommand]
    private Task RefreshInstalledAsync()
    {
        return RefreshLeftAsync();
    }

    private async Task RefreshLeftAsync()
    {
        LeftItems.Clear();
        SelectedItem = null;

        if (CurrentTab == PluginSubTab.Installed)
        {
            var installed = _pluginLoader.LoadedPlugins;
            foreach (var p in installed)
            {
                LeftItems.Add(PluginListItemViewModel.FromInstalled(p));
            }
            IsEmptyHintVisible = LeftItems.Count == 0;
        }
        else
        {
            IsMarketLoading = true;
            MarketError = null;

            try
            {
                var index = await PluginMarketService.GetMarketIndexAsync();
                if (index?.Plugins != null)
                {
                    _allMarketPlugins = new ObservableCollection<MarketPlugin>(index.Plugins);
                    await FilterMarketPluginsAsync();
                }
                else
                {
                    MarketError = "无法加载插件市场数据";
                }
            }
            catch (Exception ex)
            {
                MarketError = $"加载失败: {ex.Message}";
            }
            finally
            {
                IsMarketLoading = false;
            }
        }
    }

    private Task FilterMarketPluginsAsync()
    {
        if (_allMarketPlugins == null) return Task.CompletedTask;

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

        LeftItems.Clear();
        foreach (var p in filtered)
        {
            LeftItems.Add(PluginListItemViewModel.FromMarket(p));
        }

        IsEmptyHintVisible = LeftItems.Count == 0;
        return Task.CompletedTask;
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
            string? errorMessage = null;
            bool success = await Task.Run(() => _pluginLoader.RemovePlugin(plugin.Id, out errorMessage));

            if (success)
            {
                _notificationService.Show("插件卸载",
                    string.IsNullOrEmpty(errorMessage) ? $"插件 {plugin.Name} 已卸载" : errorMessage,
                    string.IsNullOrEmpty(errorMessage) ? NotificationType.Success : NotificationType.Warning);
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

    private async Task InstallMarketPluginAsync(MarketPlugin plugin)
    {
        try
        {
            var pluginsDir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "OMCL", "plugins");
            
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
                _pluginLoader.LoadAllPlugins();
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

    public string Title { get; }
    public string Meta { get; }
    public bool HasError { get; }
    public bool IsMarket => Source == PluginItemSource.Market;
    public bool IsInstalled => Source == PluginItemSource.Installed;
    public string? IconUrl { get; }

    private PluginListItemViewModel(PluginItemSource source, string title, string meta, bool hasError, LoadedPlugin? installed, MarketPlugin? marketPlugin, string? iconUrl = null)
    {
        Source = source;
        Title = title;
        Meta = meta;
        HasError = hasError;
        Installed = installed;
        MarketPlugin = marketPlugin;
        IconUrl = iconUrl;
    }

    public static PluginListItemViewModel FromInstalled(LoadedPlugin p)
        => new(PluginItemSource.Installed, p.Name, $"{p.Version} | {p.Author}", !string.IsNullOrEmpty(p.ErrorOutput), p, null, p.IconPath);

    public static PluginListItemViewModel FromMarket(MarketPlugin p)
    {
        var platforms = string.Join(", ", p.Platforms);
        var meta = string.IsNullOrEmpty(platforms)
            ? $"{p.Version} | {p.Author}"
            : $"{p.Version} | {p.Author} | {platforms}";
        return new PluginListItemViewModel(PluginItemSource.Market, p.Name, meta, false, null, p, p.Icon);
    }

    public PluginDetailViewModel ToDetail(PluginLoader? pluginLoader)
    {
        if (Source == PluginItemSource.Installed && Installed != null)
        {
            var status = Installed.IsLoaded ? "已启用" : "未启用";
            if (!string.IsNullOrEmpty(Installed.ErrorMessage) && !Installed.IsLoaded)
            {
                status = $"异常: {Installed.ErrorMessage}";
            }

            var outputText = string.IsNullOrWhiteSpace(Installed.ErrorOutput) ? "运行正常" : Installed.ErrorOutput;

            var detail = new PluginDetailViewModel
            {
                Title = Installed.Name,
                Meta = $"v{Installed.Version} | {Installed.Author} | {status}",
                Description = Installed.Description ?? string.Empty,
                Output = outputText,
                OutputVisible = true,
                PrimaryActionText = Installed.IsLoaded ? "禁用" : "启用",
                PrimaryActionEnabled = true,
                PrimaryActionVisible = true,
                SecondaryActionVisible = true,
                SecondaryActionText = "卸载"
            };

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

            var detail = new PluginDetailViewModel
            {
                Title = MarketPlugin.Name,
                Meta = meta,
                Description = MarketPlugin.Description,
                OutputVisible = false,
                PrimaryActionText = "安装",
                PrimaryActionEnabled = true,
                PrimaryActionVisible = true,
                SecondaryActionVisible = false,
                SecondaryActionText = ""
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
