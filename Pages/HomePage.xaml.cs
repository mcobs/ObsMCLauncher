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
        public HomePage()
        {
            InitializeComponent();
            Loaded += HomePage_Loaded;
        }

        private void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAccounts();
            LoadVersions();
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
            // 创建取消令牌源用于取消启动流程
            var launchCts = new System.Threading.CancellationTokenSource();
            
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

                // 5. 显示启动流程通知（传递CancellationTokenSource，让关闭按钮能够取消启动）
                var launchNotificationId = NotificationManager.Instance.ShowNotification(
                    "正在启动游戏",
                    "正在检查游戏完整性...",
                    NotificationType.Progress,
                    durationSeconds: null,
                    onCancel: () => 
                    {
                        Debug.WriteLine("[HomePage] 用户取消了游戏启动");
                    },
                    cancellationTokenSource: launchCts
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
                }, launchCts.Token);

                // 6. 如果检测到缺失的必需库文件，自动下载
                if (hasIntegrityIssue && GameLauncher.MissingLibraries.Count > 0)
                {
                    Debug.WriteLine($"检测到 {GameLauncher.MissingLibraries.Count} 个缺失的必需依赖库，开始自动补全...");
                    Console.WriteLine($"检测到 {GameLauncher.MissingLibraries.Count} 个缺失的必需依赖库，开始自动补全...");
                    
                    // 更新启动通知
                    NotificationManager.Instance.UpdateNotification(
                        launchNotificationId,
                        $"检测到 {GameLauncher.MissingLibraries.Count} 个缺失的必需依赖库"
                    );
                    
                    // 显示独立的依赖下载进度通知
                    var dependencyNotificationId = NotificationManager.Instance.ShowNotification(
                        "正在下载必需依赖",
                        $"准备下载 {GameLauncher.MissingLibraries.Count} 个必需依赖库...",
                        NotificationType.Progress
                    );
                    
                    LaunchButton.Content = "补全依赖中...";
                    
                    // 下载缺失的必需库文件
                    bool downloadSuccess = await DownloadMissingLibraries(versionId, config, dependencyNotificationId, isOptional: false, launchCts.Token);
                    
                    // 移除依赖下载进度通知
                    if (!string.IsNullOrEmpty(dependencyNotificationId))
                    {
                        NotificationManager.Instance.RemoveNotification(dependencyNotificationId);
                    }
                    
                    if (downloadSuccess)
                    {
                        // 显示补全成功通知
                        NotificationManager.Instance.ShowNotification(
                            "必需依赖补全完成",
                            $"已成功下载 {GameLauncher.MissingLibraries.Count} 个必需依赖库",
                            NotificationType.Success,
                            3
                        );
                        
                        // 更新启动通知，准备继续
                        NotificationManager.Instance.UpdateNotification(
                            launchNotificationId,
                            "必需依赖补全完成，继续检查资源..."
                        );
                        
                        // 设置标志，继续检查Assets（依赖已补全，认为没有完整性问题）
                        hasIntegrityIssue = false;
                    }
                    else
                    {
                        Debug.WriteLine("❌ 必需依赖库下载失败！");
                        Console.WriteLine("❌ 必需依赖库下载失败！");
                        
                        // 显示下载失败通知
                        NotificationManager.Instance.ShowNotification(
                            "必需依赖补全失败",
                            "必需依赖库下载失败，游戏无法启动",
                            NotificationType.Error,
                            5
                        );
                        
                        // 移除启动通知
                        NotificationManager.Instance.RemoveNotification(launchNotificationId);
                        return;
                    }
                }

                // 6.5 静默尝试下载可选库（natives、Twitch等），失败不影响启动
                if (!hasIntegrityIssue && GameLauncher.MissingOptionalLibraries.Count > 0)
                {
                    Debug.WriteLine($"检测到 {GameLauncher.MissingOptionalLibraries.Count} 个缺失的可选库，静默尝试下载...");
                    Console.WriteLine($"检测到 {GameLauncher.MissingOptionalLibraries.Count} 个缺失的可选库，静默尝试下载...");
                    
                    // 静默下载可选库文件（失败不阻止启动，不显示任何用户通知）
                    bool optionalSuccess = await DownloadMissingLibraries(versionId, config, notificationId: null, isOptional: true, launchCts.Token);
                    
                    // 只在调试日志中记录结果
                    if (optionalSuccess)
                    {
                        Debug.WriteLine($"✅ 可选库下载成功");
                    }
                    else
                    {
                        Debug.WriteLine("⚠️ 部分可选库下载失败（不影响游戏启动）");
                        Console.WriteLine("⚠️ 部分可选库下载失败（不影响游戏启动）");
                    }
                }

                // 7. 检查并补全Assets资源（必须的，在启动游戏前完成）
                if (!hasIntegrityIssue)
                {
                    // 检查是否是极旧版本（1.5.2等），这些版本不需要现代资源系统
                    bool isVeryOldVersion = versionId.StartsWith("1.5") || versionId.StartsWith("1.4") || 
                                           versionId.StartsWith("1.3") || versionId.StartsWith("1.2") || 
                                           versionId.StartsWith("1.1") || versionId.StartsWith("1.0");
                    
                    if (isVeryOldVersion)
                    {
                        Debug.WriteLine($"========== 跳过Assets资源检查 ==========");
                        Debug.WriteLine($"版本 {versionId} 不使用现代资源系统，跳过资源检查");
                        Console.WriteLine($"[{versionId}] 使用传统资源系统，跳过现代资源检查");
                    }
                    else
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
                        (current, total, message, speed) =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                NotificationManager.Instance.UpdateNotification(
                                    launchNotificationId,
                                    $"检查资源: {message}"
                                );
                                LaunchButton.Content = message;
                            });
                        },
                        launchCts.Token
                    );

                    if (!assetsResult.Success)
                    {
                        Debug.WriteLine($"⚠️ Assets资源下载完成，但有 {assetsResult.FailedAssets} 个文件失败");
                        
                        // 只在失败文件数量较多时才显示通知（避免与启动成功通知冲突）
                        if (assetsResult.FailedAssets > 50)
                        {
                            string notificationMessage = $"{assetsResult.FailedAssets} 个资源文件下载失败，游戏可能缺少部分资源（如声音）";
                            
                            NotificationManager.Instance.ShowNotification(
                                "部分资源下载失败",
                                notificationMessage,
                                NotificationType.Warning,
                                6
                            );
                        }
                        
                        // 如果失败资源很多，显示严重错误通知
                        if (assetsResult.FailedAssets > 200)
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
                    } // ⭐ 结束 else (!isVeryOldVersion) 块
                    
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
                    }, launchCts.Token);
                    
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
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"❌ 游戏启动已取消");
                Console.WriteLine($"❌ 游戏启动已取消");
                
                // 显示取消通知
                NotificationManager.Instance.ShowNotification(
                    "游戏启动已取消",
                    "用户取消了游戏启动流程",
                    NotificationType.Warning,
                    3
                );
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
                
                // 释放取消令牌源
                launchCts?.Dispose();
            }
        }

        /// <summary>
        /// 下载缺失的库文件
        /// </summary>
        /// <param name="isOptional">是否下载可选库（natives、Twitch等）</param>
        /// <param name="cancellationToken">取消令牌</param>
        private async Task<bool> DownloadMissingLibraries(string versionId, LauncherConfig config, string? notificationId = null, bool isOptional = false, System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                // 根据isOptional选择下载列表
                var targetLibraries = isOptional ? GameLauncher.MissingOptionalLibraries : GameLauncher.MissingLibraries;
                
                cancellationToken.ThrowIfCancellationRequested();
                
                if (targetLibraries.Count == 0)
                {
                    Debug.WriteLine($"没有需要下载的{(isOptional ? "可选" : "必需")}库");
                    return true;
                }
                
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
                
                int totalLibs = targetLibraries.Count;
                int processedLibs = 0;        // 已处理的库数量
                int successfullyDownloaded = 0;  // 成功下载的库数量
                int skippedLibs = 0;          // 跳过的库（没有URL等）

                Debug.WriteLine($"开始下载 {totalLibs} 个缺失的{(isOptional ? "可选" : "必需")}库文件...");

                foreach (var lib in versionDetail.Libraries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    if (lib.Name == null) continue;
                    
                    // 检查是否是缺失的库
                    if (!targetLibraries.Contains(lib.Name)) continue;

                    // 检查操作系统规则
                    if (!IsLibraryAllowedForOS(lib))
                    {
                        Debug.WriteLine($"⏭️ 跳过不适用的库: {lib.Name}");
                        skippedLibs++;
                        continue;
                    }

                    processedLibs++;
                    var progress = (processedLibs * 100.0 / totalLibs);
                    
                    // 更新通知
                    Dispatcher.Invoke(() =>
                    {
                        // 更新进度通知
                        if (!string.IsNullOrEmpty(notificationId))
                        {
                            NotificationManager.Instance.UpdateNotification(
                                notificationId,
                                $"正在下载 {lib.Name} ({processedLibs}/{totalLibs})"
                            );
                        }
                    });

                    try
                    {
                        bool downloaded = false;
                        
                        // 1. 优先检查并下载natives文件（classifiers）
                        if (lib.Natives != null && lib.Downloads?.Classifiers != null)
                        {
                            var osName = GetOSName();
                            if (lib.Natives.TryGetValue(osName, out var nativesKey) && !string.IsNullOrEmpty(nativesKey))
                            {
                                if (lib.Downloads.Classifiers.TryGetValue(nativesKey, out var nativeArtifact) && 
                                    !string.IsNullOrEmpty(nativeArtifact.Path))
                                {
                                    var nativesPath = Path.Combine(librariesDir, nativeArtifact.Path.Replace("/", "\\"));
                                    var nativesDir = Path.GetDirectoryName(nativesPath);
                                    
                                    if (!string.IsNullOrEmpty(nativesDir))
                                    {
                                        Directory.CreateDirectory(nativesDir);
                                        
                                        var downloadSource = DownloadSourceManager.Instance.CurrentService;
                                        string url = downloadSource.GetLibraryUrl(nativeArtifact.Path);
                                        
                                        Debug.WriteLine($"📥 下载natives: {lib.Name} -> {nativesKey}");
                                        Debug.WriteLine($"   URL: {url}");
                                        Debug.WriteLine($"   保存到: {nativesPath}");
                                        Console.WriteLine($"📥 [{processedLibs}/{totalLibs}] {lib.Name} (natives)");
                                        
                                        var response = await httpClient.GetAsync(url, cancellationToken);
                                        response.EnsureSuccessStatusCode();
                                        var fileBytes = await response.Content.ReadAsByteArrayAsync();
                                        await File.WriteAllBytesAsync(nativesPath, fileBytes);
                                        
                                        if (File.Exists(nativesPath))
                                        {
                                            var fileInfo = new FileInfo(nativesPath);
                                            successfullyDownloaded++;
                                            downloaded = true;
                                            Debug.WriteLine($"✅ 已下载natives: {lib.Name} ({fileInfo.Length} 字节)");
                                            Console.WriteLine($"✅ 已下载natives: {lib.Name} ({fileInfo.Length / 1024.0:F2} KB)");
                                        }
                                        else
                                        {
                                            Debug.WriteLine($"❌ natives下载后文件不存在: {nativesPath}");
                                            Console.WriteLine($"❌ natives下载后文件不存在: {lib.Name}");
                                        }
                                    }
                                }
                            }
                        }
                        
                        // 2. 无论是否有natives，如果有artifact，都要下载（natives和artifact可能同时存在）
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
                                Console.WriteLine($"📥 [{processedLibs}/{totalLibs}] {lib.Name}");
                                
                                // 使用HttpClient下载
                                var response = await httpClient.GetAsync(url, cancellationToken);
                                
                                // 对于404错误且是特定的Forge库，跳过（这些库可能从JAR中提取或不需要）
                                if (!response.IsSuccessStatusCode && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                                {
                                    if (lib.Name != null && (lib.Name.Contains("forge") && (lib.Name.Contains(":client") || lib.Name.Contains(":server"))))
                                    {
                                        Debug.WriteLine($"⚠️ 跳过库（Forge特殊库，不存在但可忽略）: {lib.Name}");
                                        Console.WriteLine($"⚠️ 跳过: {lib.Name} (Forge特殊库)");
                                        skippedLibs++;
                                        downloaded = true; // 标记为已处理，避免计入失败
                                        continue;
                                    }
                                }
                                
                                response.EnsureSuccessStatusCode();
                                var fileBytes = await response.Content.ReadAsByteArrayAsync();
                                await File.WriteAllBytesAsync(libPath, fileBytes);
                                
                                // 验证文件是否真的下载成功
                                if (File.Exists(libPath))
                                {
                                    var fileInfo = new FileInfo(libPath);
                                    successfullyDownloaded++;  // 成功计数
                                    downloaded = true;
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
                        // 3. 如果既没有下载成功，跳过
                        if (!downloaded)
                        {
                            Debug.WriteLine($"⚠️ 库没有下载URL或不适用于当前平台: {lib.Name}");
                            Console.WriteLine($"⚠️ 跳过: {lib.Name}");
                            skippedLibs++;  // 跳过计数
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
                
                // 显示下载结果统计
                Debug.WriteLine($"========== 库文件下载结果 ==========");
                Debug.WriteLine($"总计: {totalLibs} 个");
                Debug.WriteLine($"成功: {successfullyDownloaded} 个");
                Debug.WriteLine($"跳过: {skippedLibs} 个（无下载URL或不适用）");
                Debug.WriteLine($"失败: {totalLibs - successfullyDownloaded - skippedLibs} 个");
                
                // 只有当所有需要下载的库都成功时才返回true
                // 跳过的库（无URL）不影响成功判定，因为这些库可能不是必需的
                bool allSuccessful = (successfullyDownloaded + skippedLibs) >= totalLibs;
                
                if (successfullyDownloaded > 0)
                {
                    Debug.WriteLine($"✅ 成功下载 {successfullyDownloaded} 个库文件");
                }
                
                if (skippedLibs > 0)
                {
                    Debug.WriteLine($"⚠️ 跳过 {skippedLibs} 个库（这些库可能不是必需的或无下载源）");
                }
                
                return allSuccessful;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"❌ 库文件下载已取消");
                return false;
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
            public Dictionary<string, string>? Natives { get; set; }
        }

        private class LibraryDownloads
        {
            public Artifact? Artifact { get; set; }
            public Dictionary<string, Artifact>? Classifiers { get; set; }
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

