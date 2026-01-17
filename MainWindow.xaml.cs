using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        // 页面实例缓存 - 实现状态保持（按需创建）
        private readonly Dictionary<string, Page> _pageCache = new Dictionary<string, Page>();

        private bool _isNavCollapsed;

        // 辅助方法：在可视化树中查找子元素
        private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t)
                {
                    return t;
                }

                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }

        private void NavToggleButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyNavCollapsed(!_isNavCollapsed, animate: true);

                var cfg = LauncherConfig.Load();
                cfg.IsNavCollapsed = _isNavCollapsed;
                cfg.Save();
            }
            catch
            {
            }
        }

        private void ApplyNavCollapsed(bool collapsed, bool animate)
        {
            _isNavCollapsed = collapsed;

            var targetWidth = collapsed ? 72 : 200;

            try
            {
                if (NavMenuPanel != null)
                {
                    NavMenuPanel.Margin = collapsed ? new Thickness(0, 0, 0, 0) : new Thickness(10, 0, 10, 0);
                }

                if (NavFooterPanel != null)
                {
                    NavFooterPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                }

                if (LogoIcon != null)
                {
                    LogoIcon.Width = collapsed ? 28 : 60;
                    LogoIcon.Height = collapsed ? 28 : 60;
                }

                if (NavVersionText != null)
                {
                    NavVersionText.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                }

                if (NavTitleText != null)
                {
                    NavTitleText.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                }

                UpdateNavItemVisuals(collapsed);
            }
            catch { }

            if (!animate)
            {
                if (NavColumn != null)
                {
                    NavColumn.Width = new GridLength(targetWidth);
                }
                return;
            }

            try
            {
                var from = NavColumn != null ? NavColumn.Width.Value : 200;
                var anim = new GridLengthAnimation
                {
                    From = new GridLength(from),
                    To = new GridLength(targetWidth),
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                if (NavColumn != null)
                {
                    NavColumn.BeginAnimation(ColumnDefinition.WidthProperty, anim);
                }
            }
            catch
            {
                if (NavColumn != null)
                {
                    NavColumn.Width = new GridLength(targetWidth);
                }
            }
        }

        private void UpdateNavItemVisuals(bool collapsed)
        {
            try
            {
                UpdateSingleNavItemVisual(HomeButton, collapsed);
                UpdateSingleNavItemVisual(AccountButton, collapsed);
                UpdateSingleNavItemVisual(VersionButton, collapsed);
                UpdateSingleNavItemVisual(ResourcesButton, collapsed);
                UpdateSingleNavItemVisual(SettingsButton, collapsed);
                UpdateSingleNavItemVisual(MoreButton, collapsed);
            }
            catch
            {
            }
        }

        private void UpdateSingleNavItemVisual(RadioButton? button, bool collapsed)
        {
            if (button == null) return;

            // 折叠时：居中图标 + 隐藏文字 + tooltip 显示原文
            // 展开时：恢复左对齐 + 显示文字 + 移除 tooltip
            try
            {
                button.ToolTip = collapsed ? (button.Content?.ToString() ?? string.Empty) : null;
                button.HorizontalContentAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            }
            catch { }

            try
            {
                var presenter = FindVisualChild<ContentPresenter>(button);
                if (presenter == null) return;

                presenter.ApplyTemplate();
                if (VisualTreeHelper.GetChildrenCount(presenter) == 0) return;

                var root = VisualTreeHelper.GetChild(presenter, 0);

                var text = FindVisualChild<TextBlock>(root);
                if (text != null)
                {
                    text.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                }

                var icon = FindVisualChild<MaterialDesignThemes.Wpf.PackIcon>(root);
                if (icon != null)
                {
                    icon.Margin = collapsed ? new Thickness(0) : new Thickness(10, 0, 0, 0);
                }
            }
            catch
            {
            }
        }
        
        // 插件加载器
        private PluginLoader? _pluginLoader;

        public MainWindow()
        {
            InitializeComponent();
            
            ThemeTransitionManager.Initialize(GlobalThemeTransitionOverlay);

            // 还原侧边栏折叠状态
            try
            {
                var cfg = LauncherConfig.Load();
                ApplyNavCollapsed(cfg.IsNavCollapsed, animate: false);
            }
            catch { }

            // 初始化版本信息显示
            InitializeVersionInfo();
            
            // 初始化通知管理器
            NotificationManager.Instance.Initialize(GlobalNotificationContainer);
            
            // 初始化对话框管理器
            DialogManager.Instance.Initialize(GlobalDialogContainer);
            
            // 初始化 Yggdrasil 悬浮框管理器
            YggdrasilPanelManager.Instance.Initialize(GlobalDialogContainer);
            
            // 默认导航到主页（按需创建）
            MainFrame.Navigate(GetOrCreatePage("Home"));
            
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
                
                // 将插件加载器传递给MorePage（延迟到More页面创建时）
                // MorePage.SetPluginLoader(_pluginLoader); // 移除预设置，改为在页面创建时设置
                
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
                System.Diagnostics.Debug.WriteLine($"[启动器] {VersionInfo.FullProductName} {VersionInfo.DisplayVersion}");
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
                if (_pageCache.ContainsKey("Account") && _pageCache["Account"] is AccountManagementPage accountPage)
                {
                    accountPage.ResetLoginState();
                }
                
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
            if (_pageCache.ContainsKey("Account") && _pageCache["Account"] is AccountManagementPage accountPage)
            {
                accountPage.ResetLoginState();
            }
            
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

        /// <summary>
        /// 创建More页面并设置插件加载器
        /// </summary>
        private MorePage CreateMorePage()
        {
            var morePage = new MorePage();
            if (_pluginLoader != null)
            {
                MorePage.SetPluginLoader(_pluginLoader);
            }
            return morePage;
        }

        /// <summary>
        /// 获取或创建页面实例（按需创建，保持状态）
        /// </summary>
        private Page GetOrCreatePage(string pageTag)
        {
            if (!_pageCache.ContainsKey(pageTag))
            {
                // 按需创建页面
                Page page = pageTag switch
                {
                    "Home" => new HomePage(),
                    "Account" => new AccountManagementPage(),
                    "Version" => new VersionDownloadPage(),
                    "Resources" => new ResourcesPage(),
                    "Settings" => new SettingsPage(),
                    "More" => CreateMorePage(),
                    _ => throw new ArgumentException($"未知的页面标签: {pageTag}")
                };
                
                _pageCache[pageTag] = page;
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 创建页面: {pageTag}");
            }
            
            return _pageCache[pageTag];
        }

        // 当前页面切换动画的Storyboard引用，用于取消正在进行的动画
        private Storyboard? _currentPageTransitionStoryboard;
        
        private void NavigationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton button && button.Tag is string tag)
            {
                // 使用缓存的页面实例，保持状态（按需创建）
                var targetPage = GetOrCreatePage(tag);
                
                // 如果点击的是当前页面，不执行切换
                if (MainFrame.Content == targetPage)
                {
                    return;
                }
                
                // 应用页面切换动画
                AnimatePageTransition(targetPage);

                // 折叠状态下，点击导航后自动收起（更像 WinUI 的体验）
                if (_isNavCollapsed)
                {
                    ApplyNavCollapsed(true, animate: true);
                }
            }
        }
        
        /// <summary>
        /// 页面切换动画（淡入+滑动+缩放）
        /// 优化：处理动画进行中再次点击的情况
        /// </summary>
        private void AnimatePageTransition(Page targetPage)
        {
            // 停止当前正在进行的动画（如果存在）
            if (_currentPageTransitionStoryboard != null)
            {
                _currentPageTransitionStoryboard.Stop();
                _currentPageTransitionStoryboard = null;
            }
            
            // 先淡出当前页面
            if (MainFrame.Content is Page currentPage)
            {
                // 停止当前页面的所有动画
                currentPage.BeginAnimation(UIElement.OpacityProperty, null);
                if (currentPage.RenderTransform is TransformGroup transformGroup)
                {
                    foreach (var transform in transformGroup.Children)
                    {
                        if (transform is ScaleTransform scaleTransform)
                        {
                            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                        }
                        else if (transform is TranslateTransform translateTransform)
                        {
                            translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                        }
                    }
                }
                
                // 确保当前页面状态正确（如果动画被中断，可能状态不正确）
                currentPage.Opacity = 1.0;
                
                // 创建淡出动画
                var fadeOut = AnimationHelper.CreateFadeOutAnimation(
                    currentPage,
                    AnimationHelper.FastDuration,
                    onCompleted: (s, e) =>
                    {
                        // 淡出完成后导航到新页面
                        NavigateToPage(targetPage);
                    }
                );
                
                _currentPageTransitionStoryboard = fadeOut;
                fadeOut.Begin();
            }
            else
            {
                // 如果没有当前页面，直接导航
                NavigateToPage(targetPage);
            }
        }
        
        /// <summary>
        /// 导航到目标页面并应用淡入动画
        /// </summary>
        private void NavigateToPage(Page targetPage)
        {
            // 重置目标页面的状态，确保动画能正常播放
            targetPage.Opacity = 0;
            if (targetPage.RenderTransform is TransformGroup transformGroup)
            {
                var scaleTransform = transformGroup.Children.OfType<ScaleTransform>().FirstOrDefault();
                var translateTransform = transformGroup.Children.OfType<TranslateTransform>().FirstOrDefault();
                
                if (scaleTransform != null)
                {
                    scaleTransform.ScaleX = 0.95;
                    scaleTransform.ScaleY = 0.95;
                }
                if (translateTransform != null)
                {
                    translateTransform.Y = 20;
                }
            }
            else
            {
                // 如果还没有TransformGroup，创建一个
                targetPage.RenderTransform = new TransformGroup
                {
                    Children = new TransformCollection
                    {
                        new ScaleTransform(0.95, 0.95),
                        new TranslateTransform(0, 20)
                    }
                };
                targetPage.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            
            // 导航到新页面
            MainFrame.Navigate(targetPage);
            
            // 等待页面加载完成后应用淡入动画
            targetPage.Loaded += OnTargetPageLoaded;
        }
        
        /// <summary>
        /// 目标页面加载完成后的处理
        /// </summary>
        private void OnTargetPageLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Page targetPage)
            {
                targetPage.Loaded -= OnTargetPageLoaded;
                
                // 确保状态正确
                targetPage.Opacity = 0;
                if (targetPage.RenderTransform is TransformGroup transformGroup)
                {
                    var scaleTransform = transformGroup.Children.OfType<ScaleTransform>().FirstOrDefault();
                    var translateTransform = transformGroup.Children.OfType<TranslateTransform>().FirstOrDefault();
                    
                    if (scaleTransform != null)
                    {
                        scaleTransform.ScaleX = 0.95;
                        scaleTransform.ScaleY = 0.95;
                    }
                    if (translateTransform != null)
                    {
                        translateTransform.Y = 20;
                    }
                }
                
                // 应用淡入+滑动+缩放动画
                var fadeIn = AnimationHelper.CreateFadeInAnimation(
                    targetPage,
                    AnimationHelper.DefaultDuration
                );
                
                var scaleAnim = AnimationHelper.CreateScaleAnimation(
                    targetPage,
                    0.95,
                    1.0,
                    AnimationHelper.DefaultDuration
                );
                
                // 滑动动画
                if (targetPage.RenderTransform is TransformGroup tg)
                {
                    var translateTransform = tg.Children.OfType<TranslateTransform>().FirstOrDefault();
                    if (translateTransform != null)
                    {
                        var slideYAnim = new DoubleAnimation
                        {
                            From = 20,
                            To = 0,
                            Duration = AnimationHelper.DefaultDuration,
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        };
                        translateTransform.BeginAnimation(TranslateTransform.YProperty, slideYAnim);
                    }
                }
                
                // 同时执行淡入和缩放动画
                AnimationHelper.ExecuteParallelAnimations(
                    new List<Storyboard> { fadeIn, scaleAnim },
                    (s, args) =>
                    {
                        // 动画完成后清理引用
                        _currentPageTransitionStoryboard = null;
                    }
                );
                
                _currentPageTransitionStoryboard = fadeIn; // 使用淡入动画作为当前动画引用
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

