using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Pages
{
    /// <summary>
    /// MorePage.xaml 的交互逻辑
    /// </summary>
    public partial class MorePage : Page
    {
        // 调试控制台激活逻辑
        private int _appTitleClickCount = 0;
        private DateTime _lastAppTitleClickTime = DateTime.MinValue;
        private const int APP_TITLE_CLICK_RESET_MS = 2000; // 2秒内有效
        
        public MorePage()
        {
            InitializeComponent();
            
            // 加载版本信息
            LoadVersionInfo();
        }

        /// <summary>
        /// 加载版本信息
        /// </summary>
        private void LoadVersionInfo()
        {
            try
            {
                // 使用全局版本信息
                VersionText.Text = $"版本 {VersionInfo.DisplayVersion}";
                
                // 在调试模式下输出详细版本信息
                Debug.WriteLine("========== 版本信息 ==========");
                Debug.WriteLine(VersionInfo.GetDetailedVersionInfo());
                Debug.WriteLine("=============================");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取版本信息失败: {ex.Message}");
                VersionText.Text = "版本 1.0.0";
            }
        }

        /// <summary>
        /// 打开 GitHub 仓库
        /// </summary>
        private void OpenGitHub_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/mcobs/ObsMCLauncher");
        }

        /// <summary>
        /// 打开黑曜石论坛
        /// </summary>
        private void OpenForum_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://mcobs.cn/");
        }

        /// <summary>
        /// 打开 bangbang93 的爱发电页面
        /// </summary>
        private void OpenBangBang93_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://afdian.com/a/bangbang93");
        }

        /// <summary>
        /// 在默认浏览器中打开 URL
        /// </summary>
        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                
                Debug.WriteLine($"已打开链接: {url}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"打开链接失败: {ex.Message}");
                MessageBox.Show(
                    $"无法打开链接：{url}\n\n错误：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        /// <summary>
        /// 检查更新按钮点击
        /// </summary>
        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null)
            {
                button.IsEnabled = false;
                
                // 显示检查中状态
                var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };
                var progressRing = new MaterialDesignThemes.Wpf.PackIcon
                {
                    Kind = MaterialDesignThemes.Wpf.PackIconKind.Loading,
                    Width = 16,
                    Height = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                // 使用DynamicResource绑定前景色
                progressRing.SetResourceReference(MaterialDesignThemes.Wpf.PackIcon.ForegroundProperty, "TextBrush");
                
                var textBlock = new TextBlock
                {
                    Text = "检查中...",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13
                };
                // 使用DynamicResource绑定前景色
                textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                
                stackPanel.Children.Add(progressRing);
                stackPanel.Children.Add(textBlock);
                button.Content = stackPanel;
            }

            try
            {
                var newRelease = await UpdateService.CheckForUpdatesAsync();
                
                if (newRelease != null)
                {
                    await UpdateService.ShowUpdateDialogAsync(newRelease);
                }
                else
                {
                    NotificationManager.Instance.ShowNotification(
                        "已是最新版本",
                        $"当前版本 {VersionInfo.DisplayVersion} 已是最新版本",
                        NotificationType.Success,
                        3
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MorePage] 检查更新失败: {ex.Message}");
                NotificationManager.Instance.ShowNotification(
                    "检查更新失败",
                    "无法连接到更新服务器，请检查网络连接",
                    NotificationType.Error,
                    5
                );
            }
            finally
            {
                // 恢复按钮状态
                if (button != null)
                {
                    var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };
                    var icon = new MaterialDesignThemes.Wpf.PackIcon
                    {
                        Kind = MaterialDesignThemes.Wpf.PackIconKind.Update,
                        Width = 16,
                        Height = 16,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    // 使用DynamicResource绑定前景色
                    icon.SetResourceReference(MaterialDesignThemes.Wpf.PackIcon.ForegroundProperty, "TextBrush");
                    
                    var textBlock = new TextBlock
                    {
                        Text = "检查更新",
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 13
                    };
                    // 使用DynamicResource绑定前景色
                    textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                    
                    stackPanel.Children.Add(icon);
                    stackPanel.Children.Add(textBlock);
                    button.Content = stackPanel;
                    button.IsEnabled = true;
                }
            }
        }

        /// <summary>
        /// 应用标题点击事件（隐藏调试控制台激活）
        /// </summary>
        private void AppTitleText_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var now = DateTime.Now;
            var timeSinceLastClick = (now - _lastAppTitleClickTime).TotalMilliseconds;
            
            // 如果超过2秒，重置计数
            if (timeSinceLastClick > APP_TITLE_CLICK_RESET_MS)
            {
                _appTitleClickCount = 0;
            }
            
            _appTitleClickCount++;
            _lastAppTitleClickTime = now;
            
            Debug.WriteLine($"[MorePage] 标题点击次数: {_appTitleClickCount}");
            
            // 点击5次后弹出确认对话框
            if (_appTitleClickCount >= 5)
            {
                _appTitleClickCount = 0; // 重置计数
                ShowDebugConsoleConfirmationAsync();
            }
        }

        /// <summary>
        /// 显示调试控制台确认对话框
        /// </summary>
        private async void ShowDebugConsoleConfirmationAsync()
        {
            try
            {
                var result = await DialogManager.Instance.ShowConfirmDialogAsync(
                    "开发者模式",
                    "是否打开调试控制台？\n\n⚠️ 调试控制台仅供开发和测试使用",
                    "打开",
                    "取消"
                );
                
                if (result)
                {
                    ShowDebugConsole();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MorePage] 显示调试控制台确认对话框失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示调试控制台
        /// </summary>
        private void ShowDebugConsole()
        {
            try
            {
                // 创建调试控制台对话框
                var consoleDialog = new Border
                {
                    Width = 500,
                    Background = (System.Windows.Media.Brush)Application.Current.Resources["SurfaceBrush"],
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(24, 24, 24, 24),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var mainPanel = new StackPanel();

                // 标题
                var titlePanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 16)
                };

                var titleIcon = new MaterialDesignThemes.Wpf.PackIcon
                {
                    Kind = MaterialDesignThemes.Wpf.PackIconKind.Console,
                    Width = 28,
                    Height = 28,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                titleIcon.SetResourceReference(MaterialDesignThemes.Wpf.PackIcon.ForegroundProperty, "PrimaryBrush");

                var titleText = new TextBlock
                {
                    Text = "调试控制台",
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

                titlePanel.Children.Add(titleIcon);
                titlePanel.Children.Add(titleText);
                mainPanel.Children.Add(titlePanel);

                // 说明
                var descText = new TextBlock
                {
                    Text = "输入命令并按回车执行：",
                    FontSize = 13,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                descText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                mainPanel.Children.Add(descText);

                // 命令输入框
                var commandBox = new TextBox
                {
                    FontSize = 14,
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 12)
                };
                commandBox.SetResourceReference(TextBox.StyleProperty, typeof(TextBox));
                mainPanel.Children.Add(commandBox);

                // 可用命令列表
                var commandsListText = new TextBlock
                {
                    Text = "可用命令：\n" +
                           "• update - 测试更新对话框\n" +
                           "• version - 显示版本信息\n" +
                           "• help - 显示帮助",
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 16),
                    TextWrapping = TextWrapping.Wrap
                };
                commandsListText.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
                mainPanel.Children.Add(commandsListText);

                // 按钮区域
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var closeButton = new Button
                {
                    Content = "关闭",
                    Width = 80,
                    Height = 36,
                    FontSize = 13
                };
                closeButton.SetResourceReference(Button.StyleProperty, "MaterialDesignOutlinedButton");

                buttonPanel.Children.Add(closeButton);
                mainPanel.Children.Add(buttonPanel);

                consoleDialog.Child = mainPanel;

                // 添加到对话框容器
                var dialogContainer = Application.Current.MainWindow.FindName("GlobalDialogContainer") as Panel;
                if (dialogContainer != null)
                {
                    // 添加遮罩
                    var overlay = new Border
                    {
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(128, 0, 0, 0))
                    };

                    dialogContainer.Children.Add(overlay);
                    dialogContainer.Children.Add(consoleDialog);

                    // 关闭按钮事件
                    closeButton.Click += (s, e) =>
                    {
                        dialogContainer.Children.Remove(consoleDialog);
                        dialogContainer.Children.Remove(overlay);
                    };

                    // 遮罩点击关闭
                    overlay.MouseLeftButtonDown += (s, e) =>
                    {
                        dialogContainer.Children.Remove(consoleDialog);
                        dialogContainer.Children.Remove(overlay);
                    };

                    // 命令输入回车事件
                    commandBox.KeyDown += async (s, e) =>
                    {
                        if (e.Key == System.Windows.Input.Key.Enter)
                        {
                            var command = commandBox.Text.Trim().ToLower();
                            Debug.WriteLine($"[DebugConsole] 执行命令: {command}");

                            switch (command)
                            {
                                case "update":
                                    // 关闭控制台
                                    dialogContainer.Children.Remove(consoleDialog);
                                    dialogContainer.Children.Remove(overlay);
                                    // 显示测试更新对话框
                                    await UpdateService.ShowTestUpdateDialogAsync();
                                    break;

                                case "version":
                                    NotificationManager.Instance.ShowNotification(
                                        "版本信息",
                                        VersionInfo.GetDetailedVersionInfo(),
                                        NotificationType.Info,
                                        5
                                    );
                                    break;

                                case "help":
                                    NotificationManager.Instance.ShowNotification(
                                        "帮助",
                                        "可用命令：\n• update - 测试更新对话框\n• version - 显示版本信息\n• help - 显示帮助",
                                        NotificationType.Info,
                                        5
                                    );
                                    break;

                                case "":
                                    break;

                                default:
                                    NotificationManager.Instance.ShowNotification(
                                        "未知命令",
                                        $"命令 '{command}' 不存在，输入 'help' 查看可用命令",
                                        NotificationType.Warning,
                                        3
                                    );
                                    break;
                            }

                            commandBox.Text = "";
                        }
                    };

                    // 自动聚焦到输入框
                    commandBox.Focus();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MorePage] 显示调试控制台失败: {ex.Message}");
                NotificationManager.Instance.ShowNotification(
                    "错误",
                    "无法打开调试控制台",
                    NotificationType.Error,
                    3
                );
            }
        }
    }
}
