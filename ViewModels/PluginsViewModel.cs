using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Models;
using ObsMCLauncher.Plugins;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.ViewModels
{
    public enum PluginSubTab
    {
        Market,
        Installed
    }

    public partial class PluginsViewModel : ObservableObject
    {
        private readonly PluginLoader? _pluginLoader;

        private System.Collections.Generic.List<MarketPlugin> _allMarketPlugins = new();
        private System.Collections.Generic.List<MarketPlugin> _filteredMarketPlugins = new();
        private System.Collections.Generic.List<PluginCategory> _categories = new();

        [ObservableProperty]
        private PluginSubTab currentTab = PluginSubTab.Market;

        [ObservableProperty]
        private ObservableCollection<PluginListItemViewModel> leftItems = new();

        [ObservableProperty]
        private PluginListItemViewModel? selectedItem;

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private PluginCategory? selectedCategory;

        [ObservableProperty]
        private ObservableCollection<PluginCategory> categories = new();

        [ObservableProperty]
        private bool isMarketLoading;

        [ObservableProperty]
        private string? marketError;

        [ObservableProperty]
        private bool isEmptyHintVisible;

        [ObservableProperty]
        private PluginDetailViewModel detail = new();

        public PluginsViewModel(PluginLoader? pluginLoader)
        {
            _pluginLoader = pluginLoader;

            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CurrentTab))
                {
                    _ = RefreshLeftAsync();
                }
                else if (e.PropertyName == nameof(SearchText) || e.PropertyName == nameof(SelectedCategory))
                {
                    if (CurrentTab == PluginSubTab.Market)
                    {
                        ApplyMarketFilter();
                    }
                }
            };
        }

        partial void OnSelectedItemChanged(PluginListItemViewModel? value)
        {
            Detail = value?.ToDetail(_pluginLoader) ?? new PluginDetailViewModel();
            _ = LoadReadmeForDetailAsync(value);
        }

        private async Task LoadReadmeForDetailAsync(PluginListItemViewModel? item)
        {
            try
            {
                if (item == null)
                    return;

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

                    return;
                }

                if (item.Source == PluginItemSource.Market && item.Market != null)
                {
                    var readmeUrl = item.Market.Readme;
                    Detail.ReadmeUrl = readmeUrl;

                    if (string.IsNullOrWhiteSpace(readmeUrl))
                    {
                        Detail.Markdown = "";
                        Detail.MarkdownVisible = false;
                        return;
                    }

                    var url = ObsMCLauncher.Plugins.PluginMarketService.UseProxyIfNeeded(readmeUrl);

                    using var http = new System.Net.Http.HttpClient();
                    var content = await http.GetStringAsync(url);
                    Detail.Markdown = content;
                    Detail.MarkdownVisible = !string.IsNullOrWhiteSpace(Detail.Markdown);
                    return;
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
            await LoadMarketAsync();
            await RefreshLeftAsync();
        }

        [RelayCommand]
        private async Task RefreshMarketAsync()
        {
            await LoadMarketAsync();
            if (CurrentTab == PluginSubTab.Market)
            {
                await RefreshLeftAsync();
            }
        }

        [RelayCommand]
        private Task RefreshInstalledAsync()
        {
            return RefreshLeftAsync();
        }

        private async Task LoadMarketAsync()
        {
            try
            {
                IsMarketLoading = true;
                MarketError = null;

                var categoriesTask = PluginMarketService.GetCategoriesAsync();
                var marketIndexTask = PluginMarketService.GetMarketIndexAsync();

                await Task.WhenAll(categoriesTask, marketIndexTask);

                var categories = await categoriesTask;
                var marketIndex = await marketIndexTask;

                _categories = categories ?? new();
                Categories = new ObservableCollection<PluginCategory>(_categories.Prepend(new PluginCategory { Id = "all", Name = "全部" }));

                if (SelectedCategory == null)
                {
                    SelectedCategory = Categories.FirstOrDefault();
                }

                if (marketIndex?.Plugins == null || marketIndex.Plugins.Count == 0)
                {
                    MarketError = "无法连接到插件市场或市场暂无插件";
                    _allMarketPlugins = new();
                    _filteredMarketPlugins = new();
                    return;
                }

                _allMarketPlugins = marketIndex.Plugins;
                _filteredMarketPlugins = new System.Collections.Generic.List<MarketPlugin>(_allMarketPlugins);
            }
            catch (Exception ex)
            {
                MarketError = ex.Message;
            }
            finally
            {
                IsMarketLoading = false;
            }
        }

        private void ApplyMarketFilter()
        {
            var st = (SearchText ?? string.Empty).Trim().ToLowerInvariant();
            var catId = SelectedCategory?.Id ?? "all";

            _filteredMarketPlugins = _allMarketPlugins.Where(p =>
            {
                var matchesSearch = string.IsNullOrEmpty(st) ||
                                    p.Name.ToLowerInvariant().Contains(st) ||
                                    p.Description.ToLowerInvariant().Contains(st) ||
                                    p.Author.ToLowerInvariant().Contains(st);

                var matchesCategory = catId == "all" ||
                                     p.Category.ToLowerInvariant() == catId.ToLowerInvariant() ||
                                     p.Category == SelectedCategory?.Name;

                return matchesSearch && matchesCategory;
            }).ToList();

            _ = RefreshLeftAsync();
        }

        private Task RefreshLeftAsync()
        {
            LeftItems.Clear();
            SelectedItem = null;

            if (CurrentTab == PluginSubTab.Market)
            {
                if (!string.IsNullOrEmpty(MarketError))
                {
                    IsEmptyHintVisible = true;
                    return Task.CompletedTask;
                }

                foreach (var p in _filteredMarketPlugins)
                {
                    var isInstalled = _pluginLoader?.LoadedPlugins.Any(x => x.Id == p.Id) ?? false;
                    LeftItems.Add(PluginListItemViewModel.FromMarket(p, isInstalled));
                }
            }
            else
            {
                var installed = _pluginLoader?.LoadedPlugins ?? Array.Empty<LoadedPlugin>();
                foreach (var p in installed)
                {
                    LeftItems.Add(PluginListItemViewModel.FromInstalled(p));
                }
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
                if (item == null)
                    return;

                if (item.Source == PluginItemSource.Market)
                {
                    var market = item.Market;
                    if (market == null)
                        return;

                    if (Detail.PrimaryActionEnabled == false)
                        return;

                    await InstallSelectedMarketAsync(market);

                    await RefreshInstalledAsync();
                    // SelectedItem 可能因刷新被清空，避免空引用
                    Detail = item.ToDetail(_pluginLoader);
                }
                else
                {
                    var installed = item.Installed;
                    if (installed == null)
                        return;

                    await ToggleInstalledAsync(installed);
                    await RefreshInstalledAsync();
                }
            }
            catch (Exception ex)
            {
                NotificationManager.Instance.ShowNotification(
                    "插件",
                    $"操作失败: {ex.Message}",
                    NotificationType.Error,
                    4
                );
            }
        }

        [RelayCommand]
        private async Task InstallFromListAsync(PluginListItemViewModel? item)
        {
            try
            {
                if (item?.Source != PluginItemSource.Market)
                    return;

                var market = item.Market;
                if (market == null)
                    return;

                if (!item.MarketActionEnabled)
                    return;

                // 允许不选中也能直接安装该项
                await InstallSelectedMarketAsync(market);

                await RefreshInstalledAsync();
                await RefreshLeftAsync();

                if (ReferenceEquals(SelectedItem, item))
                {
                    Detail = item.ToDetail(_pluginLoader);
                }
            }
            catch (Exception ex)
            {
                NotificationManager.Instance.ShowNotification(
                    "插件",
                    $"安装失败: {ex.Message}",
                    NotificationType.Error,
                    4
                );
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
        private void OpenHomeAsync()
        {
            if (string.IsNullOrWhiteSpace(Detail.HomeUrl)) return;
            TryOpenUrl(Detail.HomeUrl);
        }

        [RelayCommand]
        private void OpenRepoAsync()
        {
            if (string.IsNullOrWhiteSpace(Detail.RepoUrl)) return;
            TryOpenUrl(Detail.RepoUrl);
        }

        private static void TryOpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch
            {
                // VM阶段先不弹框，避免依赖UI服务；后续mvvm-4再抽象。
            }
        }

        private async Task InstallSelectedMarketAsync(MarketPlugin plugin)
        {
            try
            {
                var config = LauncherConfig.Load();
                var pluginsDir = config.GetPluginDirectory();

                var progress = new Progress<double>(_ => { });
                var success = await PluginMarketService.DownloadAndInstallPluginAsync(plugin, pluginsDir, progress);

                if (success)
                {
                    try
                    {
                        _pluginLoader?.LoadAllPlugins();
                    }
                    catch { }

                    NotificationManager.Instance.ShowNotification(
                        "插件安装",
                        $"插件 {plugin.Name} 安装成功！已自动加载。",
                        NotificationType.Success,
                        5
                    );
                }
                else
                {
                    NotificationManager.Instance.ShowNotification(
                        "插件安装",
                        $"插件 {plugin.Name} 安装失败",
                        NotificationType.Error,
                        3
                    );
                }
            }
            catch (Exception ex)
            {
                NotificationManager.Instance.ShowNotification(
                    "插件安装",
                    $"安装失败: {ex.Message}",
                    NotificationType.Error,
                    3
                );
            }
        }

        private async Task ToggleInstalledAsync(LoadedPlugin plugin)
        {
            if (_pluginLoader == null) return;

            try
            {
                var disabledMarkerPath = System.IO.Path.Combine(plugin.DirectoryPath, ".disabled");
                var willDisable = plugin.IsLoaded || !System.IO.File.Exists(disabledMarkerPath);

                bool success = false;
                string? errorMessage = null;

                if (willDisable)
                {
                    await Task.Run(() => { success = _pluginLoader.DisablePluginImmediately(plugin.Id); });
                }
                else
                {
                    // 热加载插件可能需要UI线程，先保持旧逻辑：由MorePage code-behind迁移时再统一调度。
                    success = _pluginLoader.EnablePlugin(plugin.Id, out errorMessage);
                }

                if (success)
                {
                    NotificationManager.Instance.ShowNotification(
                        "插件管理",
                        willDisable ? $"✅ 插件 {plugin.Name} 已热禁用。" : $"✅ 插件 {plugin.Name} 已热启用。",
                        NotificationType.Success,
                        3
                    );
                }
                else
                {
                    NotificationManager.Instance.ShowNotification(
                        "插件管理",
                        $"操作失败: {errorMessage ?? "未知错误"}",
                        NotificationType.Error,
                        4
                    );
                }
            }
            catch (Exception ex)
            {
                NotificationManager.Instance.ShowNotification(
                    "插件管理",
                    $"操作失败: {ex.Message}",
                    NotificationType.Error,
                    3
                );
            }
        }

        private async Task UninstallInstalledAsync(LoadedPlugin plugin)
        {
            if (_pluginLoader == null) return;

            try
            {
                // VM阶段先保留MessageBox（后续mvvm-4抽出对话框服务）
                var result = System.Windows.MessageBox.Show(
                    $"确定要卸载插件 \"{plugin.Name}\" 吗？\n\n此操作将热卸载插件实例并删除所有文件，且无法恢复。",
                    "确认卸载",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning
                );

                if (result != System.Windows.MessageBoxResult.Yes)
                    return;

                string? errorMessage = null;
                bool success = false;

                await Task.Run(() => { success = _pluginLoader.RemovePlugin(plugin.Id, out errorMessage); });

                if (success)
                {
                    NotificationManager.Instance.ShowNotification(
                        "插件卸载",
                        string.IsNullOrEmpty(errorMessage)
                            ? $"✅ 插件 {plugin.Name} 已成功热卸载。"
                            : $"插件 {plugin.Name} 已热卸载。{errorMessage}",
                        string.IsNullOrEmpty(errorMessage) ? NotificationType.Success : NotificationType.Warning,
                        5
                    );
                }
                else
                {
                    NotificationManager.Instance.ShowNotification(
                        "插件卸载",
                        $"卸载失败: {errorMessage ?? "未知错误"}",
                        NotificationType.Error,
                        5
                    );
                }
            }
            catch (Exception ex)
            {
                NotificationManager.Instance.ShowNotification(
                    "插件卸载",
                    $"卸载失败: {ex.Message}",
                    NotificationType.Error,
                    5
                );
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
        public MarketPlugin? Market { get; }
        public LoadedPlugin? Installed { get; }

        public string Title { get; }
        public string Meta { get; }
        public bool HasError { get; }
        public bool IsMarket => Source == PluginItemSource.Market;
        public bool IsInstalled => Source == PluginItemSource.Installed;

        public string MarketActionText { get; }
        public bool MarketActionEnabled { get; }

        private PluginListItemViewModel(PluginItemSource source, string title, string meta, bool hasError, MarketPlugin? market, LoadedPlugin? installed, string marketActionText, bool marketActionEnabled)
        {
            Source = source;
            Title = title;
            Meta = meta;
            HasError = hasError;
            Market = market;
            Installed = installed;
            MarketActionText = marketActionText;
            MarketActionEnabled = marketActionEnabled;
        }

        public static PluginListItemViewModel FromMarket(MarketPlugin p, bool isInstalled)
            => new(PluginItemSource.Market, p.Name, p.Description ?? string.Empty, false, p, null, isInstalled ? "已安装" : "下载", !isInstalled);

        public static PluginListItemViewModel FromInstalled(LoadedPlugin p)
            => new(PluginItemSource.Installed, p.Name, p.Description ?? string.Empty, !string.IsNullOrEmpty(p.ErrorOutput), null, p, string.Empty, false);

        public PluginDetailViewModel ToDetail(PluginLoader? pluginLoader)
        {
            if (Source == PluginItemSource.Market && Market != null)
            {
                var isInstalled = pluginLoader?.LoadedPlugins.Any(x => x.Id == Market.Id) ?? false;

                var detail = new PluginDetailViewModel
                {
                    Title = Market.Name,
                    Meta = $"v{Market.Version} · {Market.Author}",
                    Description = Market.Description ?? string.Empty,
                    PrimaryActionText = isInstalled ? "已安装" : "安装",
                    PrimaryActionEnabled = !isInstalled,
                    PrimaryActionVisible = true,
                    SecondaryActionVisible = false
                };

                // 反射尝试主页/仓库字段（字段不确定）
                detail.HomeUrl = GetStringProp(Market, "Homepage") ?? GetStringProp(Market, "Home") ?? GetStringProp(Market, "Website");
                detail.RepoUrl = GetStringProp(Market, "Repository") ?? GetStringProp(Market, "Repo") ?? GetStringProp(Market, "GitHub");

                detail.HomeVisible = !string.IsNullOrWhiteSpace(detail.HomeUrl);
                detail.RepoVisible = !string.IsNullOrWhiteSpace(detail.RepoUrl);

                return detail;
            }

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
                    Meta = $"v{Installed.Version} · {Installed.Author} · {status}",
                    Description = Installed.Description ?? string.Empty,
                    Output = outputText,
                    OutputVisible = true,
                    PrimaryActionText = Installed.IsLoaded ? "禁用" : "启用",
                    PrimaryActionEnabled = true,
                    PrimaryActionVisible = true,
                    SecondaryActionVisible = true,
                    SecondaryActionText = "卸载"
                };

                detail.HomeUrl = GetStringProp(Installed.Metadata, "homepage") ?? GetStringProp(Installed.Metadata, "home") ?? GetStringProp(Installed.Metadata, "website");
                detail.RepoUrl = GetStringProp(Installed.Metadata, "repository") ?? GetStringProp(Installed.Metadata, "repo") ?? GetStringProp(Installed.Metadata, "github");

                detail.HomeVisible = !string.IsNullOrWhiteSpace(detail.HomeUrl);
                detail.RepoVisible = !string.IsNullOrWhiteSpace(detail.RepoUrl);

                return detail;
            }

            return new PluginDetailViewModel();
        }

        private static string? GetStringProp(object? obj, string propName)
        {
            try
            {
                if (obj == null) return null;
                var prop = obj.GetType().GetProperties().FirstOrDefault(p => string.Equals(p.Name, propName, StringComparison.OrdinalIgnoreCase));
                if (prop == null) return null;
                return prop.GetValue(obj) as string;
            }
            catch
            {
                return null;
            }
        }
    }

    public partial class PluginDetailViewModel : ObservableObject
    {
        [ObservableProperty] private string title = "选择一个插件";
        [ObservableProperty] private string meta = string.Empty;
        [ObservableProperty] private string description = string.Empty;

        [ObservableProperty] private string primaryActionText = string.Empty;
        [ObservableProperty] private bool primaryActionEnabled;
        [ObservableProperty] private bool primaryActionVisible;

        [ObservableProperty] private string secondaryActionText = string.Empty;
        [ObservableProperty] private bool secondaryActionVisible;

        [ObservableProperty] private string? homeUrl;
        [ObservableProperty] private bool homeVisible;

        [ObservableProperty] private string? repoUrl;
        [ObservableProperty] private bool repoVisible;

        [ObservableProperty] private string output = string.Empty;
        [ObservableProperty] private bool outputVisible;

        [ObservableProperty] private string markdown = string.Empty;
        [ObservableProperty] private bool markdownVisible;

        [ObservableProperty] private string? readmeUrl;

        public PluginDetailViewModel()
        {
            PrimaryActionVisible = false;
        }
    }
}

