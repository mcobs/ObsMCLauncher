using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ObsMCLauncher.Plugins;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;
using ObsMCLauncher.ViewModels;
using ObsMCLauncher.Windows;

namespace ObsMCLauncher.Pages
{
    public partial class MorePage : Page
    {
        private int _appTitleClickCount = 0;
        private DateTime _lastAppTitleClickTime = DateTime.MinValue;
        private const int APP_TITLE_CLICK_RESET_MS = 2000;

        private static PluginLoader? _pluginLoader;

        private static class PageState
        {
            public static string SelectedTab { get; set; } = "About";
        }

        private static readonly System.Collections.Generic.Dictionary<string, (RadioButton tab, ScrollViewer content)> _pluginTabs = new();
        private static readonly System.Collections.Generic.Dictionary<string, (string title, object content, string? icon)> _pendingPluginTabs = new();
        private static MorePage? _instance;

        public static void SetPluginLoader(PluginLoader pluginLoader)
        {
            _pluginLoader = pluginLoader;
        }

        public static void RegisterPluginTab(string pluginId, string title, object content, string? icon)
        {
            Debug.WriteLine($"[MorePage] 静态方法：注册插件标签页: {pluginId} - {title}");

            // MorePage 可能尚未创建：先缓存，等页面初始化后再补注册
            _pendingPluginTabs[pluginId] = (title, content, icon);

            _instance?.RegisterPluginTabInstance(pluginId, title, content);
        }

        private void RegisterPluginTabInstance(string pluginId, string title, object content)
        {
            try
            {
                if (_pluginTabs.ContainsKey(pluginId))
                    return;

                var radioButton = new RadioButton
                {
                    Content = title,
                    GroupName = "MoreTabs",
                    Padding = new Thickness(20, 8, 20, 8),
                    FontSize = 14,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                radioButton.SetResourceReference(StyleProperty, "MaterialDesignTabRadioButton");
                radioButton.Checked += (s, e) =>
                {
                    SwitchToPluginTab(pluginId);
                    PageState.SelectedTab = pluginId;
                };

                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Padding = new Thickness(20),
                    Visibility = Visibility.Collapsed
                };

                if (content is Page page)
                {
                    scrollViewer.Content = new Frame
                    {
                        Content = page,
                        NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden
                    };
                }
                else if (content is UIElement ui)
                {
                    scrollViewer.Content = ui;
                }
                else
                {
                    NotificationManager.Instance.ShowNotification(
                        $"插件错误 ({pluginId})",
                        $"不支持的UI类型: {content.GetType().Name}",
                        NotificationType.Error,
                        5
                    );
                    return;
                }

                var navBar = (StackPanel)((Border)((Grid)Content).Children[0]).Child;
                navBar.Children.Add(radioButton);

                var mainGrid = (Grid)Content;
                Grid.SetRow(scrollViewer, 1);
                mainGrid.Children.Add(scrollViewer);

                _pluginTabs[pluginId] = (radioButton, scrollViewer);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MorePage] 注册插件标签页失败: {ex.Message}");
                NotificationManager.Instance.ShowNotification(
                    $"插件错误 ({pluginId})",
                    $"注册失败: {ex.Message}",
                    NotificationType.Error,
                    5
                );
            }
        }

        public static void RemovePluginTab(string pluginId)
        {
            try
            {
                if (_instance == null)
                    return;

                if (!_pluginTabs.TryGetValue(pluginId, out var tabInfo))
                    return;

                _instance.Dispatcher.Invoke(() =>
                {
                    var mainGrid = (Grid)_instance.Content;
                    var navBar = (StackPanel)((Border)mainGrid.Children[0]).Child;

                    if (navBar.Children.Contains(tabInfo.tab))
                        navBar.Children.Remove(tabInfo.tab);

                    if (mainGrid.Children.Contains(tabInfo.content))
                        mainGrid.Children.Remove(tabInfo.content);

                    _pluginTabs.Remove(pluginId);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MorePage] 移除插件标签页失败: {ex.Message}");
            }
        }

        private void SwitchToPluginTab(string pluginId)
        {
            if (!_pluginTabs.TryGetValue(pluginId, out var tabInfo))
                return;

            AboutContent.Visibility = Visibility.Collapsed;
            PluginsContent.Visibility = Visibility.Collapsed;
            if (ScreenshotsContent != null) ScreenshotsContent.Visibility = Visibility.Collapsed;
            if (ServersContent != null) ServersContent.Visibility = Visibility.Collapsed;

            HideAllPluginTabs();
            tabInfo.content.Visibility = Visibility.Visible;
        }

        public MorePage()
        {
            InitializeComponent();
            _instance = this;

            // 补注册：在 MorePage 创建后，把之前缓存的插件标签页一次性挂上来
            try
            {
                foreach (var kv in _pendingPluginTabs)
                {
                    if (!_pluginTabs.ContainsKey(kv.Key))
                    {
                        RegisterPluginTabInstance(kv.Key, kv.Value.title, kv.Value.content);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MorePage] 补注册插件标签页失败: {ex.Message}");
            }

            Loaded += MorePage_Loaded;
            Unloaded += MorePage_Unloaded;
        }

        private async void MorePage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadVersionInfo();
            RestorePageState();

            if (DataContext == null)
            {
                var vm = new PluginsViewModel(_pluginLoader);
                DataContext = vm;
                await vm.InitializeAsync();
            }
        }

        private void MorePage_Unloaded(object sender, RoutedEventArgs e)
        {
            ImageCacheManager.CleanupCache();
        }

        private void RestorePageState()
        {
            switch (PageState.SelectedTab)
            {
                case "Plugins":
                    PluginsTab.IsChecked = true;
                    break;
                case "Screenshots":
                    ScreenshotsTab.IsChecked = true;
                    break;
                case "Servers":
                    ServersTab.IsChecked = true;
                    break;
                default:
                    AboutTab.IsChecked = true;
                    break;
            }

            SwitchTab(PageState.SelectedTab);
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton radioButton)
                return;

            if (radioButton == AboutTab)
            {
                SwitchTab("About");
                PageState.SelectedTab = "About";
            }
            else if (radioButton == PluginsTab)
            {
                SwitchTab("Plugins");
                PageState.SelectedTab = "Plugins";
            }
            else if (radioButton == ScreenshotsTab)
            {
                SwitchTab("Screenshots");
                PageState.SelectedTab = "Screenshots";
            }
            else if (radioButton == ServersTab)
            {
                SwitchTab("Servers");
                PageState.SelectedTab = "Servers";
            }
        }

