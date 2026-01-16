using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            Unloaded += HomePage_Unloaded;
        }

        private void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAccounts();
            LoadVersions();
            LoadGameLogCheckBoxState();
        }

        private void HomePage_Unloaded(object sender, RoutedEventArgs e)
        {
            // 清理事件订阅，防止内存泄漏
            if (VersionComboBox != null)
            {
                VersionComboBox.SelectionChanged -= VersionComboBox_SelectionChanged;
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

                // 尝试加载皮肤头像
                var skinHeadImage = LoadSkinHeadForComboBox(account);
                
                if (skinHeadImage != null)
                {
                    // 使用皮肤头像
                    var headBorder = new Border
                    {
                        Width = 24,
                        Height = 24,
                        CornerRadius = new CornerRadius(4),
                        ClipToBounds = true,
                        Margin = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var headImage = new System.Windows.Controls.Image
                    {
                        Source = skinHeadImage,
                        Stretch = Stretch.UniformToFill,
                        Width = 24,
                        Height = 24
                    };

                    headBorder.Child = headImage;
                    panel.Children.Add(headBorder);
                }
                else
                {
                    // 回退到账号类型图标
                    PackIconKind iconKind = account.Type switch
                    {
                        AccountType.Offline => PackIconKind.Account,
                        AccountType.Microsoft => PackIconKind.Microsoft,
                        AccountType.Yggdrasil => PackIconKind.Shield,
                        _ => PackIconKind.Account
                    };

                    var icon = new PackIcon
                    {
                        Kind = iconKind,
                        Width = 20,
                        Height = 20,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 10, 0)
                    };
                    panel.Children.Add(icon);
                }

                var text = new TextBlock
                {
                    Text = account.DisplayName,
                    VerticalAlignment = VerticalAlignment.Center
                };

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

            // 添加所有版本（优化UI元素创建）
            foreach (var version in installedVersions)
            {
                var item = new ComboBoxItem
                {
                    Tag = version.Id,
                    ToolTip = version.Id != version.ActualVersionId ? $"版本: {version.ActualVersionId}" : null
                };

                // 添加加载器图标
                var icon = GetVersionLoaderIcon(version);
                if (icon != null)
                {
                    // 创建包含图标和文本的面板（只在有图标时创建）
                    var panel = new StackPanel { Orientation = Orientation.Horizontal };
                    icon.VerticalAlignment = VerticalAlignment.Center;
                    icon.Margin = new Thickness(0, 0, 8, 0);
                    panel.Children.Add(icon);
                    
                    // 添加版本名称文本
                    var text = new TextBlock
                    {
                        Text = version.Id,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    panel.Children.Add(text);
                    item.Content = panel;
                }
                else
                {
                    // 没有图标时直接使用文本，减少UI对象创建
                    item.Content = version.Id;
                }

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
                    Console.WriteLine("请先选择一个游戏版本！");
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
                    Console.WriteLine("未找到游戏账号，请前往账号管理添加账号");
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
                    },
                    cancellationTokenSource: launchCts
                );

                // 5. 先检查游戏完整性（不启动游戏）
                
                LaunchButton.Content = "检查依赖中...";
                bool hasIntegrityIssue = await GameLauncher.CheckGameIntegrityAsync(versionId, config, (progress) =>
                {
                    NotificationManager.Instance.UpdateNotification(launchNotificationId, progress);
                    LaunchButton.Content = progress;
                }, launchCts.Token);

                // 6. 如果检测到缺失的必需库文件，自动下载
                if (hasIntegrityIssue && GameLauncher.MissingLibraries.Count > 0)
                {
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
                        Console.WriteLine("必需依赖库下载失败！");
                        
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
                    Console.WriteLine($"检测到 {GameLauncher.MissingOptionalLibraries.Count} 个缺失的可选库，静默尝试下载...");
                    
                    // 静默下载可选库文件（失败不阻止启动，不显示任何用户通知）
                    bool optionalSuccess = await DownloadMissingLibraries(versionId, config, notificationId: null, isOptional: true, launchCts.Token);
                    
                    // 只在控制台记录结果
                    if (!optionalSuccess)
                    {
                        Console.WriteLine("部分可选库下载失败（不影响游戏启动）");
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
                    } // 结束 if (!assetsResult.Success) 块
                    } // 结束 else (!isVeryOldVersion) 块
                    
                    // 8. Assets检查完成后，正式启动游戏
                    NotificationManager.Instance.UpdateNotification(
                        launchNotificationId,
                        "正在启动游戏..."
                    );
                    LaunchButton.Content = "启动中...";
                    
                    // 创建游戏日志窗口（如果配置启用）
                    Windows.GameLogWindow? logWindow = null;
                    if (config.ShowGameLogOnLaunch)
                    {
                        logWindow = new Windows.GameLogWindow(versionId);
                        logWindow.Show();
                    }
                    
                    bool finalLaunchSuccess = await GameLauncher.LaunchGameAsync(
                        versionId, 
                        account, 
                        config, 
                        (progress) =>
                        {
                            NotificationManager.Instance.UpdateNotification(launchNotificationId, progress);
                            LaunchButton.Content = progress;
                        },
                        (output) =>
                        {
                            // 游戏输出回调
                            logWindow?.AppendGameOutput(output);
                        },
                        (exitCode) =>
                        {
                            // 游戏退出回调
                            logWindow?.OnGameExit(exitCode);
                        },
                        launchCts.Token);
                    
                    // 移除启动进度通知
                    NotificationManager.Instance.RemoveNotification(launchNotificationId);

                    if (finalLaunchSuccess)
                    {
                        // 更新账号最后使用时间
                        AccountService.Instance.UpdateLastUsed(account.Id);

                        Console.WriteLine($"游戏已启动！版本: {versionId}, 账号: {account.Username}");
                        
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
                        
                        Console.WriteLine($"{errorMessage}");
                        
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
                Console.WriteLine($"游戏启动已取消");
                
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
                Console.WriteLine($"启动游戏异常: {ex.Message}\n{ex.StackTrace}");
                
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
                    return true;
                }
                
                // 读取版本JSON
                var versionJsonPath = Path.Combine(config.GameDirectory, "versions", versionId, $"{versionId}.json");
                if (!File.Exists(versionJsonPath))
                {
                    return false;
                }

                var versionJson = await File.ReadAllTextAsync(versionJsonPath);
                var versionDetail = JsonSerializer.Deserialize<VersionDetail>(versionJson, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                if (versionDetail?.Libraries == null)
                {
                    return false;
                }

                var librariesDir = Path.Combine(config.GameDirectory, "libraries");
                var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(5);
                
                int totalLibs = targetLibraries.Count;
                int processedLibs = 0;        // 已处理的库数量
                int successfullyDownloaded = 0;  // 成功下载的库数量
                int skippedLibs = 0;          // 跳过的库（没有URL等）


                foreach (var lib in versionDetail.Libraries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    if (lib.Name == null) continue;
                    
                    // 检查是否是缺失的库
                    if (!targetLibraries.Contains(lib.Name)) continue;

                    // 检查操作系统规则
                    if (!IsLibraryAllowedForOS(lib))
                    {
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
                                        
                                        Console.WriteLine($"[{processedLibs}/{totalLibs}] {lib.Name} (natives)");
                                        
                                        var response = await httpClient.GetAsync(url, cancellationToken);
                                        response.EnsureSuccessStatusCode();
                                        var fileBytes = await response.Content.ReadAsByteArrayAsync();
                                        await File.WriteAllBytesAsync(nativesPath, fileBytes);
                                        
                                        if (File.Exists(nativesPath))
                                        {
                                            var fileInfo = new FileInfo(nativesPath);
                                            successfullyDownloaded++;
                                            downloaded = true;
                                            Console.WriteLine($"已下载natives: {lib.Name} ({fileInfo.Length / 1024.0:F2} KB)");
                                        }
                                        else
                                        {
                                            Console.WriteLine($"natives下载后文件不存在: {lib.Name}");
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
                                Console.WriteLine($"无法获取库路径: {lib.Name}");
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
                                }
                                else if (!string.IsNullOrEmpty(lib.Downloads?.Artifact?.Url))
                                {
                                    // 备用方案：使用version.json中的URL
                                    url = lib.Downloads.Artifact.Url;
                                }
                                else
                                {
                                    Console.WriteLine($"无法获取下载URL: {lib.Name}");
                                    continue;
                                }
                                
                                Console.WriteLine($"[{processedLibs}/{totalLibs}] {lib.Name}");
                                
                                // 使用HttpClient下载
                                var response = await httpClient.GetAsync(url, cancellationToken);
                                
                                // 对于404错误且是特定的Forge库，跳过（这些库可能从JAR中提取或不需要）
                                if (!response.IsSuccessStatusCode && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                                {
                                    if (lib.Name != null && (lib.Name.Contains("forge") && (lib.Name.Contains(":client") || lib.Name.Contains(":server"))))
                                    {
                                        Console.WriteLine($"跳过: {lib.Name} (Forge特殊库)");
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
                                    Console.WriteLine($"已下载: {lib.Name} ({fileInfo.Length / 1024.0:F2} KB)");
                                }
                                else
                                {
                                    Console.WriteLine($"下载后文件不存在: {lib.Name}");
                                }
                            }
                        }
                        // 3. 如果既没有下载成功，跳过
                        if (!downloaded)
                        {
                            Console.WriteLine($"跳过: {lib.Name}");
                            skippedLibs++;  // 跳过计数
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"下载失败: {lib.Name} - {ex.Message}");
                        // 继续下载其他库
                    }
                }

                httpClient.Dispose();
                
                // 显示下载结果统计
                
                // 只有当所有需要下载的库都成功时才返回true
                // 跳过的库（无URL）不影响成功判定，因为这些库可能不是必需的
                bool allSuccessful = (successfullyDownloaded + skippedLibs) >= totalLibs;
                
                if (successfullyDownloaded > 0)
                {
                }
                
                if (skippedLibs > 0)
                {
                }
                
                return allSuccessful;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
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
        /// 获取版本加载器图标
        /// </summary>
        private PackIcon? GetVersionLoaderIcon(InstalledVersion version)
        {
            try
            {
                var config = LauncherConfig.Load();
                var versionJsonPath = Path.Combine(config.GameDirectory, "versions", version.ActualVersionId, $"{version.ActualVersionId}.json");
                
                if (!File.Exists(versionJsonPath))
                {
                    return null;
                }

                var jsonContent = File.ReadAllText(versionJsonPath);

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

        /// <summary>
        /// 为 ComboBox 加载皮肤头像
        /// </summary>
        private System.Windows.Media.ImageSource? LoadSkinHeadForComboBox(GameAccount account)
        {
            try
            {
                // 检查是否有缓存的皮肤
                if (!string.IsNullOrEmpty(account.CachedSkinPath) && File.Exists(account.CachedSkinPath))
                {
                    return Utils.SkinHeadRenderer.GetHeadFromSkin(account.CachedSkinPath, size: 24);
                }

                // 异步加载皮肤（不阻塞UI）
                _ = LoadSkinHeadForComboBoxAsync(account);

                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        /// <summary>
        /// 异步加载皮肤头像（用于 ComboBox）
        /// </summary>
        private async Task LoadSkinHeadForComboBoxAsync(GameAccount account)
        {
            try
            {
                var skinPath = await SkinService.Instance.GetSkinHeadPathAsync(account);
                
                if (!string.IsNullOrEmpty(skinPath))
                {
                    // 在UI线程上重新加载账号列表
                    await Dispatcher.InvokeAsync(() => LoadAccounts());
                }
            }
            catch (Exception ex)
            {
            }
        }

        /// <summary>
        /// 管理版本按钮点击事件
        /// </summary>
        private void ManageVersionButton_Click(object sender, RoutedEventArgs e)
        {
            // 获取当前选中的版本
            if (VersionComboBox.SelectedItem is not ComboBoxItem versionItem || versionItem.Tag is not string versionId)
            {
                NotificationManager.Instance.ShowNotification(
                    "无法打开",
                    "请先选择一个游戏版本",
                    NotificationType.Warning,
                    3
                );
                return;
            }

            // 获取版本信息
            var config = LauncherConfig.Load();
            var installedVersions = LocalVersionService.GetInstalledVersions(config.GameDirectory);
            var version = installedVersions.FirstOrDefault(v => v.Id == versionId);

            if (version == null)
            {
                NotificationManager.Instance.ShowNotification(
                    "版本不存在",
                    $"未找到版本 {versionId}",
                    NotificationType.Error,
                    3
                );
                return;
            }

            // 导航到版本实例管理页面
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow != null)
            {
                var mainFrame = mainWindow.FindName("MainFrame") as Frame;
                if (mainFrame != null)
                {
                    var instancePage = new VersionInstancePage(version);
                    
                    // 设置返回回调
                    instancePage.OnBackRequested = () =>
                    {
                        // 返回到主页
                        mainFrame.Navigate(this);
                        
                        // 刷新版本列表
                        LoadVersions();
                    };
                    
                    mainFrame.Navigate(instancePage);
                }
            }
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

        /// <summary>
        /// 加载游戏日志复选框状态
        /// </summary>
        private void LoadGameLogCheckBoxState()
        {
            var config = LauncherConfig.Load();
            ShowGameLogCheckBox.IsChecked = config.ShowGameLogOnLaunch;
        }

        /// <summary>
        /// 游戏日志复选框状态改变
        /// </summary>
        private void ShowGameLogCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var config = LauncherConfig.Load();
            config.ShowGameLogOnLaunch = ShowGameLogCheckBox.IsChecked == true;
            config.Save();
        }
    }
}

