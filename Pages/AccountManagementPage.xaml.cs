using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MaterialDesignThemes.Wpf;
using ObsMCLauncher.Models;
using ObsMCLauncher.Services;

namespace ObsMCLauncher.Pages
{
    public partial class AccountManagementPage : Page
    {
        private string _currentAuthUrl = "";
        private bool _isLoggingIn = false;

        public AccountManagementPage()
        {
            InitializeComponent();
            Loaded += AccountManagementPage_Loaded;
            Unloaded += AccountManagementPage_Unloaded;
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
            if (_isLoggingIn) return; // 防止重复点击
            
            _isLoggingIn = true;
            
            try
            {
                // 1. 显示登录进度
                ShowLoginProgress("准备登录...");

                // 2. 创建认证服务并设置进度回调
                var authService = new MicrosoftAuthService();
                authService.OnProgressUpdate = UpdateLoginProgress;
                authService.OnAuthUrlGenerated = ShowAuthUrlDialog;

                // 3. 开始登录
                var account = await authService.LoginAsync();

                if (account != null)
                {
                    AccountService.Instance.AddMicrosoftAccount(account);
                    LoadAccounts();
                    
                    // 隐藏所有对话框
                    HideAllDialogs();
                    
                    MessageBox.Show(
                        $"✅ 成功添加微软账户\n\n" +
                        $"游戏名: {account.Username}\n" +
                        $"UUID: {account.MinecraftUUID}",
                        "登录成功",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    HideAllDialogs();
                    MessageBox.Show(
                        "❌ 登录失败！\n\n" +
                        "可能的原因：\n" +
                        "• 未购买正版 Minecraft\n" +
                        "• 网络连接问题\n" +
                        "• 授权被取消",
                        "登录失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                HideAllDialogs();
                
                MessageBox.Show(
                    $"❌ 微软账户登录失败\n\n" +
                    $"错误: {ex.Message}\n\n" +
                    "请检查网络连接后重试",
                    "登录错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                
                System.Diagnostics.Debug.WriteLine($"微软登录错误详情: {ex}");
            }
            finally
            {
                _isLoggingIn = false;
            }
        }

        /// <summary>
        /// 显示登录进度
        /// </summary>
        private void ShowLoginProgress(string status)
        {
            Dispatcher.Invoke(() =>
            {
                LoginProgressStatus.Text = status;
                LoginProgressPanel.Visibility = Visibility.Visible;
            });
        }

        /// <summary>
        /// 更新登录进度
        /// </summary>
        private void UpdateLoginProgress(string status)
        {
            Dispatcher.Invoke(() =>
            {
                LoginProgressStatus.Text = status;
            });
        }

        /// <summary>
        /// 显示授权URL对话框
        /// </summary>
        private void ShowAuthUrlDialog(string url)
        {
            _currentAuthUrl = url;
            
            Dispatcher.Invoke(() =>
            {
                AuthUrlText.Text = url;
                ModalOverlay.Visibility = Visibility.Visible;
                AuthUrlDialog.Visibility = Visibility.Visible;
                
                // 淡入动画
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                AuthUrlDialog.BeginAnimation(OpacityProperty, fadeIn);
            });
        }

        /// <summary>
        /// 隐藏所有对话框
        /// </summary>
        private void HideAllDialogs()
        {
            Dispatcher.Invoke(() =>
            {
                LoginProgressPanel.Visibility = Visibility.Collapsed;
                ModalOverlay.Visibility = Visibility.Collapsed;
                AuthUrlDialog.Visibility = Visibility.Collapsed;
            });
        }

        /// <summary>
        /// 复制授权URL
        /// </summary>
        private void CopyAuthUrl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_currentAuthUrl);
                
                var button = sender as Button;
                if (button != null)
                {
                    var originalContent = button.Content;
                    button.Content = "✅ 已复制";
                    
                    var timer = new System.Windows.Threading.DispatcherTimer();
                    timer.Interval = TimeSpan.FromSeconds(2);
                    timer.Tick += (s, args) =>
                    {
                        button.Content = originalContent;
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"复制失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 关闭授权URL对话框
        /// </summary>
        private void CloseAuthUrlDialog_Click(object sender, RoutedEventArgs e)
        {
            ModalOverlay.Visibility = Visibility.Collapsed;
            AuthUrlDialog.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 点击遮罩层时不执行任何操作（阻止关闭）
        /// </summary>
        private void ModalOverlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 不关闭对话框，保持模态状态
        }

        private void AddOfflineAccount_Click(object sender, RoutedEventArgs e)
        {
            // 显示输入界面
            AddAccountPanel.Visibility = Visibility.Visible;
            UsernameTextBox.Clear();
            UsernameTextBox.Focus();
        }

        private void ConfirmAddAccount_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameTextBox.Text.Trim();

            if (string.IsNullOrEmpty(username))
            {
                MessageBox.Show("请输入用户名", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (username.Length < 3 || username.Length > 16)
            {
                MessageBox.Show("用户名长度必须在 3-16 个字符之间", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                AccountService.Instance.AddOfflineAccount(username);
                AddAccountPanel.Visibility = Visibility.Collapsed;
                LoadAccounts();
                MessageBox.Show($"成功添加离线账户：{username}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void DeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string accountId)
            {
                var account = AccountService.Instance.GetAllAccounts()
                    .FirstOrDefault(a => a.Id == accountId);

                if (account != null)
                {
                    var result = MessageBox.Show(
                        $"确定要删除账号 '{account.Username}' 吗？",
                        "确认删除",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        AccountService.Instance.DeleteAccount(accountId);
                        LoadAccounts();
                    }
                }
            }
        }
    }
}

