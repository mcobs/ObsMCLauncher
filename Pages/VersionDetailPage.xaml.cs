using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
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
using System.Text.Json.Nodes;

namespace ObsMCLauncher.Pages
{
    public partial class VersionDetailPage : Page
    {
        // 静态HttpClient单例，避免每次请求都创建新实例
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        
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
            
            // 异步加载NeoForge版本列表
            _ = LoadNeoForgeVersionsAsync();
            
            // 异步加载Fabric版本列表
            _ = LoadFabricVersionsAsync();
            
            // 异步加载OptiFine版本列表
            _ = LoadOptiFineVersionsAsync();
            
            // 异步加载Quilt版本列表
            _ = LoadQuiltVersionsAsync();
            
            // 根据版本类型设置 Vanilla 图标
            UpdateVanillaIcon();
        }
        
        /// <summary>
        /// 根据版本类型更新 Vanilla 图标
        /// </summary>
        private void UpdateVanillaIcon()
        {
            if (versionInfo == null) return;
            
            string iconPath = "/Assets/LoaderIcons/vanilla.png"; // 默认图标
            
            try
            {
                // 判断是否为快照版
                if (versionInfo.Type == "snapshot")
                {
                    iconPath = "/Assets/LoaderIcons/vanilia_snapshot.png";
                }
                // 判断是否为正式版
                else if (versionInfo.Type == "release")
                {
                    // 判断版本是否 <= 1.12.2
                    if (IsVersionLessThanOrEqual(currentVersion, "1.12.2"))
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
                
                // 设置图标
                VanillaIcon.Source = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri(iconPath, UriKind.Relative)
                );
            }
            catch (Exception ex)
            {
            }
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
                return false;
            }
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
            // 设置加载状态
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ForgeRadio != null)
                {
                    ForgeRadio.IsEnabled = false;
                    ForgeRadio.ToolTip = "正在加载Forge版本列表...";
                }
                if (ForgeVersionComboBox != null)
                {
                    ForgeVersionComboBox.Items.Clear();
                    var loadingItem = new ComboBoxItem 
                    { 
                        Content = "正在加载中...", 
                        IsEnabled = false,
                        FontStyle = FontStyles.Italic
                    };
                    ForgeVersionComboBox.Items.Add(loadingItem);
                    ForgeVersionComboBox.SelectedIndex = 0;
                }
            }));
            
            try
            {

                // 检查Forge是否支持当前MC版本
                var supportedVersions = await ForgeService.GetSupportedMinecraftVersionsAsync();
                
                if (!supportedVersions.Contains(currentVersion))
                {
                    
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

                            // 自动选择第一个版本（最新版本）
                            ForgeVersionComboBox.SelectedIndex = 0;

                            // 启用Forge选项
                            if (ForgeRadio != null)
                            {
                                ForgeRadio.IsEnabled = true;
                                ForgeRadio.ToolTip = $"Forge for Minecraft {currentVersion} ({forgeVersions.Count} 个版本可用)";
                            }

                        }
                    }
                }));
            }
            catch (Exception ex)
            {
                
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

        /// <summary>
        /// 加载NeoForge版本列表
        /// </summary>
        private async Task LoadNeoForgeVersionsAsync()
        {
            // 设置加载状态
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (NeoForgeRadio != null)
                {
                    NeoForgeRadio.IsEnabled = false;
                    NeoForgeRadio.ToolTip = "正在加载NeoForge版本列表...";
                }
                if (NeoForgeVersionComboBox != null)
                {
                    NeoForgeVersionComboBox.Items.Clear();
                    var loadingItem = new ComboBoxItem 
                    { 
                        Content = "正在加载中...", 
                        IsEnabled = false,
                        FontStyle = FontStyles.Italic
                    };
                    NeoForgeVersionComboBox.Items.Add(loadingItem);
                    NeoForgeVersionComboBox.SelectedIndex = 0;
                }
            }));
            
            try
            {

                // 直接获取NeoForge版本列表（如果不支持会返回空列表）
                var neoforgeVersions = await NeoForgeService.GetNeoForgeVersionsAsync(currentVersion);

                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (NeoForgeVersionComboBox != null)
                    {
                        NeoForgeVersionComboBox.Items.Clear();

                        if (neoforgeVersions.Count == 0)
                        {
                            var noVersionItem = new ComboBoxItem 
                            { 
                                Content = "不支持此版本", 
                                IsEnabled = false,
                                FontStyle = FontStyles.Italic,
                                Foreground = new SolidColorBrush(Colors.Gray)
                            };
                            NeoForgeVersionComboBox.Items.Add(noVersionItem);
                            NeoForgeVersionComboBox.SelectedIndex = 0;
                            
                            if (NeoForgeRadio != null)
                            {
                                NeoForgeRadio.IsEnabled = false;
                                NeoForgeRadio.ToolTip = $"NeoForge暂不支持 Minecraft {currentVersion}";
                            }
                            
                        }
                        else
                        {
                            foreach (var nfVersion in neoforgeVersions)
                            {
                                var item = new ComboBoxItem
                                {
                                    Content = nfVersion.DisplayName,
                                    Tag = nfVersion
                                };
                                NeoForgeVersionComboBox.Items.Add(item);
                            }

                            // 默认选择第一个版本（推荐版本）
                            NeoForgeVersionComboBox.SelectedIndex = 0;

                            if (NeoForgeRadio != null)
                            {
                                NeoForgeRadio.IsEnabled = true;
                                NeoForgeRadio.ToolTip = $"安装NeoForge Mod加载器（共 {neoforgeVersions.Count} 个版本）";
                            }

                        }
                    }
                }));
            }
            catch (Exception ex)
            {
                
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (NeoForgeRadio != null)
                    {
                        NeoForgeRadio.IsEnabled = false;
                        NeoForgeRadio.ToolTip = "加载NeoForge版本失败";
                    }
                    if (NeoForgeVersionComboBox != null)
                    {
                        NeoForgeVersionComboBox.Items.Clear();
                        var errorItem = new ComboBoxItem 
                        { 
                            Content = "加载失败", 
                            IsEnabled = false,
                            FontStyle = FontStyles.Italic,
                            Foreground = new SolidColorBrush(Colors.Red)
                        };
                        NeoForgeVersionComboBox.Items.Add(errorItem);
                        NeoForgeVersionComboBox.SelectedIndex = 0;
                    }
                }));
            }
        }

        /// <summary>
        /// 加载Fabric版本列表
        /// </summary>
        private async Task LoadFabricVersionsAsync()
        {
            // 设置加载状态
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (FabricRadio != null)
                {
                    FabricRadio.IsEnabled = false;
                    FabricRadio.ToolTip = "正在加载Fabric版本列表...";
                }
                if (FabricVersionComboBox != null)
                {
                    FabricVersionComboBox.Items.Clear();
                    var loadingItem = new ComboBoxItem 
                    { 
                        Content = "正在加载中...", 
                        IsEnabled = false,
                        FontStyle = FontStyles.Italic
                    };
                    FabricVersionComboBox.Items.Add(loadingItem);
                    FabricVersionComboBox.SelectedIndex = 0;
                }
            }));
            
            try
            {

                // 检查Fabric是否支持当前MC版本
                var supportedVersions = await FabricService.GetSupportedMinecraftVersionsAsync();
                
                if (!supportedVersions.Contains(currentVersion))
                {
                    
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (FabricRadio != null)
                        {
                            FabricRadio.IsEnabled = false;
                            FabricRadio.ToolTip = $"Fabric暂不支持 Minecraft {currentVersion}";
                        }
                        if (FabricVersionComboBox != null)
                        {
                            FabricVersionComboBox.Items.Clear();
                            var item = new ComboBoxItem 
                            { 
                                Content = "不支持此版本", 
                                IsEnabled = false,
                                FontStyle = FontStyles.Italic,
                                Foreground = new SolidColorBrush(Colors.Gray)
                            };
                            FabricVersionComboBox.Items.Add(item);
                            FabricVersionComboBox.SelectedIndex = 0;
                        }
                    }));
                    return;
                }

                // 获取Fabric版本列表
                var fabricVersions = await FabricService.GetFabricVersionsAsync(currentVersion);
                
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (fabricVersions != null && fabricVersions.Count > 0)
                    {
                        if (FabricVersionComboBox != null)
                        {
                            FabricVersionComboBox.Items.Clear();
                            
                            foreach (var version in fabricVersions)
                            {
                                var displayText = version.Version;
                                
                                // 标记推荐版本（稳定版）
                                if (version.Stable && version == fabricVersions.First(v => v.Stable))
                                {
                                    displayText += " (推荐)";
                                }
                                
                                var item = new ComboBoxItem 
                                { 
                                    Content = displayText,
                                    Tag = version.Version,
                                    ToolTip = $"Fabric Loader {version.Version}\n构建号: {version.Build}"
                                };
                                FabricVersionComboBox.Items.Add(item);
                            }
                            
                            // 自动选择第一个（最新）版本
                            FabricVersionComboBox.SelectedIndex = 0;
                        }

                        // 启用Fabric选项
                        if (FabricRadio != null)
                        {
                            FabricRadio.IsEnabled = true;
                            FabricRadio.ToolTip = $"Fabric for Minecraft {currentVersion} ({fabricVersions.Count} 个版本可用)";
                        }

                    }
                    else
                    {
                        if (FabricRadio != null)
                        {
                            FabricRadio.IsEnabled = false;
                            FabricRadio.ToolTip = "未找到可用的Fabric版本";
                        }
                        if (FabricVersionComboBox != null)
                        {
                            FabricVersionComboBox.Items.Clear();
                            var item = new ComboBoxItem { Content = "无可用版本", IsEnabled = false };
                            FabricVersionComboBox.Items.Add(item);
                            FabricVersionComboBox.SelectedIndex = 0;
                        }
                    }
                }));
            }
            catch (Exception ex)
            {
                
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (FabricRadio != null)
                    {
                        FabricRadio.IsEnabled = false;
                        FabricRadio.ToolTip = "加载Fabric版本列表失败";
                    }
                    if (FabricVersionComboBox != null)
                    {
                        FabricVersionComboBox.Items.Clear();
                        var item = new ComboBoxItem { Content = "加载失败", IsEnabled = false };
                        FabricVersionComboBox.Items.Add(item);
                        FabricVersionComboBox.SelectedIndex = 0;
                    }
                }));
            }
        }

        /// <summary>
        /// 加载OptiFine版本列表
        /// </summary>
        private async Task LoadOptiFineVersionsAsync()
        {
            // 设置加载状态
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (OptiFineCheckBox != null)
                {
                    OptiFineCheckBox.IsEnabled = false;
                    OptiFineCheckBox.ToolTip = "正在加载OptiFine版本列表...";
                }
                if (OptiFineVersionComboBox != null)
                {
                    OptiFineVersionComboBox.Items.Clear();
                    var loadingItem = new ComboBoxItem 
                    { 
                        Content = "正在加载中...", 
                        IsEnabled = false,
                        FontStyle = FontStyles.Italic
                    };
                    OptiFineVersionComboBox.Items.Add(loadingItem);
                    OptiFineVersionComboBox.SelectedIndex = 0;
                }
            }));
            
            try
            {

                // 创建 OptiFineService 实例
                var optifineService = new OptiFineService(DownloadSourceManager.Instance);
                
                // 获取OptiFine版本列表
                var optifineVersions = await optifineService.GetOptifineVersionsAsync(currentVersion);
                
                // 使用自然排序，正确处理 pre10, pre11 等版本号
                if (optifineVersions != null && optifineVersions.Count > 0)
                {
                    optifineVersions = optifineVersions
                        .OrderByDescending(v => v.FullVersion, new NaturalStringComparer())
                        .ToList();
                }
                
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (optifineVersions != null && optifineVersions.Count > 0)
                    {
                        if (OptiFineVersionComboBox != null)
                        {
                            OptiFineVersionComboBox.Items.Clear();
                            
                            foreach (var version in optifineVersions)
                            {
                                var displayText = version.FullVersion; // 例如: HD_U_H9
                                
                                // 标记推荐版本（反转后第一个版本是最新版本）
                                if (version == optifineVersions.First())
                                {
                                    displayText += " (推荐)";
                                }
                                
                                var item = new ComboBoxItem 
                                { 
                                    Content = displayText,
                                    Tag = version, // 存储完整的版本对象
                                    ToolTip = $"{version.DisplayName}\n文件名: {version.Filename}"
                                };
                                OptiFineVersionComboBox.Items.Add(item);
                            }
                            
                            // 自动选择第一个（最新）版本
                            OptiFineVersionComboBox.SelectedIndex = 0;
                        }

                        // 启用OptiFine选项
                        if (OptiFineCheckBox != null)
                        {
                            OptiFineCheckBox.IsEnabled = true;
                            OptiFineCheckBox.ToolTip = $"OptiFine for Minecraft {currentVersion} ({optifineVersions.Count} 个版本可用)";
                        }

                    }
                    else
                    {
                        
                        if (OptiFineCheckBox != null)
                        {
                            OptiFineCheckBox.IsEnabled = false;
                            OptiFineCheckBox.ToolTip = $"OptiFine暂不支持 Minecraft {currentVersion}";
                        }
                        if (OptiFineVersionComboBox != null)
                        {
                            OptiFineVersionComboBox.Items.Clear();
                            var item = new ComboBoxItem 
                            { 
                                Content = "不支持此版本", 
                                IsEnabled = false,
                                FontStyle = FontStyles.Italic,
                                Foreground = new SolidColorBrush(Colors.Gray)
                            };
                            OptiFineVersionComboBox.Items.Add(item);
                            OptiFineVersionComboBox.SelectedIndex = 0;
                        }
                    }
                }));
            }
            catch (Exception ex)
            {
                
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (OptiFineCheckBox != null)
                    {
                        OptiFineCheckBox.IsEnabled = false;
                        OptiFineCheckBox.ToolTip = "加载OptiFine版本列表失败";
                    }
                    if (OptiFineVersionComboBox != null)
                    {
                        OptiFineVersionComboBox.Items.Clear();
                        var item = new ComboBoxItem { Content = "加载失败", IsEnabled = false };
                        OptiFineVersionComboBox.Items.Add(item);
                        OptiFineVersionComboBox.SelectedIndex = 0;
                    }
                }));
            }
        }

        /// <summary>
        /// 加载Quilt版本列表
        /// </summary>
        private async Task LoadQuiltVersionsAsync()
        {
            // 设置加载状态
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (QuiltRadio != null)
                {
                    QuiltRadio.IsEnabled = false;
                    QuiltRadio.ToolTip = "正在加载Quilt版本列表...";
                }
                if (QuiltVersionComboBox != null)
                {
                    QuiltVersionComboBox.Items.Clear();
                    var loadingItem = new ComboBoxItem 
                    { 
                        Content = "正在加载中...", 
                        IsEnabled = false,
                        FontStyle = FontStyles.Italic
                    };
                    QuiltVersionComboBox.Items.Add(loadingItem);
                    QuiltVersionComboBox.SelectedIndex = 0;
                }
            }));
            
            try
            {

                // 检查Quilt是否支持当前MC版本
                var supportedVersions = await QuiltService.GetSupportedMinecraftVersionsAsync();
                
                if (supportedVersions.Count > 0)
                {
                }

                // 直接尝试获取Quilt版本列表（如果能获取到说明支持）
                var quiltVersions = await QuiltService.GetQuiltVersionsAsync(currentVersion);
                
                // 如果没有获取到版本，说明不支持
                if (quiltVersions == null || quiltVersions.Count == 0)
                {
                    
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (QuiltRadio != null)
                        {
                            QuiltRadio.IsEnabled = false;
                            QuiltRadio.ToolTip = $"Quilt暂不支持 Minecraft {currentVersion}";
                        }
                        if (QuiltVersionComboBox != null)
                        {
                            QuiltVersionComboBox.Items.Clear();
                            var item = new ComboBoxItem 
                            { 
                                Content = "不支持此版本", 
                                IsEnabled = false,
                                FontStyle = FontStyles.Italic,
                                Foreground = new SolidColorBrush(Colors.Gray)
                            };
                            QuiltVersionComboBox.Items.Add(item);
                            QuiltVersionComboBox.SelectedIndex = 0;
                        }
                    }));
                    return;
                }
                
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (quiltVersions != null && quiltVersions.Count > 0)
                    {
                        if (QuiltVersionComboBox != null)
                        {
                            QuiltVersionComboBox.Items.Clear();
                            
                            foreach (var version in quiltVersions)
                            {
                                var displayText = version.Version;
                                
                                // 标记最新版本
                                if (version == quiltVersions.First())
                                {
                                    displayText += " (最新)";
                                }
                                
                                var item = new ComboBoxItem 
                                { 
                                    Content = displayText,
                                    Tag = version.Version,
                                    ToolTip = $"Quilt Loader {version.Version}\n构建号: {version.Build}"
                                };
                                QuiltVersionComboBox.Items.Add(item);
                            }
                            
                            // 自动选择第一个（最新）版本
                            QuiltVersionComboBox.SelectedIndex = 0;
                        }

                        // 启用Quilt选项
                        if (QuiltRadio != null)
                        {
                            QuiltRadio.IsEnabled = true;
                            QuiltRadio.ToolTip = $"Quilt for Minecraft {currentVersion} ({quiltVersions.Count} 个版本可用)";
                        }

                    }
                    else
                    {
                        if (QuiltRadio != null)
                        {
                            QuiltRadio.IsEnabled = false;
                            QuiltRadio.ToolTip = "未找到可用的Quilt版本";
                        }
                        if (QuiltVersionComboBox != null)
                        {
                            QuiltVersionComboBox.Items.Clear();
                            var item = new ComboBoxItem { Content = "无可用版本", IsEnabled = false };
                            QuiltVersionComboBox.Items.Add(item);
                            QuiltVersionComboBox.SelectedIndex = 0;
                        }
                    }
                }));
            }
            catch (Exception ex)
            {
                
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (QuiltRadio != null)
                    {
                        QuiltRadio.IsEnabled = false;
                        QuiltRadio.ToolTip = "加载Quilt版本列表失败";
                    }
                    if (QuiltVersionComboBox != null)
                    {
                        QuiltVersionComboBox.Items.Clear();
                        var item = new ComboBoxItem { Content = "加载失败", IsEnabled = false };
                        QuiltVersionComboBox.Items.Add(item);
                        QuiltVersionComboBox.SelectedIndex = 0;
                    }
                }));
            }
        }

        private void LoaderRadio_Checked(object sender, RoutedEventArgs e)
        {
            // 禁用所有版本选择框（OptiFine除外，它有独立的CheckBox）
            if (ForgeVersionComboBox != null) ForgeVersionComboBox.IsEnabled = false;
            if (NeoForgeVersionComboBox != null) NeoForgeVersionComboBox.IsEnabled = false;
            if (FabricVersionComboBox != null) FabricVersionComboBox.IsEnabled = false;
            if (QuiltVersionComboBox != null) QuiltVersionComboBox.IsEnabled = false;

            // 根据选中的加载器启用对应的版本选择框
            if (sender == ForgeRadio && ForgeVersionComboBox != null)
            {
                ForgeVersionComboBox.IsEnabled = true;
            }
            else if (sender == NeoForgeRadio && NeoForgeVersionComboBox != null)
            {
                NeoForgeVersionComboBox.IsEnabled = true;
            }
            else if (sender == FabricRadio && FabricVersionComboBox != null)
            {
                FabricVersionComboBox.IsEnabled = true;
            }
            else if (sender == QuiltRadio && QuiltVersionComboBox != null)
            {
                QuiltVersionComboBox.IsEnabled = true;
            }
            
            // OptiFine兼容性控制：仅Vanilla和Forge支持OptiFine
            if (OptiFineCheckBox != null)
            {
                if (sender == VanillaRadio || sender == ForgeRadio)
                {
                    // Vanilla或Forge：启用OptiFine
                    OptiFineCheckBox.IsEnabled = true;
                    OptiFineCheckBox.Opacity = 1.0;
                    
                    // 更新提示文本
                    if (OptiFineDescriptionText != null && OptiFineCheckBox.IsChecked == true)
                    {
                        if (sender == ForgeRadio)
                        {
                            OptiFineDescriptionText.Text = "将作为 Mod 安装到 Forge 的 mods 文件夹";
                        }
                        else
                        {
                            OptiFineDescriptionText.Text = "独立安装模式，性能优化 MOD";
                        }
                    }
                }
                else
                {
                    // NeoForge/Fabric/Quilt：禁用并取消选中OptiFine
                    OptiFineCheckBox.IsEnabled = false;
                    OptiFineCheckBox.IsChecked = false;
                    OptiFineCheckBox.Opacity = 0.5;
                    
                    // 禁用OptiFine版本选择框
                    if (OptiFineVersionComboBox != null)
                    {
                        OptiFineVersionComboBox.IsEnabled = false;
                    }
                    
                    // 更新提示文本
                    if (OptiFineDescriptionText != null)
                    {
                        string loaderName = sender == NeoForgeRadio ? "NeoForge" :
                                           sender == FabricRadio ? "Fabric" :
                                           sender == QuiltRadio ? "Quilt" : "此加载器";
                        OptiFineDescriptionText.Text = $"{loaderName} 不支持与 OptiFine 一起安装";
                    }
                    
                }
            }

            // 更新版本名称
            UpdateVersionName();
            
            // 更新选中的加载器显示
            UpdateSelectedLoaderText();
        }

        /// <summary>
        /// OptiFine CheckBox 状态改变事件
        /// </summary>
        private void OptiFineCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (OptiFineVersionComboBox != null)
            {
                OptiFineVersionComboBox.IsEnabled = OptiFineCheckBox?.IsChecked == true;
            }
            
            // 更新描述文本，提示与Forge一起使用
            if (OptiFineDescriptionText != null)
            {
                if (OptiFineCheckBox?.IsChecked == true && ForgeRadio?.IsChecked == true)
                {
                    OptiFineDescriptionText.Text = "将作为 Mod 安装到 Forge 的 mods 文件夹";
                }
                else if (OptiFineCheckBox?.IsChecked == true)
                {
                    OptiFineDescriptionText.Text = "独立安装模式，性能优化 MOD";
                }
                else
                {
                    OptiFineDescriptionText.Text = "性能优化 MOD，支持光影和高清材质";
                }
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

            var selections = new List<string>();
            
            if (VanillaRadio?.IsChecked == true)
            {
                selections.Add("原版");
            }
            else if (ForgeRadio?.IsChecked == true)
            {
                var version = (ForgeVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "默认";
                selections.Add($"Forge {version}");
            }
            else if (NeoForgeRadio?.IsChecked == true)
            {
                var version = (NeoForgeVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "默认";
                selections.Add($"NeoForge {version}");
            }
            else if (FabricRadio?.IsChecked == true)
            {
                var version = (FabricVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "默认";
                selections.Add($"Fabric {version}");
            }
            else if (QuiltRadio?.IsChecked == true)
            {
                var version = (QuiltVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "默认";
                selections.Add($"Quilt {version}");
            }
            
            // 检查是否勾选了OptiFine
            if (OptiFineCheckBox?.IsChecked == true)
            {
                var optifineVersion = (OptiFineVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "默认";
                selections.Add($"OptiFine {optifineVersion}");
            }
            
            SelectedLoaderText.Text = selections.Count > 0 
                ? $"已选择：{string.Join(" + ", selections)}" 
                : "未选择加载器";
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

            // 根据选中的主加载器添加后缀
            if (ForgeRadio?.IsChecked == true)
            {
                var selectedItem = ForgeVersionComboBox?.SelectedItem as ComboBoxItem;
                var forgeVersion = selectedItem?.Content?.ToString();
                
                // 只有在选择了有效版本时才添加后缀
                if (!string.IsNullOrEmpty(forgeVersion) && 
                    !forgeVersion.Contains("请选择") && 
                    selectedItem?.IsEnabled == true)
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
            else if (NeoForgeRadio?.IsChecked == true)
            {
                var selectedItem = NeoForgeVersionComboBox?.SelectedItem as ComboBoxItem;
                var neoforgeVersion = selectedItem?.Content?.ToString();
                
                // 只有在选择了有效版本时才添加后缀
                if (!string.IsNullOrEmpty(neoforgeVersion) && 
                    !neoforgeVersion.Contains("请选择") && 
                    !neoforgeVersion.Contains("加载") &&
                    !neoforgeVersion.Contains("不支持") &&
                    selectedItem?.IsEnabled == true)
                {
                    // 移除 "(推荐)" 等标记
                    neoforgeVersion = neoforgeVersion.Replace(" (推荐)", "").Trim();
                    versionName += $"-neoforge-{neoforgeVersion}";
                }
                else
                {
                    versionName += "-neoforge";
                }
            }
            else if (FabricRadio?.IsChecked == true)
            {
                var selectedItem = FabricVersionComboBox?.SelectedItem as ComboBoxItem;
                // 从Tag获取实际版本号
                var fabricVersion = selectedItem?.Tag?.ToString();
                
                // 只有在选择了有效版本时才添加后缀
                if (!string.IsNullOrEmpty(fabricVersion) && 
                    selectedItem?.IsEnabled == true)
                {
                    versionName += $"-fabric-{fabricVersion}";
                }
                else
                {
                    versionName += "-fabric";
                }
            }
            else if (QuiltRadio?.IsChecked == true)
            {
                var selectedItem = QuiltVersionComboBox?.SelectedItem as ComboBoxItem;
                // 从Tag获取实际版本号
                var quiltVersion = selectedItem?.Tag?.ToString();
                
                // 只有在选择了有效版本时才添加后缀
                if (!string.IsNullOrEmpty(quiltVersion) && 
                    selectedItem?.IsEnabled == true)
                {
                    versionName += $"-quilt-{quiltVersion}";
                }
                else
                {
                    versionName += "-quilt";
                }
            }
            
            // 检查是否额外勾选了OptiFine（可与Forge等一起使用）
            if (OptiFineCheckBox?.IsChecked == true)
            {
                var selectedItem = OptiFineVersionComboBox?.SelectedItem as ComboBoxItem;
                var optifineVersion = selectedItem?.Content?.ToString();
                
                // 只有在选择了有效版本时才添加后缀
                if (!string.IsNullOrEmpty(optifineVersion) && 
                    !optifineVersion.Contains("请选择") && 
                    selectedItem?.IsEnabled == true)
                {
                    optifineVersion = optifineVersion.Replace(" (推荐)", "").Trim();
                    versionName += $"-optifine-{optifineVersion}";
                }
                else
                {
                    versionName += "-optifine";
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
                var selectedItem = ForgeVersionComboBox?.SelectedItem as ComboBoxItem;
                loaderVersion = selectedItem?.Content?.ToString() ?? "";
                
                // 检查是否有选择版本
                if (string.IsNullOrEmpty(loaderVersion))
                {
                    await DialogManager.Instance.ShowWarning("提示", "请先选择一个Forge版本！");
                    return;
                }
                
                // 移除 "(推荐)" 等标记
                loaderVersion = loaderVersion.Replace(" (推荐)", "").Replace(" (最新)", "").Replace(" (Latest)", "").Replace(" (Recommended)", "").Trim();
            }
            else if (NeoForgeRadio?.IsChecked == true)
            {
                loaderType = "NeoForge";
                var selectedItem = NeoForgeVersionComboBox?.SelectedItem as ComboBoxItem;
                loaderVersion = selectedItem?.Content?.ToString() ?? "";
                
                // 检查是否有选择版本
                if (string.IsNullOrEmpty(loaderVersion) || 
                    loaderVersion.Contains("加载") || 
                    loaderVersion.Contains("不支持") ||
                    selectedItem?.IsEnabled == false)
                {
                    await DialogManager.Instance.ShowWarning("提示", "请先选择一个NeoForge版本！");
                    return;
                }
                
                // 移除 "(推荐)" 等标记
                loaderVersion = loaderVersion.Replace(" (推荐)", "").Replace(" (最新)", "").Replace(" (Latest)", "").Replace(" (Recommended)", "").Trim();
            }
            else if (FabricRadio?.IsChecked == true)
            {
                loaderType = "Fabric";
                var selectedItem = FabricVersionComboBox?.SelectedItem as ComboBoxItem;
                // 从Tag获取实际版本号，而不是从Content（显示文本可能包含"(推荐)"等标签）
                loaderVersion = selectedItem?.Tag?.ToString() ?? "";
                
                // 检查是否有选择版本
                if (string.IsNullOrEmpty(loaderVersion))
                {
                    await DialogManager.Instance.ShowWarning("提示", "请先选择一个Fabric版本！");
                    return;
                }
            }
            else if (QuiltRadio?.IsChecked == true)
            {
                loaderType = "Quilt";
                var selectedItem = QuiltVersionComboBox?.SelectedItem as ComboBoxItem;
                // 从Tag获取实际版本号（显示文本可能包含"(最新)"等标签）
                loaderVersion = selectedItem?.Tag?.ToString() ?? "";
                
                // 检查是否有选择版本
                if (string.IsNullOrEmpty(loaderVersion))
                {
                    await DialogManager.Instance.ShowWarning("提示", "请先选择一个Quilt版本！");
                    return;
                }
            }

            // 获取自定义版本名称
            var customVersionName = VersionNameTextBox?.Text?.Trim();
            if (string.IsNullOrEmpty(customVersionName))
            {
                await DialogManager.Instance.ShowWarning("错误", "请输入版本名称！");
                return;
            }

            // 验证版本名称合法性（不包含非法字符）
            var invalidChars = Path.GetInvalidFileNameChars();
            if (customVersionName.Any(c => invalidChars.Contains(c)))
            {
                await DialogManager.Instance.ShowWarning("错误", "版本名称包含非法字符，请修改！");
                return;
            }

            // 获取游戏目录
            var config = LauncherConfig.Load();
            var gameDirectory = config.GameDirectory;


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
                                
                                if (assetsResult.FailedAssets > 0)
                                {
                                    NotificationManager.Instance.ShowNotification(
                                        "资源下载部分失败",
                                        $"{assetsResult.FailedAssets} 个资源文件下载失败，游戏可能缺少部分资源",
                                        NotificationType.Warning,
                                        6
                                    );
                                }
                            }
                            else
                            {
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

                        NotificationManager.Instance.ShowNotification(
                            "安装成功",
                            $"Minecraft {currentVersion} 已安装为: {customVersionName}",
                            NotificationType.Success,
                            5
                        );

                        // 返回版本列表
                        NavigationService?.GoBack();
                    }
                    else
                    {
                        await DialogManager.Instance.ShowError(
                            "安装失败",
                            "版本下载失败，请查看日志了解详细信息。"
                        );
                    }
                }
                else if (loaderType == "Forge")
                {
                    // Forge安装流程（可能包含 OptiFine）
                    await InstallForgeWithOptionalOptiFineAsync(loaderVersion, customVersionName, gameDirectory, config, progress);
                }
                else if (loaderType == "NeoForge")
                {
                    // NeoForge安装流程
                    await InstallNeoForgeAsync(loaderVersion, customVersionName, gameDirectory, config, progress);
                }
                else if (loaderType == "Fabric")
                {
                    // Fabric安装流程
                    await InstallFabricAsync(loaderVersion, customVersionName, gameDirectory, config, progress);
                }
                else if (loaderType == "Quilt")
                {
                    // Quilt安装流程
                    await InstallQuiltAsync(loaderVersion, customVersionName, gameDirectory, config, progress);
                }
                else
                {
                    // 其他加载器暂不支持
                    await DialogManager.Instance.ShowInfo(
                        "功能开发中",
                        $"{loaderType} 加载器的安装功能即将推出！"
                    );
                }
            }
            catch (OperationCanceledException)
            {
                
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
                            Directory.Delete(versionDirToDelete, true); // 递归删除
                            
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
                    }
                });
            }
            catch (Exception ex)
            {
                
                // 标记任务失败
                if (_currentDownloadTaskId != null)
                {
                    DownloadTaskManager.Instance.FailTask(_currentDownloadTaskId, ex.Message);
                    _currentDownloadTaskId = null;
                }
                
                await DialogManager.Instance.ShowError(
                    "错误",
                    $"下载过程中发生错误：\n{ex.Message}"
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
                    return;
                }


                var downloadService = DownloadSourceManager.Instance.CurrentService;
                
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
                            skipCount++;
                            continue;
                        }

                        // 检查文件是否已存在
                        if (File.Exists(savePath))
                        {
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
                        System.Diagnostics.Debug.WriteLine($"[Forge]    URL: {downloadUrl}");
                        
                        var response = await _httpClient.GetAsync(downloadUrl, _downloadCancellationToken!.Token);
                        
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

                System.Diagnostics.Debug.WriteLine($"[Forge] 库文件下载完成: 成功 {successCount}, 跳过 {skipCount}, 失败 {failedCount}");

                if (failedCount > 0)
                {
                    NotificationManager.Instance.ShowNotification(
                        "提示",
                        $"Forge库下载部分失败（成功: {successCount}, 失败: {failedCount}），将在启动时自动补全",
                        NotificationType.Warning,
                        5
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
                NotificationManager.Instance.ShowNotification(
                    "警告",
                    "下载Forge库文件时出错，将在启动时尝试自动补全",
                    NotificationType.Warning,
                    5
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
        /// 安装 Forge，并根据兼容性决定是否一并安装 OptiFine
        /// </summary>
        private async Task InstallForgeWithOptionalOptiFineAsync(
            string forgeVersion,
            string customVersionName,
            string gameDirectory,
            LauncherConfig config,
            IProgress<DownloadProgress> progress)
        {
            // 1. 检查用户是否也选择了 OptiFine
            OptifineVersionModel? selectedOptiFineVersion = null;
            await Dispatcher.InvokeAsync(() =>
            {
                // 即使 OptiFineRadio 未被选中，也检查 OptiFineVersionComboBox 是否有有效选择
                if (OptiFineVersionComboBox?.SelectedItem is ComboBoxItem selectedItem && 
                    selectedItem.Tag is OptifineVersionModel optifineVer &&
                    selectedItem.IsEnabled)
                {
                    selectedOptiFineVersion = optifineVer;
                    System.Diagnostics.Debug.WriteLine($"[ForgeOptiFine] 检测到用户同时选择了 OptiFine: {optifineVer.FullVersion}");
                }
            });

            // 2. 安装 Forge
            await InstallForgeAsync(forgeVersion, customVersionName, gameDirectory, config, progress);

            // 3. 如果用户选择了 OptiFine，检查兼容性并下载到 mods 文件夹
            if (selectedOptiFineVersion != null)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[ForgeOptiFine] 开始检查 Forge 和 OptiFine 兼容性");

                    // 检查兼容性
                    var compatibilityResult = ForgeOptiFineCompatibilityService.CheckCompatibility(
                        currentVersion,
                        forgeVersion,
                        selectedOptiFineVersion
                    );

                    if (compatibilityResult.IsCompatible)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ForgeOptiFine] ✅ 兼容性检查通过: {compatibilityResult.Reason}");

                        // 显示提示信息
                        if (!string.IsNullOrEmpty(compatibilityResult.WarningMessage))
                        {
                            System.Diagnostics.Debug.WriteLine($"[ForgeOptiFine] ⚠️ {compatibilityResult.WarningMessage}");
                        }

                        // 更新进度显示
                        _ = Dispatcher.BeginInvoke(() =>
                        {
                            DownloadStatusText.Text = "正在下载 OptiFine mod...";
                            CurrentFileText.Text = selectedOptiFineVersion.Filename;
                            DownloadOverallProgressBar.Value = 85;
                            DownloadOverallPercentageText.Text = "85%";
                        });

                        // 根据安装模式下载 OptiFine
                        if (compatibilityResult.InstallMode == InstallMode.AsMod)
                        {
                            // 根据版本隔离设置获取 mods 文件夹路径
                            var modsDir = config.GetModsDirectory(customVersionName);
                            
                            System.Diagnostics.Debug.WriteLine($"[ForgeOptiFine] 将 OptiFine 下载为 mod 到: {modsDir}");
                            System.Diagnostics.Debug.WriteLine($"[ForgeOptiFine] 版本隔离模式: {config.GameDirectoryType}");

                            var optifineService = new OptiFineService(DownloadSourceManager.Instance);
                            await optifineService.DownloadOptiFineAsModAsync(
                                selectedOptiFineVersion,
                                modsDir,
                                (status, current, total, bytes, totalBytes) =>
                                {
                                    _ = Dispatcher.BeginInvoke(() =>
                                    {
                                        DownloadStatusText.Text = status;
                                        var progress = 85 + (current / 100.0 * 10); // 85-95%
                                        DownloadOverallProgressBar.Value = progress;
                                        DownloadOverallPercentageText.Text = $"{progress:F0}%";
                                        DownloadCurrentProgressBar.Value = current;
                                        DownloadCurrentPercentageText.Text = $"{current:F0}%";
                                        
                                        if (_currentDownloadTaskId != null)
                                        {
                                            DownloadTaskManager.Instance.UpdateTaskProgress(
                                                _currentDownloadTaskId,
                                                progress,
                                                status,
                                                0
                                            );
                                        }
                                    });
                                },
                                _downloadCancellationToken!.Token
                            );

                            System.Diagnostics.Debug.WriteLine($"[ForgeOptiFine] ✅ OptiFine mod 下载完成");

                            // 显示成功提示
                            _ = NotificationManager.Instance.ShowNotification(
                                "OptiFine 已添加",
                                $"OptiFine {selectedOptiFineVersion.FullVersion} 已作为 mod 添加到 Forge",
                                NotificationType.Success,
                                5
                            );
                        }
                        else
                        {
                            // 集成安装模式（暂不实现，仅用于 1.12.2 及以下版本）
                            System.Diagnostics.Debug.WriteLine($"[ForgeOptiFine] 集成安装模式暂不支持，跳过 OptiFine 安装");
                        }
                    }
                    else
                    {
                        // 不兼容，显示警告
                        System.Diagnostics.Debug.WriteLine($"[ForgeOptiFine] ❌ 不兼容: {compatibilityResult.Reason}");
                        
                        _ = NotificationManager.Instance.ShowNotification(
                            "OptiFine 不兼容",
                            compatibilityResult.Reason,
                            NotificationType.Warning,
                            8
                        );
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ForgeOptiFine] 处理 OptiFine 时出错: {ex.Message}");
                    
                    _ = NotificationManager.Instance.ShowNotification(
                        "OptiFine 安装失败",
                        $"无法添加 OptiFine: {ex.Message}",
                        NotificationType.Error,
                        8
                    );
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ForgeOptiFine] 用户未选择 OptiFine，仅安装 Forge");
            }
        }

        /// <summary>
        /// 安装Forge（使用官方安装器）
        /// </summary>
        private async Task InstallForgeAsync(
            string forgeVersion, 
            string customVersionName, 
            string gameDirectory, 
            LauncherConfig config,
            IProgress<DownloadProgress> progress)
        {
            var forgeFullVersion = $"{currentVersion}-{forgeVersion}";
            var installerPath = Path.Combine(Path.GetTempPath(), $"forge-installer-{forgeFullVersion}.jar");
            System.Threading.Timer? progressSimulator = null;
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"[Forge] 开始使用官方安装器安装 Forge {forgeVersion} for MC {currentVersion}");

                // 1. 清理旧安装
                await CleanupPreviousForgeInstallation(gameDirectory, customVersionName);

                // 2. 下载官方 Forge 安装器
                progress?.Report(new DownloadProgress
                {
                    Status = "正在下载Forge安装器...",
                    CurrentFile = $"forge-{currentVersion}-{forgeVersion}-installer.jar",
                    TotalBytes = 0,
                    TotalDownloadedBytes = 0,
                    DownloadSpeed = 0
                });
                
                // 使用带详细进度信息的下载方法
                if (!await ForgeService.DownloadForgeInstallerWithDetailsAsync(
                    forgeFullVersion, 
                    installerPath, 
                    (currentBytes, speed, totalBytes) =>
                    {
                        // 计算百分比
                        double percentage = totalBytes > 0 ? (double)currentBytes / totalBytes * 100 : 0;
                        double overallProgress = 20 + (percentage * 0.2); // 20%-40%
                        
                        // 通过progress报告（会自动更新UI和DownloadTaskManager）
                        progress?.Report(new DownloadProgress
                        {
                            Status = "正在下载Forge安装器",
                            CurrentFile = $"forge-installer.jar",
                            CurrentFileBytes = currentBytes,
                            CurrentFileTotalBytes = totalBytes,
                            TotalDownloadedBytes = currentBytes,
                            TotalBytes = totalBytes,
                            DownloadSpeed = speed,
                            CompletedFiles = 0,
                            TotalFiles = 3 // 安装器、原版、库文件
                        });
                    },
                    _downloadCancellationToken!.Token))
                    throw new Exception("Forge安装器下载失败");

                // 3. 下载原版文件到标准位置（Forge安装器期望文件在这里）
                string standardVanillaDir = Path.Combine(gameDirectory, "versions", currentVersion);
                
                // 创建进度回调，将原版下载进度映射到40%-50%
                var vanillaProgress = new Progress<DownloadProgress>(p => {
                    // 计算文件进度百分比
                    double fileProgress = p.CurrentFileTotalBytes > 0 
                        ? (double)p.CurrentFileBytes / p.CurrentFileTotalBytes * 100 
                        : 0;
                    
                    // 映射到40%-50%的范围
                    var overallProgress = 40 + (fileProgress / 100.0 * 10);
                    
                    // 通过progress报告（自动更新UI和DownloadTaskManager）
                    progress?.Report(new DownloadProgress
                    {
                        Status = p.Status,
                        CurrentFile = p.CurrentFile ?? $"{currentVersion}.jar",
                        CurrentFileBytes = p.CurrentFileBytes,
                        CurrentFileTotalBytes = p.CurrentFileTotalBytes,
                        TotalDownloadedBytes = p.TotalDownloadedBytes,
                        TotalBytes = p.TotalBytes,
                        DownloadSpeed = p.DownloadSpeed,
                        CompletedFiles = 1,
                        TotalFiles = 3
                    });
                });
                
                await DownloadVanillaForForge(gameDirectory, currentVersion, standardVanillaDir, vanillaProgress);

                // 4. 运行官方安装器（带进度模拟）
                progress?.Report(new DownloadProgress
                {
                    Status = "执行Forge安装...",
                    CurrentFile = "正在处理Minecraft文件（请稍候）",
                    CurrentFileBytes = 50,
                    CurrentFileTotalBytes = 100,
                    CompletedFiles = 2,
                    TotalFiles = 3
                });

                // 创建一个进度模拟器（因为Forge安装器不提供进度）
                progressSimulator = SimulateForgeInstallerProgress(progress);

                bool installSuccess = await RunForgeInstallerAsync(installerPath, gameDirectory, currentVersion, forgeVersion, config);
                
                // 停止进度模拟
                progressSimulator.Dispose();
                progressSimulator = null;
                
                if (!installSuccess)
                    throw new Exception("Forge安装器执行失败，请查看日志");
                
                // 安装完成，设置为70%
                progress?.Report(new DownloadProgress
                {
                    Status = "Forge安装完成",
                    CurrentFileBytes = 70,
                    CurrentFileTotalBytes = 100,
                    CompletedFiles = 2,
                    TotalFiles = 3
                });

                // 4. 重命名官方生成的版本到自定义名称
                progress?.Report(new DownloadProgress
                {
                    Status = "配置版本信息...",
                    CurrentFile = "正在重命名版本文件",
                    CurrentFileBytes = 75,
                    CurrentFileTotalBytes = 100,
                    CompletedFiles = 2,
                    TotalFiles = 3
                });
                
                await RenameForgeVersionAsync(gameDirectory, currentVersion, forgeVersion, customVersionName);

                // 4.5. 清理原版文件夹（在复制完JAR之后）
                string vanillaDir = Path.Combine(gameDirectory, "versions", currentVersion);
                if (Directory.Exists(vanillaDir))
                {
                    try
                    {
                        await Task.Run(() => Directory.Delete(vanillaDir, true));
                        System.Diagnostics.Debug.WriteLine($"[Forge] 🗑️ 已清理原版文件夹: {currentVersion}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Forge] ⚠️ 清理原版文件夹失败: {ex.Message}");
                    }
                }

                // 5. 下载Assets (如果需要)
                if (config.DownloadAssetsWithGame)
                {
                    await DownloadAssetsForForge(gameDirectory, customVersionName);
                }

                // 6. 完成
                await FinalizeForgeInstallation(customVersionName, forgeVersion);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("[Forge] 安装被用户取消");
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Forge] 安装失败: {ex.Message}\n{ex.StackTrace}");
                _ = Dispatcher.BeginInvoke(() => {
                    _ = NotificationManager.Instance.ShowNotification("Forge安装失败", ex.Message, NotificationType.Error, 10);
                });

                if (_currentDownloadTaskId != null)
                {
                    DownloadTaskManager.Instance.FailTask(_currentDownloadTaskId, ex.Message);
                    _currentDownloadTaskId = null;
                }
                throw;
            }
            finally
            {
                // 清理工作（无论成功、失败还是取消）
                try
                {
                    // 停止进度模拟器
                    progressSimulator?.Dispose();
                    
                    // 清理Forge安装器
                    if (File.Exists(installerPath))
                    {
                        File.Delete(installerPath);
                        System.Diagnostics.Debug.WriteLine($"[Forge] 🗑️ 已清理Forge安装器: {installerPath}");
                    }
                    
                    // 最终清理：删除原版文件夹（仅在安装失败/取消时作为兜底）
                    // 正常流程中的清理已在RenameForgeVersionAsync之后完成
                    string vanillaDir = Path.Combine(gameDirectory, "versions", currentVersion);
                    if (Directory.Exists(vanillaDir))
                    {
                        try
                        {
                            await Task.Run(() => Directory.Delete(vanillaDir, true));
                            System.Diagnostics.Debug.WriteLine($"[Forge] 🗑️ [兜底清理] 已删除残留的原版文件夹: {currentVersion}");
                        }
                        catch (Exception vanillaEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Forge] ⚠️ [兜底清理] 删除原版文件夹失败: {vanillaEx.Message}");
                        }
                    }
                    
                    // 清理未完成的Forge安装文件夹（如果安装被取消）
                    // Forge官方安装器创建的目录名格式：1.21.8-forge-58.1.6
                    string officialForgeId = $"{currentVersion}-forge-{forgeVersion}";
                    string officialForgeDir = Path.Combine(gameDirectory, "versions", officialForgeId);
                    
                    // 检查是否是未完成的安装（目录存在但不是最终的自定义名称）
                    if (officialForgeId != customVersionName && Directory.Exists(officialForgeDir))
                    {
                        // 检查自定义名称的目录是否已经存在（说明安装成功并已重命名）
                        string customDir = Path.Combine(gameDirectory, "versions", customVersionName);
                        if (!Directory.Exists(customDir))
                        {
                            // 自定义目录不存在，说明安装未完成，删除临时Forge文件夹
                            await Task.Run(() => Directory.Delete(officialForgeDir, true));
                            System.Diagnostics.Debug.WriteLine($"[Forge] 🗑️ 已清理未完成的Forge文件夹: {officialForgeId}");
                        }
                    }
                }
                catch (Exception cleanupEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[Forge] ⚠️ 清理临时文件失败: {cleanupEx.Message}");
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
        
        private async Task<(string jsonPath, string jarPath, string sha1)> DownloadVanillaFilesToTemp(string tempDir)
        {
            var versionManifestUrl = "https://bmclapi2.bangbang93.com/mc/game/version_manifest.json";
            var manifestResponse = await _httpClient.GetStringAsync(versionManifestUrl, _downloadCancellationToken!.Token);
            var manifestDoc = JsonDocument.Parse(manifestResponse);
            var versions = manifestDoc.RootElement.GetProperty("versions");

            string? versionJsonUrl = versions.EnumerateArray()
                .FirstOrDefault(v => v.GetProperty("id").GetString() == currentVersion)
                .GetProperty("url").GetString();

            if (string.IsNullOrEmpty(versionJsonUrl))
                throw new Exception($"无法找到版本 {currentVersion} 的元数据URL");

            var jsonContent = await _httpClient.GetStringAsync(versionJsonUrl, _downloadCancellationToken!.Token);
            var jsonPath = Path.Combine(tempDir, $"{currentVersion}.json");
            await File.WriteAllTextAsync(jsonPath, jsonContent, _downloadCancellationToken!.Token);

            var vanillaDoc = JsonDocument.Parse(jsonContent);
            var clientElement = vanillaDoc.RootElement.GetProperty("downloads").GetProperty("client");
            var clientUrl = clientElement.GetProperty("url").GetString()!;
            var clientSha1 = clientElement.GetProperty("sha1").GetString()!;

            if (DownloadSourceManager.Instance.CurrentService is BMCLAPIService)
                clientUrl = $"https://bmclapi2.bangbang93.com/version/{currentVersion}/client";

            System.Diagnostics.Debug.WriteLine($"[Forge] 下载原版客户端JAR: {clientUrl}");
            var jarBytes = await _httpClient.GetByteArrayAsync(clientUrl, _downloadCancellationToken!.Token);
            var jarPath = Path.Combine(tempDir, $"{currentVersion}.jar");
            await File.WriteAllBytesAsync(jarPath, jarBytes, _downloadCancellationToken!.Token);
            System.Diagnostics.Debug.WriteLine($"[Forge] 原版JAR下载完成: {jarPath} ({jarBytes.Length} bytes)");

            return (jsonPath, jarPath, clientSha1);
        }

        private async Task<string> DownloadForgeInstallerToTemp(string forgeVersion, string tempDir)
        {
            var forgeFullVersion = $"{currentVersion}-{forgeVersion}";
            var installerPath = Path.Combine(tempDir, $"forge-{forgeFullVersion}-installer.jar");
            
            var progress = new Progress<double>(p => _ = Dispatcher.BeginInvoke(() => {
                DownloadCurrentProgressBar.Value = p;
                DownloadCurrentPercentageText.Text = $"{p:F0}%";
            }));

            if (!await ForgeService.DownloadForgeInstallerAsync(forgeFullVersion, installerPath, progress, _downloadCancellationToken!.Token))
                throw new Exception("Forge安装器下载失败");
            
            return installerPath;
        }

        private JsonNode MergeForgeAndVanillaJson(string forgeJsonContent, string vanillaJsonContent, Dictionary<string, object>? clientLibrary, string customVersionName)
        {
            var forgeDoc = JsonDocument.Parse(forgeJsonContent);
            var vanillaDoc = JsonDocument.Parse(vanillaJsonContent);

            var finalJson = JsonNode.Parse(forgeJsonContent)!.AsObject();
            finalJson["id"] = customVersionName;
            if (finalJson.ContainsKey("inheritsFrom"))
            {
                finalJson.Remove("inheritsFrom");
            }

            var finalLibraries = finalJson["libraries"]?.AsArray() ?? new JsonArray();
            var vanillaLibraries = vanillaDoc.RootElement.GetProperty("libraries").EnumerateArray();
            var forgeLibNames = new HashSet<string>(finalLibraries.Select(l => l!["name"]!.GetValue<string>()));

            foreach (var lib in vanillaLibraries)
            {
                var libName = lib.GetProperty("name").GetString();
                if (libName != null && !forgeLibNames.Contains(libName))
                {
                    finalLibraries.Add(JsonNode.Parse(lib.GetRawText()));
                }
            }
            
            // 只在提供了 clientLibrary 时才添加（现在我们不添加了，让Forge自己处理）
            if (clientLibrary != null)
            {
                finalLibraries.Insert(0, JsonSerializer.SerializeToNode(clientLibrary));
            }
            finalJson["libraries"] = finalLibraries;
            
            if (!finalJson.ContainsKey("assetIndex") && vanillaDoc.RootElement.TryGetProperty("assetIndex", out var assetIndex))
                finalJson["assetIndex"] = JsonNode.Parse(assetIndex.GetRawText());
            if (!finalJson.ContainsKey("assets") && vanillaDoc.RootElement.TryGetProperty("assets", out var assets))
                finalJson["assets"] = assets.GetString();
            if (!finalJson.ContainsKey("javaVersion") && vanillaDoc.RootElement.TryGetProperty("javaVersion", out var javaVersion))
                finalJson["javaVersion"] = JsonNode.Parse(javaVersion.GetRawText());

            return finalJson;
        }
        
        private async Task DownloadAssetsForForge(string gameDirectory, string customVersionName)
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                DownloadStatusText.Text = "正在下载游戏资源文件...";
                DownloadOverallProgressBar.Value = 80;
            }));

            var assetsResult = await AssetsDownloadService.DownloadAndCheckAssetsAsync(
                gameDirectory,
                customVersionName,
                (current, total, message, speed) => _ = Dispatcher.BeginInvoke(() => {
                    double assetsProgress = 80 + (current * 0.2);
                    if (_currentDownloadTaskId != null)
                        DownloadTaskManager.Instance.UpdateTaskProgress(_currentDownloadTaskId, assetsProgress, message, speed);
                    
                    CurrentFileText.Text = message;
                    DownloadOverallProgressBar.Value = assetsProgress;
                    DownloadOverallPercentageText.Text = $"{assetsProgress:F0}%";
                    DownloadCurrentProgressBar.Value = current;
                    DownloadSpeedText.Text = FormatSpeed(speed);
                }, System.Windows.Threading.DispatcherPriority.Background),
                _downloadCancellationToken!.Token
            );

            if (!assetsResult.Success && assetsResult.FailedAssets > 0)
            {
                _ = NotificationManager.Instance.ShowNotification(
                    "资源下载部分失败",
                    $"Forge已安装，但有 {assetsResult.FailedAssets} 个资源文件下载失败",
                    NotificationType.Warning, 8);
            }
        }

        /// <summary>
        /// 安装NeoForge
        /// </summary>
        private async Task InstallNeoForgeAsync(
            string neoforgeVersion,
            string customVersionName,
            string gameDirectory,
            LauncherConfig config,
            IProgress<DownloadProgress> progress)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[NeoForge] 开始安装 NeoForge {neoforgeVersion} for MC {currentVersion}");

                // 使用新的手动安装方法
                var success = await NeoForgeService.InstallNeoForgeAsync(
                    neoforgeVersion,
                    gameDirectory,
                    (status, currentProgress, totalProgress, currentBytes, totalBytes) =>
                    {
                        // 转换进度回调格式
                        progress?.Report(new DownloadProgress
                        {
                            Status = status,
                            CurrentFile = Path.GetFileName(status),
                            CurrentFileBytes = currentBytes,
                            CurrentFileTotalBytes = totalBytes,
                            TotalDownloadedBytes = currentBytes,
                            TotalBytes = totalBytes,
                            DownloadSpeed = 0
                        });

                        // 更新下载管理器任务进度
                        if (_currentDownloadTaskId != null && totalProgress > 0)
                        {
                            var percentage = currentProgress / totalProgress * 100;
                            DownloadTaskManager.Instance.UpdateTaskProgress(
                                _currentDownloadTaskId,
                                percentage,
                                status,
                                0 // 速度设为0
                            );
                        }
                    },
                    _downloadCancellationToken!.Token
                );

                if (!success)
                {
                    throw new Exception("NeoForge安装失败");
                }

                // 完成安装
                progress?.Report(new DownloadProgress
                {
                    Status = "NeoForge安装完成",
                    CurrentFile = "",
                    CurrentFileBytes = 100,
                    CurrentFileTotalBytes = 100
                });

                _ = Dispatcher.BeginInvoke(() =>
                {
                    DownloadOverallProgressBar.Value = 100;
                    DownloadOverallPercentageText.Text = "100%";
                    DownloadStatusText.Text = "安装完成";
                });

                // 标记任务完成
                if (_currentDownloadTaskId != null)
                {
                    DownloadTaskManager.Instance.CompleteTask(_currentDownloadTaskId);
                    _currentDownloadTaskId = null;
                }

                NotificationManager.Instance.ShowNotification(
                    "安装成功",
                    $"NeoForge {neoforgeVersion} 已安装为: {customVersionName}",
                    NotificationType.Success,
                    5
                );

                System.Diagnostics.Debug.WriteLine($"[NeoForge] 安装完成!");

                // 返回版本列表
                NavigationService?.GoBack();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NeoForge] 安装失败: {ex.Message}\n{ex.StackTrace}");
                
                // 标记任务失败
                if (_currentDownloadTaskId != null)
                {
                    DownloadTaskManager.Instance.FailTask(_currentDownloadTaskId, ex.Message);
                    _currentDownloadTaskId = null;
                }
                
                await DialogManager.Instance.ShowError(
                    "NeoForge安装失败",
                    $"安装过程中发生错误：\n{ex.Message}"
                );
            }
        }

        /// <summary>
        /// 安装Fabric
        /// </summary>
        private async Task InstallFabricAsync(
            string fabricLoaderVersion,
            string customVersionName,
            string gameDirectory,
            LauncherConfig config,
            IProgress<DownloadProgress> progress)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[Fabric] 开始安装 Fabric Loader {fabricLoaderVersion} for MC {currentVersion}");

                // 1. 更新UI - 开始安装
                _ = Dispatcher.BeginInvoke(() =>
                {
                    DownloadStatusText.Text = "正在准备安装Fabric...";
                    CurrentFileText.Text = $"Fabric Loader {fabricLoaderVersion}";
                    DownloadOverallProgressBar.Value = 10;
                    DownloadOverallPercentageText.Text = "10%";
                    DownloadSpeedText.Text = "准备中...";
                    DownloadSizeText.Text = "";
                });

                // 2. 使用FabricService安装Fabric
                var installSuccess = await FabricService.InstallFabricAsync(
                    currentVersion,
                    fabricLoaderVersion,
                    gameDirectory,
                    customVersionName,
                    (status, currentBytes, speed, totalBytes) =>
                    {
                        // 更新UI进度
                        _ = Dispatcher.BeginInvoke(() =>
                        {
                            DownloadStatusText.Text = status;
                            
                            // 根据状态计算总体进度
                            double overallProgress = 10;
                            if (status.Contains("获取Fabric配置"))
                                overallProgress = 20;
                            else if (status.Contains("下载基础版本"))
                            {
                                // 基础版本下载占40%（20-60）
                                if (totalBytes > 0 && currentBytes > 0)
                                {
                                    overallProgress = 20 + (currentBytes * 40.0 / totalBytes);
                                }
                                else
                                {
                                    overallProgress = 40;
                                }
                            }
                            else if (status.Contains("安装Fabric配置"))
                                overallProgress = 65;
                            else if (status.Contains("下载Fabric库"))
                            {
                                // 库文件下载占25%（65-90）
                                if (totalBytes > 0 && currentBytes > 0)
                                {
                                    overallProgress = 65 + (currentBytes * 25.0 / totalBytes);
                                }
                                else
                                {
                                    overallProgress = 80;
                                }
                            }
                            else if (status.Contains("完成"))
                                overallProgress = 100;

                            DownloadOverallProgressBar.Value = overallProgress;
                            DownloadOverallPercentageText.Text = $"{overallProgress:F0}%";
                            
                            // 更新当前文件进度（如果有）
                            if (totalBytes > 0)
                            {
                                var currentProgress = (currentBytes * 100.0 / totalBytes);
                                DownloadCurrentProgressBar.Value = currentProgress;
                                DownloadCurrentPercentageText.Text = $"{currentProgress:F0}%";
                                
                                // 格式化大小显示
                                DownloadSizeText.Text = $"{FormatFileSize(currentBytes)} / {FormatFileSize(totalBytes)}";
                            }
                            
                            // 更新速度显示
                            if (speed > 0)
                            {
                                DownloadSpeedText.Text = FormatSpeed(speed);
                            }
                            
                            // 更新下载任务管理器
                            if (_currentDownloadTaskId != null)
                            {
                                DownloadTaskManager.Instance.UpdateTaskProgress(
                                    _currentDownloadTaskId,
                                    overallProgress,
                                    status,
                                    speed
                                );
                            }
                        });
                    },
                    _downloadCancellationToken!.Token
                );

                if (!installSuccess)
                {
                    throw new Exception("Fabric安装失败");
                }

                // 3. 安装成功
                System.Diagnostics.Debug.WriteLine($"[Fabric] ✅ Fabric安装成功: {customVersionName}");
                
                _ = Dispatcher.BeginInvoke(() =>
                {
                    DownloadStatusText.Text = "Fabric安装完成！";
                    DownloadOverallProgressBar.Value = 100;
                    DownloadOverallPercentageText.Text = "100%";
                    DownloadCurrentProgressBar.Value = 100;
                    DownloadCurrentPercentageText.Text = "100%";
                    DownloadSpeedText.Text = "完成";
                });

                // 标记任务完成
                if (_currentDownloadTaskId != null)
                {
                    DownloadTaskManager.Instance.CompleteTask(_currentDownloadTaskId);
                    _currentDownloadTaskId = null;
                }

                // 4. 显示成功消息
                await Task.Delay(500); // 短暂延迟，让用户看到100%
                await DialogManager.Instance.ShowSuccess(
                    "安装完成",
                    $"Fabric Loader {fabricLoaderVersion} for Minecraft {currentVersion}\n\n已成功安装到版本 {customVersionName}！"
                );

                // 返回列表页面
                BackButton_Click(null!, null!);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"[Fabric] 安装已取消");
                throw; // 重新抛出，让上层处理
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Fabric] 安装失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[Fabric] 堆栈跟踪: {ex.StackTrace}");
                
                // 标记任务失败
                if (_currentDownloadTaskId != null)
                {
                    DownloadTaskManager.Instance.FailTask(_currentDownloadTaskId, ex.Message);
                    _currentDownloadTaskId = null;
                }

                _ = Dispatcher.BeginInvoke(() =>
                {
                    DownloadStatusText.Text = "安装失败";
                    DownloadOverallProgressBar.Value = 0;
                });

                await DialogManager.Instance.ShowError(
                    "安装失败",
                    $"Fabric安装失败：\n\n{ex.Message}\n\n请检查网络连接或尝试切换下载源。"
                );

                throw;
            }
        }

        /// <summary>
        /// 安装Quilt
        /// </summary>
        private async Task InstallQuiltAsync(
            string quiltLoaderVersion,
            string customVersionName,
            string gameDirectory,
            LauncherConfig config,
            IProgress<DownloadProgress> progress)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[Quilt] 开始安装 Quilt Loader {quiltLoaderVersion} for MC {currentVersion}");

                // 移除显示文本中的标记（如 "(最新)"）
                var actualLoaderVersion = quiltLoaderVersion.Replace(" (最新)", "").Replace(" (推荐)", "").Trim();

                // 1. 更新UI - 开始安装
                _ = Dispatcher.BeginInvoke(() =>
                {
                    DownloadStatusText.Text = "正在准备安装Quilt...";
                    CurrentFileText.Text = $"Quilt Loader {actualLoaderVersion}";
                    DownloadOverallProgressBar.Value = 10;
                    DownloadOverallPercentageText.Text = "10%";
                    DownloadSpeedText.Text = "准备中...";
                    DownloadSizeText.Text = "";
                });

                // 2. 使用QuiltService安装Quilt
                var installSuccess = await QuiltService.InstallQuiltAsync(
                    currentVersion,
                    actualLoaderVersion,
                    gameDirectory,
                    customVersionName,
                    (status, currentBytes, speed, totalBytes) =>
                    {
                        // 更新UI进度
                        _ = Dispatcher.BeginInvoke(() =>
                        {
                            DownloadStatusText.Text = status;
                            
                            // 根据状态计算总体进度
                            double overallProgress = 10;
                            if (status.Contains("获取Quilt配置"))
                                overallProgress = 20;
                            else if (status.Contains("下载基础版本"))
                            {
                                // 基础版本下载占40%（20-60）
                                if (totalBytes > 0 && currentBytes > 0)
                                {
                                    overallProgress = 20 + (currentBytes * 40.0 / totalBytes);
                                }
                                else
                                {
                                    overallProgress = 40;
                                }
                            }
                            else if (status.Contains("安装Quilt配置"))
                                overallProgress = 65;
                            else if (status.Contains("下载Quilt库"))
                            {
                                // 库文件下载占25%（65-90）
                                if (totalBytes > 0 && currentBytes > 0)
                                {
                                    overallProgress = 65 + (currentBytes * 25.0 / totalBytes);
                                }
                                else
                                {
                                    overallProgress = 80;
                                }
                            }
                            else if (status.Contains("完成"))
                                overallProgress = 100;

                            DownloadOverallProgressBar.Value = overallProgress;
                            DownloadOverallPercentageText.Text = $"{overallProgress:F0}%";
                            
                            // 更新当前文件进度（如果有）
                            if (totalBytes > 0)
                            {
                                var currentProgress = (currentBytes * 100.0 / totalBytes);
                                DownloadCurrentProgressBar.Value = currentProgress;
                                DownloadCurrentPercentageText.Text = $"{currentProgress:F0}%";
                                
                                // 格式化大小显示
                                DownloadSizeText.Text = $"{FormatFileSize(currentBytes)} / {FormatFileSize(totalBytes)}";
                            }
                            
                            // 更新速度显示
                            if (speed > 0)
                            {
                                DownloadSpeedText.Text = FormatSpeed(speed);
                            }
                            
                            // 更新下载任务管理器
                            if (_currentDownloadTaskId != null)
                            {
                                DownloadTaskManager.Instance.UpdateTaskProgress(
                                    _currentDownloadTaskId,
                                    overallProgress,
                                    status,
                                    speed
                                );
                            }
                        });
                    },
                    _downloadCancellationToken!.Token
                );

                if (!installSuccess)
                {
                    throw new Exception("Quilt安装失败");
                }

                // 3. 安装成功
                System.Diagnostics.Debug.WriteLine($"[Quilt] ✅ Quilt安装成功: {customVersionName}");
                
                _ = Dispatcher.BeginInvoke(() =>
                {
                    DownloadStatusText.Text = "Quilt安装完成！";
                    DownloadOverallProgressBar.Value = 100;
                    DownloadOverallPercentageText.Text = "100%";
                    DownloadCurrentProgressBar.Value = 100;
                    DownloadCurrentPercentageText.Text = "100%";
                    DownloadSpeedText.Text = "完成";
                });

                // 标记任务完成
                if (_currentDownloadTaskId != null)
                {
                    DownloadTaskManager.Instance.CompleteTask(_currentDownloadTaskId);
                    _currentDownloadTaskId = null;
                }

                // 4. 显示成功消息
                await Task.Delay(500); // 短暂延迟，让用户看到100%
                await DialogManager.Instance.ShowSuccess(
                    "安装完成",
                    $"Quilt Loader {actualLoaderVersion} for Minecraft {currentVersion}\n\n已成功安装到版本 {customVersionName}！"
                );

                // 返回列表页面
                BackButton_Click(null!, null!);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"[Quilt] 安装已取消");
                throw; // 重新抛出，让上层处理
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Quilt] 安装失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[Quilt] 堆栈跟踪: {ex.StackTrace}");
                
                // 标记任务失败
                if (_currentDownloadTaskId != null)
                {
                    DownloadTaskManager.Instance.FailTask(_currentDownloadTaskId, ex.Message);
                    _currentDownloadTaskId = null;
                }

                _ = Dispatcher.BeginInvoke(() =>
                {
                    DownloadStatusText.Text = "安装失败";
                    DownloadOverallProgressBar.Value = 0;
                });

                await DialogManager.Instance.ShowError(
                    "安装失败",
                    $"Quilt安装失败：\n\n{ex.Message}\n\n请检查网络连接或尝试切换下载源。"
                );

                throw;
            }
        }

        /// <summary>
        /// 安装OptiFine
        /// </summary>
        private async Task InstallOptiFineAsync(
            string optifineVersion,
            string customVersionName,
            string gameDirectory,
            LauncherConfig config,
            IProgress<DownloadProgress> progress)
        {
            // 用于清理的临时变量（在外层作用域声明）
            string? tempVanillaDir = null;
            bool needsCleanup = false;
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"[OptiFine] 开始安装 OptiFine {optifineVersion} for MC {currentVersion}");

                // 1. 更新UI - 开始准备
                _ = Dispatcher.BeginInvoke(() =>
                {
                    DownloadStatusText.Text = "正在准备安装OptiFine...";
                    CurrentFileText.Text = $"OptiFine {optifineVersion}";
                    DownloadOverallProgressBar.Value = 5;
                    DownloadOverallPercentageText.Text = "5%";
                    DownloadSpeedText.Text = "准备中...";
                    DownloadSizeText.Text = "";
                });

                // 2. 从ComboBox获取选中的OptiFine版本对象
                OptifineVersionModel? selectedOptifineVersion = null;
                await Dispatcher.InvokeAsync(() =>
                {
                    var selectedItem = OptiFineVersionComboBox?.SelectedItem as ComboBoxItem;
                    selectedOptifineVersion = selectedItem?.Tag as OptifineVersionModel;
                });

                if (selectedOptifineVersion == null)
                {
                    throw new Exception("未能获取OptiFine版本信息");
                }

                System.Diagnostics.Debug.WriteLine($"[OptiFine] 选中的版本: {selectedOptifineVersion.DisplayName}");

                // 3. 检查并下载基础 Minecraft 版本
                // 策略：不在 versions/ 下创建原版文件夹（避免与用户手动安装的原版冲突）
                // 而是在 OptiFine 版本文件夹内创建临时目录来存放原版文件
                var optiFineVersionDir = Path.Combine(gameDirectory, "versions", customVersionName);
                tempVanillaDir = Path.Combine(optiFineVersionDir, ".temp-vanilla");
                
                // 重要：DownloadService 会在 gameDirectory/versions/{versionName}/ 下创建文件
                // 所以实际路径是 tempVanillaDir/versions/currentVersion/
                var tempVanillaVersionDir = Path.Combine(tempVanillaDir, "versions", currentVersion);
                
                // 先检查用户是否已经手动安装了原版（在标准位置）
                var standardBaseVersionDir = Path.Combine(gameDirectory, "versions", currentVersion);
                var standardBaseVersionJar = Path.Combine(standardBaseVersionDir, $"{currentVersion}.jar");
                var standardBaseVersionJson = Path.Combine(standardBaseVersionDir, $"{currentVersion}.json");
                
                string actualBaseVersionDir;
                
                if (File.Exists(standardBaseVersionJar) && File.Exists(standardBaseVersionJson))
                {
                    // 用户已有原版，直接使用
                    System.Diagnostics.Debug.WriteLine($"[OptiFine] ✅ 检测到已安装的原版 Minecraft {currentVersion}，直接使用");
                    actualBaseVersionDir = standardBaseVersionDir;
                }
                else
                {
                    // 需要下载原版到临时目录
                    System.Diagnostics.Debug.WriteLine($"[OptiFine] 原版不存在，开始下载");
                    System.Diagnostics.Debug.WriteLine($"[OptiFine] - 临时游戏目录: {tempVanillaDir}");
                    System.Diagnostics.Debug.WriteLine($"[OptiFine] - 预期版本路径: {tempVanillaVersionDir}");
                    needsCleanup = true;
                    
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        DownloadStatusText.Text = $"正在下载原版 Minecraft {currentVersion}...";
                        DownloadOverallProgressBar.Value = 5;
                        DownloadOverallPercentageText.Text = "5%";
                    });

                    // 创建临时目录
                    if (!Directory.Exists(tempVanillaDir))
                    {
                        Directory.CreateDirectory(tempVanillaDir);
                    }

                    // 使用 DownloadService 下载原版 Minecraft 到临时目录
                    var vanillaProgress = new Progress<DownloadProgress>(p =>
                    {
                        // 下载进度映射到 5-25%
                        var overallProgress = 5 + (p.OverallPercentage / 100.0 * 20);
                        
                        _ = Dispatcher.BeginInvoke(() =>
                        {
                            DownloadStatusText.Text = p.Status;
                            DownloadOverallProgressBar.Value = overallProgress;
                            DownloadOverallPercentageText.Text = $"{overallProgress:F0}%";
                            DownloadCurrentProgressBar.Value = p.CurrentFilePercentage;
                            DownloadCurrentPercentageText.Text = $"{p.CurrentFilePercentage:F0}%";
                            
                            if (p.CurrentFileTotalBytes > 0)
                            {
                                DownloadSizeText.Text = $"{FormatFileSize(p.CurrentFileBytes)} / {FormatFileSize(p.CurrentFileTotalBytes)}";
                            }
                            
                            // 更新下载任务管理器
                            if (_currentDownloadTaskId != null)
                            {
                                DownloadTaskManager.Instance.UpdateTaskProgress(
                                    _currentDownloadTaskId,
                                    overallProgress,
                                    p.Status,
                                    (long)p.DownloadSpeed
                                );
                            }
                        });
                    });
                    
                    // 临时修改 gameDirectory，让下载到临时目录
                    bool downloadSuccess = await DownloadService.DownloadMinecraftVersion(
                        currentVersion,
                        tempVanillaDir, // 下载到临时目录
                        currentVersion, // 版本名称与版本ID相同
                        vanillaProgress,
                        _downloadCancellationToken!.Token
                    );

                    if (!downloadSuccess)
                    {
                        throw new Exception($"下载原版 Minecraft {currentVersion} 失败");
                    }

                    System.Diagnostics.Debug.WriteLine($"[OptiFine] ✅ 原版 Minecraft {currentVersion} 下载到临时目录完成");
                    
                    // 设置实际的基础版本路径
                    actualBaseVersionDir = tempVanillaVersionDir;
                    
                    // 验证文件是否存在
                    var downloadedJar = Path.Combine(actualBaseVersionDir, $"{currentVersion}.jar");
                    var downloadedJson = Path.Combine(actualBaseVersionDir, $"{currentVersion}.json");
                    System.Diagnostics.Debug.WriteLine($"[OptiFine] - 检查 JAR: {downloadedJar} -> {File.Exists(downloadedJar)}");
                    System.Diagnostics.Debug.WriteLine($"[OptiFine] - 检查 JSON: {downloadedJson} -> {File.Exists(downloadedJson)}");
                }

                // 4. 下载OptiFine安装包
                var tempDir = Path.Combine(Path.GetTempPath(), "ObsMCLauncher", "OptiFine");
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }

                var installerPath = Path.Combine(tempDir, selectedOptifineVersion.Filename);
                
                _ = Dispatcher.BeginInvoke(() =>
                {
                    DownloadStatusText.Text = "正在下载OptiFine安装包...";
                    DownloadOverallProgressBar.Value = 25;
                    DownloadOverallPercentageText.Text = "25%";
                });

                var optifineService = new OptiFineService(DownloadSourceManager.Instance);
                await optifineService.DownloadOptifineInstallerAsync(
                    selectedOptifineVersion,
                    installerPath,
                    (status, currentProg, totalProg, bytes, totalBytes) =>
                    {
                        // 下载进度映射到 25-35%
                        var overallProgress = 25 + (currentProg / 100.0 * 10);
                        
                        _ = Dispatcher.BeginInvoke(() =>
                        {
                            DownloadStatusText.Text = status;
                            DownloadOverallProgressBar.Value = overallProgress;
                            DownloadOverallPercentageText.Text = $"{overallProgress:F0}%";
                            DownloadCurrentProgressBar.Value = currentProg;
                            DownloadCurrentPercentageText.Text = $"{currentProg:F0}%";
                            
                            if (totalBytes > 0)
                            {
                                DownloadSizeText.Text = $"{FormatFileSize(bytes)} / {FormatFileSize(totalBytes)}";
                                var speed = bytes / (DateTime.Now - DateTime.Now.AddSeconds(-1)).TotalSeconds;
                                DownloadSpeedText.Text = FormatSpeed((long)speed);
                            }
                            
                            // 更新下载任务管理器
                            if (_currentDownloadTaskId != null)
                            {
                                DownloadTaskManager.Instance.UpdateTaskProgress(
                                    _currentDownloadTaskId,
                                    overallProgress,
                                    status,
                                    (long)(bytes / (DateTime.Now - DateTime.Now.AddSeconds(-1)).TotalSeconds)
                                );
                            }
                        });
                    },
                    _downloadCancellationToken!.Token
                );

                System.Diagnostics.Debug.WriteLine($"[OptiFine] ✅ 安装包下载完成: {installerPath}");

                // 5. 获取Java路径
                var javaPath = config.JavaPath;
                
                if (string.IsNullOrEmpty(javaPath) || !File.Exists(javaPath))
                {
                    var detectedJavas = JavaDetector.DetectAllJava();
                    var java = detectedJavas.FirstOrDefault(j => j.MajorVersion >= 8);
                    
                    if (java == null)
                    {
                        throw new Exception("未找到可用的Java运行时，请在设置中配置Java路径");
                    }
                    
                    javaPath = java.Path;
                    System.Diagnostics.Debug.WriteLine($"[OptiFine] 使用自动检测的Java: {javaPath}");
                }

                // 6. 执行OptiFine安装
                _ = Dispatcher.BeginInvoke(() =>
                {
                    DownloadStatusText.Text = "正在安装OptiFine...";
                    DownloadOverallProgressBar.Value = 35;
                    DownloadOverallPercentageText.Text = "35%";
                    CurrentFileText.Text = "执行OptiFine安装程序";
                });

                var installSuccess = await optifineService.InstallOptifineAsync(
                    selectedOptifineVersion,
                    installerPath,
                    gameDirectory,
                    currentVersion, // 基础 Minecraft 版本
                    javaPath,
                    customVersionName,
                    actualBaseVersionDir, // 传递实际的基础版本路径（可能是标准路径或临时路径）
                    (status, currentProg, totalProg, bytes, totalBytes) =>
                    {
                        // 安装进度映射到 35-100%
                        var overallProgress = 35 + (currentProg / 100.0 * 65);
                        
                        _ = Dispatcher.BeginInvoke(() =>
                        {
                            DownloadStatusText.Text = status;
                            DownloadOverallProgressBar.Value = overallProgress;
                            DownloadOverallPercentageText.Text = $"{overallProgress:F0}%";
                            DownloadCurrentProgressBar.Value = currentProg;
                            DownloadCurrentPercentageText.Text = $"{currentProg:F0}%";
                            
                            // 更新下载任务管理器
                            if (_currentDownloadTaskId != null)
                            {
                                DownloadTaskManager.Instance.UpdateTaskProgress(
                                    _currentDownloadTaskId,
                                    overallProgress,
                                    status,
                                    0
                                );
                            }
                        });
                    },
                    _downloadCancellationToken!.Token
                );

                if (!installSuccess)
                {
                    throw new Exception("OptiFine安装失败");
                }

                // 7. 复制必要文件到正确位置（清理前）
                try
                {
                    // 7.1 复制原版 client.jar 作为 OptiFine 版本的主 JAR
                    var sourceJar = Path.Combine(actualBaseVersionDir, $"{currentVersion}.jar");
                    var targetJar = Path.Combine(optiFineVersionDir, $"{customVersionName}.jar");
                    
                    if (File.Exists(sourceJar) && !File.Exists(targetJar))
                    {
                        System.Diagnostics.Debug.WriteLine($"[OptiFine] 复制主 JAR: {sourceJar} -> {targetJar}");
                        File.Copy(sourceJar, targetJar, true);
                        System.Diagnostics.Debug.WriteLine($"[OptiFine] ✅ 主 JAR 已复制");
                    }
                    
                    // 7.2 复制父版本 JSON 到 OptiFine 文件夹内（供 inheritsFrom 使用，避免创建额外的版本文件夹）
                    var sourceJson = Path.Combine(actualBaseVersionDir, $"{currentVersion}.json");
                    var targetJsonInOptiFineDir = Path.Combine(optiFineVersionDir, $"{currentVersion}.json");
                    
                    if (File.Exists(sourceJson) && !File.Exists(targetJsonInOptiFineDir))
                    {
                        System.Diagnostics.Debug.WriteLine($"[OptiFine] 复制父版本 JSON 到 OptiFine 文件夹: {sourceJson} -> {targetJsonInOptiFineDir}");
                        File.Copy(sourceJson, targetJsonInOptiFineDir, true);
                        System.Diagnostics.Debug.WriteLine($"[OptiFine] ✅ 父版本 JSON 已复制到 OptiFine 文件夹");
                    }
                    
                    // 7.3 如果父版本JAR也不存在于标准位置，也复制到OptiFine文件夹
                    var standardVersionJar = Path.Combine(gameDirectory, "versions", currentVersion, $"{currentVersion}.jar");
                    if (!File.Exists(standardVersionJar))
                    {
                        var targetJarInOptiFineDir = Path.Combine(optiFineVersionDir, $"{currentVersion}.jar");
                        if (File.Exists(sourceJar) && !File.Exists(targetJarInOptiFineDir))
                        {
                            System.Diagnostics.Debug.WriteLine($"[OptiFine] 复制父版本 JAR 到 OptiFine 文件夹: {sourceJar} -> {targetJarInOptiFineDir}");
                            File.Copy(sourceJar, targetJarInOptiFineDir, true);
                            System.Diagnostics.Debug.WriteLine($"[OptiFine] ✅ 父版本 JAR 已复制到 OptiFine 文件夹");
                        }
                    }
                }
                catch (Exception copyEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[OptiFine] 复制必要文件失败: {copyEx.Message}");
                    // 复制失败可能影响启动，但不阻止安装完成
                }

                // 8. 清理临时文件
                try
                {
                    // 清理 OptiFine 安装包
                    if (File.Exists(installerPath))
                    {
                        File.Delete(installerPath);
                        System.Diagnostics.Debug.WriteLine($"[OptiFine] ✅ 已删除临时 OptiFine 安装包");
                    }
                    
                    // 清理临时的原版文件夹（如果是我们自己下载的）
                    if (needsCleanup && Directory.Exists(tempVanillaDir))
                    {
                        System.Diagnostics.Debug.WriteLine($"[OptiFine] 正在清理临时原版文件夹: {tempVanillaDir}");
                        Directory.Delete(tempVanillaDir, true);
                        System.Diagnostics.Debug.WriteLine($"[OptiFine] ✅ 临时原版文件夹已清理");
                    }
                }
                catch (Exception cleanupEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[OptiFine] 清理临时文件失败: {cleanupEx.Message}");
                    // 清理失败不影响安装结果，只记录日志
                }

                // 9. 安装成功
                System.Diagnostics.Debug.WriteLine($"[OptiFine] ✅ OptiFine安装成功: {customVersionName}");
                
                _ = Dispatcher.BeginInvoke(() =>
                {
                    DownloadStatusText.Text = "OptiFine安装完成！";
                    DownloadOverallProgressBar.Value = 100;
                    DownloadOverallPercentageText.Text = "100%";
                    DownloadCurrentProgressBar.Value = 100;
                    DownloadCurrentPercentageText.Text = "100%";
                    DownloadSpeedText.Text = "完成";
                });

                // 标记任务完成
                if (_currentDownloadTaskId != null)
                {
                    DownloadTaskManager.Instance.CompleteTask(_currentDownloadTaskId);
                    _currentDownloadTaskId = null;
                }

                // 10. 显示成功消息
                await Task.Delay(500); // 短暂延迟，让用户看到100%
                await DialogManager.Instance.ShowSuccess(
                    "安装完成",
                    $"OptiFine {selectedOptifineVersion.FullVersion} for Minecraft {currentVersion}\n\n已成功安装到版本 {customVersionName}！"
                );

                // 返回列表页面
                BackButton_Click(null!, null!);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"[OptiFine] 安装已取消");
                throw; // 重新抛出，让上层处理
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OptiFine] 安装失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[OptiFine] 堆栈跟踪: {ex.StackTrace}");
                
                // 清理临时文件（即使安装失败）
                try
                {
                    if (needsCleanup && !string.IsNullOrEmpty(tempVanillaDir) && Directory.Exists(tempVanillaDir))
                    {
                        System.Diagnostics.Debug.WriteLine($"[OptiFine] 安装失败，清理临时原版文件夹: {tempVanillaDir}");
                        Directory.Delete(tempVanillaDir, true);
                        System.Diagnostics.Debug.WriteLine($"[OptiFine] ✅ 临时原版文件夹已清理");
                    }
                }
                catch (Exception cleanupEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[OptiFine] 清理临时文件失败: {cleanupEx.Message}");
                }
                
                // 标记任务失败
                if (_currentDownloadTaskId != null)
                {
                    DownloadTaskManager.Instance.FailTask(_currentDownloadTaskId, ex.Message);
                    _currentDownloadTaskId = null;
                }

                _ = Dispatcher.BeginInvoke(() =>
                {
                    DownloadStatusText.Text = "安装失败";
                    DownloadOverallProgressBar.Value = 0;
                });

                await DialogManager.Instance.ShowError(
                    "安装失败",
                    $"OptiFine安装失败：\n\n{ex.Message}\n\n请检查网络连接或尝试切换下载源。"
                );

                throw;
            }
        }

        private Task<bool> ValidateMinecraftJar(string jarPath)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(jarPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"[Forge] ❌ JAR文件不存在: {jarPath}");
                        return false;
                    }

                    using var zip = System.IO.Compression.ZipFile.OpenRead(jarPath);
                    var minecraftClass = zip.Entries.FirstOrDefault(e => e.FullName == "net/minecraft/client/Minecraft.class");
                    
                    if (minecraftClass == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Forge] ❌ JAR中找不到 Minecraft.class，总条目数: {zip.Entries.Count}");
                        // 列出前10个条目用于调试
                        var first10 = zip.Entries.Take(10).Select(e => e.FullName);
                        System.Diagnostics.Debug.WriteLine($"[Forge] 前10个条目: {string.Join(", ", first10)}");
                        return false;
                    }

                    System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 找到 Minecraft.class，大小: {minecraftClass.Length} 字节");
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Forge] ❌ 验证JAR时出错: {ex.Message}");
                    return false;
                }
            });
        }

        private async Task CleanupPreviousForgeInstallation(string gameDirectory, string customVersionName)
        {
            string versionDir = Path.Combine(gameDirectory, "versions", customVersionName);
            if (Directory.Exists(versionDir))
            {
                await Task.Run(() => Directory.Delete(versionDir, true));
                System.Diagnostics.Debug.WriteLine($"[Forge] 🗑️ 已清理旧的安装目录: {customVersionName}");
            }
        }

        /// <summary>
        /// 模拟Forge安装器进度（因为官方安装器不提供实时进度）
        /// </summary>
        private System.Threading.Timer SimulateForgeInstallerProgress(IProgress<DownloadProgress>? progress)
        {
            double currentProgress = 50;
            var random = new Random();
            
            var timer = new System.Threading.Timer(_ =>
            {
                try
                {
                    // 缓慢增加进度，从50%到69%
                    if (currentProgress < 69)
                    {
                        currentProgress += random.NextDouble() * 0.5; // 每次增加0-0.5%
                        
                        // 根据进度确定当前状态文本
                        string statusText;
                        if (currentProgress < 55)
                            statusText = "正在下载依赖库...";
                        else if (currentProgress < 60)
                            statusText = "正在处理混淆映射...";
                        else if (currentProgress < 65)
                            statusText = "正在应用访问转换器...";
                        else
                            statusText = "正在生成Forge客户端...";
                        
                        // 通过progress报告（自动更新UI和DownloadTaskManager）
                        progress?.Report(new DownloadProgress
                        {
                            Status = statusText,
                            CurrentFile = statusText,
                            CurrentFileBytes = (long)currentProgress,
                            CurrentFileTotalBytes = 100,
                            CompletedFiles = 2,
                            TotalFiles = 3
                        });
                    }
                }
                catch { }
            }, null, 500, 500); // 每500ms更新一次
            
            return timer;
        }

        private async Task<bool> RunForgeInstallerAsync(string installerPath, string gameDirectory, string mcVersion, string forgeVersion, LauncherConfig config)
        {
            return await Task.Run(async () =>
            {
                // 确保 launcher_profiles.json 存在（Forge安装器需要此文件）
                string profilesPath = Path.Combine(gameDirectory, "launcher_profiles.json");
                if (!File.Exists(profilesPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[Forge] 创建 launcher_profiles.json");
                    var defaultProfiles = new
                    {
                        profiles = new { },
                        selectedProfile = (string?)null,
                        clientToken = Guid.NewGuid().ToString(),
                        authenticationDatabase = new { },
                        launcherVersion = new
                        {
                            name = "ObsMCLauncher",
                            format = 21
                        }
                    };
                    await File.WriteAllTextAsync(profilesPath, System.Text.Json.JsonSerializer.Serialize(defaultProfiles, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }
                
                // ========== 原版文件管理 ==========
                // Forge安装器期望原版文件在 versions/{mcVersion}/ 目录
                // 策略：直接使用标准原版目录，避免重复复制
                string standardVanillaDir = Path.Combine(gameDirectory, "versions", mcVersion);
                string standardJsonPath = Path.Combine(standardVanillaDir, $"{mcVersion}.json");
                string standardJarPath = Path.Combine(standardVanillaDir, $"{mcVersion}.jar");
                
                // 查找Forge版本目录中的原版文件
                string versionsDir = Path.Combine(gameDirectory, "versions");
                string? forgeVersionDir = null;
                
                if (Directory.Exists(versionsDir))
                {
                    var dirs = Directory.GetDirectories(versionsDir);
                    foreach (var dir in dirs)
                    {
                        var dirName = Path.GetFileName(dir);
                        if (dirName.StartsWith($"{mcVersion}-forge-") || dirName.Contains($"-{mcVersion}-"))
                        {
                            forgeVersionDir = dir;
                            System.Diagnostics.Debug.WriteLine($"[Forge] 找到Forge版本目录: {dirName}");
                            break;
                        }
                    }
                }
                
                // 如果原版文件不在标准位置，从Forge目录移动过去（而不是复制）
                if (!string.IsNullOrEmpty(forgeVersionDir))
                {
                    string forgeJsonPath = Path.Combine(forgeVersionDir, $"{mcVersion}.json");
                    string forgeJarPath = Path.Combine(forgeVersionDir, $"{mcVersion}.jar");
                    
                    if (File.Exists(forgeJsonPath) && !File.Exists(standardJsonPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"[Forge] 移动原版文件到标准位置供安装器使用: {mcVersion}");
                        Directory.CreateDirectory(standardVanillaDir);
                        File.Move(forgeJsonPath, standardJsonPath, true);
                        if (File.Exists(forgeJarPath))
                        {
                            File.Move(forgeJarPath, standardJarPath, true);
                        }
                    }
                }
                // ========== 原版文件管理结束 ==========
                
                // 判断是否是新版本Forge（1.13+）
                bool isNewVersion = IsForgeInstallerNewVersion(mcVersion);
                
                // 判断是否是非常旧的版本（1.9 之前）
                bool isVeryOldVersion = IsVeryOldForgeVersion(mcVersion);
                
                // 准备多个可能的参数组合（按优先级排序）
                List<string> argumentsList = new List<string>();
                
                if (isNewVersion)
                {
                    // 1.13+ 版本：优先使用 --installClient
                    argumentsList.Add($"--installClient \"{gameDirectory}\"");
                }
                else if (isVeryOldVersion)
                {
                    // 1.8.9 及更早版本：这些版本的安装器参数格式不同
                    // 注意：--install 会安装服务器端而不是客户端，已删除
                    argumentsList.Add($"--installClient \"{gameDirectory}\"");  // 某些版本可能支持
                    argumentsList.Add($"--install-client \"{gameDirectory}\"");  // 带连字符的格式
                    argumentsList.Add($"-installClient \"{gameDirectory}\"");  // 短横线版本
                    System.Diagnostics.Debug.WriteLine($"[Forge] 检测到非常旧的版本 ({mcVersion})，将尝试多种参数格式");
                }
                else
                {
                    // 1.9 - 1.12.2：这些版本的安装器参数格式各不相同，尝试多种组合
                    // 1.10-1.12 的一些版本可能不支持 --installClient，需要手动安装
                    argumentsList.Add($"--installClient \"{gameDirectory}\"");  // 尝试标准参数
                    argumentsList.Add($"--install-client \"{gameDirectory}\"");  // 带连字符的格式
                    System.Diagnostics.Debug.WriteLine($"[Forge] 检测到中间版本 ({mcVersion})，将尝试多种参数格式");
                }
                
                System.Diagnostics.Debug.WriteLine($"[Forge] 准备尝试 {argumentsList.Count} 种参数组合");
                
                bool installResult = false;
                try
                {
                    // 依次尝试每种参数组合
                    for (int i = 0; i < argumentsList.Count; i++)
                    {
                        string args = argumentsList[i];
                        System.Diagnostics.Debug.WriteLine($"[Forge] 尝试参数 {i + 1}/{argumentsList.Count}: {(string.IsNullOrEmpty(args) ? "(无参数)" : args)}");
                        
                        bool success = await TryRunForgeInstallerWithArgs(installerPath, gameDirectory, args);
                        
                        if (success)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 参数 {i + 1} 安装器执行成功");
                            
                            // 对于非常旧的版本，验证是否真的创建了客户端版本目录（因为--install可能安装的是服务器端）
                            if (isVeryOldVersion)
                            {
                                // 检查可能的版本目录
                                string checkVersionsDir = Path.Combine(gameDirectory, "versions");
                                bool foundClientVersion = false;
                                
                                if (Directory.Exists(checkVersionsDir))
                                {
                                    var dirs = Directory.GetDirectories(checkVersionsDir);
                                    foreach (var dir in dirs)
                                    {
                                        var dirName = Path.GetFileName(dir);
                                        // 检查是否是Forge客户端目录（包含.json文件）
                                        if (dirName.Contains("forge") && dirName.Contains(mcVersion))
                                        {
                                            var jsonPath = Path.Combine(dir, $"{dirName}.json");
                                            if (File.Exists(jsonPath))
                                            {
                                                foundClientVersion = true;
                                                System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 找到客户端版本目录: {dirName}");
                                                break;
                                            }
                                        }
                                    }
                                }
                                
                                if (foundClientVersion)
                                {
                                    installResult = true;
                                    break;
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Forge] ⚠️ 安装器成功但未找到客户端版本目录，可能安装的是服务器端");
                                    System.Diagnostics.Debug.WriteLine($"[Forge] ⚠️ 继续尝试下一个参数...");
                                    continue;
                                }
                            }
                            
                            installResult = true;
                            break;
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"[Forge] ⚠️ 参数 {i + 1} 失败，尝试下一个...");
                    }
                    
                    // 如果所有参数都失败了
                    if (!installResult)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Forge] ❌ 所有 {argumentsList.Count} 种参数组合都失败");
                        
                        // 对于1.12.2及更早的版本，尝试手动安装
                        // （包括非常旧的版本和中间版本）
                        if (isVeryOldVersion || !isNewVersion)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Forge] 安装器参数不适用，尝试手动安装客户端...");
                            // 传递预先创建的Forge目录和标准化的版本名
                            string targetVersionName = $"{mcVersion}-forge-{forgeVersion}";
                            string targetVersionDir = Path.Combine(gameDirectory, "versions", targetVersionName);
                            installResult = await ManualInstallVeryOldForgeClient(installerPath, gameDirectory, mcVersion, targetVersionName, targetVersionDir, config);
                        }
                    }
                }
                finally
                {
                    // ========== 清理临时文件 ==========
                    // 注意：原版文件夹不在这里清理，会在RenameForgeVersionAsync之后清理
                    // 因为RenameForgeVersionAsync需要从原版文件夹复制JAR文件
                    // ========== 清理结束 ==========
                }
                
                return installResult;
            });
        }

        /// <summary>
        /// 运行NeoForge安装器
        /// </summary>
        private async Task<bool> RunNeoForgeInstallerAsync(string installerPath, string gameDirectory, string mcVersion, string neoforgeVersion, LauncherConfig config)
        {
            return await Task.Run(async () =>
            {
                // 确保 launcher_profiles.json 存在（安装器需要此文件）
                string profilesPath = Path.Combine(gameDirectory, "launcher_profiles.json");
                if (!File.Exists(profilesPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[NeoForge] 创建 launcher_profiles.json");
                    var defaultProfiles = new
                    {
                        profiles = new { },
                        selectedProfile = (string?)null,
                        clientToken = Guid.NewGuid().ToString(),
                        authenticationDatabase = new { },
                        launcherVersion = new
                        {
                            name = "ObsMCLauncher",
                            format = 21
                        }
                    };
                    await File.WriteAllTextAsync(profilesPath, System.Text.Json.JsonSerializer.Serialize(defaultProfiles, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }

                // NeoForge 使用与新版 Forge 相同的安装方式
                string arguments = $"--installClient \"{gameDirectory}\"";
                
                System.Diagnostics.Debug.WriteLine($"[NeoForge] 执行安装器: {installerPath}");
                System.Diagnostics.Debug.WriteLine($"[NeoForge] 参数: {arguments}");

                try
                {
                    bool success = await TryRunNeoForgeInstallerWithArgs(installerPath, gameDirectory, arguments);
                    
                    if (success)
                    {
                        System.Diagnostics.Debug.WriteLine($"[NeoForge] ✅ 安装器执行成功");
                        return true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[NeoForge] ❌ 安装器执行失败");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[NeoForge] 安装器执行出错: {ex.Message}");
                    return false;
                }
            });
        }

        /// <summary>
        /// 使用指定参数尝试运行NeoForge安装器
        /// </summary>
        private async Task<bool> TryRunNeoForgeInstallerWithArgs(string installerPath, string gameDirectory, string arguments)
        {
            // 查找Java路径
            var config = LauncherConfig.Load();
            var javaPath = config.JavaPath;
            
            if (string.IsNullOrEmpty(javaPath) || !File.Exists(javaPath))
            {
                // 尝试使用系统Java
                javaPath = "java";
                System.Diagnostics.Debug.WriteLine($"[NeoForge] 使用系统Java: {javaPath}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[NeoForge] 使用配置的Java: {javaPath}");
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = $"-jar \"{installerPath}\" {arguments}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = gameDirectory
            };

            System.Diagnostics.Debug.WriteLine($"[NeoForge] 执行命令: {startInfo.FileName} {startInfo.Arguments}");

            using (var process = new System.Diagnostics.Process { StartInfo = startInfo })
            {
                var outputBuilder = new System.Text.StringBuilder();
                var errorBuilder = new System.Text.StringBuilder();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        System.Diagnostics.Debug.WriteLine($"[NeoForge] {e.Data}");
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorBuilder.AppendLine(e.Data);
                        System.Diagnostics.Debug.WriteLine($"[NeoForge Error] {e.Data}");
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                var output = outputBuilder.ToString();
                var errors = errorBuilder.ToString();

                System.Diagnostics.Debug.WriteLine($"[NeoForge] 安装器退出码: {process.ExitCode}");

                // 检查是否成功
                if (process.ExitCode == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[NeoForge] 安装器返回成功");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[NeoForge] 安装器返回失败，退出码: {process.ExitCode}");
                    if (!string.IsNullOrEmpty(errors))
                    {
                        System.Diagnostics.Debug.WriteLine($"[NeoForge] 错误信息: {errors}");
                    }
                    return false;
                }
            }
        }
        
        /// <summary>
        /// 使用指定参数尝试运行Forge安装器
        /// </summary>
        private async Task<bool> TryRunForgeInstallerWithArgs(string installerPath, string gameDirectory, string arguments)
        {
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "java";
            process.StartInfo.Arguments = string.IsNullOrEmpty(arguments) 
                ? $"-jar \"{installerPath}\""
                : $"-jar \"{installerPath}\" {arguments}";
            process.StartInfo.WorkingDirectory = gameDirectory;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();
            int outputLineCount = 0;
            int lastReportedLine = 0;

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                    outputLineCount++;
                    
                    // 只记录关键信息，忽略大量的数据文件处理输出（避免UI卡死）
                    // 只记录：设置信息、消息、下载信息、错误，以及每100行一次的进度
                    if (e.Data.Contains("Setting up") || 
                        e.Data.Contains("MESSAGE:") || 
                        e.Data.Contains("Downloading") ||
                        e.Data.Contains("Download") ||
                        e.Data.Contains("Exception") ||
                        e.Data.Contains("successfully") ||
                        (outputLineCount - lastReportedLine >= 100))
                    {
                        System.Diagnostics.Debug.WriteLine($"[Forge Installer] {e.Data}");
                        lastReportedLine = outputLineCount;
                    }
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    // 错误输出总是记录
                    System.Diagnostics.Debug.WriteLine($"[Forge Installer ERROR] {e.Data}");
                    errorBuilder.AppendLine(e.Data);
                }
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // 创建一个任务来等待进程退出，设置超时时间（Forge需要下载库文件，给予充足时间）
                var processTask = process.WaitForExitAsync(_downloadCancellationToken!.Token);
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5), _downloadCancellationToken!.Token); // 5分钟超时（足够下载所有库）
                
                var completedTask = await Task.WhenAny(processTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    // 超时，说明这个参数可能导致安装器卡住或弹出GUI
                    System.Diagnostics.Debug.WriteLine("[Forge] 安装器超时（可能需要用户交互），终止进程");
                    
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                    
                    return false;
                }
                
                // 正常退出
                System.Diagnostics.Debug.WriteLine($"[Forge] 安装器退出码: {process.ExitCode}");
                System.Diagnostics.Debug.WriteLine($"[Forge] 处理了 {outputLineCount} 行输出");
                
                if (process.ExitCode == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[Forge] ✅ 官方安装器执行成功");
                    return true;
                }
                
                // 记录错误输出（但不抛出异常，因为还要尝试其他参数）
                if (errorBuilder.Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[Forge] 安装器错误: {errorBuilder.ToString().Trim()}");
                }
                
                return false;
            }
            catch (OperationCanceledException)
            {
                // 用户取消了下载
                System.Diagnostics.Debug.WriteLine("[Forge] 用户取消安装，正在终止Forge安装器进程...");
                
                if (!process.HasExited)
                {
                    process.Kill(true);
                    System.Diagnostics.Debug.WriteLine("[Forge] ✅ 已终止Forge安装器进程");
                }
                
                throw; // 重新抛出，停止整个安装流程
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Forge] 运行安装器时出错: {ex.Message}");
                return false;
            }
            finally
            {
                process.Dispose();
            }
        }

        private async Task RenameForgeVersionAsync(string gameDirectory, string gameVersion, string forgeVersion, string customVersionName)
        {
            await Task.Run(async () =>
            {
                // Forge官方安装器生成的目录名
                string officialForgeId = $"{gameVersion}-forge-{forgeVersion}";
                string officialDir = Path.Combine(gameDirectory, "versions", officialForgeId);
                string customDir = Path.Combine(gameDirectory, "versions", customVersionName);

                if (!Directory.Exists(officialDir))
                {
                    System.Diagnostics.Debug.WriteLine($"[Forge] ⚠️ 官方Forge目录不存在: {officialForgeId}，尝试查找其他变体...");
                    
                    // 尝试其他可能的目录名
                    string[] possibleNames = {
                        $"forge-{gameVersion}-{forgeVersion}",
                        $"{gameVersion}-Forge{forgeVersion}",
                        $"forge-{gameVersion}"
                    };

                    foreach (var name in possibleNames)
                    {
                        var testDir = Path.Combine(gameDirectory, "versions", name);
                        if (Directory.Exists(testDir))
                        {
                            officialForgeId = name;
                            officialDir = testDir;
                            System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 找到Forge安装目录: {name}");
                            break;
                        }
                    }

                    // 如果还是找不到，尝试模糊搜索（用于手动安装的目录）
                    if (!Directory.Exists(officialDir))
                    {
                        System.Diagnostics.Debug.WriteLine($"[Forge] 尝试模糊搜索包含 'forge' 和 '{gameVersion}' 的目录...");
                        var versionsDir = Path.Combine(gameDirectory, "versions");
                        if (Directory.Exists(versionsDir))
                        {
                            var dirs = Directory.GetDirectories(versionsDir);
                            foreach (var dir in dirs)
                            {
                                var dirName = Path.GetFileName(dir);
                                // 检查是否包含 forge 和游戏版本号
                                if (dirName.Contains("forge", StringComparison.OrdinalIgnoreCase) && 
                                    dirName.Contains(gameVersion))
                                {
                                    // 验证目录中是否有 JSON 文件
                                    var jsonPath = Path.Combine(dir, $"{dirName}.json");
                                    if (File.Exists(jsonPath))
                                    {
                                        officialForgeId = dirName;
                                        officialDir = dir;
                                        System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 通过模糊搜索找到Forge安装目录: {dirName}");
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (!Directory.Exists(officialDir))
                    {
                        throw new Exception($"找不到Forge安装目录，请检查安装器是否正确执行");
                    }
                }

                // 如果目标目录已存在，先删除
                if (Directory.Exists(customDir) && customDir != officialDir)
                {
                    Directory.Delete(customDir, true);
                    System.Diagnostics.Debug.WriteLine($"[Forge] 🗑️ 已删除旧版本目录: {customVersionName}");
                }

                // 如果名称不同，则重命名
                if (customVersionName != officialForgeId)
                {
                    Directory.Move(officialDir, customDir);
                    System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 已重命名版本目录: {officialForgeId} -> {customVersionName}");

                    // 重命名 JSON 文件
                    string oldJsonPath = Path.Combine(customDir, $"{officialForgeId}.json");
                    string newJsonPath = Path.Combine(customDir, $"{customVersionName}.json");
                    
                    if (File.Exists(oldJsonPath))
                    {
                        File.Move(oldJsonPath, newJsonPath);
                        System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 已重命名 JSON 文件");

                        // 更新 JSON 并合并父版本信息（移除inheritsFrom依赖）
                        await MergeVanillaIntoForgeJson(newJsonPath, customVersionName, gameDirectory, gameVersion);
                    }

                    // 重命名 JAR 文件（如果存在）
                    string oldJarPath = Path.Combine(customDir, $"{officialForgeId}.jar");
                    string newJarPath = Path.Combine(customDir, $"{customVersionName}.jar");
                    
                    if (File.Exists(oldJarPath))
                    {
                        File.Move(oldJarPath, newJarPath);
                        System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 已重命名 JAR 文件");
                    }
                }
                else
                {
                    // 即使名称相同，也要合并父版本信息
                    string jsonPath = Path.Combine(officialDir, $"{officialForgeId}.json");
                    if (File.Exists(jsonPath))
                    {
                        await MergeVanillaIntoForgeJson(jsonPath, customVersionName, gameDirectory, gameVersion);
                    }
                    System.Diagnostics.Debug.WriteLine($"[Forge] ℹ️ 版本名称相同，无需重命名");
                }
                
                // 确保 Forge 版本目录中存在 JAR 文件（对于旧版本 Forge 很重要）
                string forgeJarPath = Path.Combine(customDir, $"{customVersionName}.jar");
                if (!File.Exists(forgeJarPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[Forge] ⚠️ Forge版本缺少主JAR文件，尝试复制原版JAR...");
                    
                    // 尝试1：从同一Forge目录中的原版JAR文件复制（旧版本Forge手动安装）
                    string vanillaJarInForgeDir = Path.Combine(customDir, $"{gameVersion}.jar");
                    
                    if (File.Exists(vanillaJarInForgeDir))
                    {
                        File.Copy(vanillaJarInForgeDir, forgeJarPath, true);
                        System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 已从Forge目录内的原版文件复制主JAR: {gameVersion}.jar -> {customVersionName}.jar");
                        
                        // 删除临时的原版JAR文件
                        try
                        {
                            File.Delete(vanillaJarInForgeDir);
                            System.Diagnostics.Debug.WriteLine($"[Forge] 🗑️ 已删除临时原版JAR: {gameVersion}.jar");
                        }
                        catch (Exception deleteEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Forge] ⚠️ 删除临时原版JAR失败: {deleteEx.Message}");
                        }
                    }
                    else
                    {
                        // 尝试2：从标准原版目录复制（高版本Forge）
                        string vanillaJarInStandardDir = Path.Combine(gameDirectory, "versions", gameVersion, $"{gameVersion}.jar");
                        
                        if (File.Exists(vanillaJarInStandardDir))
                        {
                            File.Copy(vanillaJarInStandardDir, forgeJarPath, true);
                            System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 已从标准原版目录复制主JAR: {gameVersion}.jar -> {customVersionName}.jar");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[Forge] ⚠️ 未找到原版JAR文件");
                            System.Diagnostics.Debug.WriteLine($"[Forge] ℹ️ 对于新版本Forge（1.13+），主JAR文件由库文件提供，可能不需要独立JAR");
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Forge] ✅ Forge版本已包含主JAR文件");
                }
            });
        }

        /// <summary>
        /// 为Forge安装器下载原版文件到指定目录
        /// </summary>
        /// <param name="gameDirectory">游戏目录</param>
        /// <param name="version">原版Minecraft版本号</param>
        /// <param name="targetDirectory">目标目录（Forge版本目录）</param>
        /// <param name="progress">进度回调</param>
        private async Task DownloadVanillaForForge(string gameDirectory, string version, string targetDirectory, IProgress<DownloadProgress>? progress = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[Forge] 开始下载原版 {version} 文件");
                System.Diagnostics.Debug.WriteLine($"[Forge] 目标目录: {targetDirectory}");

                // 确保目标目录存在
                await Task.Run(() => Directory.CreateDirectory(targetDirectory));

                string jsonPath = Path.Combine(targetDirectory, $"{version}.json");
                string jarPath = Path.Combine(targetDirectory, $"{version}.jar");

                // 如果文件已存在，跳过下载
                if (File.Exists(jsonPath) && File.Exists(jarPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[Forge] 原版文件已存在，跳过下载");
                    progress?.Report(new DownloadProgress { Status = "原版文件已准备完成" });
                    return;
                }

                progress?.Report(new DownloadProgress { Status = "正在获取版本信息..." });

                // 获取版本信息URL
                var versionManifest = await MinecraftVersionService.GetVersionListAsync();
                var versionInfo = versionManifest?.Versions?.FirstOrDefault(v => v.Id == version);
                if (versionInfo == null || string.IsNullOrEmpty(versionInfo.Url))
                {
                    throw new Exception($"找不到版本 {version} 的信息");
                }

                progress?.Report(new DownloadProgress { Status = "正在下载版本清单..." });

                // 下载版本JSON
                if (!File.Exists(jsonPath))
                {
                    var jsonContent = await _httpClient.GetStringAsync(versionInfo.Url, _downloadCancellationToken?.Token ?? default);
                    await File.WriteAllTextAsync(jsonPath, jsonContent);
                    System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 已下载原版JSON");
                }

                progress?.Report(new DownloadProgress { Status = "正在解析版本信息..." });

                // 解析JSON获取JAR下载URL和大小
                var jsonDoc = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
                var clientUrl = jsonDoc.RootElement.GetProperty("downloads").GetProperty("client").GetProperty("url").GetString();
                var clientSize = jsonDoc.RootElement.GetProperty("downloads").GetProperty("client").GetProperty("size").GetInt64();
                
                if (string.IsNullOrEmpty(clientUrl))
                {
                    throw new Exception("无法获取原版JAR下载地址");
                }

                // 根据配置的下载源转换URL
                var downloadUrl = clientUrl;
                if (DownloadSourceManager.Instance.CurrentService is BMCLAPIService)
                {
                    downloadUrl = $"https://bmclapi2.bangbang93.com/version/{version}/client";
                    System.Diagnostics.Debug.WriteLine($"[Forge] 使用BMCLAPI镜像下载原版JAR");
                }

                // 下载原版JAR（带详细进度和速度计算）
                if (!File.Exists(jarPath))
                {
                    var startTime = DateTime.Now;
                    var lastReportTime = startTime;
                    long lastReportedBytes = 0;
                    
                    using var response = await _httpClient.GetAsync(downloadUrl!, HttpCompletionOption.ResponseHeadersRead, _downloadCancellationToken?.Token ?? default);
                    response.EnsureSuccessStatusCode();
                    
                    var totalBytes = response.Content.Headers.ContentLength ?? clientSize;
                    using var contentStream = await response.Content.ReadAsStreamAsync(_downloadCancellationToken?.Token ?? default);
                    using var fileStream = new FileStream(jarPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    
                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int bytesRead;
                    
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, _downloadCancellationToken?.Token ?? default)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, _downloadCancellationToken?.Token ?? default);
                        totalRead += bytesRead;
                        
                        // 每200ms报告一次进度（避免UI更新过于频繁）
                        var now = DateTime.Now;
                        if ((now - lastReportTime).TotalMilliseconds >= 200)
                        {
                            var elapsed = (now - lastReportTime).TotalSeconds;
                            var speed = elapsed > 0 ? (totalRead - lastReportedBytes) / elapsed : 0;
                            
                            progress?.Report(new DownloadProgress
                            {
                                Status = $"正在下载原版文件 {version}.jar",
                                CurrentFile = $"{version}.jar",
                                CurrentFileBytes = totalRead,
                                CurrentFileTotalBytes = totalBytes,
                                TotalDownloadedBytes = totalRead,
                                TotalBytes = totalBytes,
                                DownloadSpeed = speed
                            });
                            
                            lastReportTime = now;
                            lastReportedBytes = totalRead;
                        }
                    }
                    
                    // 最后报告一次完成
                    progress?.Report(new DownloadProgress
                    {
                        Status = $"原版文件下载完成",
                        CurrentFile = $"{version}.jar",
                        CurrentFileBytes = totalRead,
                        CurrentFileTotalBytes = totalBytes,
                        TotalDownloadedBytes = totalRead,
                        TotalBytes = totalBytes,
                        DownloadSpeed = 0
                    });
                    
                    System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 已下载原版JAR ({totalRead / 1024 / 1024} MB)");
                }

                progress?.Report(new DownloadProgress { Status = "原版文件准备完成" });
                System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 原版文件准备完成");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Forge] ❌ 下载原版文件失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 更新Forge JSON配置（旧版本保留inheritsFrom，新版本完全合并）
        /// </summary>
        private async Task MergeVanillaIntoForgeJson(string forgeJsonPath, string customVersionName, string gameDirectory, string vanillaVersion)
        {
            try
            {
                // 读取Forge JSON
                var forgeJsonContent = await File.ReadAllTextAsync(forgeJsonPath);
                var forgeJson = System.Text.Json.Nodes.JsonNode.Parse(forgeJsonContent)!.AsObject();
                
                // 判断是否是旧版本Forge（1.12.2及之前）
                bool isOldVersion = IsForgeInstallerNewVersion(vanillaVersion) == false;
                
                // 1. 更新ID
                forgeJson["id"] = customVersionName;
                
                // 读取原版JSON（现在应该在标准位置）
                string vanillaJsonPath = Path.Combine(gameDirectory, "versions", vanillaVersion, $"{vanillaVersion}.json");
                
                if (!File.Exists(vanillaJsonPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[Forge] ⚠️ 原版JSON不存在，保留inheritsFrom");
                    forgeJson["inheritsFrom"] = vanillaVersion;
                    await File.WriteAllTextAsync(forgeJsonPath, forgeJson.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"[Forge] 从标准位置读取原版JSON: {vanillaJsonPath}");

                var vanillaJsonContent = await File.ReadAllTextAsync(vanillaJsonPath);
                var vanillaJson = System.Text.Json.Nodes.JsonNode.Parse(vanillaJsonContent)!.AsObject();
                
                if (isOldVersion)
                {
                    // 旧版本Forge：完全合并原版信息，然后删除原版文件夹
                    System.Diagnostics.Debug.WriteLine($"[Forge] 旧版本Forge，开始完全合并原版信息");
                    
                    // 1. 合并 libraries（去重，Forge的在前，原版的在后）
                    var forgeLibraries = forgeJson["libraries"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray();
                    var vanillaLibraries = vanillaJson["libraries"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray();
                    
                    // 收集已存在的库
                    var existingLibs = new HashSet<string>();
                    foreach (var lib in forgeLibraries)
                    {
                        if (lib?["name"] != null)
                        {
                            string libKey = GetLibraryKeyFromName(lib["name"]!.ToString());
                            existingLibs.Add(libKey);
                        }
                    }
                    
                    // 只添加不重复的库
                    // 特别注意：natives库需要完整复制，即使name相同也不跳过
                    int addedCount = 0;
                    foreach (var lib in vanillaLibraries)
                    {
                        // 检查是否是natives库（有natives字段）
                        bool isNativesLib = lib?["natives"] != null;
                        
                        if (isNativesLib)
                        {
                            // natives库总是添加，因为它包含平台特定的本地库文件
                            forgeLibraries.Add(lib?.DeepClone());
                            addedCount++;
                            System.Diagnostics.Debug.WriteLine($"[Forge] 添加natives库: {lib?["name"]}");
                            continue;
                        }
                        
                        if (lib?["name"] != null)
                        {
                            string libKey = GetLibraryKeyFromName(lib["name"]!.ToString());
                            if (!existingLibs.Contains(libKey))
                            {
                                forgeLibraries.Add(lib?.DeepClone());
                                existingLibs.Add(libKey);
                                addedCount++;
                            }
                        }
                    }
                    forgeJson["libraries"] = forgeLibraries;
                    
                    System.Diagnostics.Debug.WriteLine($"[Forge] 从原版添加了 {addedCount} 个不重复的库，总数: {forgeLibraries.Count}");
                    
                    // 2. 合并 assetIndex（如果Forge中没有）
                    if (!forgeJson.ContainsKey("assetIndex") && vanillaJson.ContainsKey("assetIndex"))
                    {
                        forgeJson["assetIndex"] = vanillaJson["assetIndex"]?.DeepClone();
                    }
                    
                    // 3. 合并 assets（如果Forge中没有）
                    if (!forgeJson.ContainsKey("assets") && vanillaJson.ContainsKey("assets"))
                    {
                        forgeJson["assets"] = vanillaJson["assets"]?.DeepClone();
                    }
                    
                    // 4. 移除 inheritsFrom（已完全合并）
                    forgeJson.Remove("inheritsFrom");
                    
                    System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 旧版本Forge已完全合并，总libraries: {forgeLibraries.Count}");
                    System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 已移除inheritsFrom依赖，可以删除原版文件夹");
                    
                    await File.WriteAllTextAsync(forgeJsonPath, forgeJson.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    // 新版本Forge：完全合并，移除inheritsFrom依赖
                    System.Diagnostics.Debug.WriteLine($"[Forge] 新版本Forge，开始完全合并原版信息");
                    
                    // 2. 合并libraries（去重）
                    var forgeLibraries = forgeJson["libraries"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray();
                    var vanillaLibraries = vanillaJson["libraries"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray();
                    
                    // 收集已存在的库（groupId:artifactId）
                    var existingLibs = new HashSet<string>();
                    foreach (var lib in forgeLibraries)
                    {
                        if (lib?["name"] != null)
                        {
                            string libKey = GetLibraryKeyFromName(lib["name"]!.ToString());
                            existingLibs.Add(libKey);
                        }
                    }
                    
                    // 只添加Forge中不存在的原版libraries
                    // 特别注意：natives库需要完整复制，即使name相同也不跳过
                    int addedCount = 0;
                    foreach (var vanillaLib in vanillaLibraries)
                    {
                        // 检查是否是natives库（有natives字段）
                        bool isNativesLib = vanillaLib?["natives"] != null;
                        
                        if (isNativesLib)
                        {
                            // natives库总是添加，因为它包含平台特定的本地库文件
                            forgeLibraries.Add(vanillaLib?.DeepClone());
                            addedCount++;
                            System.Diagnostics.Debug.WriteLine($"[Forge] 添加natives库: {vanillaLib?["name"]}");
                            continue;
                        }
                        
                        if (vanillaLib?["name"] != null)
                        {
                            string libKey = GetLibraryKeyFromName(vanillaLib["name"]!.ToString());
                            if (!existingLibs.Contains(libKey))
                            {
                                forgeLibraries.Add(vanillaLib.DeepClone());
                                existingLibs.Add(libKey);
                                addedCount++;
                            }
                        }
                    }
                    forgeJson["libraries"] = forgeLibraries;
                    
                    System.Diagnostics.Debug.WriteLine($"[Forge] 从原版添加了 {addedCount} 个不重复的库，Forge库数: {forgeLibraries.Count - addedCount}, 总数: {forgeLibraries.Count}");
                    
                    // 3. 从原版复制缺失的字段
                    if (!forgeJson.ContainsKey("assetIndex") && vanillaJson.ContainsKey("assetIndex"))
                        forgeJson["assetIndex"] = vanillaJson["assetIndex"]!.DeepClone();
                    if (!forgeJson.ContainsKey("assets") && vanillaJson.ContainsKey("assets"))
                        forgeJson["assets"] = vanillaJson["assets"]!.DeepClone();
                    if (!forgeJson.ContainsKey("arguments") && vanillaJson.ContainsKey("arguments"))
                        forgeJson["arguments"] = vanillaJson["arguments"]!.DeepClone();
                    
                    // 4. 移除inheritsFrom字段
                    forgeJson.Remove("inheritsFrom");
                    
                    // 5. 保存合并后的JSON
                    await File.WriteAllTextAsync(forgeJsonPath, forgeJson.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    
                    System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 已合并原版信息到Forge JSON，总libraries: {forgeLibraries.Count}");
                    System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 已移除inheritsFrom依赖");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Forge] ❌ 更新Forge JSON失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 从库名称获取唯一标识（groupId:artifactId[:classifier]，忽略版本号）
        /// 例如: net.sf.jopt-simple:jopt-simple:5.0.4 -> net.sf.jopt-simple:jopt-simple
        ///       org.lwjgl:lwjgl:3.3.3:natives-windows -> org.lwjgl:lwjgl:natives-windows
        /// </summary>
        private string GetLibraryKeyFromName(string libraryName)
        {
            if (string.IsNullOrEmpty(libraryName))
                return string.Empty;
            
            // 库名格式：groupId:artifactId:version[:classifier][@extension]
            var parts = libraryName.Split(':');
            
            if (parts.Length >= 4)
            {
                // 有classifier（如natives-windows），返回 groupId:artifactId:classifier
                // 这样natives库不会和普通库冲突
                return $"{parts[0]}:{parts[1]}:{parts[3]}";
            }
            else if (parts.Length >= 2)
            {
                // 没有classifier，返回 groupId:artifactId（忽略版本号）
                return $"{parts[0]}:{parts[1]}";
            }
            
            return libraryName;
        }

        /// <summary>
        /// 判断Forge安装器是否是新版本（1.13+需要 --installClient 参数）
        /// </summary>
        private bool IsForgeInstallerNewVersion(string mcVersion)
        {
            try
            {
                if (string.IsNullOrEmpty(mcVersion)) return false;
                
                // 解析版本号
                var versionParts = mcVersion.Split('.');
                if (versionParts.Length < 2) return false;
                
                if (!int.TryParse(versionParts[0], out int major)) return false;
                if (!int.TryParse(versionParts[1], out int minor)) return false;
                
                // 1.13.0 及以上需要 --installClient 参数
                if (major > 1) return true;
                if (major == 1 && minor >= 13) return true;
                
                return false;
            }
            catch
            {
                // 解析失败，默认使用旧版本参数（更安全）
                return false;
            }
        }
        
        /// <summary>
        /// 判断是否是非常旧的 Forge 版本（1.9 之前），这些版本的安装器参数格式不同
        /// </summary>
        private bool IsVeryOldForgeVersion(string mcVersion)
        {
            try
            {
                if (string.IsNullOrEmpty(mcVersion)) return false;
                
                // 解析版本号
                var versionParts = mcVersion.Split('.');
                if (versionParts.Length < 2) return false;
                
                if (!int.TryParse(versionParts[0], out int major)) return false;
                if (!int.TryParse(versionParts[1], out int minor)) return false;
                
                // 1.8.x 及更早版本视为非常旧的版本
                if (major < 1) return true;
                if (major == 1 && minor < 9) return true;
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// 手动安装非常旧的Forge客户端（1.8.9等），直接从安装器JAR提取文件
        /// </summary>
        private async Task<bool> ManualInstallVeryOldForgeClient(string installerPath, string gameDirectory, string mcVersion, 
            string targetVersionName, string targetVersionDir, LauncherConfig config)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[Forge] 开始手动安装 Forge {mcVersion} 客户端到 {targetVersionName}");
                
                using (var zip = System.IO.Compression.ZipFile.OpenRead(installerPath))
                {
                    // 1. 读取 install_profile.json
                    var profileEntry = zip.GetEntry("install_profile.json");
                    if (profileEntry == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Forge] ❌ 安装器中找不到 install_profile.json");
                        return false;
                    }
                    
                    string profileJson;
                    using (var stream = profileEntry.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        profileJson = await reader.ReadToEndAsync();
                    }
                    
                    var profile = System.Text.Json.JsonDocument.Parse(profileJson);
                    var versionInfo = profile.RootElement.GetProperty("versionInfo");
                    
                    // 2. 获取原始Forge版本ID（如 1.7.10-Forge10.13.4.1614-1.7.10）
                    string originalForgeVersionId = versionInfo.GetProperty("id").GetString()!;
                    System.Diagnostics.Debug.WriteLine($"[Forge] 原始Forge版本ID: {originalForgeVersionId}");
                    System.Diagnostics.Debug.WriteLine($"[Forge] 使用标准化版本名: {targetVersionName}");
                    
                    // 3. 使用或创建目标版本目录
                    string versionDir = targetVersionDir;
                    if (!Directory.Exists(versionDir))
                    {
                        Directory.CreateDirectory(versionDir);
                        System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 创建版本目录: {targetVersionName}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 使用已有版本目录: {targetVersionName}");
                    }
                    
                    // 4. 提取并修正 version.json（使用标准化的版本名）
                    string versionJsonPath = Path.Combine(versionDir, $"{targetVersionName}.json");
                    
                    // 解析为 JsonObject 以便修正字段格式
                    var versionJson = System.Text.Json.Nodes.JsonNode.Parse(versionInfo.GetRawText())!.AsObject();
                    
                    // 修正 releaseTime 和 time 字段格式（确保符合 ISO 8601 标准）
                    // 旧版 Forge 可能使用非标准时间格式（如 "1960-01-01T00:00:00-0700"）
                    if (versionJson.ContainsKey("releaseTime"))
                    {
                        var releaseTimeValue = versionJson["releaseTime"];
                        if (releaseTimeValue != null)
                        {
                            var releaseTimeStr = releaseTimeValue.GetValue<string>();
                            try
                            {
                                // 尝试解析并重新格式化为标准 ISO 8601 格式
                                if (DateTime.TryParse(releaseTimeStr, System.Globalization.CultureInfo.InvariantCulture, 
                                    System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime parsedTime))
                                {
                                    versionJson["releaseTime"] = parsedTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                                    System.Diagnostics.Debug.WriteLine($"[Forge] 已规范化 releaseTime 字段: {releaseTimeStr} -> {parsedTime:yyyy-MM-ddTHH:mm:ssZ}");
                                }
                                else
                                {
                                    // 解析失败，使用默认值
                                    versionJson["releaseTime"] = "2020-01-01T00:00:00Z";
                                    System.Diagnostics.Debug.WriteLine($"[Forge] 无法解析 releaseTime，使用默认值: {releaseTimeStr}");
                                }
                            }
                            catch
                            {
                                versionJson["releaseTime"] = "2020-01-01T00:00:00Z";
                            }
                        }
                    }
                    else
                    {
                        versionJson["releaseTime"] = "2020-01-01T00:00:00Z";
                    }
                    
                    if (versionJson.ContainsKey("time"))
                    {
                        var timeValue = versionJson["time"];
                        if (timeValue != null)
                        {
                            var timeStr = timeValue.GetValue<string>();
                            try
                            {
                                // 尝试解析并重新格式化为标准 ISO 8601 格式
                                if (DateTime.TryParse(timeStr, System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime parsedTime))
                                {
                                    versionJson["time"] = parsedTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                                    System.Diagnostics.Debug.WriteLine($"[Forge] 已规范化 time 字段: {timeStr} -> {parsedTime:yyyy-MM-ddTHH:mm:ssZ}");
                                }
                                else
                                {
                                    // 解析失败，使用默认值
                                    versionJson["time"] = "2020-01-01T00:00:00Z";
                                    System.Diagnostics.Debug.WriteLine($"[Forge] 无法解析 time，使用默认值: {timeStr}");
                                }
                            }
                            catch
                            {
                                versionJson["time"] = "2020-01-01T00:00:00Z";
                            }
                        }
                    }
                    else
                    {
                        versionJson["time"] = "2020-01-01T00:00:00Z";
                    }
                    
                    // 修改id字段为标准化的版本名
                    versionJson["id"] = targetVersionName;
                    System.Diagnostics.Debug.WriteLine($"[Forge] 已将id修改为: {targetVersionName}");
                    
                    await File.WriteAllTextAsync(versionJsonPath, versionJson.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 已创建 version.json");
                    
                    // 5. 提取 Forge universal JAR（如果存在）
                    var forgeJarEntry = zip.Entries.FirstOrDefault(e => 
                        e.Name.Contains("universal.jar") && !e.Name.Contains("installer"));
                    
                    if (forgeJarEntry != null)
                    {
                        string forgeJarPath = Path.Combine(gameDirectory, "libraries", "net", "minecraftforge", "forge");
                        Directory.CreateDirectory(forgeJarPath);
                        
                        // 解析库路径
                        var forgeLibraries = versionInfo.GetProperty("libraries").EnumerateArray();
                        foreach (var lib in forgeLibraries)
                        {
                            var libName = lib.GetProperty("name").GetString();
                            if (libName != null && libName.Contains("minecraftforge:forge"))
                            {
                                // 提取库文件
                                var nameParts = libName.Split(':');
                                if (nameParts.Length == 3)
                                {
                                    string libVersion = nameParts[2];
                                    string libPath = Path.Combine(gameDirectory, "libraries", "net", "minecraftforge", "forge", libVersion, $"forge-{libVersion}.jar");
                                    Directory.CreateDirectory(Path.GetDirectoryName(libPath)!);
                                    
                                    using (var entryStream = forgeJarEntry.Open())
                                    using (var fileStream = File.Create(libPath))
                                    {
                                        await entryStream.CopyToAsync(fileStream);
                                    }
                                    System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 已提取 Forge universal JAR");
                                }
                                break;
                            }
                        }
                    }
                    
                    // 6. 下载其他必需的库文件
                    System.Diagnostics.Debug.WriteLine($"[Forge] 开始下载Forge依赖库...");
                    var libraries = versionInfo.GetProperty("libraries").EnumerateArray();
                    int downloadedCount = 0;
                    int totalCount = 0;
                    
                    foreach (var lib in libraries)
                    {
                        totalCount++;
                        var libName = lib.GetProperty("name").GetString();
                        if (libName == null) continue;
                        
                        // 跳过Forge自己的库（已经提取了）
                        if (libName.Contains("minecraftforge:forge")) 
                        {
                            downloadedCount++; // 计数但跳过下载
                            continue;
                        }
                        
                        // 构建库文件路径
                        var nameParts = libName.Split(':');
                        if (nameParts.Length != 3) continue;
                        
                        string group = nameParts[0].Replace('.', '/');
                        string artifact = nameParts[1];
                        string version = nameParts[2];
                        string fileName = $"{artifact}-{version}.jar";
                        string libPath = Path.Combine(gameDirectory, "libraries", group, artifact, version, fileName);
                        
                        // 如果文件已存在，跳过
                        if (File.Exists(libPath)) 
                        {
                            downloadedCount++;
                            continue;
                        }
                        
                        // 下载库文件
                        Directory.CreateDirectory(Path.GetDirectoryName(libPath)!);
                        
                        // 检查是否有自定义URL
                        string? customUrl = null;
                        if (lib.TryGetProperty("url", out var urlProp))
                        {
                            customUrl = urlProp.GetString();
                        }
                        
                        // 构建下载URL列表（根据用户配置的下载源）
                        List<string> urls = new List<string>();
                        
                        // 如果有自定义URL，优先使用
                        if (!string.IsNullOrEmpty(customUrl))
                        {
                            var baseUrl = customUrl.TrimEnd('/');
                            urls.Add($"{baseUrl}/{group}/{artifact}/{version}/{fileName}");
                        }
                        
                        // 根据用户配置的下载源决定URL顺序
                        if (config.DownloadSource == DownloadSource.BMCLAPI)
                        {
                            // BMCLAPI优先
                            urls.Add($"https://bmclapi2.bangbang93.com/maven/{group}/{artifact}/{version}/{fileName}");
                            urls.Add($"https://maven.minecraftforge.net/{group}/{artifact}/{version}/{fileName}");
                            urls.Add($"https://libraries.minecraft.net/{group}/{artifact}/{version}/{fileName}");
                        }
                        else
                        {
                            // 官方源优先
                            urls.Add($"https://maven.minecraftforge.net/{group}/{artifact}/{version}/{fileName}");
                            urls.Add($"https://libraries.minecraft.net/{group}/{artifact}/{version}/{fileName}");
                            urls.Add($"https://bmclapi2.bangbang93.com/maven/{group}/{artifact}/{version}/{fileName}");
                        }
                        
                        // 尝试下载
                        bool downloaded = false;
                        foreach (var url in urls)
                        {
                            try
                            {
                                var response = await _httpClient.GetAsync(url, _downloadCancellationToken!.Token);
                                
                                if (response.IsSuccessStatusCode)
                                {
                                    using var stream = await response.Content.ReadAsStreamAsync();
                                    using var fileStream = File.Create(libPath);
                                    await stream.CopyToAsync(fileStream, _downloadCancellationToken!.Token);
                                    downloaded = true;
                                    downloadedCount++;
                                    System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 已下载库: {libName} (从 {url})");
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[Forge] ⚠️ 下载失败: {url} - {ex.Message}");
                            }
                        }
                        
                        if (!downloaded)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Forge] ❌ 无法下载库: {libName}，所有URL都失败");
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 库文件下载完成: {downloadedCount}/{totalCount}");
                    
                    // 7. 合并原版信息到Forge JSON
                    // 原版文件现在在标准位置（versions/1.13.2/）
                    string vanillaJsonPath = Path.Combine(gameDirectory, "versions", mcVersion, $"{mcVersion}.json");
                    if (File.Exists(vanillaJsonPath))
                    {
                        await MergeVanillaIntoForgeJson(versionJsonPath, targetVersionName, gameDirectory, mcVersion);
                    }
                    
                    // 8. 复制原版JAR到Forge版本目录（重命名后需要）
                    string vanillaJarPath = Path.Combine(gameDirectory, "versions", mcVersion, $"{mcVersion}.jar");
                    string targetJarPath = Path.Combine(versionDir, $"{mcVersion}.jar");
                    if (File.Exists(vanillaJarPath) && !File.Exists(targetJarPath))
                    {
                        File.Copy(vanillaJarPath, targetJarPath, true);
                        System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 已复制原版JAR到Forge版本目录: {mcVersion}.jar");
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[Forge] ✅ 手动安装完成！");
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Forge] ❌ 手动安装失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[Forge] 堆栈跟踪: {ex.StackTrace}");
                return false;
            }
        }

        private async Task FinalizeForgeInstallation(string customVersionName, string forgeVersion)
        {
            await Dispatcher.BeginInvoke(() =>
            {
                DownloadOverallProgressBar.Value = 100;
                DownloadStatusText.Text = "安装完成";
            });

            if (_currentDownloadTaskId != null)
            {
                DownloadTaskManager.Instance.CompleteTask(_currentDownloadTaskId);
                _currentDownloadTaskId = null;
            }

            _ = NotificationManager.Instance.ShowNotification(
                "Forge安装完成",
                $"版本 '{customVersionName}' 已成功安装",
                NotificationType.Success, 5);

            await Task.Delay(500);
            
            // 安全地返回上一页（检查是否可以返回）
            _ = Dispatcher.BeginInvoke(() => 
            {
                if (NavigationService?.CanGoBack == true)
                {
                    NavigationService.GoBack();
                }
            });
        }
    }

    /// <summary>
    /// 自然字符串比较器 - 正确处理字符串中的数字
    /// 例如: pre2 < pre10 < pre11
    /// </summary>
    public class NaturalStringComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            
            int x1 = 0, y1 = 0;
            
            while (x1 < x.Length && y1 < y.Length)
            {
                // 检查当前字符是否是数字
                bool xIsDigit = char.IsDigit(x[x1]);
                bool yIsDigit = char.IsDigit(y[y1]);
                
                if (xIsDigit && yIsDigit)
                {
                    // 两者都是数字，提取完整的数字并比较
                    int xNum = 0, yNum = 0;
                    
                    while (x1 < x.Length && char.IsDigit(x[x1]))
                    {
                        xNum = xNum * 10 + (x[x1] - '0');
                        x1++;
                    }
                    
                    while (y1 < y.Length && char.IsDigit(y[y1]))
                    {
                        yNum = yNum * 10 + (y[y1] - '0');
                        y1++;
                    }
                    
                    if (xNum != yNum)
                        return xNum.CompareTo(yNum);
                }
                else
                {
                    // 至少有一个不是数字，按字符比较
                    if (x[x1] != y[y1])
                        return x[x1].CompareTo(y[y1]);
                    
                    x1++;
                    y1++;
                }
            }
            
            // 如果一个字符串是另一个的前缀
            return x.Length.CompareTo(y.Length);
        }
    }
}

