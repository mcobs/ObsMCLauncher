using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ObsMCLauncher.Models;
using ObsMCLauncher.Pages;
using ObsMCLauncher.Plugins;
using ObsMCLauncher.Utils;
using ObsMCLauncher.Services;

namespace ObsMCLauncher
{
    public partial class MainWindow : Window
    {
        private string _currentAuthUrl = "";
        private string? _currentLoginNotificationId;
        private CancellationTokenSource? _loginCancellationTokenSource;

        // 页面实例缓存 - 实现状态保持
        private readonly HomePage _homePage;
        private readonly AccountManagementPage _accountPage;
        private readonly VersionDownloadPage _versionPage;
        private readonly ResourcesPage _resourcesPage;
        private readonly SettingsPage _settingsPage;
        private readonly MorePage _morePage;
        
        // 插件加载器
        private PluginLoader? _pluginLoader;

        public MainWindow()
        {
            InitializeComponent();
            
            // 初始化版本信息显示
            InitializeVersionInfo();
            
            // 初始化通知管理器
            NotificationManager.Instance.Initialize(GlobalNotificationContainer);
            
            // 初始化对话框管理器
            DialogManager.Instance.Initialize(GlobalDialogContainer);
            
            // 预创建所有页面实例
            _homePage = new HomePage();
            _accountPage = new AccountManagementPage();
            _versionPage = new VersionDownloadPage();
            _resourcesPage = new ResourcesPage();
            _settingsPage = new SettingsPage();
            _morePage = new MorePage();
            
            // 默认导航到主页
            MainFrame.Navigate(_homePage);
            
            // 初始化下载管理器
            InitializeDownloadManager();
            
            // 初始化插件系统
            InitializePlugins();
        }
        
        /// <summary>
        /// 初始化插件系统
        /// </summary>
        private void InitializePlugins()
        {
            try
            {
                // 从配置读取插件目录
                var config = LauncherConfig.Load();
                var pluginsDir = config.GetPluginDirectory();
                
                _pluginLoader = new PluginLoader(pluginsDir);
                
                // 设置插件标签页注册回调
                PluginContext.OnTabRegistered = (pluginId, title, content, icon) =>
                {
                    // 在More页面添加插件的自定义标签页
                    System.Diagnostics.Debug.WriteLine($"[MainWindow] 插件 {pluginId} 注册标签页: {title}");
                    
                    // 在主线程中添加标签页
                    Dispatcher.Invoke(() =>
                    {
                        MorePage.RegisterPluginTab(pluginId, title, content, icon);
                    });
                };
                
                // 加载所有插件
                _pluginLoader.LoadAllPlugins();
                
                // 将插件加载器传递给MorePage
                MorePage.SetPluginLoader(_pluginLoader);
                
                System.Diagnostics.Debug.WriteLine("[MainWindow] 插件系统初始化完成");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 插件系统初始化失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 窗口关闭事件
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // 关闭插件
                _pluginLoader?.ShutdownPlugins();
                _pluginLoader?.UnloadAllPlugins();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 关闭插件时出错: {ex.Message}");
            }
            
            base.OnClosed(e);
        }

