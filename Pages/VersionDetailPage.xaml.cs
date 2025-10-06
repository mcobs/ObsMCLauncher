using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ObsMCLauncher.Models;
using ObsMCLauncher.Services;

namespace ObsMCLauncher.Pages
{
    public partial class VersionDetailPage : Page
    {
        private string currentVersion;
        private MinecraftVersion? versionInfo;
        private CancellationTokenSource? _downloadCancellationToken;
        private bool _isUpdatingVersionName = false; // 防止循环更新

        public VersionDetailPage(MinecraftVersion version)
        {
            InitializeComponent();
            versionInfo = version;
            currentVersion = version.Id;
            
            // 设置版本标题
            VersionTitle.Text = $"Minecraft {version.Id}";
            VersionNumber.Text = version.Id;
            DownloadVersionText.Text = $"Minecraft {version.Id}";
            
            // 填充版本信息
            FillVersionInfo();
            
            // 初始化版本名称
            UpdateVersionName();
            
            // 更新选中的加载器显示
            UpdateSelectedLoaderText();
            
            // 显示下载提示（如果启用了完整下载）
            UpdateDownloadAssetsHint();
        }
        
        /// <summary>
        /// 填充版本信息
        /// </summary>
        private void FillVersionInfo()
        {
            if (versionInfo == null) return;
            
            // 发布日期
            var releaseDate = this.FindName("ReleaseDate") as TextBlock;
            if (releaseDate != null)
            {
                releaseDate.Text = versionInfo.ReleaseTime.ToString("yyyy-MM-dd");
            }
            
            // 版本类型
            var typeText = versionInfo.Type == "release" ? "正式版" :
                          versionInfo.Type == "snapshot" ? "快照版" :
                          versionInfo.Type == "old_alpha" ? "远古Alpha" :
                          versionInfo.Type == "old_beta" ? "远古Beta" : "其他";
            
            var typeBadge = this.FindName("VersionTypeBadge") as TextBlock;
            if (typeBadge != null)
            {
                typeBadge.Text = typeText;
            }
            
            var typeBorder = this.FindName("VersionTypeBorder") as Border;
            if (typeBorder != null)
            {
                var typeColor = versionInfo.Type == "release" ? "#22C55E" :
                               versionInfo.Type == "snapshot" ? "#3B82F6" : 
                               "#F59E0B";
                typeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(typeColor));
            }
            
            // 下载大小 - 将在后台异步获取
            GetDownloadSizeAsync();
        }
        
