using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MaterialDesignThemes.Wpf;
using ObsMCLauncher.Models;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Pages
{
    public partial class AccountManagementPage : Page
    {
        private bool _isLoggingIn = false;

        public AccountManagementPage()
        {
            InitializeComponent();
            Loaded += AccountManagementPage_Loaded;
            Unloaded += AccountManagementPage_Unloaded;
        }

        /// <summary>
        /// 重置登录状态（供外部调用）
        /// </summary>
        public void ResetLoginState()
        {
            _isLoggingIn = false;
            System.Diagnostics.Debug.WriteLine("[AccountManagementPage] 登录状态已重置");
        }

        private void AccountManagementPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAccounts();
        }

        private void AccountManagementPage_Unloaded(object sender, RoutedEventArgs e)
        {
            // 页面卸载时，确保清理状态
        }

        /// <summary>
        /// 加载账号列表
        /// </summary>
        private void LoadAccounts()
        {
            AccountListPanel.Children.Clear();

            var accounts = AccountService.Instance.GetAllAccounts();

            if (accounts.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    Text = "暂无账号，请添加账号",
                    FontSize = 14,
                    Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 40, 0, 0)
                };
                AccountListPanel.Children.Add(emptyText);
                return;
            }

            foreach (var account in accounts)
            {
                var accountCard = CreateAccountCard(account);
                AccountListPanel.Children.Add(accountCard);
            }

            // 检查并更新外置登录按钮状态
            UpdateYggdrasilButtonState();
        }

        /// <summary>
        /// 更新外置登录按钮状态
        /// </summary>
        private void UpdateYggdrasilButtonState()
        {
            // 查找外置登录按钮（通过遍历可视树）
            var yggdrasilButton = FindVisualChild<Button>(this, btn =>
            {
                var content = btn.Content as StackPanel;
                if (content != null)
                {
                    foreach (var child in content.Children)
                    {
                        if (child is TextBlock tb && tb.Text == "外置登录")
                        {
                            return true;
                        }
                    }
                }
                return false;
            });

            if (yggdrasilButton != null)
            {
                bool hasAuthlibInjector = AuthlibInjectorService.IsAuthlibInjectorExists();

                if (!hasAuthlibInjector)
                {
                    yggdrasilButton.Opacity = 0.5;
                    yggdrasilButton.ToolTip = "需要下载 authlib-injector.jar";
                }
                else
                {
                    yggdrasilButton.Opacity = 1.0;
                    yggdrasilButton.ToolTip = null;
                }
            }
        }

        /// <summary>
        /// 查找可视树中的子元素
        /// </summary>
        private T? FindVisualChild<T>(DependencyObject parent, Func<T, bool> predicate) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild && predicate(typedChild))
                {
                    return typedChild;
                }

                var result = FindVisualChild(child, predicate);
                if (result != null) return result;
            }
            return null;
        }

        /// <summary>
        /// 获取 Yggdrasil 服务器名称
        /// </summary>
        private string GetYggdrasilServerName(GameAccount account)
        {
            if (account.Type != AccountType.Yggdrasil || string.IsNullOrEmpty(account.YggdrasilServerId))
            {
                return "外置登录账户";
            }

            var server = YggdrasilServerService.Instance.GetServerById(account.YggdrasilServerId);
            return server != null ? $"外置登录 • {server.Name}" : "外置登录账户";
        }

        /// <summary>
        /// 创建账号卡片
        /// </summary>
        private Border CreateAccountCard(GameAccount account)
        {
            var border = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceElevatedBrush"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 15)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 头像容器
            Brush avatarBackground = account.Type switch
            {
                AccountType.Offline => (Brush)Application.Current.FindResource("PrimaryBrush"),
                AccountType.Microsoft => (Brush)Application.Current.FindResource("SecondaryBrush"),
                AccountType.Yggdrasil => new SolidColorBrush(Color.FromRgb(102, 187, 106)),
                _ => (Brush)Application.Current.FindResource("PrimaryBrush")
            };

            var avatarBorder = new Border
            {
                Width = 60,
                Height = 60,
                CornerRadius = new CornerRadius(8),
                Background = avatarBackground,
                Margin = new Thickness(0, 0, 20, 0),
                ClipToBounds = true
            };

            // 尝试加载皮肤头像
            var skinHeadImage = LoadSkinHead(account);
            
            if (skinHeadImage != null)
            {
                // 使用皮肤头像
                var headImage = new System.Windows.Controls.Image
                {
                    Source = skinHeadImage,
                    Stretch = Stretch.UniformToFill,
                    Width = 60,
                    Height = 60
                };
                avatarBorder.Child = headImage;
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

                var avatarIcon = new PackIcon
                {
                    Kind = iconKind,
                    Width = 40,
                    Height = 40,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.White
                };
                avatarBorder.Child = avatarIcon;
            }

            Grid.SetColumn(avatarBorder, 0);

            // 账号信息
            var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var nameText = new TextBlock
            {
                FontSize = 18,
                FontWeight = FontWeights.SemiBold
            };

            nameText.Inlines.Add(new System.Windows.Documents.Run(account.Username));
            if (account.IsDefault)
            {
                nameText.Inlines.Add(new System.Windows.Documents.Run(" [默认]")
                {
                    FontSize = 13,
                    Foreground = (Brush)Application.Current.FindResource("PrimaryBrush")
                });
            }

            string typeDescription = account.Type switch
            {
                AccountType.Offline => "离线账户",
                AccountType.Microsoft => $"微软账户 • {account.Email}",
                AccountType.Yggdrasil => GetYggdrasilServerName(account),
                _ => "未知类型"
            };

            var typeText = new TextBlock
            {
                Text = typeDescription,
                FontSize = 13,
                Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 5, 0, 0)
            };

            infoPanel.Children.Add(nameText);
            infoPanel.Children.Add(typeText);
            Grid.SetColumn(infoPanel, 1);

            // 操作按钮
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 刷新按钮（刷新令牌和皮肤）
            var refreshButton = new Button
            {
                Style = (Style)Application.Current.FindResource("MaterialDesignIconButton"),
                ToolTip = "刷新令牌和皮肤",
                Tag = account.Id,
                Margin = new Thickness(0, 0, 5, 0)
            };
            refreshButton.Content = new PackIcon
            {
                Kind = PackIconKind.Refresh,
                Width = 20,
                Height = 20,
                Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush")
            };
            refreshButton.Click += RefreshAccount_Click;

            // 设为默认按钮
            var defaultButton = new Button
            {
                Style = (Style)Application.Current.FindResource("MaterialDesignIconButton"),
                ToolTip = account.IsDefault ? "已是默认账号" : "设为默认",
                Tag = account.Id,
                Margin = new Thickness(0, 0, 5, 0),
                IsEnabled = !account.IsDefault
            };
            defaultButton.Content = new PackIcon
            {
                Kind = account.IsDefault ? PackIconKind.CheckCircle : PackIconKind.CheckCircleOutline,
                Width = 20,
                Height = 20,
                Foreground = account.IsDefault 
                    ? (Brush)Application.Current.FindResource("PrimaryBrush")
                    : (Brush)Application.Current.FindResource("TextSecondaryBrush")
            };
            defaultButton.Click += SetDefaultAccount_Click;

            // 删除按钮
            var deleteButton = new Button
            {
                Style = (Style)Application.Current.FindResource("MaterialDesignIconButton"),
                ToolTip = "删除",
                Tag = account.Id,
                Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68))
            };
            deleteButton.Content = new PackIcon
            {
                Kind = PackIconKind.Delete,
                Width = 20,
                Height = 20
            };
            deleteButton.Click += DeleteAccount_Click;

            buttonPanel.Children.Add(refreshButton);
            buttonPanel.Children.Add(defaultButton);
            buttonPanel.Children.Add(deleteButton);
            Grid.SetColumn(buttonPanel, 2);

            grid.Children.Add(avatarBorder);
            grid.Children.Add(infoPanel);
            grid.Children.Add(buttonPanel);

            border.Child = grid;
            return border;
        }

        private async void AddMicrosoftAccount_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoggingIn)
            {
                System.Diagnostics.Debug.WriteLine("[AccountManagementPage] 登录正在进行中，忽略重复点击");
                return; // 防止重复点击
            }
            
            System.Diagnostics.Debug.WriteLine("[AccountManagementPage] 开始微软账户登录流程");
            _isLoggingIn = true;
            
            // 获取主窗口引用
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null) return;
            
            // 创建取消令牌源
            var cts = new System.Threading.CancellationTokenSource();
            
            try
            {
                // 1. 先设置取消令牌源到MainWindow（必须在ShowLoginProgress之前）
                mainWindow.SetLoginCancellationTokenSource(cts);
                
                // 2. 显示登录进度（使用MainWindow的全局UI）
                mainWindow.ShowLoginProgress("准备登录...");

                // 3. 创建认证服务并设置进度回调
                var authService = new MicrosoftAuthService();
                authService.OnProgressUpdate = mainWindow.UpdateLoginProgress;
                authService.OnAuthUrlGenerated = mainWindow.ShowAuthUrlDialog;

                // 4. 开始登录（传入取消令牌）
                var account = await authService.LoginAsync(cts.Token);

                if (account != null)
                {
                    AccountService.Instance.AddMicrosoftAccount(account);
                    LoadAccounts();
                    
                    // 隐藏所有对话框
                    mainWindow.HideAllLoginDialogs();
                    
                    // 使用通知管理器显示成功消息
                    NotificationManager.Instance.ShowNotification(
                        "登录成功",
                        $"成功添加微软账户：{account.Username}",
                        NotificationType.Success,
                        5
                    );
                }
                else
                {
                    mainWindow.HideAllLoginDialogs();
                    await DialogManager.Instance.ShowWarning(
                        "登录失败",
                        "登录失败！\n\n可能的原因：\n• 未购买正版 Minecraft\n• 网络连接问题\n• 授权被取消"
                    );
                }
            }
            catch (OperationCanceledException)
            {
                // 用户取消登录，不显示错误消息
                mainWindow.HideAllLoginDialogs();
                System.Diagnostics.Debug.WriteLine("[AccountManagementPage] 用户取消了微软登录");
            }
            catch (Exception ex)
            {
                mainWindow.HideAllLoginDialogs();
                
                await DialogManager.Instance.ShowError(
                    "登录错误",
                    $"微软账户登录失败\n\n错误: {ex.Message}\n\n请检查网络连接后重试"
                );
                
                System.Diagnostics.Debug.WriteLine($"微软登录错误详情: {ex}");
            }
            finally
            {
                _isLoggingIn = false;
                cts?.Dispose();
            }
        }

        private async void AddYggdrasilAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 先检查 authlib-injector.jar 是否存在
                if (!AuthlibInjectorService.IsAuthlibInjectorExists())
                {
                    var result = await DialogManager.Instance.ShowQuestion(
                        "缺少必需文件",
                        "外置登录需要 authlib-injector.jar 文件。\n\n是否立即下载？",
                        DialogButtons.YesNo
                    );

                    if (result != Utils.DialogResult.Yes)
                    {
                        return;
                    }

                    // 下载 authlib-injector.jar
                    var config = LauncherConfig.Load();
                    var useBMCLAPI = config.DownloadSource == DownloadSource.BMCLAPI;

                    var notificationId = NotificationManager.Instance.ShowNotification(
                        "下载中",
                        "正在下载 authlib-injector.jar...",
                        NotificationType.Progress
                    );

                    try
                    {
                        var service = new AuthlibInjectorService();
                        service.OnProgressUpdate = (downloaded, total) =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (total > 0)
                                {
                                    var progress = (double)downloaded / total * 100;
                                    var downloadedMB = downloaded / 1024.0 / 1024.0;
                                    var totalMB = total / 1024.0 / 1024.0;
                                    NotificationManager.Instance.UpdateNotification(notificationId,
                                        $"正在下载... {downloadedMB:F2} MB / {totalMB:F2} MB ({progress:F1}%)");
                                }
                            });
                        };

                        await service.DownloadAuthlibInjectorAsync(useBMCLAPI);

                        NotificationManager.Instance.RemoveNotification(notificationId);
                        NotificationManager.Instance.ShowNotification(
                            "下载完成",
                            $"authlib-injector.jar 下载成功！",
                            NotificationType.Success,
                            3
                        );
                        
                        // 更新按钮状态
                        UpdateYggdrasilButtonState();
                    }
                    catch (Exception ex)
                    {
                        NotificationManager.Instance.RemoveNotification(notificationId);
                        await DialogManager.Instance.ShowError(
                            "下载失败",
                            $"下载 authlib-injector.jar 失败：\n\n{ex.Message}\n\n请检查网络连接或稍后重试。"
                        );
                        return;
                    }
                }

                // 使用悬浮框显示外置登录面板
                var account = await YggdrasilPanelManager.Instance.ShowLoginPanelAsync();
                
                if (account != null)
                {
                    // 添加账号
                    AccountService.Instance.AddYggdrasilAccount(account);
                    LoadAccounts();
                    
                    // 显示成功消息
                    NotificationManager.Instance.ShowNotification(
                        "登录成功",
                        $"成功添加外置登录账号：{account.Username}",
                        NotificationType.Success,
                        3
                    );
                }
            }
            catch (Exception ex)
            {
                await DialogManager.Instance.ShowError("错误", $"添加外置登录账号失败：{ex.Message}");
            }
        }

        private void AddOfflineAccount_Click(object sender, RoutedEventArgs e)
        {
            // 显示输入界面
            AddAccountPanel.Visibility = Visibility.Visible;
            UsernameTextBox.Clear();
            UsernameTextBox.Focus();
        }

        private async void ConfirmAddAccount_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameTextBox.Text.Trim();

            if (string.IsNullOrEmpty(username))
            {
                await DialogManager.Instance.ShowWarning("提示", "请输入用户名");
                return;
            }

            if (username.Length < 3 || username.Length > 16)
            {
                await DialogManager.Instance.ShowWarning("提示", "用户名长度必须在 3-16 个字符之间");
                return;
            }

            try
            {
                AccountService.Instance.AddOfflineAccount(username);
                AddAccountPanel.Visibility = Visibility.Collapsed;
                LoadAccounts();
                
                // 使用通知管理器显示成功消息
                NotificationManager.Instance.ShowNotification(
                    "添加成功",
                    $"成功添加离线账户：{username}",
                    NotificationType.Success,
                    3
                );
            }
            catch (Exception ex)
            {
                await DialogManager.Instance.ShowError("错误", ex.Message);
            }
        }

        private void CancelAddAccount_Click(object sender, RoutedEventArgs e)
        {
            AddAccountPanel.Visibility = Visibility.Collapsed;
            UsernameTextBox.Clear();
        }

        private void SetDefaultAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string accountId)
            {
                AccountService.Instance.SetDefaultAccount(accountId);
                LoadAccounts();
            }
        }

        private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string accountId)
            {
                var account = AccountService.Instance.GetAllAccounts()
                    .FirstOrDefault(a => a.Id == accountId);

                if (account != null)
                {
                    var result = await DialogManager.Instance.ShowQuestion(
                        "确认删除",
                        $"确定要删除账号 '{account.Username}' 吗？",
                        DialogButtons.YesNo
                    );

                    if (result == DialogResult.Yes)
                    {
                        AccountService.Instance.DeleteAccount(accountId);
                        LoadAccounts();
                        
                        // 使用通知管理器显示成功消息
                        NotificationManager.Instance.ShowNotification(
                            "删除成功",
                            $"账号 '{account.Username}' 已删除",
                            NotificationType.Success,
                            3
                        );
                    }
                }
            }
        }

        /// <summary>
        /// 刷新账号（令牌和皮肤）
        /// </summary>
        private async void RefreshAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string accountId)
            {
                var account = AccountService.Instance.GetAllAccounts()
                    .FirstOrDefault(a => a.Id == accountId);

                if (account == null) return;

                // 禁用刷新按钮，防止重复点击
                button.IsEnabled = false;
                var originalIcon = (PackIcon)button.Content;
                var originalKind = originalIcon.Kind;

                try
                {
                    // 显示加载动画
                    originalIcon.Kind = PackIconKind.Loading;

                    var notificationId = NotificationManager.Instance.ShowNotification(
                        "刷新中",
                        $"正在刷新账号 '{account.Username}'...",
                        NotificationType.Progress
                    );

                    bool tokenRefreshed = false;

                    // 根据账号类型刷新令牌
                    if (account.Type == AccountType.Microsoft)
                    {
                        tokenRefreshed = await AccountService.Instance.RefreshMicrosoftAccountAsync(accountId);
                    }
                    else if (account.Type == AccountType.Yggdrasil)
                    {
                        tokenRefreshed = await AccountService.Instance.RefreshYggdrasilAccountAsync(accountId);
                    }

                    // 刷新皮肤
                    var skinPath = await SkinService.Instance.GetSkinHeadPathAsync(account, forceRefresh: true);

                    NotificationManager.Instance.RemoveNotification(notificationId);

                    if (account.Type == AccountType.Offline || tokenRefreshed || skinPath != null)
                    {
                        // 重新加载账号列表以更新UI
                        LoadAccounts();

                        NotificationManager.Instance.ShowNotification(
                            "刷新成功",
                            $"账号 '{account.Username}' 已刷新",
                            NotificationType.Success,
                            3
                        );
                    }
                    else
                    {
                        await DialogManager.Instance.ShowWarning(
                            "刷新失败",
                            $"刷新账号 '{account.Username}' 失败\n\n可能的原因：\n• 令牌已过期，需要重新登录\n• 网络连接问题"
                        );
                    }
                }
                catch (Exception ex)
                {
                    await DialogManager.Instance.ShowError(
                        "刷新错误",
                        $"刷新账号时发生错误：\n\n{ex.Message}"
                    );
                }
                finally
                {
                    // 恢复按钮状态
                    originalIcon.Kind = originalKind;
                    button.IsEnabled = true;
                }
            }
        }

        /// <summary>
        /// 加载皮肤头像
        /// </summary>
        private System.Windows.Media.ImageSource? LoadSkinHead(GameAccount account)
        {
            try
            {
                // 检查是否有缓存的皮肤
                if (!string.IsNullOrEmpty(account.CachedSkinPath) && File.Exists(account.CachedSkinPath))
                {
                    return Utils.SkinHeadRenderer.GetHeadFromSkin(account.CachedSkinPath);
                }

                // 异步加载皮肤（不阻塞UI）
                _ = LoadSkinHeadAsync(account);

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AccountManagementPage] 加载皮肤头像失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 异步加载皮肤头像
        /// </summary>
        private async System.Threading.Tasks.Task LoadSkinHeadAsync(GameAccount account)
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
                System.Diagnostics.Debug.WriteLine($"[AccountManagementPage] 异步加载皮肤失败: {ex.Message}");
            }
        }
    }
}

