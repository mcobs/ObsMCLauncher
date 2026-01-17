using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using ObsMCLauncher.Models;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;
using ObsMCLauncher.Windows;

namespace ObsMCLauncher.Pages
{
    public partial class VersionDownloadPage : Page
    {
        private List<MinecraftVersion> _allVersions = new();
        private List<MinecraftVersion> _filteredVersions = new();
        
        // 缓存版本详情页实例，保持下载状态
        private readonly Dictionary<string, VersionDetailPage> _versionDetailPages = new Dictionary<string, VersionDetailPage>();
        
        // 记住当前显示的详情页
        private VersionDetailPage? _currentDetailPage = null;
        private bool _isShowingDetail = false;
        
        // 记住滚动位置
        private double _savedScrollOffset = 0;

        public VersionDownloadPage()
        {
            InitializeComponent();
            // 页面加载完成后自动加载版本列表
            Loaded += VersionDownloadPage_Loaded;
        }

        private async void VersionDownloadPage_Loaded(object sender, RoutedEventArgs e)
        {
            // 如果之前在显示详情页，恢复显示
            if (_isShowingDetail && _currentDetailPage != null)
            {
                ShowDetailPage(_currentDetailPage);
            }
            else
            {
                // 每次加载页面都刷新已安装版本
                RefreshInstalledVersions();
                
                // 自动加载在线版本列表（仅在首次加载时）
                if (_allVersions.Count == 0)
                {
                    await LoadVersionsAsync();
                }
                
                // 恢复滚动位置
                if (VersionScrollViewer != null && _savedScrollOffset > 0)
                {
                    VersionScrollViewer.ScrollToVerticalOffset(_savedScrollOffset);
                }
            }
        }

