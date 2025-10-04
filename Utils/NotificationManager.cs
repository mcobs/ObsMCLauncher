using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using MaterialDesignThemes.Wpf;

namespace ObsMCLauncher.Utils
{
    /// <summary>
    /// 通知类型
    /// </summary>
    public enum NotificationType
    {
        Info,       // 信息
        Success,    // 成功
        Warning,    // 警告
        Error,      // 错误
        Progress    // 进度
    }

    /// <summary>
    /// 通知项
    /// </summary>
    public class NotificationItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public NotificationType Type { get; set; }
        public Border? UIElement { get; set; }
        public bool IsProgress { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public System.Windows.Threading.DispatcherTimer? Timer { get; set; } // 用于存储定时器
    }

    /// <summary>
    /// 通知管理器（单例）
    /// </summary>
    public class NotificationManager
    {
        private static readonly Lazy<NotificationManager> _instance = new(() => new NotificationManager());
        public static NotificationManager Instance => _instance.Value;

        private readonly List<NotificationItem> _notifications = new();
        private Panel? _container;
        private const int MaxNotifications = 3; // 每类通知最多3个
        private const int NotificationSpacing = 10;
        private const int DefaultDurationSeconds = 3; // 默认3秒

        private NotificationManager() { }

        /// <summary>
        /// 初始化通知容器
        /// </summary>
        public void Initialize(Panel container)
        {
            _container = container;
        }

        /// <summary>
        /// 显示通知
        /// </summary>
        public string ShowNotification(string title, string message, NotificationType type = NotificationType.Info, int? durationSeconds = null)
        {
            if (_container == null)
            {
                System.Diagnostics.Debug.WriteLine("通知容器未初始化");
                return string.Empty;
            }

            // 使用默认时长（3秒），进度通知除外
            if (!durationSeconds.HasValue && type != NotificationType.Progress)
            {
                durationSeconds = DefaultDurationSeconds;
            }

            // 检查是否有相同内容的通知
            var sameNotifications = _notifications
                .Where(n => n.Title == title && n.Message == message && n.Type == type)
                .ToList();

            // 如果同类通知超过3个，移除最早的
            if (sameNotifications.Count >= MaxNotifications)
            {
                RemoveNotification(sameNotifications.First().Id);
            }

            var notification = new NotificationItem
            {
                Title = title,
                Message = message,
                Type = type,
                IsProgress = type == NotificationType.Progress
            };

            var element = CreateNotificationElement(notification, durationSeconds);
            notification.UIElement = element;

            _container.Dispatcher.Invoke(() =>
            {
                _notifications.Add(notification);
                _container.Children.Add(element);
                UpdateNotificationPositions();
                // 不再使用淡入动画，通知直接显示

                // 启动倒计时进度条动画（延迟1000ms，确保用户看到满条状态）
                if (durationSeconds.HasValue && type != NotificationType.Progress)
                {
                    var delayTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(1000)
                    };
                    delayTimer.Tick += (s, e) =>
                    {
                        delayTimer.Stop();
                        StartCountdownAnimation(element, durationSeconds.Value - 1.0); // 减去延迟时间
                    };
                    delayTimer.Start();
                }
            });

            // 自动消失（进度通知除外）
            if (durationSeconds.HasValue && type != NotificationType.Progress)
            {
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(durationSeconds.Value);
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    RemoveNotification(notification.Id);
                };
                notification.Timer = timer; // 存储定时器引用
                timer.Start();
            }

