using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ObsMCLauncher.Pages;

namespace ObsMCLauncher
{
    public partial class MainWindow : Window
    {
        private string _currentAuthUrl = "";

        public MainWindow()
        {
            InitializeComponent();
            
            // 默认导航到主页
            MainFrame.Navigate(new HomePage());
        }

        #region 全局登录UI控制方法

        /// <summary>
        /// 显示登录进度
        /// </summary>
        public void ShowLoginProgress(string status)
        {
            Dispatcher.Invoke(() =>
            {
                GlobalLoginProgressStatus.Text = status;
                GlobalLoginProgressPanel.Visibility = Visibility.Visible;
            });
        }

        /// <summary>
        /// 更新登录进度
        /// </summary>
        public void UpdateLoginProgress(string status)
        {
            Dispatcher.Invoke(() =>
            {
                GlobalLoginProgressStatus.Text = status;
            });
        }

        /// <summary>
        /// 显示授权URL对话框
        /// </summary>
        public void ShowAuthUrlDialog(string url)
        {
            _currentAuthUrl = url;
            
            Dispatcher.Invoke(() =>
            {
                GlobalAuthUrlText.Text = url;
                GlobalModalOverlay.Visibility = Visibility.Visible;
                GlobalAuthUrlDialog.Visibility = Visibility.Visible;
                
                // 淡入动画
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                GlobalAuthUrlDialog.BeginAnimation(OpacityProperty, fadeIn);
            });
        }

        /// <summary>
        /// 隐藏所有登录对话框
        /// </summary>
        public void HideAllLoginDialogs()
        {
            Dispatcher.Invoke(() =>
            {
                GlobalLoginProgressPanel.Visibility = Visibility.Collapsed;
                GlobalModalOverlay.Visibility = Visibility.Collapsed;
                GlobalAuthUrlDialog.Visibility = Visibility.Collapsed;
            });
        }

        /// <summary>
        /// 复制授权URL
        /// </summary>
        private void GlobalCopyAuthUrl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_currentAuthUrl);
                
                var button = sender as Button;
                if (button != null)
                {
                    var originalContent = button.Content;
                    button.Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new MaterialDesignThemes.Wpf.PackIcon
                            {
                                Kind = MaterialDesignThemes.Wpf.PackIconKind.Check,
                                Width = 16,
                                Height = 16,
                                Margin = new Thickness(0, 0, 6, 0)
                            },
                            new TextBlock
                            {
                                Text = "已复制",
                                VerticalAlignment = VerticalAlignment.Center,
                                FontSize = 13
                            }
                        }
                    };
                    
                    var timer = new DispatcherTimer();
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
        /// 关闭授权URL对话框（仅关闭对话框，不关闭遮罩）
        /// </summary>
        private void GlobalCloseAuthUrlDialog_Click(object sender, RoutedEventArgs e)
        {
            GlobalAuthUrlDialog.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 点击遮罩层时不执行任何操作（保持模态）
        /// </summary>
        private void GlobalModalOverlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 不关闭对话框，保持模态状态
        }

        #endregion

        private void NavigationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton button && button.Tag is string tag)
            {
                switch (tag)
                {
                    case "Home":
                        MainFrame.Navigate(new HomePage());
                        break;
                    case "Account":
                        MainFrame.Navigate(new AccountManagementPage());
                        break;
                    case "Version":
                        MainFrame.Navigate(new VersionDownloadPage());
                        break;
                    case "Resources":
                        MainFrame.Navigate(new ResourcesPage());
                        break;
                    case "Settings":
                        MainFrame.Navigate(new SettingsPage());
                        break;
                }
            }
        }

        // 窗口控制按钮事件
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                MaximizeIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.WindowMaximize;
            }
            else
            {
                WindowState = WindowState.Maximized;
                MaximizeIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.WindowRestore;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

