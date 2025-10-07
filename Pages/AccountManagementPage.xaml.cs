using System;
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
                // 显示空状态
                var emptyText = new TextBlock
                {
                    Text = "还没有添加账号，点击上方按钮添加账号",
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

            // 头像
            var avatarBorder = new Border
            {
                Width = 60,
                Height = 60,
                CornerRadius = new CornerRadius(30),
                Background = account.Type == AccountType.Offline
                    ? (Brush)Application.Current.FindResource("PrimaryBrush")
                    : (Brush)Application.Current.FindResource("SecondaryBrush"),
                Margin = new Thickness(0, 0, 20, 0)
            };

            var avatarIcon = new PackIcon
            {
                Kind = account.Type == AccountType.Offline ? PackIconKind.Account : PackIconKind.Microsoft,
                Width = 40,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White
            };

            avatarBorder.Child = avatarIcon;
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

            var typeText = new TextBlock
            {
                Text = account.Type == AccountType.Offline ? "离线账户" : $"微软账户 • {account.Email}",
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
    }
}

