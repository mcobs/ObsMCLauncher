using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using ObsMCLauncher.Services;
using ObsMCLauncher.Models;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Pages
{
    public partial class HomePage : Page
    {
        private DispatcherTimer? _systemInfoTimer;

        public HomePage()
        {
            InitializeComponent();
            Loaded += HomePage_Loaded;
            Unloaded += HomePage_Unloaded;
        }

        private void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAccounts();
            LoadVersions();
            StartSystemInfoTimer();
        }

        private void HomePage_Unloaded(object sender, RoutedEventArgs e)
        {
            _systemInfoTimer?.Stop();
        }

        /// <summary>
        /// 启动系统信息定时器
        /// </summary>
        private void StartSystemInfoTimer()
        {
            // 立即更新一次
            UpdateSystemInfo();

            // 创建定时器，每5秒更新一次
            _systemInfoTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _systemInfoTimer.Tick += (s, e) => UpdateSystemInfo();
            _systemInfoTimer.Start();
        }

        /// <summary>
        /// 更新系统信息显示
        /// </summary>
        private void UpdateSystemInfo()
        {
            try
            {
                SystemInfoText.Text = SystemInfo.GetSystemInfoSummary();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"更新系统信息失败: {ex.Message}");
                SystemInfoText.Text = "系统信息获取失败";
            }
        }

        private void LoadAccounts()
        {
            AccountComboBox.Items.Clear();

            var accounts = AccountService.Instance.GetAllAccounts();

            if (accounts.Count == 0)
            {
                // 没有账号时显示提示
                var emptyItem = new ComboBoxItem
                {
                    Content = "请先添加账号",
                    IsEnabled = false
                };
                AccountComboBox.Items.Add(emptyItem);
                AccountComboBox.SelectedIndex = 0;
                return;
            }

            // 添加所有账号
            foreach (var account in accounts)
            {
                var item = new ComboBoxItem
                {
                    Tag = account.Id
                };

                var panel = new StackPanel { Orientation = Orientation.Horizontal };

                var icon = new PackIcon
                {
                    Kind = account.Type == AccountType.Offline ? PackIconKind.Account : PackIconKind.Microsoft,
                    Width = 20,
                    Height = 20,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };

                var text = new TextBlock
                {
                    Text = account.DisplayName,
                    VerticalAlignment = VerticalAlignment.Center
                };

                panel.Children.Add(icon);
                panel.Children.Add(text);
                item.Content = panel;

                AccountComboBox.Items.Add(item);

                // 选中默认账号
                if (account.IsDefault)
                {
                    AccountComboBox.SelectedItem = item;
                }
            }

            // 如果没有默认账号，选中第一个
            if (AccountComboBox.SelectedIndex == -1 && AccountComboBox.Items.Count > 0)
            {
                AccountComboBox.SelectedIndex = 0;
            }
        }

        private void LoadVersions()
        {
            VersionComboBox.Items.Clear();

            var config = LauncherConfig.Load();
            var installedVersions = LocalVersionService.GetInstalledVersions(config.GameDirectory);

            if (installedVersions.Count == 0)
            {
                // 没有版本时显示提示
                var emptyItem = new ComboBoxItem
                {
                    Content = "请先下载版本",
                    IsEnabled = false
                };
                VersionComboBox.Items.Add(emptyItem);
                VersionComboBox.SelectedIndex = 0;
                return;
            }

            // 添加所有版本
            foreach (var version in installedVersions)
            {
                var item = new ComboBoxItem
                {
                    Content = version.Id, // 显示自定义名称
                    Tag = version.Id,
                    ToolTip = version.Id != version.ActualVersionId ? $"版本: {version.ActualVersionId}" : null
                };

                VersionComboBox.Items.Add(item);

                // 选中配置中保存的版本
                if (version.Id == config.SelectedVersion)
                {
                    VersionComboBox.SelectedItem = item;
                }
            }

            // 如果没有选中的版本，选中第一个
            if (VersionComboBox.SelectedIndex == -1 && VersionComboBox.Items.Count > 0)
            {
                VersionComboBox.SelectedIndex = 0;
                // 保存选中的版本
                if (VersionComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string versionId)
                {
                    LocalVersionService.SetSelectedVersion(versionId);
                }
            }

            // 监听版本选择变化
            VersionComboBox.SelectionChanged += VersionComboBox_SelectionChanged;
        }

        private void VersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VersionComboBox.SelectedItem is ComboBoxItem item && item.Tag is string versionId)
            {
                LocalVersionService.SetSelectedVersion(versionId);
                System.Diagnostics.Debug.WriteLine($"版本已切换到: {versionId}");
            }
        }

        /// <summary>
        /// 启动游戏按钮点击事件
        /// </summary>
        private async void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. 检查是否选择了版本
                if (VersionComboBox.SelectedItem is not ComboBoxItem versionItem || versionItem.Tag is not string versionId)
                {
                    Debug.WriteLine("⚠️ 请先选择一个游戏版本！");
                    Console.WriteLine("⚠️ 请先选择一个游戏版本！");
                    return;
                }

                // 2. 获取账号
                GameAccount? account = null;
                if (AccountComboBox.SelectedItem is ComboBoxItem accountItem && accountItem.Tag is string accountId)
                {
                    var accounts = AccountService.Instance.GetAllAccounts();
                    account = accounts.FirstOrDefault(a => a.Id == accountId);
                }

                if (account == null)
                {
                    Debug.WriteLine("⚠️ 未找到游戏账号，请前往账号管理添加账号");
                    Console.WriteLine("⚠️ 未找到游戏账号，请前往账号管理添加账号");
                    return;
                }

                // 3. 加载配置
                var config = LauncherConfig.Load();

                // 4. 禁用启动按钮，防止重复点击
                LaunchButton.IsEnabled = false;
                LaunchButton.Content = "检查中...";

                // 5. 显示启动流程通知
                var launchNotificationId = NotificationManager.Instance.ShowNotification(
                    "正在启动游戏",
                    "正在检查游戏完整性...",
                    NotificationType.Progress
                );

                // 5. 先检查游戏完整性（不启动游戏）
                Debug.WriteLine($"========== 准备启动游戏 ==========");
                Debug.WriteLine($"版本: {versionId}");
                Debug.WriteLine($"账号: {account.Username} ({account.Type})");
                
                LaunchButton.Content = "检查依赖中...";
                bool hasIntegrityIssue = await GameLauncher.CheckGameIntegrityAsync(versionId, config, (progress) =>
                {
                    NotificationManager.Instance.UpdateNotification(launchNotificationId, progress);
                    LaunchButton.Content = progress;
                });

                // 6. 如果检测到缺失的库文件，自动下载
                if (hasIntegrityIssue && GameLauncher.MissingLibraries.Count > 0)
                {
                    Debug.WriteLine($"检测到 {GameLauncher.MissingLibraries.Count} 个缺失的依赖库，开始自动补全...");
                    Console.WriteLine($"检测到 {GameLauncher.MissingLibraries.Count} 个缺失的依赖库，开始自动补全...");
                    
                    // 更新启动通知
                    NotificationManager.Instance.UpdateNotification(
                        launchNotificationId,
                        $"检测到 {GameLauncher.MissingLibraries.Count} 个缺失的依赖库"
                    );
                    
                    // 显示独立的依赖下载进度通知
                    var dependencyNotificationId = NotificationManager.Instance.ShowNotification(
                        "正在下载游戏依赖",
                        $"准备下载 {GameLauncher.MissingLibraries.Count} 个依赖库文件...",
                        NotificationType.Progress
                    );
                    
                    LaunchButton.Content = "补全依赖中...";
                    
                    // 下载缺失的库文件
                    bool downloadSuccess = await DownloadMissingLibraries(versionId, config, dependencyNotificationId);
                    
                    // 移除依赖下载进度通知
                    if (!string.IsNullOrEmpty(dependencyNotificationId))
                    {
                        NotificationManager.Instance.RemoveNotification(dependencyNotificationId);
                    }
                    
                    if (downloadSuccess)
                    {
                        // 显示补全成功通知
                        NotificationManager.Instance.ShowNotification(
                            "依赖补全完成",
                            $"已成功下载 {GameLauncher.MissingLibraries.Count} 个依赖库",
                            NotificationType.Success,
                            3
                        );
                        
                        // 更新启动通知，准备继续
                        NotificationManager.Instance.UpdateNotification(
                            launchNotificationId,
                            "依赖补全完成，继续检查资源..."
                        );
                        
                        // 设置标志，继续检查Assets（依赖已补全，认为没有完整性问题）
                        hasIntegrityIssue = false;
                    }
                    else
                    {
                        Debug.WriteLine("❌ 依赖库下载失败！");
                        Console.WriteLine("❌ 依赖库下载失败！");
                        
                        // 显示下载失败通知
                        NotificationManager.Instance.ShowNotification(
                            "依赖补全失败",
                            "部分依赖库下载失败，请检查网络连接或稍后重试",
                            NotificationType.Error,
                            5
                        );
                        
                        // 移除启动通知
                        NotificationManager.Instance.RemoveNotification(launchNotificationId);
                        return;
                    }
                }

                // 7. 检查并补全Assets资源（必须的，在启动游戏前完成）
                if (!hasIntegrityIssue)
                {
                    // 更新启动通知
                    NotificationManager.Instance.UpdateNotification(
                        launchNotificationId,
                        "正在检查游戏资源文件..."
                    );
                    LaunchButton.Content = "检查资源中...";

                    Debug.WriteLine("========== 开始检查Assets资源 ==========");
                    
                    var assetsResult = await AssetsDownloadService.DownloadAndCheckAssetsAsync(
                        config.GameDirectory,
                        versionId,
                        (current, total, message) =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                NotificationManager.Instance.UpdateNotification(
                                    launchNotificationId,
                                    $"检查资源: {message}"
                                );
                                LaunchButton.Content = message;
                            });
                        }
                    );

                    if (!assetsResult.Success)
                    {
                        Debug.WriteLine($"⚠️ Assets资源下载完成，但有 {assetsResult.FailedAssets} 个文件失败");
                        
                        // 显示详细的失败通知
                        string notificationMessage;
                        if (assetsResult.FailedAssets > 0)
                        {
                            notificationMessage = $"{assetsResult.FailedAssets} 个资源文件下载失败，游戏可能缺少部分资源（如声音）";
                        }
                        else
                        {
                            notificationMessage = "资源检查失败，游戏可能缺少资源文件";
                        }
                        
                        NotificationManager.Instance.ShowNotification(
                            "资源文件下载失败",
                            notificationMessage,
                            NotificationType.Warning,
                            6
                        );
                        
                        // 如果失败资源很多，显示错误通知
                        if (assetsResult.FailedAssets > 100)
                        {
                            NotificationManager.Instance.ShowNotification(
                                "大量资源下载失败",
                                $"共 {assetsResult.FailedAssets} 个资源文件下载失败\n可能是网络问题或服务器繁忙\n建议稍后重试或更换下载源",
                                NotificationType.Error,
                                8
                            );
                        }
                    }
                    else
                    {
                        Debug.WriteLine("✅ Assets资源检查完成");
                    }
                    
                    // 8. Assets检查完成后，正式启动游戏
                    NotificationManager.Instance.UpdateNotification(
                        launchNotificationId,
                        "正在启动游戏..."
                    );
                    LaunchButton.Content = "启动中...";
                    
                    bool finalLaunchSuccess = await GameLauncher.LaunchGameAsync(versionId, account, config, (progress) =>
                    {
                        NotificationManager.Instance.UpdateNotification(launchNotificationId, progress);
                        LaunchButton.Content = progress;
                    });
                    
                    // 移除启动进度通知
                    NotificationManager.Instance.RemoveNotification(launchNotificationId);

                    if (finalLaunchSuccess)
                    {
                        // 更新账号最后使用时间
                        AccountService.Instance.UpdateLastUsed(account.Id);

                        Debug.WriteLine($"✅ 游戏已启动！版本: {versionId}, 账号: {account.Username}");
                        Console.WriteLine($"✅ 游戏已启动！版本: {versionId}, 账号: {account.Username}");
                        
                        // 显示启动成功通知
                        NotificationManager.Instance.ShowNotification(
                            "游戏启动成功",
                            $"Minecraft {versionId} 已启动",
                            NotificationType.Success,
                            3
                        );
                    }
                    else
                    {
                        var errorMessage = "游戏启动失败！";
                        var notificationMessage = "游戏启动失败";
                        
                        if (!string.IsNullOrEmpty(GameLauncher.LastError))
                        {
                            errorMessage += $"\n错误详情：{GameLauncher.LastError}";
                            notificationMessage = GameLauncher.LastError;
                        }
                        else
                        {
                            notificationMessage = "请检查Java路径和游戏文件完整性";
                        }
                        
                        errorMessage += "\n\n请检查：" +
                            "\n1. Java路径是否正确（设置→Java路径）" +
                            "\n2. 游戏文件是否完整（重新下载版本）" +
                            "\n3. 查看调试输出窗口（Debug）获取详细日志";
                        
                        Debug.WriteLine($"❌ {errorMessage}");
                        Console.WriteLine($"❌ {errorMessage}");
                        
                        // 显示启动失败通知
                        NotificationManager.Instance.ShowNotification(
                            "游戏启动失败",
                            notificationMessage,
                            NotificationType.Error,
                            5
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 启动游戏异常: {ex.Message}");
                Console.WriteLine($"❌ 启动游戏异常: {ex.Message}\n{ex.StackTrace}");
                
                // 显示异常通知
                NotificationManager.Instance.ShowNotification(
                    "启动游戏时发生错误",
                    ex.Message,
                    NotificationType.Error,
                    5
                );
            }
            finally
            {
                // 恢复启动按钮
                LaunchButton.IsEnabled = true;
                LaunchButton.Content = "启动游戏";
                
                // 隐藏下载面板
                DependencyDownloadPanel.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 下载缺失的库文件
        /// </summary>
        private async Task<bool> DownloadMissingLibraries(string versionId, LauncherConfig config, string? notificationId = null)
        {
            try
            {
                // 显示下载面板
                DependencyDownloadPanel.Visibility = Visibility.Visible;
                
                // 读取版本JSON
                var versionJsonPath = Path.Combine(config.GameDirectory, "versions", versionId, $"{versionId}.json");
                if (!File.Exists(versionJsonPath))
                {
                    Debug.WriteLine($"❌ 版本JSON不存在: {versionJsonPath}");
                    return false;
                }

                var versionJson = await File.ReadAllTextAsync(versionJsonPath);
                var versionDetail = JsonSerializer.Deserialize<VersionDetail>(versionJson, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                if (versionDetail?.Libraries == null)
                {
                    Debug.WriteLine($"❌ 无法解析版本JSON或没有库");
                    return false;
                }

                var librariesDir = Path.Combine(config.GameDirectory, "libraries");
                var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(5);
                
                int totalLibs = GameLauncher.MissingLibraries.Count;
                int downloadedLibs = 0;

                Debug.WriteLine($"开始下载 {totalLibs} 个缺失的库文件...");

                foreach (var lib in versionDetail.Libraries)
                {
                    if (lib.Name == null) continue;
                    
                    // 检查是否是缺失的库
                    if (!GameLauncher.MissingLibraries.Contains(lib.Name)) continue;

                    // 检查操作系统规则
                    if (!IsLibraryAllowedForOS(lib))
                    {
                        Debug.WriteLine($"⏭️ 跳过不适用的库: {lib.Name}");
                        continue;
                    }

                    downloadedLibs++;
                    var progress = (downloadedLibs * 100.0 / totalLibs);
                    
                    // 更新UI和通知
                    Dispatcher.Invoke(() =>
                    {
                        DependencyDownloadStatus.Text = $"下载中: {lib.Name} ({downloadedLibs}/{totalLibs})";
                        DependencyDownloadProgress.Value = progress;
                        
                        // 更新进度通知
                        if (!string.IsNullOrEmpty(notificationId))
                        {
                            NotificationManager.Instance.UpdateNotification(
                                notificationId,
                                $"正在下载 {lib.Name} ({downloadedLibs}/{totalLibs})"
                            );
                        }
                    });

                    try
                    {
                        // 下载库文件
                        if (lib.Downloads?.Artifact?.Url != null)
                        {
                            var libPath = GetLibraryPath(librariesDir, lib);
                            
                            if (string.IsNullOrEmpty(libPath))
                            {
                                Debug.WriteLine($"⚠️ 无法获取库路径: {lib.Name}");
                                Console.WriteLine($"⚠️ 无法获取库路径: {lib.Name}");
                                continue;
                            }
                            
                            var libDir = Path.GetDirectoryName(libPath);
                            
                            if (!string.IsNullOrEmpty(libDir))
                            {
                                Directory.CreateDirectory(libDir);
                                
                                // 使用下载源服务获取URL，而不是直接使用Mojang URL
                                var downloadSource = DownloadSourceManager.Instance.CurrentService;
                                string url;
                                
                                if (!string.IsNullOrEmpty(lib.Downloads?.Artifact?.Path))
                                {
                                    // 优先使用下载源镜像（如BMCLAPI的maven端点）
                                    url = downloadSource.GetLibraryUrl(lib.Downloads.Artifact.Path);
                                    Debug.WriteLine($"📥 下载: {lib.Name} (使用下载源: {config.DownloadSource})");
                                }
                                else if (!string.IsNullOrEmpty(lib.Downloads?.Artifact?.Url))
                                {
                                    // 备用方案：使用version.json中的URL
                                    url = lib.Downloads.Artifact.Url;
                                    Debug.WriteLine($"📥 下载: {lib.Name} (使用原始URL)");
                                }
                                else
                                {
                                    Debug.WriteLine($"⚠️ 无法获取下载URL: {lib.Name}");
                                    Console.WriteLine($"⚠️ 无法获取下载URL: {lib.Name}");
                                    continue;
                                }
                                
                                Debug.WriteLine($"   URL: {url}");
                                Debug.WriteLine($"   保存到: {libPath}");
                                Console.WriteLine($"📥 [{downloadedLibs}/{totalLibs}] {lib.Name}");
                                
                                // 使用HttpClient下载
                                var response = await httpClient.GetAsync(url);
                                response.EnsureSuccessStatusCode();
                                var fileBytes = await response.Content.ReadAsByteArrayAsync();
                                await File.WriteAllBytesAsync(libPath, fileBytes);
                                
                                // 验证文件是否真的下载成功
                                if (File.Exists(libPath))
                                {
                                    var fileInfo = new FileInfo(libPath);
                                    Debug.WriteLine($"✅ 已下载: {lib.Name} ({fileInfo.Length} 字节)");
                                    Console.WriteLine($"✅ 已下载: {lib.Name} ({fileInfo.Length / 1024.0:F2} KB)");
                                }
                                else
                                {
                                    Debug.WriteLine($"❌ 下载后文件不存在: {libPath}");
                                    Console.WriteLine($"❌ 下载后文件不存在: {lib.Name}");
                                }
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"⚠️ 库没有下载URL: {lib.Name}");
                            Console.WriteLine($"⚠️ 库没有下载URL: {lib.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"❌ 下载失败: {lib.Name}");
                        Debug.WriteLine($"   错误: {ex.Message}");
                        Debug.WriteLine($"   堆栈: {ex.StackTrace}");
                        Console.WriteLine($"❌ 下载失败: {lib.Name} - {ex.Message}");
                        // 继续下载其他库
                    }
                }

                httpClient.Dispose();
                Debug.WriteLine($"✅ 库文件下载完成！共 {downloadedLibs}/{totalLibs}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 下载库文件时发生错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检查库是否适用于当前操作系统
        /// </summary>
        private bool IsLibraryAllowedForOS(Library lib)
        {
            if (lib.Rules == null || lib.Rules.Length == 0)
                return true;

            bool allowed = false;
            foreach (var rule in lib.Rules)
            {
                bool matches = true;

                if (rule.Os != null && rule.Os.Name != null)
                {
                    var osName = GetOSName();
                    matches = rule.Os.Name.Equals(osName, StringComparison.OrdinalIgnoreCase);
                }

                if (matches)
                {
                    allowed = rule.Action == "allow";
                }
            }

            return allowed;
        }

        /// <summary>
        /// 获取库文件路径
        /// </summary>
        private string GetLibraryPath(string librariesDir, Library lib)
        {
            if (lib.Downloads?.Artifact?.Path != null)
            {
                return Path.Combine(librariesDir, lib.Downloads.Artifact.Path.Replace("/", "\\"));
            }

            // 备用方式：从name构建路径
            if (!string.IsNullOrEmpty(lib.Name))
            {
                var parts = lib.Name.Split(':');
                if (parts.Length >= 3)
                {
                    var package = parts[0].Replace('.', '\\');
                    var name = parts[1];
                    var version = parts[2];
                    return Path.Combine(librariesDir, package, name, version, $"{name}-{version}.jar");
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 获取操作系统名称
        /// </summary>
        private string GetOSName()
        {
            if (OperatingSystem.IsWindows())
                return "windows";
            if (OperatingSystem.IsLinux())
                return "linux";
            if (OperatingSystem.IsMacOS())
                return "osx";
            return "unknown";
        }

        // 版本详情模型（用于解析JSON）
        private class VersionDetail
        {
            public Library[]? Libraries { get; set; }
        }

        private class Library
        {
            public string? Name { get; set; }
            public LibraryDownloads? Downloads { get; set; }
            public Rule[]? Rules { get; set; }
        }

        private class LibraryDownloads
        {
            public Artifact? Artifact { get; set; }
        }

        private class Artifact
        {
            public string? Path { get; set; }
            public string? Url { get; set; }
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
    }
}

