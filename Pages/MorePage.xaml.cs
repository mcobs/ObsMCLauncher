using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using ObsMCLauncher.Plugins;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Pages
{
    /// <summary>
    /// MorePage.xaml 的交互逻辑
    /// </summary>
    public partial class MorePage : Page
    {
        // 调试控制台激活逻辑
        private int _appTitleClickCount = 0;
        private DateTime _lastAppTitleClickTime = DateTime.MinValue;
        private const int APP_TITLE_CLICK_RESET_MS = 2000; // 2秒内有效
        
        // 插件加载器
        private static PluginLoader? _pluginLoader;
        
        // 插件市场数据
        private System.Collections.Generic.List<MarketPlugin> _allMarketPlugins = new();
        private System.Collections.Generic.List<MarketPlugin> _filteredMarketPlugins = new();
        private System.Collections.Generic.List<PluginCategory> _categories = new();
        
        // 页面状态保存
        private static class PageState
        {
            public static string SelectedTab { get; set; } = "About"; // "About" or "Plugins" or plugin tab id
        }
        
        // 插件标签页存储
        private static readonly System.Collections.Generic.Dictionary<string, (RadioButton tab, ScrollViewer content)> _pluginTabs = new();
        
        // MorePage实例引用（用于插件注册标签页）
        private static MorePage? _instance;
        
        /// <summary>
        /// 设置插件加载器（由MainWindow调用）
        /// </summary>
        public static void SetPluginLoader(PluginLoader pluginLoader)
        {
            _pluginLoader = pluginLoader;
        }
        
        /// <summary>
        /// 注册插件标签页（由插件调用）
        /// </summary>
        public static void RegisterPluginTab(string pluginId, string title, object content, string? icon)
        {
            Debug.WriteLine($"[MorePage] 静态方法：注册插件标签页: {pluginId} - {title}");
            
            // 调用实例方法
            _instance?.RegisterPluginTabInstance(pluginId, title, content, icon);
        }
        
        /// <summary>
        /// 实例方法：注册插件标签页
        /// </summary>
        private void RegisterPluginTabInstance(string pluginId, string title, object content, string? icon)
        {
            try
            {
                Debug.WriteLine($"[MorePage] 注册插件标签页: {pluginId} - {title}");
                
                // 检查是否已注册
                if (_pluginTabs.ContainsKey(pluginId))
                {
                    Debug.WriteLine($"[MorePage] 插件标签页已存在: {pluginId}");
                    return;
                }
                
                // 创建RadioButton
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
                
                // 创建内容容器
                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Padding = new Thickness(20),
                    Visibility = Visibility.Collapsed
                };
                
                // 设置内容（添加异常处理）
                try
                {
                    if (content is Page page)
                    {
                        var frame = new Frame
                        {
                            Content = page,
                            NavigationUIVisibility = NavigationUIVisibility.Hidden
                        };
                        scrollViewer.Content = frame;
                    }
                    else if (content is UIElement uiElement)
                    {
                        scrollViewer.Content = uiElement;
                    }
                    else
                    {
                        Debug.WriteLine($"[MorePage] ❌ 不支持的内容类型: {content.GetType()}");
                        ShowPluginErrorNotification(pluginId, $"不支持的UI类型: {content.GetType().Name}");
                        return;
                    }
                }
                catch (Exception contentEx)
                {
                    Debug.WriteLine($"[MorePage] ❌ 创建插件内容失败: {contentEx.Message}");
                    Debug.WriteLine($"[MorePage] 堆栈跟踪: {contentEx.StackTrace}");
                    
                    // 创建错误显示页面
                    var errorPage = CreatePluginErrorPage(pluginId, title, contentEx);
                    scrollViewer.Content = errorPage;
                    
                    ShowPluginErrorNotification(pluginId, $"UI创建失败: {contentEx.Message}");
                }
                
                // 添加到UI
                try
                {
                    // 获取导航栏的StackPanel
                    var navBar = (StackPanel)((Border)((Grid)this.Content).Children[0]).Child;
                    navBar.Children.Add(radioButton);
                    
                    // 添加内容到Grid
                    var mainGrid = (Grid)this.Content;
                    Grid.SetRow(scrollViewer, 1);
                    mainGrid.Children.Add(scrollViewer);
                    
                    // 保存引用
                    _pluginTabs[pluginId] = (radioButton, scrollViewer);
                    
                    Debug.WriteLine($"[MorePage] ✅ 插件标签页注册成功: {title}");
                }
                catch (Exception uiEx)
                {
                    Debug.WriteLine($"[MorePage] ❌ 添加插件UI到界面失败: {uiEx.Message}");
                    ShowPluginErrorNotification(pluginId, $"UI添加失败: {uiEx.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MorePage] ❌ 注册插件标签页失败: {ex.Message}");
                Debug.WriteLine($"[MorePage] 完整异常: {ex}");
                ShowPluginErrorNotification(pluginId, $"注册失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 创建插件错误显示页面
        /// </summary>
        private UIElement CreatePluginErrorPage(string pluginId, string title, Exception ex)
        {
            var errorPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 600
            };
            
            // 错误图标
            var errorIcon = new MaterialDesignThemes.Wpf.PackIcon
            {
                Kind = MaterialDesignThemes.Wpf.PackIconKind.AlertCircle,
                Width = 64,
                Height = 64,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 71, 87)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            };
            errorPanel.Children.Add(errorIcon);
            
            // 标题
            var titleText = new TextBlock
            {
                Text = $"插件 {title} 加载失败",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            errorPanel.Children.Add(titleText);
            
            // 插件ID
            var idText = new TextBlock
            {
                Text = $"插件ID: {pluginId}",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            };
            idText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            errorPanel.Children.Add(idText);
            
            // 错误信息
            var errorBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(25, 255, 71, 87)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 16)
            };
            
            var errorText = new TextBlock
            {
                Text = $"错误信息：\n{ex.Message}",
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 71, 87))
            };
            
            errorBorder.Child = errorText;
            errorPanel.Children.Add(errorBorder);
            
            // 建议
            var suggestionText = new TextBlock
            {
                Text = "建议：\n• 检查插件是否与当前启动器版本兼容\n• 查看插件文档或联系插件作者\n• 尝试重新安装插件\n• 查看调试控制台获取详细错误信息",
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            suggestionText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            errorPanel.Children.Add(suggestionText);
            
            return errorPanel;
        }
        
        /// <summary>
        /// 显示插件错误通知
        /// </summary>
        private void ShowPluginErrorNotification(string pluginId, string message)
        {
            try
            {
                NotificationManager.Instance.ShowNotification(
                    $"插件错误 ({pluginId})",
                    message,
                    NotificationType.Error,
                    5
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MorePage] 显示通知失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 切换到插件标签页
        /// </summary>
        private void SwitchToPluginTab(string pluginId)
        {
            if (!_pluginTabs.TryGetValue(pluginId, out var tabInfo))
                return;
                
            // 隐藏所有内置页面
            if (AboutContent != null)
                AboutContent.Visibility = Visibility.Collapsed;
            if (PluginsContent != null)
                PluginsContent.Visibility = Visibility.Collapsed;
            
            // 隐藏所有插件页面（使用统一方法）
            HideAllPluginTabs();
            
            // 显示选中的插件页面
            tabInfo.content.Visibility = Visibility.Visible;
            
            Debug.WriteLine($"[MorePage] 切换到插件标签页: {pluginId}");
        }
        
        public MorePage()
        {
            InitializeComponent();
            
            // 保存实例引用
            _instance = this;
            
            // 加载版本信息
            LoadVersionInfo();
            
            // 恢复页面状态
            RestorePageState();
            
            // 加载插件市场（异步）
            _ = LoadMarketPluginsAsync();
        }
        
        /// <summary>
        /// 恢复页面状态
        /// </summary>
        private void RestorePageState()
        {
            if (PageState.SelectedTab == "Plugins")
            {
                PluginsTab.IsChecked = true;
            }
            else
            {
                AboutTab.IsChecked = true;
            }
            
            // 强制触发Tab_Checked以更新UI
            SwitchTab(PageState.SelectedTab);
        }
        
        /// <summary>
        /// 标签切换事件
        /// </summary>
        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radioButton)
            {
                if (radioButton == AboutTab)
                {
                    SwitchTab("About");
                    PageState.SelectedTab = "About";
                }
                else if (radioButton == PluginsTab)
                {
                    SwitchTab("Plugins");
                    PageState.SelectedTab = "Plugins";
                    
                    // 切换到插件页面时，刷新插件列表
                    RefreshPluginList();
                }
            }
        }
        
        /// <summary>
        /// 切换标签页内容
        /// </summary>
        private void SwitchTab(string tabName)
        {
            if (AboutContent == null || PluginsContent == null)
                return;
                
            if (tabName == "About")
            {
                AboutContent.Visibility = Visibility.Visible;
                PluginsContent.Visibility = Visibility.Collapsed;
                
                // 隐藏所有插件标签页
                HideAllPluginTabs();
                
                Debug.WriteLine("[MorePage] 切换到关于页面");
            }
            else if (tabName == "Plugins")
            {
                AboutContent.Visibility = Visibility.Collapsed;
                PluginsContent.Visibility = Visibility.Visible;
                
                // 隐藏所有插件标签页
                HideAllPluginTabs();
                
                Debug.WriteLine("[MorePage] 切换到插件页面");
            }
        }
        
        /// <summary>
        /// 隐藏所有插件标签页
        /// </summary>
        private void HideAllPluginTabs()
        {
            foreach (var (_, content) in _pluginTabs.Values)
            {
                content.Visibility = Visibility.Collapsed;
            }
        }
        
        /// <summary>
        /// 刷新插件列表
        /// </summary>
        private void RefreshPluginList()
        {
            try
            {
                Debug.WriteLine("[MorePage] 刷新插件列表");
                
                if (_pluginLoader == null)
                {
                    InstalledPluginCountText.Text = "0";
                    EmptyPluginsHint.Visibility = Visibility.Visible;
                    return;
                }
                
                var plugins = _pluginLoader.LoadedPlugins;
                InstalledPluginCountText.Text = plugins.Count(p => p.IsLoaded).ToString();
                
                if (plugins.Count == 0)
                {
                    EmptyPluginsHint.Visibility = Visibility.Visible;
                }
                else
                {
                    EmptyPluginsHint.Visibility = Visibility.Collapsed;
                    DisplayPlugins(plugins);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MorePage] 刷新插件列表失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 显示插件列表
        /// </summary>
        private void DisplayPlugins(System.Collections.Generic.IReadOnlyList<LoadedPlugin> plugins)
        {
            if (PluginListPanel == null)
                return;
                
            // 清空现有内容（保留EmptyPluginsHint）
            for (int i = PluginListPanel.Children.Count - 1; i >= 0; i--)
            {
                if (PluginListPanel.Children[i] != EmptyPluginsHint)
                {
                    PluginListPanel.Children.RemoveAt(i);
                }
            }
            
            Debug.WriteLine($"[MorePage] 显示 {plugins.Count} 个插件");
            
            foreach (var plugin in plugins)
            {
                var pluginCard = CreatePluginCard(plugin);
                // 插入到EmptyPluginsHint之前
                PluginListPanel.Children.Insert(0, pluginCard);
            }
        }
        
        /// <summary>
        /// 创建插件卡片
        /// </summary>
        private Border CreatePluginCard(LoadedPlugin plugin)
        {
            var card = new Border
            {
                Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };
            
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            
            // 插件图标
            Border iconBorder;
            if (!string.IsNullOrEmpty(plugin.IconPath) && System.IO.File.Exists(plugin.IconPath))
            {
                try
                {
                    var iconImage = new Image
                    {
                        Width = 48,
                        Height = 48,
                        Source = new BitmapImage(new Uri(plugin.IconPath))
                    };
                    iconBorder = new Border
                    {
                        Width = 48,
                        Height = 48,
                        CornerRadius = new CornerRadius(6),
                        ClipToBounds = true,
                        Child = iconImage
                    };
                }
                catch
                {
                    iconBorder = CreateDefaultPluginIcon();
                }
            }
            else
            {
                iconBorder = CreateDefaultPluginIcon();
            }
            
            Grid.SetColumn(iconBorder, 0);
            grid.Children.Add(iconBorder);
            
            // 插件信息
            var infoPanel = new StackPanel
            {
                Margin = new Thickness(12, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(infoPanel, 1);
            
            // 插件名称
            var nameText = new TextBlock
            {
                Text = plugin.Name,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            };
            nameText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            infoPanel.Children.Add(nameText);
            
            // 插件版本和作者
            var versionText = new TextBlock
            {
                Text = $"v{plugin.Version} by {plugin.Author}",
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0)
            };
            versionText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            infoPanel.Children.Add(versionText);
            
            // 插件描述
            if (!string.IsNullOrEmpty(plugin.Description))
            {
                var descText = new TextBlock
                {
                    Text = plugin.Description,
                    FontSize = 12,
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 400
                };
                descText.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
                infoPanel.Children.Add(descText);
            }
            
            // 错误信息
            if (!plugin.IsLoaded && !string.IsNullOrEmpty(plugin.ErrorMessage))
            {
                var errorText = new TextBlock
                {
                    Text = $"❌ {plugin.ErrorMessage}",
                    FontSize = 12,
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 400,
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 71, 87))
                };
                infoPanel.Children.Add(errorText);
            }
            
            grid.Children.Add(infoPanel);
            
            // 状态标签
            var statusBadge = new Border
            {
                Background = plugin.IsLoaded 
                    ? new SolidColorBrush(Color.FromArgb(25, 102, 187, 106)) 
                    : new SolidColorBrush(Color.FromArgb(25, 255, 71, 87)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            
            var statusText = new TextBlock
            {
                Text = plugin.IsLoaded ? "已加载" : "加载失败",
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = plugin.IsLoaded 
                    ? new SolidColorBrush(Color.FromRgb(102, 187, 106)) 
                    : new SolidColorBrush(Color.FromRgb(255, 71, 87))
            };
            
            statusBadge.Child = statusText;
            Grid.SetColumn(statusBadge, 2);
            grid.Children.Add(statusBadge);
            
            card.Child = grid;
            return card;
        }
        
        /// <summary>
        /// 创建默认插件图标
        /// </summary>
        private Border CreateDefaultPluginIcon()
        {
            var iconBorder = new Border
            {
                Width = 48,
                Height = 48,
                Background = (Brush)Application.Current.Resources["PrimaryBrush"],
                CornerRadius = new CornerRadius(6)
            };
            
            var icon = new MaterialDesignThemes.Wpf.PackIcon
            {
                Kind = MaterialDesignThemes.Wpf.PackIconKind.Puzzle,
                Width = 28,
                Height = 28,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            iconBorder.Child = icon;
            return iconBorder;
        }
        
        /// <summary>
        /// 刷新插件按钮点击
        /// </summary>
        private void RefreshPlugins_Click(object sender, RoutedEventArgs e)
        {
            RefreshPluginList();
            NotificationManager.Instance.ShowNotification(
                "插件列表",
                "插件列表已刷新",
                NotificationType.Success,
                2
            );
        }
        
        /// <summary>
        /// 打开插件开发文档
        /// </summary>
        private void OpenPluginDocs_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/mcobs/ObsMCLauncher/blob/main/Plugin-Development.md");
        }

        /// <summary>
        /// 加载版本信息
        /// </summary>
        private void LoadVersionInfo()
        {
            try
            {
                // 使用全局版本信息
                VersionText.Text = $"版本 {VersionInfo.DisplayVersion}";
                
                // 在调试模式下输出详细版本信息
                Debug.WriteLine("========== 版本信息 ==========");
                Debug.WriteLine(VersionInfo.GetDetailedVersionInfo());
                Debug.WriteLine("=============================");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取版本信息失败: {ex.Message}");
                VersionText.Text = "版本 1.0.0";
            }
        }

        /// <summary>
        /// 打开 GitHub 仓库
        /// </summary>
        private void OpenGitHub_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/mcobs/ObsMCLauncher");
        }

        /// <summary>
        /// 打开黑曜石论坛
        /// </summary>
        private void OpenForum_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://mcobs.cn/");
        }

        /// <summary>
        /// 打开 bangbang93 的爱发电页面
        /// </summary>
        private void OpenBangBang93_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://afdian.com/a/bangbang93");
        }

        /// <summary>
        /// 在默认浏览器中打开 URL
        /// </summary>
        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                
                Debug.WriteLine($"已打开链接: {url}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"打开链接失败: {ex.Message}");
                MessageBox.Show(
                    $"无法打开链接：{url}\n\n错误：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        /// <summary>
        /// 检查更新按钮点击
        /// </summary>
        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null)
            {
                button.IsEnabled = false;
                
                // 显示检查中状态
                var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };
                var progressRing = new MaterialDesignThemes.Wpf.PackIcon
                {
                    Kind = MaterialDesignThemes.Wpf.PackIconKind.Loading,
                    Width = 16,
                    Height = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                // 使用DynamicResource绑定前景色
                progressRing.SetResourceReference(MaterialDesignThemes.Wpf.PackIcon.ForegroundProperty, "TextBrush");
                
                var textBlock = new TextBlock
                {
                    Text = "检查中...",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13
                };
                // 使用DynamicResource绑定前景色
                textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                
                stackPanel.Children.Add(progressRing);
                stackPanel.Children.Add(textBlock);
                button.Content = stackPanel;
            }

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
            catch (Exception ex)
            {
                Debug.WriteLine($"[MorePage] 检查更新失败: {ex.Message}");
                NotificationManager.Instance.ShowNotification(
                    "检查更新失败",
                    "无法连接到更新服务器，请检查网络连接",
                    NotificationType.Error,
                    5
                );
            }
            finally
            {
                // 恢复按钮状态
                if (button != null)
                {
                    var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };
                    var icon = new MaterialDesignThemes.Wpf.PackIcon
                    {
                        Kind = MaterialDesignThemes.Wpf.PackIconKind.Update,
                        Width = 16,
                        Height = 16,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    // 使用DynamicResource绑定前景色
                    icon.SetResourceReference(MaterialDesignThemes.Wpf.PackIcon.ForegroundProperty, "TextBrush");
                    
                    var textBlock = new TextBlock
                    {
                        Text = "检查更新",
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 13
                    };
                    // 使用DynamicResource绑定前景色
                    textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                    
                    stackPanel.Children.Add(icon);
                    stackPanel.Children.Add(textBlock);
                    button.Content = stackPanel;
                    button.IsEnabled = true;
                }
            }
        }

        /// <summary>
        /// 应用标题点击事件（隐藏调试控制台激活）
        /// </summary>
        private void AppTitleText_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var now = DateTime.Now;
            var timeSinceLastClick = (now - _lastAppTitleClickTime).TotalMilliseconds;
            
            // 如果超过2秒，重置计数
            if (timeSinceLastClick > APP_TITLE_CLICK_RESET_MS)
            {
                _appTitleClickCount = 0;
            }
            
            _appTitleClickCount++;
            _lastAppTitleClickTime = now;
            
            Debug.WriteLine($"[MorePage] 标题点击次数: {_appTitleClickCount}");
            
            // 点击5次后弹出确认对话框
            if (_appTitleClickCount >= 5)
            {
                _appTitleClickCount = 0; // 重置计数
                ShowDebugConsoleConfirmationAsync();
            }
        }

        /// <summary>
        /// 显示调试控制台确认对话框
        /// </summary>
        private async void ShowDebugConsoleConfirmationAsync()
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
                    ShowDebugConsole();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MorePage] 显示调试控制台确认对话框失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示调试控制台
        /// </summary>
        private void ShowDebugConsole()
        {
            try
            {
                // 创建调试控制台对话框
                var consoleDialog = new Border
                {
                    Width = 500,
                    Background = (System.Windows.Media.Brush)Application.Current.Resources["SurfaceBrush"],
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(24, 24, 24, 24),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var mainPanel = new StackPanel();

                // 标题
                var titlePanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 16)
                };

                var titleIcon = new MaterialDesignThemes.Wpf.PackIcon
                {
                    Kind = MaterialDesignThemes.Wpf.PackIconKind.Console,
                    Width = 28,
                    Height = 28,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                titleIcon.SetResourceReference(MaterialDesignThemes.Wpf.PackIcon.ForegroundProperty, "PrimaryBrush");

                var titleText = new TextBlock
                {
                    Text = "调试控制台",
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

                titlePanel.Children.Add(titleIcon);
                titlePanel.Children.Add(titleText);
                mainPanel.Children.Add(titlePanel);

                // 说明
                var descText = new TextBlock
                {
                    Text = "输入命令并按回车执行：",
                    FontSize = 13,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                descText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                mainPanel.Children.Add(descText);

                // 命令输入框
                var commandBox = new TextBox
                {
                    FontSize = 14,
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 12)
                };
                commandBox.SetResourceReference(TextBox.StyleProperty, typeof(TextBox));
                mainPanel.Children.Add(commandBox);

                // 可用命令列表
                var commandsListText = new TextBlock
                {
                    Text = "可用命令：\n" +
                           "• update - 测试更新对话框\n" +
                           "• version - 显示版本信息\n" +
                           "• help - 显示帮助",
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 16),
                    TextWrapping = TextWrapping.Wrap
                };
                commandsListText.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
                mainPanel.Children.Add(commandsListText);

                // 按钮区域
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var closeButton = new Button
                {
                    Content = "关闭",
                    Width = 80,
                    Height = 36,
                    FontSize = 13
                };
                closeButton.SetResourceReference(Button.StyleProperty, "MaterialDesignOutlinedButton");

                buttonPanel.Children.Add(closeButton);
                mainPanel.Children.Add(buttonPanel);

                consoleDialog.Child = mainPanel;

                // 添加到对话框容器
                var dialogContainer = Application.Current.MainWindow.FindName("GlobalDialogContainer") as Panel;
                if (dialogContainer != null)
                {
                    // 添加遮罩
                    var overlay = new Border
                    {
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(128, 0, 0, 0))
                    };

                    dialogContainer.Children.Add(overlay);
                    dialogContainer.Children.Add(consoleDialog);

                    // 关闭按钮事件
                    closeButton.Click += (s, e) =>
                    {
                        dialogContainer.Children.Remove(consoleDialog);
                        dialogContainer.Children.Remove(overlay);
                    };

                    // 遮罩点击关闭
                    overlay.MouseLeftButtonDown += (s, e) =>
                    {
                        dialogContainer.Children.Remove(consoleDialog);
                        dialogContainer.Children.Remove(overlay);
                    };

                    // 命令输入回车事件
                    commandBox.KeyDown += async (s, e) =>
                    {
                        if (e.Key == System.Windows.Input.Key.Enter)
                        {
                            var command = commandBox.Text.Trim().ToLower();
                            Debug.WriteLine($"[DebugConsole] 执行命令: {command}");

                            switch (command)
                            {
                                case "update":
                                    // 关闭控制台
                                    dialogContainer.Children.Remove(consoleDialog);
                                    dialogContainer.Children.Remove(overlay);
                                    // 显示测试更新对话框
                                    await UpdateService.ShowTestUpdateDialogAsync();
                                    break;

                                case "version":
                                    NotificationManager.Instance.ShowNotification(
                                        "版本信息",
                                        VersionInfo.GetDetailedVersionInfo(),
                                        NotificationType.Info,
                                        5
                                    );
                                    break;

                                case "help":
                                    NotificationManager.Instance.ShowNotification(
                                        "帮助",
                                        "可用命令：\n• update - 测试更新对话框\n• version - 显示版本信息\n• help - 显示帮助",
                                        NotificationType.Info,
                                        5
                                    );
                                    break;

                                case "":
                                    break;

                                default:
                                    NotificationManager.Instance.ShowNotification(
                                        "未知命令",
                                        $"命令 '{command}' 不存在，输入 'help' 查看可用命令",
                                        NotificationType.Warning,
                                        3
                                    );
                                    break;
                            }

                            commandBox.Text = "";
                        }
                    };

                    // 自动聚焦到输入框
                    commandBox.Focus();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MorePage] 显示调试控制台失败: {ex.Message}");
                NotificationManager.Instance.ShowNotification(
                    "错误",
                    "无法打开调试控制台",
                    NotificationType.Error,
                    3
                );
            }
        }
        
        #region 插件市场功能
        
        /// <summary>
        /// 刷新插件市场
        /// </summary>
        private async void RefreshMarket_Click(object sender, RoutedEventArgs e)
        {
            await LoadMarketPluginsAsync();
        }
        
        /// <summary>
        /// 加载插件市场数据
        /// </summary>
        private async Task LoadMarketPluginsAsync()
        {
            try
            {
                // 显示加载状态
                MarketLoadingIndicator.Visibility = Visibility.Visible;
                MarketErrorHint.Visibility = Visibility.Collapsed;
                MarketEmptyHint.Visibility = Visibility.Collapsed;
                MarketPluginsList.ItemsSource = null;
                
                Debug.WriteLine("[MorePage] 开始加载插件市场...");
                
                // 并行加载分类和插件数据
                var categoriesTask = PluginMarketService.GetCategoriesAsync();
                var marketIndexTask = PluginMarketService.GetMarketIndexAsync();
                
                await Task.WhenAll(categoriesTask, marketIndexTask);
                
                var categories = await categoriesTask;
                var marketIndex = await marketIndexTask;
                
                // 加载分类
                if (categories != null && categories.Count > 0)
                {
                    _categories = categories;
                    LoadCategories();
                }
                
                if (marketIndex == null || marketIndex.Plugins == null || marketIndex.Plugins.Count == 0)
                {
                    // 显示错误提示
                    MarketLoadingIndicator.Visibility = Visibility.Collapsed;
                    MarketErrorHint.Visibility = Visibility.Visible;
                    MarketErrorText.Text = "无法连接到插件市场或市场暂无插件";
                    Debug.WriteLine("[MorePage] 插件市场加载失败或为空");
                    return;
                }
                
                // 保存数据
                _allMarketPlugins = marketIndex.Plugins;
                _filteredMarketPlugins = new System.Collections.Generic.List<MarketPlugin>(_allMarketPlugins);
                
                // 隐藏加载状态
                MarketLoadingIndicator.Visibility = Visibility.Collapsed;
                
                // 显示插件列表
                DisplayMarketPlugins(_filteredMarketPlugins);
                
                Debug.WriteLine($"[MorePage] 成功加载 {_allMarketPlugins.Count} 个市场插件");
                
                NotificationManager.Instance.ShowNotification(
                    "插件市场",
                    $"成功加载 {_allMarketPlugins.Count} 个插件",
                    NotificationType.Success,
                    2
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MorePage] 加载插件市场失败: {ex.Message}");
                
                // 显示错误提示
                MarketLoadingIndicator.Visibility = Visibility.Collapsed;
                MarketErrorHint.Visibility = Visibility.Visible;
                MarketErrorText.Text = $"加载失败: {ex.Message}";
                
                NotificationManager.Instance.ShowNotification(
                    "插件市场",
                    "加载插件市场失败",
                    NotificationType.Error,
                    3
                );
            }
        }
        
        /// <summary>
        /// 加载分类到下拉框
        /// </summary>
        private void LoadCategories()
        {
            if (CategoryFilterCombo == null)
                return;
            
            // 清空现有项
            CategoryFilterCombo.Items.Clear();
            
            // 添加"全部"选项
            var allCategory = new PluginCategory
            {
                Id = "all",
                Name = "全部"
            };
            CategoryFilterCombo.Items.Add(allCategory);
            
            // 添加云端获取的分类
            foreach (var category in _categories)
            {
                CategoryFilterCombo.Items.Add(category);
            }
            
            // 选中第一项（全部）
            CategoryFilterCombo.SelectedIndex = 0;
            
            Debug.WriteLine($"[MorePage] 已加载 {_categories.Count} 个分类");
        }
        
        /// <summary>
        /// 显示市场插件列表
        /// </summary>
        private void DisplayMarketPlugins(System.Collections.Generic.List<MarketPlugin> plugins)
        {
            if (plugins == null || plugins.Count == 0)
            {
                MarketEmptyHint.Visibility = Visibility.Visible;
                MarketPluginsList.ItemsSource = null;
                return;
            }
            
            MarketEmptyHint.Visibility = Visibility.Collapsed;
            
            // 创建插件卡片
            var pluginCards = new System.Collections.Generic.List<UIElement>();
            
            foreach (var plugin in plugins)
            {
                var card = CreateMarketPluginCard(plugin);
                pluginCards.Add(card);
            }
            
            MarketPluginsList.ItemsSource = pluginCards;
        }
        
        /// <summary>
        /// 创建市场插件卡片
        /// </summary>
        private Border CreateMarketPluginCard(MarketPlugin plugin)
        {
            var card = new Border
            {
                Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };
            
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            
            // 插件图标
            Border iconBorder;
            if (!string.IsNullOrEmpty(plugin.Icon))
            {
                try
                {
                    var iconImage = new Image
                    {
                        Width = 48,
                        Height = 48,
                        Source = new BitmapImage(new Uri(plugin.Icon))
                    };
                    iconBorder = new Border
                    {
                        Width = 48,
                        Height = 48,
                        CornerRadius = new CornerRadius(6),
                        ClipToBounds = true,
                        Child = iconImage
                    };
                }
                catch
                {
                    iconBorder = CreateDefaultPluginIcon();
                }
            }
            else
            {
                iconBorder = CreateDefaultPluginIcon();
            }
            
            Grid.SetColumn(iconBorder, 0);
            grid.Children.Add(iconBorder);
            
            // 插件信息
            var infoPanel = new StackPanel
            {
                Margin = new Thickness(12, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(infoPanel, 1);
            
            // 插件名称
            var nameText = new TextBlock
            {
                Text = plugin.Name,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            };
            nameText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            infoPanel.Children.Add(nameText);
            
            // 插件版本和作者
            var versionText = new TextBlock
            {
                Text = $"v{plugin.Version} by {plugin.Author}",
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0)
            };
            versionText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            infoPanel.Children.Add(versionText);
            
            // 插件描述
            if (!string.IsNullOrEmpty(plugin.Description))
            {
                var descText = new TextBlock
                {
                    Text = plugin.Description,
                    FontSize = 12,
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 400
                };
                descText.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
                infoPanel.Children.Add(descText);
            }
            
            // 分类标签
            if (!string.IsNullOrEmpty(plugin.Category))
            {
                var categoryBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(25, 98, 0, 234)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                
                var categoryText = new TextBlock
                {
                    Text = plugin.Category,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(98, 0, 234))
                };
                
                categoryBadge.Child = categoryText;
                infoPanel.Children.Add(categoryBadge);
            }
            
            grid.Children.Add(infoPanel);
            
            // 检查是否已安装
            var isInstalled = _pluginLoader?.LoadedPlugins.Any(p => p.Id == plugin.Id) ?? false;
            
            // 安装/已安装按钮
            var installButton = new Button
            {
                Height = 32,
                Padding = new Thickness(16, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            
            if (isInstalled)
            {
                installButton.Content = "已安装";
                installButton.IsEnabled = false;
                installButton.SetResourceReference(Button.StyleProperty, "MaterialDesignOutlinedButton");
            }
            else
            {
                var buttonStack = new StackPanel { Orientation = Orientation.Horizontal };
                
                var icon = new MaterialDesignThemes.Wpf.PackIcon
                {
                    Kind = MaterialDesignThemes.Wpf.PackIconKind.Download,
                    Width = 16,
                    Height = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                
                var text = new TextBlock
                {
                    Text = "安装",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13
                };
                
                buttonStack.Children.Add(icon);
                buttonStack.Children.Add(text);
                installButton.Content = buttonStack;
                installButton.SetResourceReference(Button.StyleProperty, "MaterialDesignRaisedButton");
                installButton.Click += async (s, e) => await InstallMarketPluginAsync(plugin, installButton);
            }
            
            Grid.SetColumn(installButton, 2);
            grid.Children.Add(installButton);
            
            card.Child = grid;
            return card;
        }
        
        /// <summary>
        /// 安装市场插件
        /// </summary>
        private async Task InstallMarketPluginAsync(MarketPlugin plugin, Button button)
        {
            try
            {
                // 禁用按钮
                button.IsEnabled = false;
                
                // 更新按钮文本
                var originalContent = button.Content;
                var progressStack = new StackPanel { Orientation = Orientation.Horizontal };
                var progressBar = new ProgressBar
                {
                    Width = 16,
                    Height = 16,
                    Style = (Style)Application.Current.Resources["MaterialDesignCircularProgressBar"],
                    IsIndeterminate = true,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                var progressText = new TextBlock
                {
                    Text = "安装中...",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13
                };
                progressStack.Children.Add(progressBar);
                progressStack.Children.Add(progressText);
                button.Content = progressStack;
                
                Debug.WriteLine($"[MorePage] 开始安装插件: {plugin.Name}");
                
                // 获取插件目录
                var pluginsDir = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "plugins"
                );
                
                // 下载并安装
                var progress = new Progress<double>(percent =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        progressText.Text = $"安装中 {percent:F0}%";
                    });
                });
                
                var success = await PluginMarketService.DownloadAndInstallPluginAsync(
                    plugin,
                    pluginsDir,
                    progress
                );
                
                if (success)
                {
                    // 更新按钮状态
                    button.Content = "已安装";
                    button.SetResourceReference(Button.StyleProperty, "MaterialDesignOutlinedButton");
                    
                    NotificationManager.Instance.ShowNotification(
                        "插件安装",
                        $"插件 {plugin.Name} 安装成功！请重启启动器以加载插件。",
                        NotificationType.Success,
                        5
                    );
                    
                    Debug.WriteLine($"[MorePage] 插件安装成功: {plugin.Name}");
                }
                else
                {
                    // 恢复按钮
                    button.Content = originalContent;
                    button.IsEnabled = true;
                    
                    NotificationManager.Instance.ShowNotification(
                        "插件安装",
                        $"插件 {plugin.Name} 安装失败",
                        NotificationType.Error,
                        3
                    );
                    
                    Debug.WriteLine($"[MorePage] 插件安装失败: {plugin.Name}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MorePage] 安装插件失败: {ex.Message}");
                button.IsEnabled = true;
                
                NotificationManager.Instance.ShowNotification(
                    "插件安装",
                    $"安装失败: {ex.Message}",
                    NotificationType.Error,
                    3
                );
            }
        }
        
        /// <summary>
        /// 搜索框文本变化
        /// </summary>
        private void MarketSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterMarketPlugins();
        }
        
        /// <summary>
        /// 分类筛选变化
        /// </summary>
        private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterMarketPlugins();
        }
        
        /// <summary>
        /// 筛选市场插件
        /// </summary>
        private void FilterMarketPlugins()
        {
            if (_allMarketPlugins == null || _allMarketPlugins.Count == 0)
                return;
            
            var searchText = MarketSearchBox?.Text?.ToLower() ?? "";
            var selectedCategory = CategoryFilterCombo?.SelectedItem as PluginCategory;
            var selectedCategoryId = selectedCategory?.Id ?? "all";
            
            _filteredMarketPlugins = _allMarketPlugins.Where(plugin =>
            {
                // 搜索筛选
                var matchesSearch = string.IsNullOrEmpty(searchText) ||
                                  plugin.Name.ToLower().Contains(searchText) ||
                                  plugin.Description.ToLower().Contains(searchText) ||
                                  plugin.Author.ToLower().Contains(searchText);
                
                // 分类筛选（使用分类ID进行匹配）
                var matchesCategory = selectedCategoryId == "all" ||
                                    plugin.Category.ToLower() == selectedCategoryId.ToLower() ||
                                    // 兼容旧的中文分类名称
                                    plugin.Category == selectedCategory?.Name;
                
                return matchesSearch && matchesCategory;
            }).ToList();
            
            DisplayMarketPlugins(_filteredMarketPlugins);
        }
        
        #endregion
    }
}
