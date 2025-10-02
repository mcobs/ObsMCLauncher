using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using ObsMCLauncher.Services;

namespace ObsMCLauncher.Pages
{
    public partial class VersionDownloadPage : Page
    {
        private List<MinecraftVersion> _allVersions = new();
        private List<MinecraftVersion> _filteredVersions = new();

        public VersionDownloadPage()
        {
            InitializeComponent();
            // 页面加载完成后自动加载版本列表
            Loaded += VersionDownloadPage_Loaded;
        }

        private async void VersionDownloadPage_Loaded(object sender, RoutedEventArgs e)
        {
            // 自动加载版本列表
            await LoadVersionsAsync();
        }

        private void VersionItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string versionTag)
            {
                // 导航到版本详情配置页面
                NavigationService?.Navigate(new VersionDetailPage(versionTag));
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
                
                MessageBox.Show(errorMessage, "加载失败", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 50, 0, 0)
                };
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
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#999999")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 20)
                };
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
                Tag = version.Id,
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
                Background = (Brush)Application.Current.TryFindResource("SurfaceElevatedBrush") 
                    ?? new SolidColorBrush(Color.FromRgb(39, 39, 42)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 图标
            var icon = new PackIcon
            {
                Kind = PackIconKind.Cube,
                Width = 40,
                Height = 40,
                Foreground = (Brush)Application.Current.TryFindResource("PrimaryBrush")
                    ?? new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 20, 0)
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
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.TryFindResource("TextBrush")
                    ?? Brushes.White
            };

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
                FontSize = 13,
                Foreground = new SolidColorBrush(typeColor)
            });

            infoPanel.Children.Add(titleText);

            // 详情
            var detailText = new TextBlock
            {
                FontSize = 13,
                Foreground = (Brush)Application.Current.TryFindResource("TextSecondaryBrush")
                    ?? new SolidColorBrush(Color.FromRgb(153, 153, 153)),
                Margin = new Thickness(0, 5, 0, 0),
                Text = $"发布时间: {version.ReleaseTime:yyyy-MM-dd HH:mm}"
            };
            infoPanel.Children.Add(detailText);

            Grid.SetColumn(infoPanel, 1);
            grid.Children.Add(infoPanel);

            // 箭头
            var arrow = new PackIcon
            {
                Kind = PackIconKind.ChevronRight,
                Width = 24,
                Height = 24,
                Foreground = (Brush)Application.Current.TryFindResource("TextSecondaryBrush")
                    ?? new SolidColorBrush(Color.FromRgb(153, 153, 153)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(arrow, 2);
            grid.Children.Add(arrow);

            border.Child = grid;
            button.Content = border;

            return button;
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
    }
}