        /// <summary>
        /// 异步获取下载大小
        /// </summary>
        private async void GetDownloadSizeAsync()
        {
            try
            {
                var versionJson = await MinecraftVersionService.GetVersionJsonAsync(currentVersion);
                if (!string.IsNullOrEmpty(versionJson))
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var versionDetail = JsonSerializer.Deserialize<VersionDetail>(versionJson, options);
                    
                    if (versionDetail != null)
                    {
                        long totalSize = 0;
                        
                        // 1. 客户端 JAR
                        if (versionDetail.Downloads?.Client?.Size > 0)
                        {
                            totalSize += versionDetail.Downloads.Client.Size;
                        }
                        
                        // 2. 库文件
                        if (versionDetail.Libraries != null)
                        {
                            foreach (var library in versionDetail.Libraries)
                            {
                                // 检查是否允许下载该库
                                if (IsLibraryAllowedForSize(library) && 
                                    library.Downloads?.Artifact?.Size > 0)
                                {
                                    totalSize += library.Downloads.Artifact.Size;
                                }
                            }
                        }
                        
                        // 3. 资源索引文件（很小，但也算上）
                        if (versionDetail.AssetIndex?.Size > 0)
                        {
                            totalSize += versionDetail.AssetIndex.Size;
                        }
                        
                        var sizeInMB = totalSize / 1024.0 / 1024.0;
                        var downloadSize = this.FindName("DownloadSize") as TextBlock;
                        if (downloadSize != null)
                        {
                            if (sizeInMB < 1)
                            {
                                downloadSize.Text = $"约 {totalSize / 1024.0:F0} KB";
                            }
                            else
                            {
                                downloadSize.Text = $"约 {sizeInMB:F1} MB";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取下载大小失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 检查库是否允许下载（用于计算大小）
        /// </summary>
        private bool IsLibraryAllowedForSize(Library library)
        {
            if (library.Rules == null || library.Rules.Count == 0)
                return true;

            foreach (var rule in library.Rules)
            {
                if (rule.Action == "allow")
                {
                    if (rule.Os?.Name == "windows" || rule.Os == null)
                        return true;
                }
                else if (rule.Action == "disallow")
                {
                    if (rule.Os?.Name == "windows")
                        return false;
                }
            }

            return true;
        }
        
        // 简化的版本详情类，用于获取下载大小
        private class VersionDetail
        {
            public DownloadsInfo? Downloads { get; set; }
            public List<Library>? Libraries { get; set; }
            public AssetIndexInfo? AssetIndex { get; set; }
        }
        
        private class DownloadsInfo
        {
            public DownloadItem? Client { get; set; }
        }
        
        private class DownloadItem
        {
            public long Size { get; set; }
        }
        
        private class Library
        {
            public LibraryDownloads? Downloads { get; set; }
            public List<Rule>? Rules { get; set; }
        }
        
        private class LibraryDownloads
        {
            public Artifact? Artifact { get; set; }
        }
        
        private class Artifact
        {
            public string? Path { get; set; }
            public long Size { get; set; }
        }
        
        private class Rule
        {
            public string? Action { get; set; }
            public OsInfo? Os { get; set; }
        }
        
        private class OsInfo
        {
            public string? Name { get; set; }
        }
        
        private class AssetIndexInfo
        {
            public long Size { get; set; }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // 返回版本列表
            NavigationService?.GoBack();
        }

        private void LoaderRadio_Checked(object sender, RoutedEventArgs e)
        {
            // 禁用所有版本选择框
            if (ForgeVersionComboBox != null) ForgeVersionComboBox.IsEnabled = false;
            if (FabricVersionComboBox != null) FabricVersionComboBox.IsEnabled = false;
            if (OptiFineVersionComboBox != null) OptiFineVersionComboBox.IsEnabled = false;
            if (QuiltVersionComboBox != null) QuiltVersionComboBox.IsEnabled = false;

            // 根据选中的加载器启用对应的版本选择框
            if (sender == ForgeRadio && ForgeVersionComboBox != null)
            {
                ForgeVersionComboBox.IsEnabled = true;
            }
            else if (sender == FabricRadio && FabricVersionComboBox != null)
            {
                FabricVersionComboBox.IsEnabled = true;
            }
            else if (sender == OptiFineRadio && OptiFineVersionComboBox != null)
            {
                OptiFineVersionComboBox.IsEnabled = true;
            }
            else if (sender == QuiltRadio && QuiltVersionComboBox != null)
            {
                QuiltVersionComboBox.IsEnabled = true;
            }

            // 更新版本名称
            UpdateVersionName();
            
            // 更新选中的加载器显示
            UpdateSelectedLoaderText();
        }

        private void UpdateSelectedLoaderText()
        {
            if (SelectedLoaderText == null) return;

            if (VanillaRadio?.IsChecked == true)
            {
                SelectedLoaderText.Text = "已选择：原版";
            }
            else if (ForgeRadio?.IsChecked == true)
            {
                var version = (ForgeVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "默认";
                SelectedLoaderText.Text = $"已选择：Forge {version}";
            }
            else if (FabricRadio?.IsChecked == true)
            {
                var version = (FabricVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "默认";
                SelectedLoaderText.Text = $"已选择：Fabric {version}";
            }
            else if (OptiFineRadio?.IsChecked == true)
            {
                var version = (OptiFineVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "默认";
                SelectedLoaderText.Text = $"已选择：OptiFine {version}";
            }
            else if (QuiltRadio?.IsChecked == true)
            {
                var version = (QuiltVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "默认";
                SelectedLoaderText.Text = $"已选择：Quilt {version}";
            }
        }

        /// <summary>
        /// 更新下载资源提示的显示
        /// </summary>
        private void UpdateDownloadAssetsHint()
        {
            if (DownloadAssetsHintText == null) return;

            // 读取配置
            var config = LauncherConfig.Load();
            
            // 如果启用了完整下载，显示提示
            if (config.DownloadAssetsWithGame)
            {
                DownloadAssetsHintText.Visibility = Visibility.Visible;
            }
            else
            {
                DownloadAssetsHintText.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 更新版本名称
        /// </summary>
        private void UpdateVersionName()
        {
            if (VersionNameTextBox == null || _isUpdatingVersionName) return;

            _isUpdatingVersionName = true;

            string versionName = $"Minecraft-{currentVersion}";

            // 根据选中的加载器添加后缀
            if (ForgeRadio?.IsChecked == true)
            {
                var forgeVersion = (ForgeVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (!string.IsNullOrEmpty(forgeVersion))
                {
                    // 移除 "(推荐)" 等标记
                    forgeVersion = forgeVersion.Replace(" (推荐)", "").Trim();
                    versionName += $"-forge-{forgeVersion}";
                }
                else
                {
                    versionName += "-forge";
                }
            }
            else if (FabricRadio?.IsChecked == true)
            {
                var fabricVersion = (FabricVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (!string.IsNullOrEmpty(fabricVersion))
                {
                    fabricVersion = fabricVersion.Replace(" (推荐)", "").Trim();
                    versionName += $"-fabric-{fabricVersion}";
                }
                else
                {
                    versionName += "-fabric";
                }
            }
            else if (OptiFineRadio?.IsChecked == true)
            {
                var optifineVersion = (OptiFineVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (!string.IsNullOrEmpty(optifineVersion))
                {
                    optifineVersion = optifineVersion.Replace(" (推荐)", "").Trim();
                    versionName += $"-optifine-{optifineVersion}";
                }
                else
                {
                    versionName += "-optifine";
                }
            }
            else if (QuiltRadio?.IsChecked == true)
            {
                var quiltVersion = (QuiltVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (!string.IsNullOrEmpty(quiltVersion))
                {
                    quiltVersion = quiltVersion.Replace(" (推荐)", "").Trim();
                    versionName += $"-quilt-{quiltVersion}";
                }
                else
                {
                    versionName += "-quilt";
                }
            }

            VersionNameTextBox.Text = versionName;
            UpdateVersionNamePreview(versionName);

            _isUpdatingVersionName = false;
        }

        /// <summary>
        /// 更新版本名称预览
        /// </summary>
        private void UpdateVersionNamePreview(string name)
        {
            if (VersionNamePreview != null)
            {
                VersionNamePreview.Text = name;
            }
        }

        /// <summary>
        /// 版本名称文本框变化事件
        /// </summary>
        private void VersionNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isUpdatingVersionName && VersionNameTextBox != null)
            {
                UpdateVersionNamePreview(VersionNameTextBox.Text);
            }
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            // 确定加载器类型
            string loaderType = "原版";
            string loaderVersion = "";

            if (ForgeRadio?.IsChecked == true)
            {
                loaderType = "Forge";
                loaderVersion = (ForgeVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            }
            else if (FabricRadio?.IsChecked == true)
            {
                loaderType = "Fabric";
                loaderVersion = (FabricVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            }
            else if (OptiFineRadio?.IsChecked == true)
            {
                loaderType = "OptiFine";
                loaderVersion = (OptiFineVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            }
            else if (QuiltRadio?.IsChecked == true)
            {
                loaderType = "Quilt";
                loaderVersion = (QuiltVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            }

            // 获取自定义版本名称
            var customVersionName = VersionNameTextBox?.Text?.Trim();
            if (string.IsNullOrEmpty(customVersionName))
            {
                MessageBox.Show("请输入版本名称！", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 验证版本名称合法性（不包含非法字符）
            var invalidChars = Path.GetInvalidFileNameChars();
            if (customVersionName.Any(c => invalidChars.Contains(c)))
            {
                MessageBox.Show("版本名称包含非法字符，请修改！", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 获取游戏目录
            var config = LauncherConfig.Load();
            var gameDirectory = config.GameDirectory;

            System.Diagnostics.Debug.WriteLine($"开始下载版本 {currentVersion} (安装名称: {customVersionName}) 到目录 {gameDirectory}");

            // 显示进度面板，隐藏安装按钮和加载器选择（带动画）
            ShowDownloadPanel();
            
            // 禁用版本名称编辑框
            if (VersionNameTextBox != null)
            {
                VersionNameTextBox.IsEnabled = false;
            }

            try
            {
                _downloadCancellationToken = new CancellationTokenSource();

                // 检查是否启用了完整下载（包括 Assets）
                var enableAssetsDownload = config.DownloadAssetsWithGame;
                
                // 创建进度报告器
                var progress = new Progress<DownloadProgress>(p =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        // 如果启用了完整下载，主文件下载占 60%，否则占 100%
                        double adjustedProgress = enableAssetsDownload 
                            ? p.OverallPercentage * 0.6  // 主文件和库占 60%
                            : p.OverallPercentage;
                        
                        // 更新总体进度
                        DownloadOverallProgressBar.Value = adjustedProgress;
                        DownloadOverallPercentageText.Text = $"{adjustedProgress:F0}%";
                        DownloadOverallStatsText.Text = $"{p.CompletedFiles} / {p.TotalFiles} 个文件";
                        
                        // 更新当前文件进度
                        DownloadCurrentProgressBar.Value = p.CurrentFilePercentage;
                        DownloadCurrentPercentageText.Text = $"{p.CurrentFilePercentage:F0}%";
                        
                        // 更新详细信息
                        DownloadStatusText.Text = p.Status;
                        CurrentFileText.Text = p.CurrentFile;
                        DownloadSpeedText.Text = FormatSpeed(p.DownloadSpeed);
                        DownloadSizeText.Text = $"{FormatFileSize(p.TotalDownloadedBytes)} / {FormatFileSize(p.TotalBytes)}";
                    });
                });

                // 开始下载（目前仅支持原版）
                if (loaderType == "原版")
                {
                    var success = await DownloadService.DownloadMinecraftVersion(
                        currentVersion,
                        gameDirectory,
                        customVersionName,
                        progress,
                        _downloadCancellationToken.Token
                    );

                    if (success)
                    {
                        // 检查是否需要下载Assets资源文件
                        if (config.DownloadAssetsWithGame)
                        {
                            System.Diagnostics.Debug.WriteLine("配置已启用，开始下载Assets资源文件...");
                            
                            // Assets 阶段进度从 60% 开始，占剩余的 40%
                            const double assetsBaseProgress = 60.0;
                            const double assetsProgressRange = 40.0;
                            
                            // 更新进度显示
                            Dispatcher.Invoke(() =>
                            {
                                DownloadStatusText.Text = "正在下载游戏资源文件...";
                                CurrentFileText.Text = "Assets资源包";
                                DownloadOverallProgressBar.Value = assetsBaseProgress;
                                DownloadOverallPercentageText.Text = $"{assetsBaseProgress:F0}%";
                                DownloadCurrentProgressBar.Value = 0;
                                DownloadCurrentPercentageText.Text = "0%";
                            });

                            var assetsResult = await AssetsDownloadService.DownloadAndCheckAssetsAsync(
                                gameDirectory,
                                customVersionName,
                                (current, total, message, speed) =>
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        // Assets 进度映射到 60%-100% 范围
                                        double assetsProgress = assetsBaseProgress + (current * assetsProgressRange / 100.0);
                                        
                                        DownloadStatusText.Text = "下载游戏资源";
                                        CurrentFileText.Text = message;
                                        DownloadOverallProgressBar.Value = assetsProgress;
                                        DownloadOverallPercentageText.Text = $"{assetsProgress:F0}%";
                                        DownloadCurrentProgressBar.Value = current;
                                        DownloadCurrentPercentageText.Text = $"{current:F0}%";
                                        DownloadSpeedText.Text = FormatSpeed(speed);
                                    });
                                },
                                _downloadCancellationToken.Token
                            );

                            if (!assetsResult.Success)
                            {
                                System.Diagnostics.Debug.WriteLine($"⚠️ Assets资源下载完成，但有 {assetsResult.FailedAssets} 个文件失败");
                                
                                if (assetsResult.FailedAssets > 0)
                                {
                                    MessageBox.Show(
                                        $"游戏主体已安装完成，但有 {assetsResult.FailedAssets} 个资源文件下载失败。\n\n游戏可能缺少部分资源（如声音、语言文件等）。\n\n建议稍后在启动游戏时重新下载，或更换下载源。",
                                        "资源下载部分失败",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Warning
                                    );
                                }
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("✅ Assets资源下载完成");
                            }
                            
                            // Assets 下载完成，确保进度条到达 100%
                            Dispatcher.Invoke(() =>
                            {
                                DownloadOverallProgressBar.Value = 100;
                                DownloadOverallPercentageText.Text = "100%";
                                DownloadStatusText.Text = "下载完成";
                            });
                        }
                        else
                        {
                            // 如果没有下载 Assets，确保进度条到达 100%
                            Dispatcher.Invoke(() =>
                            {
                                DownloadOverallProgressBar.Value = 100;
                                DownloadOverallPercentageText.Text = "100%";
                                DownloadStatusText.Text = "下载完成";
                            });
                        }

                        MessageBox.Show(
                            $"Minecraft {currentVersion} 安装完成！\n\n版本已安装为: {customVersionName}",
                            "安装成功",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );

                        // 返回版本列表
                        NavigationService?.GoBack();
                    }
                    else
                    {
                        MessageBox.Show(
                            "版本下载失败，请查看日志了解详细信息。",
                            "安装失败",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    }
                }
                else
                {
                    MessageBox.Show(
                        $"{loaderType} 加载器的安装功能即将推出！",
                        "功能开发中",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"下载已被用户取消");
                
                // 删除已下载的文件夹
                try
                {
                    var versionDir = Path.Combine(gameDirectory, "versions", customVersionName);
                    
                    if (Directory.Exists(versionDir))
                    {
                        System.Diagnostics.Debug.WriteLine($"正在删除已下载的文件夹: {versionDir}");
                        Directory.Delete(versionDir, true); // 递归删除
                        System.Diagnostics.Debug.WriteLine($"✅ 已删除文件夹: {versionDir}");
                    }
                }
                catch (Exception deleteEx)
                {
                    System.Diagnostics.Debug.WriteLine($"删除文件夹失败: {deleteEx.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"下载出错: {ex.Message}");
                MessageBox.Show(
                    $"下载过程中发生错误：\n{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                // 恢复界面
                HideDownloadPanel();
                
                // 启用版本名称编辑框
                if (VersionNameTextBox != null)
                {
                    VersionNameTextBox.IsEnabled = true;
                }
                
                // 恢复取消按钮状态
                if (CancelDownloadButton != null)
                {
                    CancelDownloadButton.IsEnabled = true;
                    CancelDownloadButton.Content = "取消下载";
                }
                
                _downloadCancellationToken?.Dispose();
                _downloadCancellationToken = null;
            }
        }

        /// <summary>
        /// 显示下载面板（带动画）
        /// </summary>
        private void ShowDownloadPanel()
        {
            // 隐藏安装按钮和提示文本
            InstallButton.Visibility = Visibility.Collapsed;
            SelectedLoaderText.Visibility = Visibility.Collapsed;
            InstallHintText.Visibility = Visibility.Collapsed;

            // 创建淡出动画隐藏加载器选择面板
            var fadeOutAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            fadeOutAnimation.Completed += (s, e) =>
            {
                LoaderSelectionPanel.Visibility = Visibility.Collapsed;
                
                // 显示下载进度面板
                DownloadProgressPanel.Visibility = Visibility.Visible;
                DownloadProgressPanel.Opacity = 0;
                
                // 创建淡入动画显示下载进度面板
                var fadeInAnimation = new DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };
                
                DownloadProgressPanel.BeginAnimation(OpacityProperty, fadeInAnimation);
            };

            LoaderSelectionPanel.BeginAnimation(OpacityProperty, fadeOutAnimation);
        }

        /// <summary>
        /// 隐藏下载面板（带动画）
        /// </summary>
        private void HideDownloadPanel()
        {
            // 创建淡出动画隐藏下载进度面板
            var fadeOutAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            fadeOutAnimation.Completed += (s, e) =>
            {
                DownloadProgressPanel.Visibility = Visibility.Collapsed;
                
                // 显示加载器选择面板
                LoaderSelectionPanel.Visibility = Visibility.Visible;
                LoaderSelectionPanel.Opacity = 0;
                
                // 创建淡入动画显示加载器选择面板
                var fadeInAnimation = new DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };
                
                LoaderSelectionPanel.BeginAnimation(OpacityProperty, fadeInAnimation);
                
                // 恢复按钮和提示文本
                InstallButton.Visibility = Visibility.Visible;
                SelectedLoaderText.Visibility = Visibility.Visible;
                InstallHintText.Visibility = Visibility.Visible;
            };

            DownloadProgressPanel.BeginAnimation(OpacityProperty, fadeOutAnimation);
        }

        /// <summary>
        /// 格式化文件大小
        /// </summary>
        private string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "0 B";
            
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            
            return $"{size:F2} {sizes[order]}";
        }

        /// <summary>
        /// 格式化下载速度
        /// </summary>
        private string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond == 0) return "0 B/s";
            
            string[] sizes = { "B/s", "KB/s", "MB/s", "GB/s" };
            int order = 0;
            double speed = bytesPerSecond;
            
            while (speed >= 1024 && order < sizes.Length - 1)
            {
                order++;
                speed /= 1024;
            }
            
            return $"{speed:F2} {sizes[order]}";
        }
        
        /// <summary>
        /// 取消下载按钮点击事件
        /// </summary>
        private void CancelDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[VersionDetailPage] 用户点击了取消下载按钮");
                
                // 触发取消令牌
                _downloadCancellationToken?.Cancel();
                
                // 禁用取消按钮，防止重复点击
                if (CancelDownloadButton != null)
                {
                    CancelDownloadButton.IsEnabled = false;
                    CancelDownloadButton.Content = "正在取消...";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VersionDetailPage] 取消下载时出错: {ex.Message}");
            }
        }
    }
}

