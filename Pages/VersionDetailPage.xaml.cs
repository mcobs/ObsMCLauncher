using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ObsMCLauncher.Models;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Pages
{
    public partial class VersionDetailPage : Page
    {
        private string currentVersion;
        private MinecraftVersion? versionInfo;
        private CancellationTokenSource? _downloadCancellationToken;
        private bool _isUpdatingVersionName = false; // 防止循环更新
        private string? _currentDownloadTaskId; // 当前下载任务ID
        
        // 返回回调，由父页面设置
        public Action? OnBackRequested { get; set; }

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
            
            // 异步加载Forge版本列表
            _ = LoadForgeVersionsAsync();
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
            // 如果设置了返回回调，使用回调（由父页面控制）
            if (OnBackRequested != null)
            {
                OnBackRequested.Invoke();
            }
            else
            {
                // 否则使用默认导航返回
            NavigationService?.GoBack();
            }
        }

        /// <summary>
        /// 加载Forge版本列表
        /// </summary>
        private async Task LoadForgeVersionsAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[VersionDetailPage] 检查Forge支持: {currentVersion}");

                // 检查Forge是否支持当前MC版本
                var supportedVersions = await ForgeService.GetSupportedMinecraftVersionsAsync();
                
                if (!supportedVersions.Contains(currentVersion))
                {
                    System.Diagnostics.Debug.WriteLine($"[VersionDetailPage] Forge不支持版本 {currentVersion}");
                    
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (ForgeRadio != null)
                        {
                            ForgeRadio.IsEnabled = false;
                            ForgeRadio.ToolTip = $"Forge暂不支持 Minecraft {currentVersion}";
                        }
                        if (ForgeVersionComboBox != null)
                        {
                            ForgeVersionComboBox.Items.Clear();
                            var item = new ComboBoxItem { Content = "不支持此版本", IsEnabled = false };
                            ForgeVersionComboBox.Items.Add(item);
                            ForgeVersionComboBox.SelectedIndex = 0;
                        }
                    }));
                    return;
                }

                // 获取Forge版本列表
                System.Diagnostics.Debug.WriteLine($"[VersionDetailPage] 获取Forge版本列表...");
                var forgeVersions = await ForgeService.GetForgeVersionsAsync(currentVersion);

                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ForgeVersionComboBox != null)
                    {
                        ForgeVersionComboBox.Items.Clear();

                        if (forgeVersions.Count == 0)
                        {
                            var item = new ComboBoxItem { Content = "暂无可用版本", IsEnabled = false };
                            ForgeVersionComboBox.Items.Add(item);
                            ForgeVersionComboBox.SelectedIndex = 0;
                            
                            if (ForgeRadio != null)
                            {
                                ForgeRadio.IsEnabled = false;
                                ForgeRadio.ToolTip = "暂无可用的Forge版本";
                            }
                        }
                        else
                        {
                            // 添加Forge版本到下拉列表
                            foreach (var version in forgeVersions)
                            {
                                var item = new ComboBoxItem 
                                { 
                                    Content = version.Version,
                                    Tag = version,
                                    ToolTip = $"Build {version.Build} - {version.Modified}"
                                };
                                ForgeVersionComboBox.Items.Add(item);
                            }

                            // 默认选择第一个（最新版本）
                            ForgeVersionComboBox.SelectedIndex = 0;

                            // 启用Forge选项
                            if (ForgeRadio != null)
                            {
                                ForgeRadio.IsEnabled = true;
                                ForgeRadio.ToolTip = $"Forge for Minecraft {currentVersion} ({forgeVersions.Count} 个版本可用)";
                            }

                            System.Diagnostics.Debug.WriteLine($"[VersionDetailPage] 加载了 {forgeVersions.Count} 个Forge版本");
                        }
                    }
                }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VersionDetailPage] 加载Forge版本失败: {ex.Message}");
                
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ForgeRadio != null)
                    {
                        ForgeRadio.IsEnabled = false;
                        ForgeRadio.ToolTip = "加载Forge版本列表失败";
                    }
                    if (ForgeVersionComboBox != null)
                    {
                        ForgeVersionComboBox.Items.Clear();
                        var item = new ComboBoxItem { Content = "加载失败", IsEnabled = false };
                        ForgeVersionComboBox.Items.Add(item);
                        ForgeVersionComboBox.SelectedIndex = 0;
                    }
                }));
            }
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

        /// <summary>
        /// 加载器版本下拉框选择改变事件
        /// </summary>
        private void LoaderVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
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
                
                // 创建进度报告器，同时更新UI和下载管理器
                var progress = new Progress<DownloadProgress>(p =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // 如果启用了完整下载，主文件下载占 60%，否则占 100%
                        double adjustedProgress = enableAssetsDownload 
                            ? p.OverallPercentage * 0.6  // 主文件和库占 60%
                            : p.OverallPercentage;
                        
                        // 更新下载管理器任务进度
                        if (_currentDownloadTaskId != null)
                        {
                            DownloadTaskManager.Instance.UpdateTaskProgress(
                                _currentDownloadTaskId,
                                adjustedProgress,
                                p.CurrentFile,
                                p.DownloadSpeed
                            );
                        }
                        
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
                    }), System.Windows.Threading.DispatcherPriority.Normal);
                });

                // 创建下载任务并添加到管理器
                var versionName = string.IsNullOrWhiteSpace(customVersionName) ? currentVersion : customVersionName;
                var taskName = loaderType == "原版" 
                    ? $"Minecraft {versionName}"
                    : $"{loaderType} {loaderVersion} ({currentVersion})";
                    
                var downloadTask = DownloadTaskManager.Instance.AddTask(
                    taskName,
                    DownloadTaskType.Version,
                    _downloadCancellationToken
                );
                _currentDownloadTaskId = downloadTask.Id;

                // 开始下载
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
                            
                            // 更新进度显示（异步）
                            _ = Dispatcher.BeginInvoke(new Action(() =>
                            {
                                DownloadStatusText.Text = "正在下载游戏资源文件...";
                                CurrentFileText.Text = "Assets资源包";
                                DownloadOverallProgressBar.Value = assetsBaseProgress;
                                DownloadOverallPercentageText.Text = $"{assetsBaseProgress:F0}%";
                                DownloadCurrentProgressBar.Value = 0;
                                DownloadCurrentPercentageText.Text = "0%";
                            }));

                            var assetsResult = await AssetsDownloadService.DownloadAndCheckAssetsAsync(
                                gameDirectory,
                                customVersionName,
                                (current, total, message, speed) =>
                                {
                                    // 异步更新UI
                                    Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        // Assets 进度映射到 60%-100% 范围
                                        double assetsProgress = assetsBaseProgress + (current * assetsProgressRange / 100.0);
                                        
                                        // 更新下载管理器任务
                                        if (_currentDownloadTaskId != null)
                                        {
                                            DownloadTaskManager.Instance.UpdateTaskProgress(
                                                _currentDownloadTaskId,
                                                assetsProgress,
                                                message,
                                                speed
                                            );
                                        }
                                        
                                        DownloadStatusText.Text = "下载游戏资源";
                                        CurrentFileText.Text = message;
                                        DownloadOverallProgressBar.Value = assetsProgress;
                                        DownloadOverallPercentageText.Text = $"{assetsProgress:F0}%";
                                        DownloadCurrentProgressBar.Value = current;
                                        DownloadCurrentPercentageText.Text = $"{current:F0}%";
                                        DownloadSpeedText.Text = FormatSpeed(speed);
                                    }), System.Windows.Threading.DispatcherPriority.Background);
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
                            _ = Dispatcher.BeginInvoke(new Action(() =>
                            {
                                DownloadOverallProgressBar.Value = 100;
                                DownloadOverallPercentageText.Text = "100%";
                                DownloadStatusText.Text = "下载完成";
                            }));
                        }
                        else
                        {
                            // 如果没有下载 Assets，确保进度条到达 100%
                            _ = Dispatcher.BeginInvoke(new Action(() =>
                            {
                                DownloadOverallProgressBar.Value = 100;
                                DownloadOverallPercentageText.Text = "100%";
                                DownloadStatusText.Text = "下载完成";
                            }));
                        }

                        // 标记任务完成
                        if (_currentDownloadTaskId != null)
                        {
                            DownloadTaskManager.Instance.CompleteTask(_currentDownloadTaskId);
                            _currentDownloadTaskId = null;
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
                else if (loaderType == "Forge")
                {
                    // Forge安装流程
                    await InstallForgeAsync(loaderVersion, customVersionName, gameDirectory, config, progress);
                }
                else
                {
                    // 其他加载器暂不支持
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
                
                // 标记任务已取消 (由 DownloadTaskManager 的 CancelTask 自动处理)
                _currentDownloadTaskId = null;
                
                // 在后台异步删除已下载的文件夹，避免阻塞UI
                var versionDirToDelete = Path.Combine(gameDirectory, "versions", customVersionName);
                _ = Task.Run(() =>
                {
                    try
                    {
                        if (Directory.Exists(versionDirToDelete))
                        {
                            System.Diagnostics.Debug.WriteLine($"正在后台删除已下载的文件夹: {versionDirToDelete}");
                            Directory.Delete(versionDirToDelete, true); // 递归删除
                            System.Diagnostics.Debug.WriteLine($"✅ 已删除文件夹: {versionDirToDelete}");
                            
                            // 删除完成后在UI线程显示通知
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                NotificationManager.Instance.ShowNotification(
                                    "下载已取消",
                                    "已删除未完成的下载文件",
                                    NotificationType.Info,
                                    3
                                );
                            }));
                        }
                    }
                    catch (Exception deleteEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"删除文件夹失败: {deleteEx.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"下载出错: {ex.Message}");
                
                // 标记任务失败
                if (_currentDownloadTaskId != null)
                {
                    DownloadTaskManager.Instance.FailTask(_currentDownloadTaskId, ex.Message);
                    _currentDownloadTaskId = null;
                }
                
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
        /// 下载Forge依赖库
        /// </summary>
        private async Task DownloadForgeLibrariesAsync(string versionJsonPath, string gameDirectory, LauncherConfig config)
        {
            try
            {
                // 读取version.json
                var jsonContent = await File.ReadAllTextAsync(versionJsonPath);
                var jsonDoc = JsonDocument.Parse(jsonContent);
                var root = jsonDoc.RootElement;
                
                // 解析libraries数组
                if (!root.TryGetProperty("libraries", out var librariesElement))
                {
                    System.Diagnostics.Debug.WriteLine("[Forge] version.json中没有libraries字段");
                    return;
                }
                
                var libraries = new List<ForgeLibrary>();
                foreach (var libElement in librariesElement.EnumerateArray())
                {
                    var lib = new ForgeLibrary();
                    
                    if (libElement.TryGetProperty("name", out var nameElement))
                    {
                        lib.Name = nameElement.GetString();
                    }
                    
                    if (libElement.TryGetProperty("downloads", out var downloadsElement))
                    {
                        if (downloadsElement.TryGetProperty("artifact", out var artifactElement))
                        {
                            lib.Downloads = new ForgeDownloads();
                            lib.Downloads.Artifact = new ForgeArtifact();
                            
                            if (artifactElement.TryGetProperty("path", out var pathElement))
                            {
                                lib.Downloads.Artifact.Path = pathElement.GetString();
                            }
                            if (artifactElement.TryGetProperty("url", out var urlElement))
                            {
                                lib.Downloads.Artifact.Url = urlElement.GetString();
                            }
                        }
                    }
                    
                    libraries.Add(lib);
                }
                
                if (libraries.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[Forge] 没有库文件需要下载");
                    return;
                }
                
                // 过滤需要下载的Forge库
                var forgeLibs = libraries.Where(lib => 
                    lib.Name != null && (
                        lib.Name.Contains("net.minecraftforge") ||
                        lib.Name.Contains("org.ow2.asm") ||
                        lib.Name.Contains("de.oceanlabs.mcp") ||
                        lib.Name.Contains("org.openjdk.nashorn") ||
                        lib.Name.Contains("com.electronwill") ||
                        lib.Name.Contains("org.apache.maven") ||
                        lib.Name.Contains("net.minecrell") ||
                        lib.Name.Contains("org.jline") ||
                        lib.Name.Contains("org.spongepowered") ||
                        lib.Name.Contains("org.jspecify")
                    )).ToList();

                var librariesDir = Path.Combine(gameDirectory, "libraries");
                Directory.CreateDirectory(librariesDir);

                if (forgeLibs.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[Forge] 没有Forge库文件需要下载");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[Forge] 检测到 {forgeLibs.Count} 个Forge库文件");

                var downloadService = DownloadSourceManager.Instance.CurrentService;
                var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                
                int successCount = 0;
                int skipCount = 0;
                int failedCount = 0;

                for (int i = 0; i < forgeLibs.Count; i++)
                {
                    var lib = forgeLibs[i];
                    
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        CurrentFileText.Text = $"[{i + 1}/{forgeLibs.Count}] {lib.Name}";
                        var libraryProgress = 70 + ((double)(i + 1) / forgeLibs.Count * 8);
                        DownloadOverallProgressBar.Value = libraryProgress;
                        DownloadOverallPercentageText.Text = $"{libraryProgress:F0}%";
                        
                        if (_currentDownloadTaskId != null)
                        {
                            DownloadTaskManager.Instance.UpdateTaskProgress(
                                _currentDownloadTaskId,
                                libraryProgress,
                                $"下载Forge库 ({i + 1}/{forgeLibs.Count}): {lib.Name}",
                                0
                            );
                        }
                    }));

                    try
                    {
                        string? downloadUrl = null;
                        string? savePath = null;

                        // 尝试从Downloads.Artifact获取URL
                        if (lib.Downloads?.Artifact != null)
                        {
                            if (!string.IsNullOrEmpty(lib.Downloads.Artifact.Path))
                            {
                                downloadUrl = downloadService.GetLibraryUrl(lib.Downloads.Artifact.Path);
                                savePath = Path.Combine(librariesDir, lib.Downloads.Artifact.Path.Replace("/", "\\"));
                            }
                            else if (!string.IsNullOrEmpty(lib.Downloads.Artifact.Url))
                            {
                                downloadUrl = lib.Downloads.Artifact.Url;
                                savePath = Path.Combine(librariesDir, lib.Downloads.Artifact.Path?.Replace("/", "\\") ?? "");
                            }
                        }

                        // 如果没有Downloads信息，尝试从Name构建（使用Maven格式）
                        if (string.IsNullOrEmpty(downloadUrl) && !string.IsNullOrEmpty(lib.Name))
                        {
                            var mavenPath = ForgeService.MavenToPath(lib.Name);
                            if (!string.IsNullOrEmpty(mavenPath))
                            {
                                downloadUrl = downloadService.GetLibraryUrl(mavenPath);
                                savePath = Path.Combine(librariesDir, mavenPath.Replace("/", "\\"));
                            }
                        }

                        // 跳过无法构建URL或路径的库
                        if (string.IsNullOrEmpty(downloadUrl) || string.IsNullOrEmpty(savePath))
                        {
                            System.Diagnostics.Debug.WriteLine($"[Forge] ⚠️ 跳过库（无URL）: {lib.Name}");
                            skipCount++;
                            continue;
                        }

                        // 检查文件是否已存在
                        if (File.Exists(savePath))
                        {
                            System.Diagnostics.Debug.WriteLine($"[Forge] ✓ 库已存在: {lib.Name}");
                            successCount++;
                            continue;
                        }

                        // 创建目录
                        var dir = Path.GetDirectoryName(savePath);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        // 下载文件
                        System.Diagnostics.Debug.WriteLine($"[Forge] 📥 下载: {lib.Name}");
                        System.Diagnostics.Debug.WriteLine($"[Forge]    URL: {downloadUrl}");
                        
                        var response = await httpClient.GetAsync(downloadUrl, _downloadCancellationToken!.Token);
                        
                        // 对于404错误且是特定的Forge库，跳过（这些库可能从JAR中提取或不需要）
                        if (!response.IsSuccessStatusCode && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            if (lib.Name != null && (lib.Name.Contains(":client") || lib.Name.Contains(":server")))
                            {
                                System.Diagnostics.Debug.WriteLine($"[Forge] ⚠️ 跳过库（不存在，但可忽略）: {lib.Name}");
                                skipCount++;
                                continue;
                            }
                        }
                        
                        response.EnsureSuccessStatusCode();
                        var fileBytes = await response.Content.ReadAsByteArrayAsync();
                        await File.WriteAllBytesAsync(savePath, fileBytes);
                        
                        System.Diagnostics.Debug.WriteLine($"[Forge] ✓ 下载完成: {lib.Name} ({fileBytes.Length} bytes)");
                        successCount++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // 对于特定的Forge库下载失败，记录但不中断安装
                        if (lib.Name != null && (lib.Name.Contains(":client") || lib.Name.Contains(":server")))
                        {
                            System.Diagnostics.Debug.WriteLine($"[Forge] ⚠️ 下载失败但继续: {lib.Name} - {ex.Message}");
                            skipCount++;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[Forge] ❌ 下载失败: {lib.Name} - {ex.Message}");
                            failedCount++;
                        }
                    }
                }

                httpClient.Dispose();

                System.Diagnostics.Debug.WriteLine($"[Forge] 库文件下载完成: 成功 {successCount}, 跳过 {skipCount}, 失败 {failedCount}");

                if (failedCount > 0)
                {
                    MessageBox.Show(
                        $"Forge库下载部分失败：\n成功: {successCount}\n跳过: {skipCount}\n失败: {failedCount}\n\n可能需要在启动时自动补全。",
                        "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Forge] 下载库文件时出错: {ex.Message}");
                MessageBox.Show(
                    $"下载Forge库文件时出错：\n{ex.Message}\n\n将在启动时尝试自动补全。",
                    "警告",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        // Forge库下载辅助类
        private class ForgeLibrary
        {
            public string? Name { get; set; }
            public ForgeDownloads? Downloads { get; set; }
        }
        
        private class ForgeDownloads
        {
            public ForgeArtifact? Artifact { get; set; }
        }
        
        private class ForgeArtifact
        {
            public string? Path { get; set; }
            public string? Url { get; set; }
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
        /// <summary>
        /// 安装Forge
        /// </summary>
        private async Task InstallForgeAsync(
            string forgeVersion, 
            string customVersionName, 
            string gameDirectory, 
            LauncherConfig config,
            IProgress<DownloadProgress> progress)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[Forge] 开始安装 Forge {forgeVersion} for MC {currentVersion}");

                // 1. 先下载原版Minecraft
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    DownloadStatusText.Text = "下载原版Minecraft...";
                    CurrentFileText.Text = $"Minecraft {currentVersion}";
                }));

                var vanillaSuccess = await DownloadService.DownloadMinecraftVersion(
                    currentVersion,
                    gameDirectory,
                    currentVersion, // 原版使用MC版本号作为文件夹名
                    progress,
                    _downloadCancellationToken!.Token
                );

                if (!vanillaSuccess)
                {
                    MessageBox.Show("原版Minecraft下载失败！", "安装失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 2. 下载Forge安装器
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    DownloadStatusText.Text = "下载Forge安装器...";
                    CurrentFileText.Text = $"forge-{currentVersion}-{forgeVersion}-installer.jar";
                    DownloadOverallProgressBar.Value = 50;
                    DownloadOverallPercentageText.Text = "50%";
                }));

                var forgeFullVersion = $"{currentVersion}-{forgeVersion}";
                var tempDir = Path.Combine(Path.GetTempPath(), "ObsMCLauncher_Forge");
                Directory.CreateDirectory(tempDir);
                var installerPath = Path.Combine(tempDir, $"forge-{forgeFullVersion}-installer.jar");

                var downloadProgress = new Progress<double>(p =>
                {
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        DownloadCurrentProgressBar.Value = p;
                        DownloadCurrentPercentageText.Text = $"{p:F0}%";
                    }));
                });

                var downloadSuccess = await ForgeService.DownloadForgeInstallerAsync(
                    forgeFullVersion,
                    installerPath,
                    downloadProgress
                );

                if (!downloadSuccess)
                {
                    MessageBox.Show("Forge安装器下载失败！", "安装失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 3. 解析install_profile.json
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    DownloadStatusText.Text = "解析Forge安装器...";
                    CurrentFileText.Text = "install_profile.json";
                    DownloadOverallProgressBar.Value = 60;
                    DownloadOverallPercentageText.Text = "60%";
                }));

                var installProfile = await ForgeService.ExtractInstallProfileAsync(installerPath);
                if (installProfile == null)
                {
                    MessageBox.Show("解析Forge安装器失败！", "安装失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 4. 提取version.json
                var versionJson = await ForgeService.ExtractVersionJsonAsync(installerPath, forgeFullVersion);
                if (string.IsNullOrEmpty(versionJson))
                {
                    MessageBox.Show("提取Forge version.json失败！", "安装失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 5. 创建Forge版本目录
                var forgeVersionDir = Path.Combine(gameDirectory, "versions", customVersionName);
                Directory.CreateDirectory(forgeVersionDir);

                // 6. 合并原版和Forge的version.json
                var vanillaJsonPath = Path.Combine(gameDirectory, "versions", currentVersion, $"{currentVersion}.json");
                var vanillaJsonContent = await File.ReadAllTextAsync(vanillaJsonPath);
                var vanillaDoc = JsonDocument.Parse(vanillaJsonContent);
                var vanillaRoot = vanillaDoc.RootElement;
                
                var forgeDoc = JsonDocument.Parse(versionJson);
                var forgeRoot = forgeDoc.RootElement;
                
                var modifiedJson = new Dictionary<string, object>();
                
                // 首先复制Forge的所有属性
                foreach (var property in forgeRoot.EnumerateObject())
                {
                    if (property.Name == "id")
                    {
                        modifiedJson["id"] = customVersionName;
                    }
                    else if (property.Name == "libraries")
                    {
                        // 稍后处理libraries的合并
                        continue;
                    }
                    else
                    {
                        modifiedJson[property.Name] = JsonSerializer.Deserialize<object>(property.Value.GetRawText())!;
                    }
                }
                
                // 合并libraries：Forge的libraries + 原版的libraries
                var mergedLibraries = new List<object>();
                
                // 先添加Forge的libraries
                if (forgeRoot.TryGetProperty("libraries", out var forgeLibraries))
                {
                    foreach (var lib in forgeLibraries.EnumerateArray())
                    {
                        mergedLibraries.Add(JsonSerializer.Deserialize<object>(lib.GetRawText())!);
                    }
                    System.Diagnostics.Debug.WriteLine($"[Forge] 添加了 {mergedLibraries.Count} 个Forge库");
                }
                
                // 再添加原版的libraries（去重）
                var forgeLibNames = new HashSet<string>();
                if (forgeRoot.TryGetProperty("libraries", out var forgeLibs))
                {
                    foreach (var lib in forgeLibs.EnumerateArray())
                    {
                        if (lib.TryGetProperty("name", out var name))
                        {
                            forgeLibNames.Add(name.GetString() ?? "");
                        }
                    }
                }
                
                if (vanillaRoot.TryGetProperty("libraries", out var vanillaLibraries))
                {
                    int vanillaCount = 0;
                    foreach (var lib in vanillaLibraries.EnumerateArray())
                    {
                        // 跳过已经在Forge libraries中的库
                        if (lib.TryGetProperty("name", out var libName))
                        {
                            var name = libName.GetString() ?? "";
                            if (forgeLibNames.Contains(name))
                            {
                                continue;
                            }
                        }
                        mergedLibraries.Add(JsonSerializer.Deserialize<object>(lib.GetRawText())!);
                        vanillaCount++;
                    }
                    System.Diagnostics.Debug.WriteLine($"[Forge] 添加了 {vanillaCount} 个原版库（去重后）");
                }
                
                modifiedJson["libraries"] = mergedLibraries;
                System.Diagnostics.Debug.WriteLine($"[Forge] 合并后共 {mergedLibraries.Count} 个库");
                
                // 从原版继承部分缺失的属性（如assetIndex）
                if (!modifiedJson.ContainsKey("assetIndex") && vanillaRoot.TryGetProperty("assetIndex", out var assetIndex))
                {
                    modifiedJson["assetIndex"] = JsonSerializer.Deserialize<object>(assetIndex.GetRawText())!;
                    System.Diagnostics.Debug.WriteLine($"[Forge] 从原版继承了assetIndex");
                }
                
                if (!modifiedJson.ContainsKey("assets") && vanillaRoot.TryGetProperty("assets", out var assets))
                {
                    modifiedJson["assets"] = assets.GetString()!;
                    System.Diagnostics.Debug.WriteLine($"[Forge] 从原版继承了assets: {assets.GetString()}");
                }
                
                if (!modifiedJson.ContainsKey("javaVersion") && vanillaRoot.TryGetProperty("javaVersion", out var javaVersion))
                {
                    modifiedJson["javaVersion"] = JsonSerializer.Deserialize<object>(javaVersion.GetRawText())!;
                    System.Diagnostics.Debug.WriteLine($"[Forge] 从原版继承了javaVersion");
                }

                var versionJsonPath = Path.Combine(forgeVersionDir, $"{customVersionName}.json");
                await File.WriteAllTextAsync(versionJsonPath, JsonSerializer.Serialize(modifiedJson, new JsonSerializerOptions { WriteIndented = true }));
                System.Diagnostics.Debug.WriteLine($"[Forge] 已创建合并后的version.json: {versionJsonPath}");

                // 7. 复制原版Minecraft客户端JAR到Forge版本目录（Forge需要原版JAR）
                var vanillaJarPath = Path.Combine(gameDirectory, "versions", currentVersion, $"{currentVersion}.jar");
                var forgeJarPath = Path.Combine(forgeVersionDir, $"{customVersionName}.jar");
                
                if (!File.Exists(vanillaJarPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[Forge] ❌ 原版JAR不存在: {vanillaJarPath}");
                    throw new FileNotFoundException($"原版Minecraft客户端JAR不存在！\n路径: {vanillaJarPath}\n\nForge需要原版Minecraft才能正常运行。");
                }
                
                File.Copy(vanillaJarPath, forgeJarPath, true);
                System.Diagnostics.Debug.WriteLine($"[Forge] 已复制原版JAR: {vanillaJarPath} -> {forgeJarPath}");

                // 8. 下载Forge依赖库
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    DownloadStatusText.Text = "下载Forge依赖库...";
                    CurrentFileText.Text = "正在解析库列表";
                    DownloadOverallProgressBar.Value = 70;
                    DownloadOverallPercentageText.Text = "70%";
                }));

                await DownloadForgeLibrariesAsync(versionJsonPath, gameDirectory, config);

                System.Diagnostics.Debug.WriteLine($"[Forge] Forge版本安装完成");

                // 9. 如果配置启用，下载Assets资源文件
                if (config.DownloadAssetsWithGame)
                {
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        DownloadStatusText.Text = "正在下载游戏资源文件...";
                        CurrentFileText.Text = "Assets资源包";
                        DownloadOverallProgressBar.Value = 80;
                        DownloadOverallPercentageText.Text = "80%";
                    }));

                    var assetsResult = await AssetsDownloadService.DownloadAndCheckAssetsAsync(
                        gameDirectory,
                        currentVersion, // Assets使用原版MC版本
                        (current, total, message, speed) =>
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                double assetsProgress = 80 + (current * 0.2);
                                
                                if (_currentDownloadTaskId != null)
                                {
                                    DownloadTaskManager.Instance.UpdateTaskProgress(
                                        _currentDownloadTaskId,
                                        assetsProgress,
                                        message,
                                        speed
                                    );
                                }
                                
                                DownloadStatusText.Text = "下载游戏资源";
                                CurrentFileText.Text = message;
                                DownloadOverallProgressBar.Value = assetsProgress;
                                DownloadOverallPercentageText.Text = $"{assetsProgress:F0}%";
                                DownloadCurrentProgressBar.Value = current;
                                DownloadCurrentPercentageText.Text = $"{current:F0}%";
                                DownloadSpeedText.Text = FormatSpeed(speed);
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        },
                        _downloadCancellationToken!.Token
                    );

                    if (!assetsResult.Success && assetsResult.FailedAssets > 0)
                    {
                        NotificationManager.Instance.ShowNotification(
                            "资源下载部分失败",
                            $"Forge已安装完成，但有 {assetsResult.FailedAssets} 个资源文件下载失败\n游戏可能缺少部分资源（如声音、语言文件等）",
                            NotificationType.Warning,
                            durationSeconds: 8
                        );
                    }
                }

                // 10. 完成
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    DownloadOverallProgressBar.Value = 100;
                    DownloadOverallPercentageText.Text = "100%";
                    DownloadStatusText.Text = "安装完成";
                }));

                // 标记任务完成
                if (_currentDownloadTaskId != null)
                {
                    DownloadTaskManager.Instance.CompleteTask(_currentDownloadTaskId);
                    _currentDownloadTaskId = null;
                }

                // 清理临时文件
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch { }

                // 使用通知管理器显示安装完成消息
                NotificationManager.Instance.ShowNotification(
                    "Forge安装完成",
                    $"Forge {forgeVersion} 安装完成！\n版本: {customVersionName}\nMinecraft: {currentVersion}",
                    NotificationType.Success,
                    durationSeconds: 5
                );

                // 返回版本列表
                NavigationService?.GoBack();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Forge] 安装失败: {ex.Message}");
                MessageBox.Show(
                    $"Forge安装失败：{ex.Message}",
                    "安装失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                // 标记任务失败
                if (_currentDownloadTaskId != null)
                {
                    DownloadTaskManager.Instance.FailTask(_currentDownloadTaskId, ex.Message);
                    _currentDownloadTaskId = null;
                }
            }
        }

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

