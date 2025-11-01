using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using ObsMCLauncher.Models;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Pages
{
    public partial class ResourcesPage : Page
    {
        private ResourceSource _currentSource = ResourceSource.CurseForge;
        private string _currentResourceType = "Mods";
        private int _currentPage = 0;
        private const int PAGE_SIZE = 20;
        private string? _selectedVersionId = null;

        public ResourcesPage()
        {
            InitializeComponent();
            
            // 在 Loaded 事件中初始化 UI 状态
            Loaded += ResourcesPage_Loaded;
        }

        private void ResourcesPage_Loaded(object sender, RoutedEventArgs e)
        {
            // 加载已安装的版本列表
            LoadInstalledVersions();
            
            // 页面加载完成后显示空状态
            ShowEmptyState();
        }

        /// <summary>
        /// 加载已安装的版本列表
        /// </summary>
        private void LoadInstalledVersions()
        {
            try
            {
                var config = LauncherConfig.Load();
                var versionsDir = Path.Combine(config.GameDirectory, "versions");
                
                if (!Directory.Exists(versionsDir))
                {
                    InstalledVersionComboBox.Items.Add(new ComboBoxItem 
                    { 
                        Content = "未找到已安装的版本", 
                        IsEnabled = false 
                    });
                    InstalledVersionComboBox.SelectedIndex = 0;
                    return;
                }
                
                var versionDirs = Directory.GetDirectories(versionsDir);
                var versionInfos = new List<(string name, string type)>();
                
                foreach (var versionDir in versionDirs)
                {
                    var versionName = Path.GetFileName(versionDir);
                    var versionJsonPath = Path.Combine(versionDir, $"{versionName}.json");
                    
                    if (File.Exists(versionJsonPath))
                    {
                        // 检测版本类型
                        var versionType = DetectVersionType(versionName);
                        versionInfos.Add((versionName, versionType));
                    }
                }
                
                // 排序：先按类型（支持mod的在前），再按版本名
                versionInfos = versionInfos
                    .OrderBy(v => v.type == "vanilla" || v.type == "optifine" ? 1 : 0)
                    .ThenBy(v => v.name)
                    .ToList();
                
                foreach (var (name, type) in versionInfos)
                {
                    var displayText = type switch
                    {
                        "vanilla" => $"{name} (原版 - 不支持mod)",
                        "optifine" => $"{name} (OptiFine - 不支持mod)",
                        "forge" => $"{name} (Forge)",
                        "neoforge" => $"{name} (NeoForge)",
                        "fabric" => $"{name} (Fabric)",
                        "quilt" => $"{name} (Quilt)",
                        _ => name
                    };
                    
                    var item = new ComboBoxItem 
                    { 
                        Content = displayText, 
                        Tag = name,
                        IsEnabled = type != "vanilla" && type != "optifine" // 禁用原版和OptiFine
                    };
                    
                    InstalledVersionComboBox.Items.Add(item);
                }
                
                // 选择第一个支持mod的版本
                for (int i = 0; i < InstalledVersionComboBox.Items.Count; i++)
                {
                    if (InstalledVersionComboBox.Items[i] is ComboBoxItem item && item.IsEnabled)
                    {
                        InstalledVersionComboBox.SelectedIndex = i;
                        break;
                    }
                }
                
                if (InstalledVersionComboBox.SelectedIndex == -1 && InstalledVersionComboBox.Items.Count > 0)
                {
                    InstalledVersionComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ResourcesPage] 加载版本列表失败: {ex.Message}");
                InstalledVersionComboBox.Items.Add(new ComboBoxItem 
                { 
                    Content = "加载失败", 
                    IsEnabled = false 
                });
                InstalledVersionComboBox.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// 检测版本类型
        /// </summary>
        private string DetectVersionType(string versionName)
        {
            var lowerName = versionName.ToLower();
            
            if (lowerName.Contains("forge") && !lowerName.Contains("neoforge"))
                return "forge";
            if (lowerName.Contains("neoforge"))
                return "neoforge";
            if (lowerName.Contains("fabric"))
                return "fabric";
            if (lowerName.Contains("quilt"))
                return "quilt";
            if (lowerName.Contains("optifine"))
                return "optifine";
            
            // 原版检测：不包含任何加载器标识
            return "vanilla";
        }

        /// <summary>
        /// 版本选择改变事件
        /// </summary>
        private void InstalledVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (InstalledVersionComboBox.SelectedItem is ComboBoxItem item && item.Tag is string versionId)
            {
                _selectedVersionId = versionId;
                Debug.WriteLine($"[ResourcesPage] 选择的版本: {versionId}");
            }
        }

        private void ResourceTab_Click(object sender, RoutedEventArgs e)
        {
            // 防止在初始化期间触发
            if (!IsLoaded || ResourceListPanel == null) return;
            
            if (sender is RadioButton button && button.Tag is string tag)
            {
                _currentResourceType = tag;
                _currentPage = 0;
                
                // 清空当前列表
                ResourceListPanel.Children.Clear();
                ShowEmptyState();
                
                Debug.WriteLine($"[ResourcesPage] 切换到资源类型: {tag}");
                
                // TODO: 根据不同的资源类型加载内容
                // 目前只实现了 MOD 搜索
            }
        }

        private void SourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 防止在初始化期间触发
            if (!IsLoaded || SourceComboBox == null) return;
            
            if (SourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                _currentSource = tag == "Modrinth" ? ResourceSource.Modrinth : ResourceSource.CurseForge;
                _currentPage = 0;
                
                Debug.WriteLine($"[ResourcesPage] 切换下载源: {_currentSource}");
                
                // 清空当前搜索结果
                if (ResourceListPanel != null)
                {
                    ResourceListPanel.Children.Clear();
                    ShowEmptyState();
                }
            }
        }

        private void FilterChanged(object sender, SelectionChangedEventArgs e)
        {
            // 防止在初始化期间触发
            if (!IsLoaded || ResourceListPanel == null) return;
            
            // 筛选条件变化时，如果已经有搜索结果，重新搜索
            if (ResourceListPanel.Children.Count > 0)
            {
                _ = SearchModsAsync();
            }
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _ = SearchModsAsync();
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            _ = SearchModsAsync();
        }

        /// <summary>
        /// 搜索MOD
        /// </summary>
        private async System.Threading.Tasks.Task SearchModsAsync()
        {
            if (_currentSource != ResourceSource.CurseForge)
            {
                NotificationManager.Instance.ShowNotification("提示", "Modrinth功能即将推出！", NotificationType.Warning);
                return;
            }

            var searchText = SearchBox.Text?.Trim() ?? "";
            
            // 获取版本筛选
            string gameVersion = "";
            if (VersionFilterComboBox.SelectedItem is ComboBoxItem versionItem && versionItem.Tag is string version)
            {
                gameVersion = version;
            }

            // 获取排序方式
            int sortField = 2; // 默认按人气排序
            if (SortComboBox.SelectedItem is ComboBoxItem sortItem && sortItem.Tag is string sortTag)
            {
                int.TryParse(sortTag, out sortField);
            }

            Debug.WriteLine($"[ResourcesPage] 搜索MOD - 关键词: '{searchText}', 版本: '{gameVersion}', 排序: {sortField}");

            ShowLoading();

            try
            {
                var result = await CurseForgeService.SearchModsAsync(
                    searchFilter: searchText,
                    gameVersion: gameVersion,
                    categoryId: 0,
                    pageIndex: _currentPage,
                    pageSize: PAGE_SIZE,
                    sortField: sortField,
                    sortOrder: "desc"
                );

                if (result?.Data != null && result.Data.Count > 0)
                {
                    DisplayMods(result.Data);
                    Debug.WriteLine($"[ResourcesPage] ✅ 显示 {result.Data.Count} 个MOD");
                }
                else
                {
                    ShowNoResults();
                    Debug.WriteLine($"[ResourcesPage] ⚠️ 没有找到匹配的MOD");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ResourcesPage] ❌ 搜索失败: {ex.Message}");
                NotificationManager.Instance.ShowNotification("错误", $"搜索失败: {ex.Message}", NotificationType.Error);
                ShowEmptyState();
            }
        }

        /// <summary>
        /// 显示MOD列表
        /// </summary>
        private void DisplayMods(List<CurseForgeMod> mods)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ResourceListPanel.Children.Clear();
                HideLoading();
                HideEmptyState();

                foreach (var mod in mods)
                {
                    var modCard = CreateModCard(mod);
                    ResourceListPanel.Children.Add(modCard);
                }
            }));
        }

        /// <summary>
        /// 创建MOD卡片
        /// </summary>
        private Border CreateModCard(CurseForgeMod mod)
        {
            var border = new Border
            {
                Background = (Brush)Application.Current.TryFindResource("SurfaceElevatedBrush")
                    ?? new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 15)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 图标
            var iconBorder = new Border
            {
                Width = 60,
                Height = 60,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
                VerticalAlignment = VerticalAlignment.Top
            };

            if (mod.Logo != null && !string.IsNullOrEmpty(mod.Logo.ThumbnailUrl))
            {
                try
                {
                    var image = new Image
                    {
                        Source = new BitmapImage(new Uri(mod.Logo.ThumbnailUrl)),
                        Stretch = Stretch.UniformToFill
                    };
                    iconBorder.Child = image;
                }
                catch
                {
                    // 如果加载图片失败，使用默认图标
                    iconBorder.Child = CreateDefaultIcon();
                }
            }
            else
            {
                iconBorder.Child = CreateDefaultIcon();
            }

            Grid.SetColumn(iconBorder, 0);
            grid.Children.Add(iconBorder);

            // 信息区
            var infoPanel = new StackPanel
            {
                Margin = new Thickness(15, 0, 15, 0)
            };

            // 标题
            var titleText = new TextBlock
            {
                Text = mod.Name,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            infoPanel.Children.Add(titleText);

            // 描述
            var descText = new TextBlock
            {
                Text = mod.Summary,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 40,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 5, 0, 8)
            };
            infoPanel.Children.Add(descText);

            // 标签区
            var tagsPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // 作者
            if (mod.Authors.Count > 0)
            {
                var authorBorder = CreateTagBorder(mod.Authors[0].Name, "#607D8B");
                tagsPanel.Children.Add(authorBorder);
            }

            // 分类
            if (mod.Categories.Count > 0)
            {
                var categoryBorder = CreateTagBorder(mod.Categories[0].Name, "#2196F3");
                tagsPanel.Children.Add(categoryBorder);
            }

            // 下载量
            var downloadIcon = new PackIcon
            {
                Kind = PackIconKind.Download,
                Width = 14,
                Height = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 3, 0)
            };
            tagsPanel.Children.Add(downloadIcon);

            var downloadText = new TextBlock
            {
                Text = CurseForgeService.FormatDownloadCount(mod.DownloadCount),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                VerticalAlignment = VerticalAlignment.Center
            };
            tagsPanel.Children.Add(downloadText);

            infoPanel.Children.Add(tagsPanel);

            Grid.SetColumn(infoPanel, 1);
            grid.Children.Add(infoPanel);

            // 操作按钮区
            var buttonPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var downloadButton = new Button
            {
                Content = "下载",
                Style = (Style)Application.Current.TryFindResource("MaterialDesignRaisedButton"),
                Width = 100,
                Background = (Brush)Application.Current.TryFindResource("PrimaryBrush"),
                Margin = new Thickness(0, 0, 0, 8),
                Tag = mod
            };
            downloadButton.Click += DownloadModButton_Click;
            buttonPanel.Children.Add(downloadButton);

            var detailButton = new Button
            {
                Content = "详情",
                Style = (Style)Application.Current.TryFindResource("MaterialDesignOutlinedButton"),
                Width = 100,
                Tag = mod
            };
            detailButton.Click += ModDetailButton_Click;
            buttonPanel.Children.Add(detailButton);

            Grid.SetColumn(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            border.Child = grid;
            return border;
        }

        /// <summary>
        /// 创建默认图标
        /// </summary>
        private UIElement CreateDefaultIcon()
        {
            return new PackIcon
            {
                Kind = PackIconKind.Cube,
                Width = 35,
                Height = 35,
                Foreground = (Brush)Application.Current.TryFindResource("PrimaryBrush")
                    ?? new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>
        /// 创建标签边框
        /// </summary>
        private Border CreateTagBorder(string text, string colorHex)
        {
            return new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 8, 0),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 11
                }
            };
        }

        /// <summary>
        /// 下载MOD按钮点击
        /// </summary>
        private async void DownloadModButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is CurseForgeMod mod)
            {
                Debug.WriteLine($"[ResourcesPage] 准备下载MOD: {mod.Name}");
                
                // 检查是否选择了版本
                if (string.IsNullOrEmpty(_selectedVersionId))
                {
                    NotificationManager.Instance.ShowNotification("提示", "请先选择要安装到的游戏版本", NotificationType.Warning);
                    return;
                }
                
                // 检查选择的版本是否支持mod
                var versionType = DetectVersionType(_selectedVersionId);
                if (versionType == "vanilla" || versionType == "optifine")
                {
                    NotificationManager.Instance.ShowNotification("提示", "所选版本不支持安装MOD", NotificationType.Warning);
                    return;
                }
                
                // 获取MOD的文件列表
                var filesResult = await CurseForgeService.GetModFilesAsync(mod.Id);
                
                if (filesResult?.Data != null && filesResult.Data.Count > 0)
                {
                    // 选择最新的文件
                    var latestFile = filesResult.Data.OrderByDescending(f => f.FileDate).First();
                    
                    // 根据版本隔离设置获取mods文件夹路径
                    var config = LauncherConfig.Load();
                    var modsFolder = config.GetModsDirectory(_selectedVersionId);
                    
                    Debug.WriteLine($"[ResourcesPage] MOD下载路径: {modsFolder}");
                    Debug.WriteLine($"[ResourcesPage] 版本隔离模式: {config.GameDirectoryType}");
                    
                    if (!Directory.Exists(modsFolder))
                    {
                        Directory.CreateDirectory(modsFolder);
                    }
                    
                    var savePath = Path.Combine(modsFolder, latestFile.FileName);
                    
                    NotificationManager.Instance.ShowNotification("下载", $"开始下载: {mod.Name}", NotificationType.Info);
                    button.IsEnabled = false;
                    button.Content = "下载中...";
                    
                    var progress = new Progress<int>(percent =>
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            button.Content = $"下载中 {percent}%";
                        }));
                    });
                    
                    var success = await CurseForgeService.DownloadModFileAsync(latestFile, savePath, progress);
                    
                    if (success)
                    {
                        NotificationManager.Instance.ShowNotification("完成", $"下载完成: {mod.Name}", NotificationType.Success);
                        button.Content = "已下载";
                    }
                    else
                    {
                        NotificationManager.Instance.ShowNotification("错误", $"下载失败: {mod.Name}", NotificationType.Error);
                        button.Content = "下载";
                        button.IsEnabled = true;
                    }
                }
                else
                {
                    NotificationManager.Instance.ShowNotification("警告", "无法获取下载文件", NotificationType.Warning);
                }
            }
        }

        /// <summary>
        /// MOD详情按钮点击
        /// </summary>
        private void ModDetailButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is CurseForgeMod mod)
            {
                Debug.WriteLine($"[ResourcesPage] 查看MOD详情: {mod.Name}");
                
                // 打开MOD的网页
                if (mod.Links != null && !string.IsNullOrEmpty(mod.Links.WebsiteUrl))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = mod.Links.WebsiteUrl,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ResourcesPage] 打开链接失败: {ex.Message}");
                        NotificationManager.Instance.ShowNotification("错误", "无法打开链接", NotificationType.Error);
                    }
                }
            }
        }

        /// <summary>
        /// 显示加载状态
        /// </summary>
        private void ShowLoading()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LoadingIndicator.Visibility = Visibility.Visible;
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                ResourceScrollViewer.Visibility = Visibility.Collapsed;
            }));
        }

        /// <summary>
        /// 隐藏加载状态
        /// </summary>
        private void HideLoading()
        {
            LoadingIndicator.Visibility = Visibility.Collapsed;
            ResourceScrollViewer.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 显示空状态
        /// </summary>
        private void ShowEmptyState()
        {
            if (LoadingIndicator != null)
                LoadingIndicator.Visibility = Visibility.Collapsed;
            
            if (EmptyStatePanel != null)
                EmptyStatePanel.Visibility = Visibility.Visible;
            
            if (ResourceScrollViewer != null)
                ResourceScrollViewer.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 隐藏空状态
        /// </summary>
        private void HideEmptyState()
        {
            if (EmptyStatePanel != null)
                EmptyStatePanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 显示无结果状态
        /// </summary>
        private void ShowNoResults()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                HideLoading();
                ResourceListPanel.Children.Clear();
                
                var noResultsText = new TextBlock
                {
                    Text = "没有找到匹配的资源，请尝试其他关键词",
                    FontSize = 16,
                    Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 50, 0, 0)
                };
                ResourceListPanel.Children.Add(noResultsText);
            }));
        }
    }
}