            return notification.Id;
        }

        /// <summary>
        /// 更新进度通知
        /// </summary>
        public void UpdateNotification(string id, string message)
        {
            var notification = _notifications.FirstOrDefault(n => n.Id == id);
            if (notification?.UIElement != null)
            {
                _container?.Dispatcher.Invoke(() =>
                {
                    var textBlock = FindVisualChild<TextBlock>(notification.UIElement, "MessageText");
                    if (textBlock != null)
                    {
                        textBlock.Text = message;
                    }
                });
            }
        }

        /// <summary>
        /// 移除通知
        /// </summary>
        public void RemoveNotification(string id)
        {
            var notification = _notifications.FirstOrDefault(n => n.Id == id);
            if (notification?.UIElement != null)
            {
                // 停止定时器
                if (notification.Timer != null)
                {
                    notification.Timer.Stop();
                    notification.Timer = null;
                }

                _container?.Dispatcher.Invoke(() =>
                {
                    // 停止通知自身的所有动画
                    notification.UIElement.BeginAnimation(UIElement.OpacityProperty, null);
                    notification.UIElement.BeginAnimation(Canvas.TopProperty, null);
                    
                    // 停止倒计时进度条动画
                    var progressBar = FindVisualChild<ProgressBar>(notification.UIElement, "CountdownProgress");
                    if (progressBar != null)
                    {
                        progressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
                        progressBar.Opacity = 0;
                    }

                    AnimateOut(notification.UIElement, () =>
                    {
                        _container.Children.Remove(notification.UIElement);
                        _notifications.Remove(notification);
                        UpdateNotificationPositions();
                    });
                });
            }
        }

        /// <summary>
        /// 创建通知UI元素
        /// </summary>
        private Border CreateNotificationElement(NotificationItem notification, int? durationSeconds = null)
        {
            var border = new Border
            {
                Width = 380,
                Background = (Brush)Application.Current.FindResource("SurfaceBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Opacity = 1, // 直接显示，不使用淡入动画
                IsHitTestVisible = true, // 确保可以接收鼠标事件
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 20,
                    ShadowDepth = 2,
                    Opacity = 0.3
                }
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var stackPanel = new StackPanel { Margin = new Thickness(14, 11, 14, 11) };

            // 顶部内容（图标 + 标题 + 关闭按钮）
            var topGrid = new Grid { Margin = new Thickness(0, 0, 0, notification.IsProgress ? 8 : 0) };
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36, GridUnitType.Pixel) }); // 固定宽度36px

            // 图标
            var iconBorder = new Border
            {
                Background = GetTypeColor(notification.Type),
                CornerRadius = new CornerRadius(14),
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 10, 0)
            };
            iconBorder.Child = new PackIcon
            {
                Kind = GetTypeIcon(notification.Type),
                Width = 16,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White
            };
            Grid.SetColumn(iconBorder, 0);

            // 标题和消息
            var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textPanel.Children.Add(new TextBlock
            {
                Text = notification.Title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2)
            });
            textPanel.Children.Add(new TextBlock
            {
                Name = "MessageText",
                Text = notification.Message,
                FontSize = 11,
                Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush")
            });
            Grid.SetColumn(textPanel, 1);

            // 先添加基本元素到Grid
            topGrid.Children.Add(iconBorder);
            topGrid.Children.Add(textPanel);

            // TODO: 技术债 - 关闭按钮点击区域问题待解决
            // 临时隐藏关闭按钮，通知会在3秒后自动消失
            // 问题：WPF Grid + Button 的点击区域和视觉区域不一致
            // 后续需要：
            // 1. 重新设计通知布局（使用StackPanel而不是Grid）
            // 2. 或者使用XAML定义Button样式而不是代码创建
            // 3. 或者使用自定义UserControl
            
            /* 关闭按钮代码已临时注释
            if (!notification.IsProgress)
            {
                var closeButton = new Button { ... };
                Grid.SetColumn(closeButton, 2);
                topGrid.Children.Add(closeButton);
            }
            */

            stackPanel.Children.Add(topGrid);

            // 进度条（仅进度通知）
            if (notification.IsProgress)
            {
                var progressBar = new ProgressBar
                {
                    Height = 4,
                    IsIndeterminate = true,
                    Foreground = (Brush)Application.Current.FindResource("PrimaryBrush"),
                    Background = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
                    BorderThickness = new Thickness(0),
                    Margin = new Thickness(0, 8, 0, 0),
                    Opacity = 1.0 // 确保完全可见
                };
                
                // 确保使用MaterialDesign样式
                try
                {
                    progressBar.Style = (Style)Application.Current.FindResource("MaterialDesignLinearProgressBar");
                }
                catch
                {
                    // 如果找不到样式，使用默认
                }
                
                stackPanel.Children.Add(progressBar);
            }

            Grid.SetRow(stackPanel, 0);
            mainGrid.Children.Add(stackPanel);

            // 倒计时进度条（细线，优雅隐蔽）
            if (durationSeconds.HasValue && notification.Type != NotificationType.Progress)
            {
                var countdownProgressBar = new ProgressBar
                {
                    Name = "CountdownProgress",
                    Height = 2,
                    Minimum = 0,
                    Maximum = 100,
                    Value = 100, // 初始值满
                    Foreground = GetTypeColor(notification.Type),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Opacity = 1.0, // 立即完全可见，满条状态
                    VerticalAlignment = VerticalAlignment.Bottom,
                    IsIndeterminate = false // 确保不是不确定进度模式
                };
                Grid.SetRow(countdownProgressBar, 1);
                mainGrid.Children.Add(countdownProgressBar);
            }

            border.Child = mainGrid;
            return border;
        }

        /// <summary>
        /// 启动倒计时进度条动画（简单直接，从100%倒计时到0%）
        /// </summary>
        private void StartCountdownAnimation(Border element, double durationSeconds)
        {
            var progressBar = FindVisualChild<ProgressBar>(element, "CountdownProgress");
            if (progressBar != null)
            {
                progressBar.Value = 100;
                progressBar.Opacity = 1.0;
                
                var animation = new DoubleAnimation
                {
                    From = 100,
                    To = 0,
                    Duration = TimeSpan.FromSeconds(durationSeconds),
                    FillBehavior = FillBehavior.HoldEnd
                };
                
                progressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, animation);
            }
        }

        /// <summary>
        /// 更新所有通知位置（水平居中，从顶部开始向下排列）
        /// </summary>
        private void UpdateNotificationPositions()
        {
            if (_container == null) return;
            
            _container.Dispatcher.Invoke(() =>
            {
                if (_notifications.Count == 0) return;

                // 获取容器宽度用于水平居中
                var containerWidth = _container.ActualWidth;
                if (containerWidth <= 0) containerWidth = 1200; // 默认宽度

                // 从顶部开始，15px边距
                double currentY = 15;
                
                foreach (var notification in _notifications)
                {
                    if (notification.UIElement != null)
                    {
                        try
                        {
                            // 确保元素已经测量过
                            notification.UIElement.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                            var height = notification.UIElement.DesiredSize.Height;
                            var width = notification.UIElement.DesiredSize.Width;
                            if (height <= 0 || double.IsNaN(height)) height = 70; // 默认高度
                            if (width <= 0 || double.IsNaN(width)) width = 380; // 默认宽度
                            
                            // 水平居中：(容器宽度 - 通知宽度) / 2 - 微调偏移
                            // 减去100px使通知偏左，相对于整个窗口更居中（考虑左侧侧边栏200px）
                            double centerX = (containerWidth - width) / 2 - 100;
                            if (centerX < 0) centerX = 0;
                            notification.UIElement.SetValue(Canvas.LeftProperty, centerX);
                            
                            AnimatePosition(notification.UIElement, currentY);
                            currentY += height + NotificationSpacing;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[NotificationManager] 位置更新错误: {ex.Message}");
                            double centerX = (containerWidth - 380) / 2 - 100;
                            if (centerX < 0) centerX = 0;
                            notification.UIElement.SetValue(Canvas.LeftProperty, centerX);
                            AnimatePosition(notification.UIElement, currentY);
                            currentY += 70 + NotificationSpacing;
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 淡入动画
        /// </summary>
        private void AnimateIn(UIElement element)
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            element.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        /// <summary>
        /// 淡出动画
        /// </summary>
        private void AnimateOut(UIElement element, Action onComplete)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, e) => onComplete?.Invoke();
            element.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        /// <summary>
        /// 位置动画
        /// </summary>
        private void AnimatePosition(FrameworkElement element, double targetTop)
        {
            var currentTop = (double)element.GetValue(Canvas.TopProperty);
            if (double.IsNaN(currentTop)) currentTop = 0;

            if (Math.Abs(currentTop - targetTop) > 0.1)
            {
                var animation = new DoubleAnimation(currentTop, targetTop, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                element.BeginAnimation(Canvas.TopProperty, animation);
            }
            else
            {
                element.SetValue(Canvas.TopProperty, targetTop);
            }
        }

        /// <summary>
        /// 获取类型颜色
        /// </summary>
        private Brush GetTypeColor(NotificationType type)
        {
            return type switch
            {
                NotificationType.Success => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                NotificationType.Warning => new SolidColorBrush(Color.FromRgb(251, 191, 36)),
                NotificationType.Error => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                _ => (Brush)Application.Current.FindResource("PrimaryBrush")
            };
        }

        /// <summary>
        /// 获取类型图标
        /// </summary>
        private PackIconKind GetTypeIcon(NotificationType type)
        {
            return type switch
            {
                NotificationType.Success => PackIconKind.CheckCircle,
                NotificationType.Warning => PackIconKind.AlertCircle,
                NotificationType.Error => PackIconKind.CloseCircle,
                NotificationType.Progress => PackIconKind.Loading,
                _ => PackIconKind.InformationOutline
            };
        }

        /// <summary>
        /// 查找子元素
        /// </summary>
        private T? FindVisualChild<T>(DependencyObject parent, string name = "") where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild && (string.IsNullOrEmpty(name) || (child as FrameworkElement)?.Name == name))
                {
                    return typedChild;
                }

                var result = FindVisualChild<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }
    }
}