        /// <summary>
        /// 初始化版本信息显示
        /// </summary>
        private void InitializeVersionInfo()
        {
            try
            {
                // 设置导航栏版本号
                NavVersionText.Text = $"v{VersionInfo.ShortVersion}";
                
                // 设置窗口标题（可选）
                Title = $"{VersionInfo.FullProductName} - {VersionInfo.DisplayVersion}";
                
                // 输出版本信息到调试控制台
                System.Diagnostics.Debug.WriteLine($"[启动器] {VersionInfo.FullProductName} {VersionInfo.DisplayVersion} (Build {VersionInfo.BuildVersion})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[错误] 初始化版本信息失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 设置登录取消令牌源
        /// </summary>
        public void SetLoginCancellationTokenSource(CancellationTokenSource cts)
        {
            // 清理旧的取消令牌源（如果存在）
            _loginCancellationTokenSource?.Dispose();
            _loginCancellationTokenSource = cts;
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 登录取消令牌源已设置: {cts.GetHashCode()}");
        }

        #region 新版通知系统方法

        /// <summary>
        /// 显示通知
        /// </summary>
        public string ShowNotification(string title, string message, NotificationType type = NotificationType.Info, int? durationSeconds = null)
        {
            return NotificationManager.Instance.ShowNotification(title, message, type, durationSeconds);
        }

        /// <summary>
        /// 更新通知
        /// </summary>
        public void UpdateNotification(string id, string message)
        {
            NotificationManager.Instance.UpdateNotification(id, message);
        }

        /// <summary>
        /// 移除通知
        /// </summary>
        public void RemoveNotification(string id)
        {
            NotificationManager.Instance.RemoveNotification(id);
        }

        #endregion

        #region 全局登录UI控制方法

        /// <summary>
        /// 显示登录进度（使用新通知系统）
        /// </summary>
        public void ShowLoginProgress(string status)
        {
            if (_currentLoginNotificationId != null)
            {
                UpdateNotification(_currentLoginNotificationId, status);
            }
            else
            {
                // 传递CancellationTokenSource，让通知的关闭按钮能够取消登录操作
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 创建登录通知，CTS: {_loginCancellationTokenSource?.GetHashCode() ?? 0}");
                _currentLoginNotificationId = NotificationManager.Instance.ShowNotification(
                    "微软账户登录", 
                    status, 
                    NotificationType.Progress, 
                    durationSeconds: null,
                    onCancel: null,
                    cancellationTokenSource: _loginCancellationTokenSource
                );
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 登录通知ID: {_currentLoginNotificationId}");
            }
        }

        /// <summary>
        /// 更新登录进度（使用新通知系统）
        /// </summary>
        public void UpdateLoginProgress(string status)
        {
            if (_currentLoginNotificationId != null)
            {
                UpdateNotification(_currentLoginNotificationId, status);
            }
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
        /// 隐藏所有登录对话框（同时移除登录通知）
        /// </summary>
        public void HideAllLoginDialogs()
        {
            Dispatcher.Invoke(() =>
            {
                // 移除登录通知
                if (_currentLoginNotificationId != null)
                {
                    RemoveNotification(_currentLoginNotificationId);
                    _currentLoginNotificationId = null;
                }
                
                // 清理登录取消令牌源
                _loginCancellationTokenSource?.Dispose();
                _loginCancellationTokenSource = null;
                
                // 重置账户页面的登录状态
                _accountPage.ResetLoginState();
                
                // 隐藏旧版UI（保留兼容）
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
        /// 关闭授权URL对话框（同时关闭遮罩）
        /// </summary>
        private void GlobalCloseAuthUrlDialog_Click(object sender, RoutedEventArgs e)
        {
            GlobalModalOverlay.Visibility = Visibility.Collapsed;
            GlobalAuthUrlDialog.Visibility = Visibility.Collapsed;
        }
        
        /// <summary>
        /// 关闭按钮点击事件（取消微软登录）
        /// </summary>
        private void GlobalAuthUrlCloseButton_Click(object sender, RoutedEventArgs e)
        {
            // 取消登录操作
            _loginCancellationTokenSource?.Cancel();
            
            // 关闭对话框
            GlobalModalOverlay.Visibility = Visibility.Collapsed;
            GlobalAuthUrlDialog.Visibility = Visibility.Collapsed;
            
            // 移除登录通知
            if (_currentLoginNotificationId != null)
            {
                RemoveNotification(_currentLoginNotificationId);
                _currentLoginNotificationId = null;
            }
            
            // 清理登录取消令牌源
            _loginCancellationTokenSource?.Dispose();
            _loginCancellationTokenSource = null;
            
            // 重置账户页面的登录状态
            _accountPage.ResetLoginState();
            
            System.Diagnostics.Debug.WriteLine("[MainWindow] 用户取消了微软登录");
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
                // 使用缓存的页面实例，保持状态
                switch (tag)
                {
                    case "Home":
                        MainFrame.Navigate(_homePage);
                        break;
                    case "Account":
                        MainFrame.Navigate(_accountPage);
                        break;
                    case "Version":
                        MainFrame.Navigate(_versionPage);
                        break;
                    case "Resources":
                        MainFrame.Navigate(_resourcesPage);
                        break;
                    case "Settings":
                        MainFrame.Navigate(_settingsPage);
                        break;
                    case "More":
                        MainFrame.Navigate(_morePage);
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

        #region 下载管理器

        /// <summary>
        /// 初始化下载管理器
        /// </summary>
        private void InitializeDownloadManager()
        {
            // 绑定任务列表
            DownloadTasksList.ItemsSource = DownloadTaskManager.Instance.Tasks;

            // 监听任务变化
            DownloadTaskManager.Instance.TasksChanged += OnDownloadTasksChanged;
            DownloadTaskManager.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DownloadTaskManager.HasActiveTasks))
                {
                    UpdateDownloadManagerVisibility();
                }
            };
        }

        /// <summary>
        /// 任务变化时更新UI
        /// </summary>
        private void OnDownloadTasksChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateDownloadManagerVisibility();
                UpdateDownloadCount();
            });
        }

        /// <summary>
        /// 更新下载管理器按钮可见性
        /// </summary>
        private void UpdateDownloadManagerVisibility()
        {
            var hasActiveTasks = DownloadTaskManager.Instance.HasActiveTasks;
            DownloadManagerButton.Visibility = hasActiveTasks ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 更新任务计数
        /// </summary>
        private void UpdateDownloadCount()
        {
            var count = DownloadTaskManager.Instance.ActiveTaskCount;
            DownloadCountText.Text = count.ToString();
            DownloadManagerCountText.Text = $"({count} 个任务)";
        }

        /// <summary>
        /// 下载管理器按钮点击
        /// </summary>
        private void DownloadManagerButton_Click(object sender, RoutedEventArgs e)
        {
            DownloadManagerPanel.Visibility = DownloadManagerPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        /// <summary>
        /// 关闭下载管理器面板
        /// </summary>
        private void CloseDownloadManager_Click(object sender, RoutedEventArgs e)
        {
            DownloadManagerPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 取消任务
        /// </summary>
        private void CancelTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string taskId)
            {
                DownloadTaskManager.Instance.CancelTask(taskId);
            }
        }

        /// <summary>
        /// 清除已完成的任务
        /// </summary>
        private void ClearCompletedTasks_Click(object sender, RoutedEventArgs e)
        {
            DownloadTaskManager.Instance.ClearInactiveTasks();
        }

        #endregion
    }
}