        private void SwitchTab(string tabName)
        {
            if (AboutContent == null || PluginsContent == null)
                return;

            AboutContent.Visibility = Visibility.Collapsed;
            PluginsContent.Visibility = Visibility.Collapsed;
            if (ScreenshotsContent != null) ScreenshotsContent.Visibility = Visibility.Collapsed;
            if (ServersContent != null) ServersContent.Visibility = Visibility.Collapsed;

            HideAllPluginTabs();

            if (tabName == "About")
            {
                AboutContent.Visibility = Visibility.Visible;
            }
            else if (tabName == "Plugins")
            {
                PluginsContent.Visibility = Visibility.Visible;
            }
            else if (tabName == "Screenshots")
            {
                if (ScreenshotsContent != null)
                {
                    ScreenshotsContent.Visibility = Visibility.Visible;
                    if (ScreenshotsContent.Content == null)
                        ScreenshotsContent.Navigate(new ScreenshotsPage());
                }
            }
            else if (tabName == "Servers")
            {
                if (ServersContent != null)
                {
                    ServersContent.Visibility = Visibility.Visible;
                    if (ServersContent.Content == null)
                        ServersContent.Navigate(new ServersPage());
                }
            }
        }

        private static void HideAllPluginTabs()
        {
            foreach (var (_, content) in _pluginTabs.Values)
            {
                content.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadVersionInfo()
        {
            try
            {
                VersionText.Text = $"版本 {VersionInfo.DisplayVersion}";
            }
            catch
            {
                VersionText.Text = "版本 1.0.0";
            }
        }

        private void OpenGitHub_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/mcobs/ObsMCLauncher");
        private void OpenForum_Click(object sender, RoutedEventArgs e) => OpenUrl("https://mcobs.cn/");
        private void OpenBangBang93_Click(object sender, RoutedEventArgs e) => OpenUrl("https://afdian.com/a/bangbang93");
        private void OpenAuthlibInjector_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/yushijinhun/authlib-injector");

        private void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"无法打开链接：{url}\n\n错误：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var newRelease = await UpdateService.CheckForUpdatesAsync();
                if (newRelease != null)
                {
                    await UpdateService.ShowUpdateDialogAsync(newRelease);
                }
                else
                {
                    NotificationManager.Instance.ShowNotification(
                        "已是最新版本",
                        $"当前版本 {VersionInfo.DisplayVersion} 已是最新版本",
                        NotificationType.Success,
                        3
                    );
                }
            }
            catch
            {
                NotificationManager.Instance.ShowNotification(
                    "检查更新失败",
                    "无法连接到更新服务器，请检查网络连接",
                    NotificationType.Error,
                    5
                );
            }
        }

        private void AppTitleText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var now = DateTime.Now;
            var timeSinceLastClick = (now - _lastAppTitleClickTime).TotalMilliseconds;
            if (timeSinceLastClick > APP_TITLE_CLICK_RESET_MS)
                _appTitleClickCount = 0;

            _appTitleClickCount++;
            _lastAppTitleClickTime = now;

            if (_appTitleClickCount >= 5)
            {
                _appTitleClickCount = 0;
                _ = ShowDebugConsoleConfirmationAsync();
            }
        }

        private async System.Threading.Tasks.Task ShowDebugConsoleConfirmationAsync()
        {
            try
            {
                var result = await DialogManager.Instance.ShowConfirmDialogAsync(
                    "开发者模式",
                    "是否打开调试控制台？\n\n⚠️ 调试控制台仅供开发和测试使用",
                    "打开",
                    "取消"
                );

                if (result)
                {
                    try
                    {
                        var win = new DevConsoleWindow
                        {
                            Owner = Window.GetWindow(this)
                        };
                        win.Show();
                    }
                    catch (Exception ex)
                    {
                        NotificationManager.Instance.ShowNotification(
                            "开发者模式",
                            $"打开调试控制台失败: {ex.Message}",
                            NotificationType.Error,
                            4
                        );
                    }
                }
            }
            catch { }
        }
    }
}