        private void VersionItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is MinecraftVersion version)
            {
                // 使用缓存的版本详情页，如果不存在则创建
                if (!_versionDetailPages.ContainsKey(version.Id))
                {
                    var detailPage = new VersionDetailPage(version);
                    
                    // 设置返回回调，不使用导航历史
                    detailPage.OnBackRequested = () => DetailPage_BackButton_Click(null, null);
                    
                    _versionDetailPages[version.Id] = detailPage;
                }
                
                // 显示版本详情页
                ShowDetailPage(_versionDetailPages[version.Id]);
            }
        }
        
        /// <summary>
        /// 显示详情页
        /// </summary>
        private void ShowDetailPage(VersionDetailPage detailPage)
        {
            _currentDetailPage = detailPage;
            _isShowingDetail = true;
            
            // 保存当前滚动位置
            if (VersionScrollViewer != null)
            {
                _savedScrollOffset = VersionScrollViewer.VerticalOffset;
            }
            
            // 使用嵌套 Frame 显示详情页，保持Page特性
            DetailFrame.Navigate(detailPage);
            DetailFrame.Visibility = Visibility.Visible;
            VersionListGrid.Visibility = Visibility.Collapsed;
        }
        
        /// <summary>
        /// 详情页返回按钮点击事件
        /// </summary>
        private void DetailPage_BackButton_Click(object? sender, RoutedEventArgs? e)
        {
            _isShowingDetail = false;
            
            // 返回到版本列表（不使用导航，直接显示隐藏）
            DetailFrame.Visibility = Visibility.Collapsed;
            VersionListGrid.Visibility = Visibility.Visible;
            
            // 恢复滚动位置
            if (VersionScrollViewer != null && _savedScrollOffset > 0)
            {
                // 使用 Dispatcher 延迟执行，确保布局更新完成
                Dispatcher.InvokeAsync(() =>
                {
                    VersionScrollViewer.ScrollToVerticalOffset(_savedScrollOffset);
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void VersionTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // 使用当前配置的下载源刷新版本列表
            await LoadVersionsAsync();
        }

        /// <summary>
        /// 加载版本列表
        /// </summary>
        private async System.Threading.Tasks.Task LoadVersionsAsync()
        {
            try
            {
                // 显示加载指示器
                ShowLoading(true);
                UpdateLoadingText("正在连接服务器...");
                RefreshButton_Click_SetEnabled(false);

                System.Diagnostics.Debug.WriteLine("开始加载版本列表...");

                // 从当前下载源获取版本列表
                UpdateLoadingText("正在获取版本列表...");
                var manifest = await MinecraftVersionService.GetVersionListAsync();

                _allVersions = manifest?.Versions ?? new List<MinecraftVersion>();

                System.Diagnostics.Debug.WriteLine($"✅ 成功获取版本列表，共 {_allVersions.Count} 个版本");
                System.Diagnostics.Debug.WriteLine($"   最新正式版: {manifest?.Latest?.Release ?? "未知"}");
                System.Diagnostics.Debug.WriteLine($"   最新快照版: {manifest?.Latest?.Snapshot ?? "未知"}");
                
                // 应用筛选并显示版本列表
                UpdateLoadingText("正在生成版本列表...");
                await System.Threading.Tasks.Task.Delay(100); // 让UI有时间更新
                ApplyFilters();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 加载版本列表失败: {ex.Message}");
                
                var errorMessage = $"加载版本列表失败:\n\n{ex.Message}\n\n";
                
                // 添加故障排查建议
                errorMessage += "故障排查建议：\n";
                errorMessage += "1. 检查网络连接是否正常\n";
                errorMessage += "2. 尝试在设置中切换下载源\n";
                errorMessage += "3. 检查防火墙是否拦截了启动器\n";
                errorMessage += "4. 点击设置中的\"测试下载源\"按钮";
                
                await DialogManager.Instance.ShowError("加载失败", errorMessage);
            }
            finally
            {
                ShowLoading(false);
                RefreshButton_Click_SetEnabled(true);
            }
        }

        /// <summary>
        /// 应用筛选条件
        /// </summary>
        private void ApplyFilters()
        {
            if (_allVersions == null || _allVersions.Count == 0)
                return;

            var searchText = SearchBox?.Text?.ToLower() ?? "";
            var selectedType = (VersionTypeComboBox?.SelectedIndex ?? 0);

            // 筛选版本
            _filteredVersions = _allVersions.Where(v =>
            {
                // 搜索过滤
                if (!string.IsNullOrEmpty(searchText) && !v.Id.ToLower().Contains(searchText))
                    return false;

                // 类型过滤
                if (selectedType == 1 && v.Type != "release") // 正式版
                    return false;
                if (selectedType == 2 && v.Type != "snapshot") // 快照版
                    return false;
                if (selectedType == 3) // 远古版（包括 old_alpha, old_beta 等，排除 release 和 snapshot）
                {
                    if (v.Type == "release" || v.Type == "snapshot")
                        return false;
                }

                return true;
            }).ToList();

            System.Diagnostics.Debug.WriteLine($"筛选后版本数量: {_filteredVersions.Count}");

            // 更新UI
            DisplayVersions();
        }

        /// <summary>
        /// 显示版本列表
        /// </summary>
        private void DisplayVersions()
        {
            VersionListPanel.Children.Clear();

            if (_filteredVersions.Count == 0)
            {
                // 显示空状态
                var emptyText = new TextBlock
                {
                    Text = "没有找到匹配的版本",
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 50, 0, 0)
                };
                emptyText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                VersionListPanel.Children.Add(emptyText);
                return;
            }

            // 动态生成版本项
            foreach (var version in _filteredVersions.Take(50)) // 限制显示前50个
            {
                var button = CreateVersionButton(version);
                VersionListPanel.Children.Add(button);
            }

            // 如果版本太多，显示提示
            if (_filteredVersions.Count > 50)
            {
                var moreText = new TextBlock
                {
                    Text = $"还有 {_filteredVersions.Count - 50} 个版本未显示，请使用搜索功能",
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 20)
                };
                moreText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                VersionListPanel.Children.Add(moreText);
            }
        }

        /// <summary>
        /// 创建版本按钮
        /// </summary>
        private Button CreateVersionButton(MinecraftVersion version)
        {
            var button = new Button
            {
                Tag = version,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Padding = new Thickness(0),
                Margin = new Thickness(10),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Height = double.NaN  // 自动高度
            };

            button.Click += VersionItem_Click;

            // 尝试应用样式（可选）
            try
            {
                button.Style = (Style)FindResource("MaterialDesignFlatButton");
            }
            catch
            {
                // 如果找不到样式，使用默认样式
                System.Diagnostics.Debug.WriteLine("未找到 MaterialDesignFlatButton 样式");
            }

            // 创建内容 - 使用更可靠的方式
            var border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10)
            };
            border.SetResourceReference(Border.BackgroundProperty, "SurfaceElevatedBrush");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 图标 - 根据版本类型动态选择
            var iconPath = GetVersionIconPath(version);
            var icon = new Image
            {
                Width = 28,
                Height = 28,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath, UriKind.Relative))
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            // 版本信息
            var infoPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            // 标题
            var titleText = new TextBlock
            {
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            };
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            var typeText = version.Type == "release" ? "正式版" :
                          version.Type == "snapshot" ? "快照版" :
                          version.Type == "old_alpha" ? "远古Alpha" :
                          version.Type == "old_beta" ? "远古Beta" : "其他";
            
            var typeColor = version.Type == "release" ? Color.FromRgb(34, 197, 94) :
                           version.Type == "snapshot" ? Color.FromRgb(59, 130, 246) : 
                           Color.FromRgb(245, 158, 11);

            titleText.Inlines.Add(new Run($"Minecraft {version.Id}"));
            titleText.Inlines.Add(new Run($" [{typeText}]")
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(typeColor)
            });

            infoPanel.Children.Add(titleText);

            // 详情
            var detailText = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0),
                Text = $"发布时间: {version.ReleaseTime:yyyy-MM-dd HH:mm}"
            };
            detailText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            infoPanel.Children.Add(detailText);

            Grid.SetColumn(infoPanel, 1);
            grid.Children.Add(infoPanel);

            // 箭头
            var arrow = new PackIcon
            {
                Kind = PackIconKind.ChevronRight,
                Width = 18,
                Height = 18,
                VerticalAlignment = VerticalAlignment.Center
            };
            arrow.SetResourceReference(PackIcon.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(arrow, 2);
            grid.Children.Add(arrow);

            border.Child = grid;
            button.Content = border;

            return button;
        }

        /// <summary>
        /// 根据版本类型获取图标路径
        /// </summary>
        private string GetVersionIconPath(MinecraftVersion version)
        {
            string iconPath = "/Assets/LoaderIcons/vanilla.png"; // 默认图标
            
            try
            {
                // 判断是否为快照版
                if (version.Type == "snapshot")
                {
                    iconPath = "/Assets/LoaderIcons/vanilia_snapshot.png";
                }
                // 判断是否为正式版
                else if (version.Type == "release")
                {
                    // 判断版本是否 <= 1.12.2
                    if (IsVersionLessThanOrEqual(version.Id, "1.12.2"))
                    {
                        iconPath = "/Assets/LoaderIcons/vanilla_old.png";
                    }
                    else
                    {
                        iconPath = "/Assets/LoaderIcons/vanilla.png";
                    }
                }
                // 其他版本类型（old_alpha, old_beta 等）
                else
                {
                    iconPath = "/Assets/LoaderIcons/vanilla_old.png";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VersionIcon] 获取图标路径失败: {ex.Message}");
            }
            
            return iconPath;
        }

        /// <summary>
        /// 比较版本号是否小于或等于目标版本
        /// </summary>
        private bool IsVersionLessThanOrEqual(string version, string targetVersion)
        {
            try
            {
                // 提取主版本号部分（例如 "1.12.2" 或 "1.20.1"）
                var versionParts = version.Split('.');
                var targetParts = targetVersion.Split('.');
                
                // 比较主版本号
                for (int i = 0; i < Math.Min(versionParts.Length, targetParts.Length); i++)
                {
                    if (int.TryParse(versionParts[i], out int vNum) && 
                        int.TryParse(targetParts[i], out int tNum))
                    {
                        if (vNum < tNum) return true;
                        if (vNum > tNum) return false;
                        // 相等则继续比较下一位
                    }
                    else
                    {
                        // 如果包含非数字字符，按字符串比较
                        int cmp = string.Compare(versionParts[i], targetParts[i], StringComparison.Ordinal);
                        if (cmp < 0) return true;
                        if (cmp > 0) return false;
                    }
                }
                
                // 如果所有部分都相等，则判断长度
                return versionParts.Length <= targetParts.Length;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VersionIcon] 版本比较失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 显示/隐藏加载指示器
        /// </summary>
        private void ShowLoading(bool show)
        {
            LoadingPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            VersionScrollViewer.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// 更新加载文本
        /// </summary>
        private void UpdateLoadingText(string text)
        {
            if (LoadingDetailText != null)
            {
                LoadingDetailText.Text = text;
            }
        }

        private void RefreshButton_Click_SetEnabled(bool enabled)
        {
            if (RefreshButton != null)
            {
                RefreshButton.IsEnabled = enabled;
            }
        }

        #region 已安装版本管理


        /// <summary>
        /// 加载已安装版本
        /// </summary>
        private void LoadInstalledVersions()
        {
            InstalledVersionsPanel.Children.Clear();

            var config = LauncherConfig.Load();
            var installedVersions = LocalVersionService.GetInstalledVersions(config.GameDirectory);
            var selectedVersion = config.SelectedVersion;

            InstalledVersionCountText.Text = installedVersions.Count.ToString();

            if (installedVersions.Count == 0)
            {
                // 显示空状态
                var emptyText = new TextBlock
                {
                    Text = "暂无已安装版本",
                    FontSize = 16,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 50, 0, 0)
                };
                InstalledVersionsPanel.Children.Add(emptyText);
                return;
            }

            // 动态生成已安装版本项
            foreach (var version in installedVersions)
            {
                var card = CreateInstalledVersionCard(version, version.Id == selectedVersion);
                InstalledVersionsPanel.Children.Add(card);
            }
        }

        /// <summary>
        /// 创建已安装版本卡片
        /// </summary>
        private Border CreateInstalledVersionCard(InstalledVersion version, bool isSelected)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(15, 12, 15, 12),
                Margin = new Thickness(10, 5, 10, 5)
            };
            border.SetResourceReference(Border.BackgroundProperty, "SurfaceElevatedBrush");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 左侧信息
            var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            // 版本号（带加载器图标）
            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
            
            // 添加加载器图标
            var loaderIcon = GetVersionLoaderIcon(version);
            if (loaderIcon != null)
            {
                loaderIcon.VerticalAlignment = VerticalAlignment.Center;
                loaderIcon.Margin = new Thickness(0, 0, 8, 0);
                titlePanel.Children.Add(loaderIcon);
            }
            
            var titleText = new TextBlock
            {
                Text = version.Id,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            titlePanel.Children.Add(titleText);
            
            // 如果自定义名称与实际版本ID不同，显示实际版本
            if (version.Id != version.ActualVersionId)
            {
                var actualVersionBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var actualVersionText = new TextBlock
                {
                    Text = version.ActualVersionId,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold
                };
                actualVersionBadge.Child = actualVersionText;
                titlePanel.Children.Add(actualVersionBadge);
            }

            // 选中标记
            if (isSelected)
            {
                var selectedBadge = new Border
                {
                    Background = (Brush)Application.Current.TryFindResource("PrimaryBrush")
                        ?? new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var badgeText = new TextBlock
                {
                    Text = "当前版本",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold
                };
                selectedBadge.Child = badgeText;
                titlePanel.Children.Add(selectedBadge);
            }

            infoPanel.Children.Add(titlePanel);

            // 详情
            var detailText = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0)
            };
            detailText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

            var typeText = version.Type == "release" ? "正式版" :
                          version.Type == "snapshot" ? "快照版" : "其他";
            detailText.Text = $"{typeText} · 上次游玩: {version.LastPlayed:yyyy-MM-dd HH:mm}";
            infoPanel.Children.Add(detailText);

            Grid.SetColumn(infoPanel, 0);
            grid.Children.Add(infoPanel);

            // 右侧操作按钮
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // 选择按钮
            if (!isSelected)
            {
                var selectButton = new Button
                {
                    Content = "选择",
                    Tag = version,
                    Style = (Style)Application.Current.TryFindResource("MaterialDesignOutlinedButton"),
                    Height = 28,
                    FontSize = 12,
                    Padding = new Thickness(12, 0, 12, 0),
                    Margin = new Thickness(0, 0, 8, 0)
                };
                selectButton.Click += SelectVersionButton_Click;
                buttonPanel.Children.Add(selectButton);
            }

            // 快速启动按钮
            var launchButton = new Button
            {
                Tag = version.Id,
                Style = (Style)Application.Current.TryFindResource("MaterialDesignIconButton"),
                ToolTip = "快速启动",
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 8, 0)
            };
            launchButton.Click += QuickLaunchButton_Click;
            var launchIcon = new PackIcon
            {
                Kind = PackIconKind.Play,
                Width = 16,
                Height = 16
            };
            launchIcon.SetResourceReference(PackIcon.ForegroundProperty, "PrimaryBrush");
            launchButton.Content = launchIcon;
            buttonPanel.Children.Add(launchButton);

            // 管理按钮
            var manageButton = new Button
            {
                Tag = version,
                Style = (Style)Application.Current.TryFindResource("MaterialDesignIconButton"),
                ToolTip = "管理实例",
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 8, 0)
            };
            manageButton.Click += ManageVersionButton_Click;
            var manageIcon = new PackIcon
            {
                Kind = PackIconKind.Cog,
                Width = 16,
                Height = 16
            };
            manageIcon.SetResourceReference(PackIcon.ForegroundProperty, "PrimaryBrush");
            manageButton.Content = manageIcon;
            buttonPanel.Children.Add(manageButton);

            // 删除按钮
            var deleteButton = new Button
            {
                Tag = new Tuple<string, string>(version.Id, version.Path),
                Style = (Style)Application.Current.TryFindResource("MaterialDesignIconButton"),
                ToolTip = "删除版本",
                Foreground = new SolidColorBrush(Color.FromRgb(220, 53, 69)),
                Width = 28,
                Height = 28,
                Padding = new Thickness(0)
            };
            deleteButton.Click += DeleteVersionButton_Click;
            var deleteIcon = new PackIcon
            {
                Kind = PackIconKind.Delete,
                Width = 16,
                Height = 16
            };
            deleteButton.Content = deleteIcon;
            buttonPanel.Children.Add(deleteButton);

            Grid.SetColumn(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            border.Child = grid;
            return border;
        }

        /// <summary>
        /// 选择版本按钮点击
        /// </summary>
        private void SelectVersionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is InstalledVersion version)
            {
                LocalVersionService.SetSelectedVersion(version.Id);
                
                // 使用动画将选中的版本移动到顶部
                AnimateMoveToTop(version);
            }
        }

        /// <summary>
        /// 快速启动按钮点击事件
        /// </summary>
        private async void QuickLaunchButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string versionId)
            {
                // 获取默认账号
                var accounts = AccountService.Instance.GetAllAccounts();
                var defaultAccount = accounts.FirstOrDefault(a => a.IsDefault) ?? accounts.FirstOrDefault();
                
                if (defaultAccount == null)
                {
                    NotificationManager.Instance.ShowNotification(
                        "无法启动",
                        "请先在账号管理中添加账号",
                        NotificationType.Error
                    );
                    return;
                }
                
                // 禁用按钮
                button.IsEnabled = false;
                var originalContent = button.Content;
                
                try
                {
                    // 显示加载动画
                    var progressRing = new ProgressBar
                    {
                        Width = 16,
                        Height = 16,
                        IsIndeterminate = true,
                        Style = (Style)Application.Current.TryFindResource("MaterialDesignCircularProgressBar")
                    };
                    button.Content = progressRing;
                    
                    // 加载配置
                    var config = LauncherConfig.Load();
                    
                    // 显示启动通知
                    var launchNotificationId = NotificationManager.Instance.ShowNotification(
                        "正在启动游戏",
                        $"版本: {versionId}",
                        NotificationType.Progress
                    );
                    
                    // 检查游戏完整性
                    bool hasIntegrityIssue = await GameLauncher.CheckGameIntegrityAsync(
                        versionId, 
                        config, 
                        (progress) => NotificationManager.Instance.UpdateNotification(launchNotificationId, progress)
                    );
                    
                    // 如果有缺失依赖，下载
                    if (hasIntegrityIssue && GameLauncher.MissingLibraries.Count > 0)
                    {
                        NotificationManager.Instance.UpdateNotification(
                            launchNotificationId,
                            $"检测到 {GameLauncher.MissingLibraries.Count} 个缺失依赖，正在下载..."
                        );
                        
                        var (successCount, failedCount) = await LibraryDownloader.DownloadMissingLibrariesAsync(
                            config.GameDirectory,
                            versionId,
                            GameLauncher.MissingLibraries,
                            (libName, current, total) => 
                            {
                                NotificationManager.Instance.UpdateNotification(
                                    launchNotificationId, 
                                    $"下载依赖: {libName} ({current}/{total})"
                                );
                            }
                        );
                        
                        if (failedCount > 0)
                        {
                            NotificationManager.Instance.ShowNotification(
                                "启动失败",
                                $"依赖库下载失败 ({failedCount}/{GameLauncher.MissingLibraries.Count})",
                                NotificationType.Error
                            );
                            return;
                        }
                    }
                    
                    // 启动游戏
                    NotificationManager.Instance.UpdateNotification(launchNotificationId, "正在启动游戏...");
                    
                    // 创建日志窗口（如果配置启用）
                    GameLogWindow? logWindow = null;
                    if (config.ShowGameLogOnLaunch)
                    {
                        logWindow = new GameLogWindow(versionId);
                        logWindow.Show();
                    }
                    
                    await GameLauncher.LaunchGameAsync(
                        versionId, 
                        defaultAccount, 
                        config,
                        (progress) => NotificationManager.Instance.UpdateNotification(launchNotificationId, progress),
                        (output) => logWindow?.AppendGameOutput(output),
                        (exitCode) => 
                        {
                            logWindow?.OnGameExit(exitCode);
                            // 移除启动进度通知
                            NotificationManager.Instance.RemoveNotification(launchNotificationId);
                        }
                    );
                    
                    NotificationManager.Instance.ShowNotification(
                        "启动成功",
                        $"游戏 {versionId} 已启动",
                        NotificationType.Success
                    );
                }
                catch (Exception ex)
                {
                    NotificationManager.Instance.ShowNotification(
                        "启动失败",
                        ex.Message,
                        NotificationType.Error
                    );
                    System.Diagnostics.Debug.WriteLine($"[QuickLaunch] 启动失败: {ex.Message}");
                }
                finally
                {
                    // 恢复按钮
                    button.Content = originalContent;
                    button.IsEnabled = true;
                }
            }
        }

        /// <summary>
        /// 动画移动版本到顶部
        /// </summary>
        private void AnimateMoveToTop(InstalledVersion selectedVersion)
        {
            var config = LauncherConfig.Load();
            var installedVersions = LocalVersionService.GetInstalledVersions(config.GameDirectory);
            
            // 找到被选中的版本在列表中的位置
            int selectedIndex = -1;
            for (int i = 0; i < installedVersions.Count; i++)
            {
                if (installedVersions[i].Id == selectedVersion.Id)
                {
                    selectedIndex = i;
                    break;
                }
            }
            
            if (selectedIndex < 0 || selectedIndex >= InstalledVersionsPanel.Children.Count) return;
            
            var selectedCard = InstalledVersionsPanel.Children[selectedIndex] as Border;
            if (selectedCard == null) return;
            
            // 如果已经在顶部，只需重新加载
            if (selectedIndex == 0)
            {
                LoadInstalledVersions();
                
                // 通知HomePage刷新
                NotifyHomePageToRefresh();
                return;
            }
            
            // 创建淡出动画
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };
            
            fadeOut.Completed += (s, e) =>
            {
                // 重新加载列表（这次选中的会在顶部）
                LoadInstalledVersions();
                
                // 通知HomePage刷新
                NotifyHomePageToRefresh();
                
                // 对第一个元素添加淡入动画
                if (InstalledVersionsPanel.Children.Count > 0)
                {
                    var topCard = InstalledVersionsPanel.Children[0] as Border;
                    if (topCard != null)
                    {
                        topCard.Opacity = 0;
                        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 0.0,
                            To = 1.0,
                            Duration = TimeSpan.FromMilliseconds(300),
                            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };
                        topCard.BeginAnimation(Border.OpacityProperty, fadeIn);
                    }
                }
            };
            
            selectedCard.BeginAnimation(Border.OpacityProperty, fadeOut);
        }
        
        /// <summary>
        /// 通知主页刷新版本列表
        /// </summary>
        private void NotifyHomePageToRefresh()
        {
            try
            {
                // 查找MainWindow中的ContentFrame
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    var contentFrame = mainWindow.FindName("ContentFrame") as Frame;
                    if (contentFrame?.Content is HomePage homePage)
                    {
                        // 调用HomePage的刷新方法
                        var loadVersionsMethod = homePage.GetType().GetMethod("LoadVersions", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        loadVersionsMethod?.Invoke(homePage, null);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"刷新主页失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 打开文件夹按钮点击
        /// </summary>
        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string path)
            {
                LocalVersionService.OpenVersionFolder(path);
            }
        }

        /// <summary>
        /// 删除版本按钮点击
        /// </summary>
        private async void DeleteVersionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Tuple<string, string> data)
            {
                var versionId = data.Item1;
                var versionPath = data.Item2;

                var result = await DialogManager.Instance.ShowWarning(
                    "确认删除",
                    $"确定要删除版本 {versionId} 吗？\n\n此操作不可恢复！",
                    DialogButtons.YesNo
                );

                if (result == DialogResult.Yes)
                {
                    if (LocalVersionService.DeleteVersion(versionPath))
                    {
                        RefreshInstalledVersions(); // 刷新列表
                        
                        // 使用通知管理器显示成功消息
                        NotificationManager.Instance.ShowNotification(
                            "删除成功",
                            $"版本 {versionId} 已删除",
                            NotificationType.Success,
                            3
                        );
                    }
                    else
                    {
                        await DialogManager.Instance.ShowError(
                            "删除失败",
                            $"删除版本 {versionId} 失败，可能文件正在使用或没有权限"
                        );
                    }
                }
            }
        }

        /// <summary>
        /// 管理版本按钮点击
        /// </summary>
        private void ManageVersionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is InstalledVersion version)
            {
                // 创建版本实例管理页面
                var instancePage = new VersionInstancePage(version);
                
                // 设置返回回调
                instancePage.OnBackRequested = () =>
                {
                    // 返回到版本列表
                    DetailFrame.Visibility = Visibility.Collapsed;
                    VersionListGrid.Visibility = Visibility.Visible;
                    
                    // 刷新已安装版本列表
                    RefreshInstalledVersions();
                };
                
                // 显示实例管理页面
                DetailFrame.Navigate(instancePage);
                DetailFrame.Visibility = Visibility.Visible;
                VersionListGrid.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 刷新已安装版本按钮点击
        /// </summary>
        private void RefreshInstalledButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshInstalledVersions();
        }

        /// <summary>
        /// 刷新已安装版本列表（公共方法，供外部调用）
        /// </summary>
        public void RefreshInstalledVersions()
        {
            LoadInstalledVersions();
            System.Diagnostics.Debug.WriteLine("已刷新已安装版本列表");
        }

        /// <summary>
        /// 获取加载器图标
        /// </summary>
        private PackIcon? GetVersionLoaderIcon(InstalledVersion version)
        {
            try
            {
                // 读取版本JSON文件
                var versionJsonPath = System.IO.Path.Combine(version.Path, $"{version.ActualVersionId}.json");
                if (!System.IO.File.Exists(versionJsonPath))
                {
                    return null;
                }

                var jsonContent = System.IO.File.ReadAllText(versionJsonPath);

                // 检测加载器类型
                PackIconKind iconKind = PackIconKind.Minecraft;
                System.Windows.Media.Color iconColor = System.Windows.Media.Colors.Green;

                // 检查是否有 Forge
                if (jsonContent.Contains("net.minecraftforge") || jsonContent.Contains("forge"))
                {
                    iconKind = PackIconKind.Anvil;
                    iconColor = System.Windows.Media.Color.FromRgb(205, 92, 92); // Forge红色
                }
                // 检查是否有 Fabric
                else if (jsonContent.Contains("fabric") || jsonContent.Contains("net.fabricmc"))
                {
                    iconKind = PackIconKind.AlphaFBox;
                    iconColor = System.Windows.Media.Color.FromRgb(222, 184, 135); // Fabric棕色
                }
                // 检查是否有 Quilt
                else if (jsonContent.Contains("quilt") || jsonContent.Contains("org.quiltmc"))
                {
                    iconKind = PackIconKind.AlphaQBox;
                    iconColor = System.Windows.Media.Color.FromRgb(138, 43, 226); // Quilt紫色
                }
                // 检查是否有 NeoForge
                else if (jsonContent.Contains("neoforge") || jsonContent.Contains("net.neoforged"))
                {
                    iconKind = PackIconKind.Anvil;
                    iconColor = System.Windows.Media.Color.FromRgb(255, 140, 0); // NeoForge橙色
                }
                // 检查是否有 OptiFine
                else if (jsonContent.Contains("optifine"))
                {
                    iconKind = PackIconKind.Sunglasses;
                    iconColor = System.Windows.Media.Color.FromRgb(100, 149, 237); // OptiFine蓝色
                }

                return new PackIcon
                {
                    Kind = iconKind,
                    Width = 20,
                    Height = 20,
                    Foreground = new System.Windows.Media.SolidColorBrush(iconColor)
                };
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
