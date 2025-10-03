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
        private const int MaxNotifications = 2;
        private const int NotificationSpacing = 10;

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

            // 如果超过最大数量，移除最早的通知
            if (_notifications.Count >= MaxNotifications)
            {
                RemoveNotification(_notifications.First().Id);
            }

            var notification = new NotificationItem
            {
                Title = title,
                Message = message,
                Type = type,
                IsProgress = type == NotificationType.Progress
            };

            var element = CreateNotificationElement(notification);
            notification.UIElement = element;

            _container.Dispatcher.Invoke(() =>
            {
                _notifications.Add(notification);
                _container.Children.Add(element);
                UpdateNotificationPositions();
                AnimateIn(element);
            });

            // 自动消失（进度通知除外）
            if (durationSeconds.HasValue && type != NotificationType.Progress)
            {
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(durationSeconds.Value);
                timer.Tick += (s, e) =>
                {
                    RemoveNotification(notification.Id);
                    timer.Stop();
                };
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
                _container?.Dispatcher.Invoke(() =>
                {
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
        private Border CreateNotificationElement(NotificationItem notification)
        {
            var border = new Border
            {
                Width = 380,
                Background = (Brush)Application.Current.FindResource("SurfaceBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 11, 14, 11),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Opacity = 0,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 20,
                    ShadowDepth = 2,
                    Opacity = 0.3
                }
            };

            var stackPanel = new StackPanel();

            // 顶部内容（图标 + 标题 + 关闭按钮）
            var topGrid = new Grid { Margin = new Thickness(0, 0, 0, notification.IsProgress ? 8 : 0) };
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

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

            // 关闭按钮（非进度通知）
            if (!notification.IsProgress)
            {
                var closeButton = new Button
                {
                    Style = (Style)Application.Current.FindResource("MaterialDesignIconButton"),
                    Width = 24,
                    Height = 24,
                    Padding = new Thickness(0),
                    Tag = notification.Id
                };
                closeButton.Content = new PackIcon
                {
                    Kind = PackIconKind.Close,
                    Width = 16,
                    Height = 16
                };
                closeButton.Click += (s, e) => RemoveNotification(notification.Id);
                Grid.SetColumn(closeButton, 2);
                topGrid.Children.Add(closeButton);
            }

            topGrid.Children.Add(iconBorder);
            topGrid.Children.Add(textPanel);
            stackPanel.Children.Add(topGrid);

            // 进度条（仅进度通知）
            if (notification.IsProgress)
            {
                var progressBar = new ProgressBar
                {
                    Height = 3,
                    IsIndeterminate = true,
                    Foreground = (Brush)Application.Current.FindResource("PrimaryBrush"),
                    Background = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
                    BorderThickness = new Thickness(0)
                };
                stackPanel.Children.Add(progressBar);
            }

            border.Child = stackPanel;
            return border;
        }

        /// <summary>
        /// 更新所有通知位置
        /// </summary>
        private void UpdateNotificationPositions()
        {
            _container?.Dispatcher.Invoke(() =>
            {
                double topOffset = 15;
                foreach (var notification in _notifications)
                {
                    if (notification.UIElement != null)
                    {
                        AnimatePosition(notification.UIElement, topOffset);
                        topOffset += notification.UIElement.ActualHeight + NotificationSpacing;
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
