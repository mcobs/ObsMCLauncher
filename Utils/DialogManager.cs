using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using MaterialDesignThemes.Wpf;

namespace ObsMCLauncher.Utils
{
    /// <summary>
    /// 对话框类型
    /// </summary>
    public enum DialogType
    {
        Info,       // 信息
        Success,    // 成功
        Warning,    // 警告
        Error,      // 错误
        Question    // 询问
    }

    /// <summary>
    /// 对话框按钮类型
    /// </summary>
    public enum DialogButtons
    {
        OK,             // 确定
        OKCancel,       // 确定 + 取消
        YesNo,          // 是 + 否
        YesNoCancel     // 是 + 否 + 取消
    }

    /// <summary>
    /// 对话框结果
    /// </summary>
    public enum DialogResult
    {
        None,
        OK,
        Cancel,
        Yes,
        No
    }

    /// <summary>
    /// 对话框管理器（单例）- 提供美观的模态对话框
    /// </summary>
    public class DialogManager
    {
        private static readonly Lazy<DialogManager> _instance = new(() => new DialogManager());
        public static DialogManager Instance => _instance.Value;

        private Panel? _container;
        private Grid? _overlay;
        private Border? _dialogBorder;
        private TaskCompletionSource<DialogResult>? _currentTaskSource;

        private DialogManager() { }

        /// <summary>
        /// 初始化对话框容器
        /// </summary>
        public void Initialize(Panel container)
        {
            _container = container;
        }

        /// <summary>
        /// 显示信息对话框
        /// </summary>
        public Task<DialogResult> ShowInfo(string title, string message, DialogButtons buttons = DialogButtons.OK)
        {
            return ShowDialog(title, message, DialogType.Info, buttons);
        }

        /// <summary>
        /// 显示成功对话框
        /// </summary>
        public Task<DialogResult> ShowSuccess(string title, string message, DialogButtons buttons = DialogButtons.OK)
        {
            return ShowDialog(title, message, DialogType.Success, buttons);
        }

        /// <summary>
        /// 显示警告对话框
        /// </summary>
        public Task<DialogResult> ShowWarning(string title, string message, DialogButtons buttons = DialogButtons.OK)
        {
            return ShowDialog(title, message, DialogType.Warning, buttons);
        }

        /// <summary>
        /// 显示错误对话框
        /// </summary>
        public Task<DialogResult> ShowError(string title, string message, DialogButtons buttons = DialogButtons.OK)
        {
            return ShowDialog(title, message, DialogType.Error, buttons);
        }

        /// <summary>
        /// 显示询问对话框（常用于确认操作）
        /// </summary>
        public Task<DialogResult> ShowQuestion(string title, string message, DialogButtons buttons = DialogButtons.YesNo)
        {
            return ShowDialog(title, message, DialogType.Question, buttons);
        }

        /// <summary>
        /// 显示确认对话框（YesNo的便捷方法）
        /// </summary>
        public async Task<bool> Confirm(string title, string message)
        {
            var result = await ShowQuestion(title, message, DialogButtons.YesNo);
            return result == DialogResult.Yes;
        }

        /// <summary>
        /// 显示确认对话框（支持自定义按钮文本）
        /// </summary>
        public async Task<bool> ShowConfirmDialogAsync(string title, string message, string confirmText = "确定", string cancelText = "取消")
        {
            // 目前使用默认的确认对话框，忽略自定义按钮文本
            // 未来可以扩展 ShowDialog 方法以支持自定义按钮文本
            var result = await ShowQuestion(title, message, DialogButtons.YesNo);
            return result == DialogResult.Yes;
        }

        /// <summary>
        /// 显示输入对话框
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="message">提示信息</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>输入的文本，如果取消则返回null</returns>
        public async Task<string?> ShowInputDialogAsync(string title, string message, string defaultValue = "")
        {
            if (_container == null)
            {
                System.Diagnostics.Debug.WriteLine("对话框容器未初始化");
                return null;
            }

            var tcs = new TaskCompletionSource<string?>();

            await _container.Dispatcher.InvokeAsync(() =>
            {
                // 创建遮罩层
                _overlay = new Grid
                {
                    Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Opacity = 0
                };
                Panel.SetZIndex(_overlay, 9); // 设置 ZIndex 为 9

                // 创建对话框（应用玻璃态效果，使用动态资源）
                var dialogBorder = new Border
                {
                    MinWidth = 420,
                    MaxWidth = 550,
                    Background = (Brush)Application.Current.FindResource("GlassmorphismBackgroundBrush"),
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0,
                    BorderBrush = (Brush)Application.Current.FindResource("GlassmorphismBorderBrush"),
                    BorderThickness = new Thickness(1),
                    Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 40,
                        ShadowDepth = 0,
                        Opacity = 0.6
                    }
                };

                var mainGrid = new Grid();
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 标题栏
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 消息
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 输入框
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 按钮区

                // 标题栏
                var titleText = new TextBlock
                {
                    Text = title,
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(24, 20, 24, 12)
                };
                Grid.SetRow(titleText, 0);
                mainGrid.Children.Add(titleText);

                // 消息
                var messageText = new TextBlock
                {
                    Text = message,
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
                    Margin = new Thickness(24, 0, 24, 16)
                };
                Grid.SetRow(messageText, 1);
                mainGrid.Children.Add(messageText);

                // 输入框
                var inputBox = new TextBox
                {
                    Text = defaultValue,
                    FontSize = 14,
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(24, 0, 24, 20)
                };

                try
                {
                    inputBox.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedTextBox");
                }
                catch { }

                // 自动选中默认文本
                inputBox.Loaded += (s, e) =>
                {
                    inputBox.Focus();
                    inputBox.SelectAll();
                };

                Grid.SetRow(inputBox, 2);
                mainGrid.Children.Add(inputBox);

                // 按钮区
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(24, 0, 24, 20)
                };

                var cancelButton = new Button
                {
                    Content = "取消",
                    MinWidth = 90,
                    Height = 36,
                    Margin = new Thickness(0, 0, 8, 0),
                    FontSize = 14
                };

                var confirmButton = new Button
                {
                    Content = "确定",
                    MinWidth = 90,
                    Height = 36,
                    FontSize = 14
                };

                try
                {
                    cancelButton.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedButton");
                    confirmButton.Style = (Style)Application.Current.FindResource("MaterialDesignRaisedButton");
                }
                catch { }

                void CloseInputDialog(string? result)
                {
                    var overlayFadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                    var dialogFadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));

                    dialogFadeOut.Completed += (s, e) =>
                    {
                        _container?.Children.Remove(_overlay);
                        tcs.TrySetResult(result);
                    };

                    _overlay.BeginAnimation(UIElement.OpacityProperty, overlayFadeOut);
                    dialogBorder.BeginAnimation(UIElement.OpacityProperty, dialogFadeOut);
                }

                cancelButton.Click += (s, e) => CloseInputDialog(null);
                confirmButton.Click += (s, e) =>
                {
                    var input = inputBox.Text.Trim();
                    if (string.IsNullOrEmpty(input))
                    {
                        inputBox.Focus();
                        return;
                    }
                    CloseInputDialog(input);
                };

                // 支持回车键确认
                inputBox.KeyDown += (s, e) =>
                {
                    if (e.Key == System.Windows.Input.Key.Enter)
                    {
                        var input = inputBox.Text.Trim();
                        if (!string.IsNullOrEmpty(input))
                        {
                            CloseInputDialog(input);
                        }
                    }
                    else if (e.Key == System.Windows.Input.Key.Escape)
                    {
                        CloseInputDialog(null);
                    }
                };

                buttonPanel.Children.Add(cancelButton);
                buttonPanel.Children.Add(confirmButton);

                Grid.SetRow(buttonPanel, 3);
                mainGrid.Children.Add(buttonPanel);

                dialogBorder.Child = mainGrid;
                _overlay.Children.Add(dialogBorder);

                // 添加到容器
                _container.Children.Add(_overlay);

                // 淡入动画
                var overlayFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
                _overlay.BeginAnimation(UIElement.OpacityProperty, overlayFadeIn);

                var dialogFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                var scaleTransform = new ScaleTransform(0.9, 0.9);
                dialogBorder.RenderTransform = scaleTransform;
                dialogBorder.RenderTransformOrigin = new Point(0.5, 0.5);

                var scaleXAnimation = new DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                };
                var scaleYAnimation = new DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                };

                dialogBorder.BeginAnimation(UIElement.OpacityProperty, dialogFadeIn);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnimation);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnimation);
            });

            return await tcs.Task;
        }

        /// <summary>
        /// 显示对话框
        /// </summary>
        private Task<DialogResult> ShowDialog(string title, string message, DialogType type, DialogButtons buttons)
        {
            if (_container == null)
            {
                System.Diagnostics.Debug.WriteLine("对话框容器未初始化");
                return Task.FromResult(DialogResult.None);
            }

            _currentTaskSource = new TaskCompletionSource<DialogResult>();

            _container.Dispatcher.Invoke(() =>
            {
                // 创建遮罩层
                _overlay = new Grid
                {
                    Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Opacity = 0
                };
                Panel.SetZIndex(_overlay, 9); // 设置 ZIndex 为 9，高于外置登录窗口，低于通知

                // 创建对话框
                _dialogBorder = CreateDialogElement(title, message, type, buttons);
                _overlay.Children.Add(_dialogBorder);

                // 添加到容器
                _container.Children.Add(_overlay);

                // 淡入动画
                AnimateIn();
            });

            return _currentTaskSource.Task;
        }

        /// <summary>
        /// 创建对话框UI元素
        /// </summary>
        private Border CreateDialogElement(string title, string message, DialogType type, DialogButtons buttons)
        {
            // 应用玻璃态效果（使用动态资源，支持主题切换）
            var border = new Border
            {
                MinWidth = 420,
                MaxWidth = 650,
                MaxHeight = 600, // 限制最大高度，避免对话框过高
                Background = (Brush)Application.Current.FindResource("GlassmorphismBackgroundBrush"),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0,
                BorderBrush = (Brush)Application.Current.FindResource("GlassmorphismBorderBrush"),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 40,
                    ShadowDepth = 0,
                    Opacity = 0.6
                }
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 标题栏
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 内容区
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 按钮区

            // 标题栏
            var titleGrid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 图标
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 标题

            // 图标
            var iconBorder = new Border
            {
                Background = GetTypeColor(type),
                CornerRadius = new CornerRadius(20),
                Width = 40,
                Height = 40,
                Margin = new Thickness(24, 20, 12, 20)
            };
            iconBorder.Child = new PackIcon
            {
                Kind = GetTypeIcon(type),
                Width = 22,
                Height = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White
            };
            Grid.SetColumn(iconBorder, 0);
            titleGrid.Children.Add(iconBorder);

            // 标题文本
            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 20, 24, 20)
            };
            Grid.SetColumn(titleText, 1);
            titleGrid.Children.Add(titleText);

            Grid.SetRow(titleGrid, 0);
            mainGrid.Children.Add(titleGrid);

            // 内容区（添加ScrollViewer支持长内容）
            var scrollViewer = new ScrollViewer
            {
                MaxHeight = 400, // 最大高度400px，超出则滚动
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(24, 0, 24, 20)
            };
            
            var contentPanel = new StackPanel();
            var messageText = new TextBlock
            {
                Text = message,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
                LineHeight = 22,
                Padding = new Thickness(2, 0, 2, 0) // 留出滚动条空间
            };
            contentPanel.Children.Add(messageText);
            scrollViewer.Content = contentPanel;

            Grid.SetRow(scrollViewer, 1);
            mainGrid.Children.Add(scrollViewer);

            // 按钮区
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(24, 0, 24, 20)
            };

            // 根据按钮类型添加按钮
            switch (buttons)
            {
                case DialogButtons.OK:
                    buttonPanel.Children.Add(CreateButton("确定", DialogResult.OK, true));
                    break;

                case DialogButtons.OKCancel:
                    buttonPanel.Children.Add(CreateButton("取消", DialogResult.Cancel, false));
                    buttonPanel.Children.Add(CreateButton("确定", DialogResult.OK, true));
                    break;

                case DialogButtons.YesNo:
                    buttonPanel.Children.Add(CreateButton("否", DialogResult.No, false));
                    buttonPanel.Children.Add(CreateButton("是", DialogResult.Yes, true));
                    break;

                case DialogButtons.YesNoCancel:
                    buttonPanel.Children.Add(CreateButton("取消", DialogResult.Cancel, false));
                    buttonPanel.Children.Add(CreateButton("否", DialogResult.No, false));
                    buttonPanel.Children.Add(CreateButton("是", DialogResult.Yes, true));
                    break;
            }

            Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            border.Child = mainGrid;
            return border;
        }

        /// <summary>
        /// 创建按钮
        /// </summary>
        private Button CreateButton(string text, DialogResult result, bool isPrimary)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 90,
                Height = 36,
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 14,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            // 应用样式
            try
            {
                if (isPrimary)
                {
                    button.Style = (Style)Application.Current.FindResource("MaterialDesignRaisedButton");
                }
                else
                {
                    button.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedButton");
                }
            }
            catch
            {
                // 使用默认样式
            }

            button.Click += (s, e) => CloseDialog(result);

            return button;
        }

        /// <summary>
        /// 关闭对话框
        /// </summary>
        private void CloseDialog(DialogResult result)
        {
            if (_overlay != null && _dialogBorder != null)
            {
                // 淡出动画
                var overlayFadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                var dialogFadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                
                // 只在一个动画上绑定完成事件，避免重复调用
                dialogFadeOut.Completed += (s, e) =>
                {
                    _container?.Children.Remove(_overlay);
                    _currentTaskSource?.TrySetResult(result); // 使用TrySetResult避免重复设置异常
                };

                _overlay.BeginAnimation(UIElement.OpacityProperty, overlayFadeOut);
                _dialogBorder.BeginAnimation(UIElement.OpacityProperty, dialogFadeOut);
            }
        }

        /// <summary>
        /// 淡入动画
        /// </summary>
        private void AnimateIn()
        {
            if (_overlay != null && _dialogBorder != null)
            {
                // 遮罩层淡入
                var overlayFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
                _overlay.BeginAnimation(UIElement.OpacityProperty, overlayFadeIn);

                // 对话框淡入 + 缩放
                var dialogFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                var scaleTransform = new ScaleTransform(0.9, 0.9);
                _dialogBorder.RenderTransform = scaleTransform;
                _dialogBorder.RenderTransformOrigin = new Point(0.5, 0.5);

                var scaleXAnimation = new DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                };
                var scaleYAnimation = new DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                };

                _dialogBorder.BeginAnimation(UIElement.OpacityProperty, dialogFadeIn);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnimation);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnimation);
            }
        }

        /// <summary>
        /// 获取类型颜色
        /// </summary>
        private Brush GetTypeColor(DialogType type)
        {
            return type switch
            {
                DialogType.Success => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                DialogType.Warning => new SolidColorBrush(Color.FromRgb(251, 191, 36)),
                DialogType.Error => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                DialogType.Question => new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                _ => (Brush)Application.Current.FindResource("PrimaryBrush")
            };
        }

        /// <summary>
        /// 获取类型图标
        /// </summary>
        private PackIconKind GetTypeIcon(DialogType type)
        {
            return type switch
            {
                DialogType.Success => PackIconKind.CheckCircle,
                DialogType.Warning => PackIconKind.AlertCircle,
                DialogType.Error => PackIconKind.CloseCircle,
                DialogType.Question => PackIconKind.HelpCircle,
                _ => PackIconKind.InformationOutline
            };
        }

        /// <summary>
        /// 显示更新对话框（支持Markdown格式的更新日志）
        /// </summary>
        public async Task<bool> ShowUpdateDialogAsync(string title, string markdownContent, string confirmText = "确定", string cancelText = "取消")
        {
            var tcs = new TaskCompletionSource<bool>();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (_container == null)
                {
                    throw new InvalidOperationException("DialogManager未初始化");
                }

                var overlay = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Opacity = 0
                };
                Panel.SetZIndex(overlay, 9); // 设置 ZIndex 为 9

                // 应用玻璃态效果（使用动态资源）
                var dialogBorder = new Border
                {
                    Background = (Brush)Application.Current.FindResource("GlassmorphismBackgroundBrush"),
                    BorderBrush = (Brush)Application.Current.FindResource("GlassmorphismBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(16),
                    MaxWidth = 600,
                    MaxHeight = 700,
                    Padding = new Thickness(30, 25, 30, 25),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 40,
                        Opacity = 0.6,
                        ShadowDepth = 0
                    }
                };

                var stackPanel = new StackPanel();

                // 标题
                var titleBlock = new TextBlock
                {
                    Text = title,
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    Foreground = (SolidColorBrush)Application.Current.Resources["TextBrush"],
                    Margin = new Thickness(0, 0, 0, 20),
                    TextWrapping = TextWrapping.Wrap
                };
                stackPanel.Children.Add(titleBlock);

                // 更新日志滚动区域
                var scrollViewer = new ScrollViewer
                {
                    MaxHeight = 400,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(0, 0, 0, 25)
                };

                // 解析 Markdown 并创建 UI
                var contentPanel = ParseMarkdownToPanel(markdownContent);

                scrollViewer.Content = contentPanel;
                stackPanel.Children.Add(scrollViewer);

                // 按钮区域
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var cancelButton = new Button
                {
                    Content = cancelText,
                    Width = 100,
                    Height = 36,
                    Margin = new Thickness(0, 0, 10, 0),
                    Style = (Style)Application.Current.Resources["MaterialDesignOutlinedButton"]
                };

                var confirmButton = new Button
                {
                    Content = confirmText,
                    Width = 120,
                    Height = 36,
                    Background = (SolidColorBrush)Application.Current.Resources["PrimaryBrush"],
                    Foreground = Brushes.White,
                    Style = (Style)Application.Current.Resources["MaterialDesignRaisedButton"]
                };

                buttonPanel.Children.Add(cancelButton);
                buttonPanel.Children.Add(confirmButton);
                stackPanel.Children.Add(buttonPanel);

                dialogBorder.Child = stackPanel;
                overlay.Child = dialogBorder;

                _container!.Children.Add(overlay);

                // 动画
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
                overlay.BeginAnimation(UIElement.OpacityProperty, fadeIn);

                // 关闭对话框的方法
                void CloseDialog(bool result)
                {
                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                    fadeOut.Completed += (s, e) =>
                    {
                        _container.Children.Remove(overlay);
                        tcs.TrySetResult(result);
                    };
                    overlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                }

                confirmButton.Click += (s, e) => CloseDialog(true);
                cancelButton.Click += (s, e) => CloseDialog(false);
            });

            return await tcs.Task;
        }

        /// <summary>
        /// 简单的 Markdown 解析器，将 Markdown 文本转换为 WPF 控件
        /// </summary>
        private StackPanel ParseMarkdownToPanel(string markdown)
        {
            var panel = new StackPanel();
            
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return panel;
            }

            var lines = markdown.Split('\n');
            var inCodeBlock = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.TrimEnd();
                
                // 处理代码块标记
                if (trimmedLine.StartsWith("```"))
                {
                    inCodeBlock = !inCodeBlock;
                    continue;
                }

                // 代码块内容
                if (inCodeBlock)
                {
                    var codeBlock = new TextBlock
                    {
                        Text = line,
                        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                        FontSize = 12,
                        Foreground = (SolidColorBrush)Application.Current.Resources["TextSecondaryBrush"],
                        Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
                        Padding = new Thickness(8, 4, 8, 4),
                        Margin = new Thickness(0, 2, 0, 2)
                    };
                    panel.Children.Add(codeBlock);
                    continue;
                }

                // 分隔线
                if (trimmedLine == "---" || trimmedLine == "***" || trimmedLine == "___")
                {
                    var separator = new Border
                    {
                        Height = 1,
                        Background = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                        Margin = new Thickness(0, 12, 0, 12)
                    };
                    panel.Children.Add(separator);
                    continue;
                }

                // 标题
                if (trimmedLine.StartsWith("#"))
                {
                    int level = 0;
                    while (level < trimmedLine.Length && trimmedLine[level] == '#')
                    {
                        level++;
                    }

                    var headerText = trimmedLine.Substring(level).TrimStart();
                    var fontSize = level switch
                    {
                        1 => 20.0,
                        2 => 18.0,
                        3 => 16.0,
                        _ => 14.0
                    };

                    var headerBlock = new TextBlock
                    {
                        Text = headerText,
                        FontSize = fontSize,
                        FontWeight = FontWeights.Bold,
                        Foreground = (SolidColorBrush)Application.Current.Resources["TextBrush"],
                        Margin = new Thickness(0, level == 1 ? 16 : 12, 0, 8),
                        TextWrapping = TextWrapping.Wrap
                    };
                    panel.Children.Add(headerBlock);
                    continue;
                }

                // 列表项
                if (trimmedLine.TrimStart().StartsWith("- ") || trimmedLine.TrimStart().StartsWith("* "))
                {
                    var indent = trimmedLine.Length - trimmedLine.TrimStart().Length;
                    var listText = trimmedLine.TrimStart().Substring(2);
                    
                    var listPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(indent * 10, 2, 0, 2)
                    };

                    var bullet = new TextBlock
                    {
                        Text = "•",
                        FontSize = 14,
                        Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryBrush"],
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Top
                    };

                    var listContent = ParseInlineMarkdown(listText);
                    
                    listPanel.Children.Add(bullet);
                    listPanel.Children.Add(listContent);
                    panel.Children.Add(listPanel);
                    continue;
                }

                // 空行
                if (string.IsNullOrWhiteSpace(trimmedLine))
                {
                    var spacer = new Border
                    {
                        Height = 8
                    };
                    panel.Children.Add(spacer);
                    continue;
                }

                // 普通文本
                var textBlock = ParseInlineMarkdown(trimmedLine);
                textBlock.Margin = new Thickness(0, 2, 0, 2);
                panel.Children.Add(textBlock);
            }

            return panel;
        }

        /// <summary>
        /// 解析行内 Markdown（粗体、链接、代码等）
        /// </summary>
        private TextBlock ParseInlineMarkdown(string text)
        {
            var textBlock = new TextBlock
            {
                FontSize = 14,
                Foreground = (SolidColorBrush)Application.Current.Resources["TextSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20
            };

            var index = 0;
            while (index < text.Length)
            {
                // 处理粗体 **text**
                if (index < text.Length - 1 && text[index] == '*' && text[index + 1] == '*')
                {
                    var endIndex = text.IndexOf("**", index + 2);
                    if (endIndex != -1)
                    {
                        var boldText = text.Substring(index + 2, endIndex - index - 2);
                        var run = new Run(boldText)
                        {
                            FontWeight = FontWeights.Bold,
                            Foreground = (SolidColorBrush)Application.Current.Resources["TextBrush"]
                        };
                        textBlock.Inlines.Add(run);
                        index = endIndex + 2;
                        continue;
                    }
                }

                // 处理行内代码 `code`
                if (text[index] == '`')
                {
                    var endIndex = text.IndexOf('`', index + 1);
                    if (endIndex != -1)
                    {
                        var codeText = text.Substring(index + 1, endIndex - index - 1);
                        var run = new Run(codeText)
                        {
                            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                            Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
                            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryBrush"]
                        };
                        textBlock.Inlines.Add(run);
                        index = endIndex + 1;
                        continue;
                    }
                }

                // 处理链接 [text](url)
                if (text[index] == '[')
                {
                    var textEndIndex = text.IndexOf(']', index + 1);
                    if (textEndIndex != -1 && textEndIndex + 1 < text.Length && text[textEndIndex + 1] == '(')
                    {
                        var urlEndIndex = text.IndexOf(')', textEndIndex + 2);
                        if (urlEndIndex != -1)
                        {
                            var linkText = text.Substring(index + 1, textEndIndex - index - 1);
                            var url = text.Substring(textEndIndex + 2, urlEndIndex - textEndIndex - 2);
                            
                            var hyperlink = new Hyperlink(new Run(linkText))
                            {
                                NavigateUri = new Uri(url, UriKind.RelativeOrAbsolute),
                                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryBrush"],
                                TextDecorations = null
                            };
                            
                            hyperlink.RequestNavigate += (s, e) =>
                            {
                                try
                                {
                                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = e.Uri.ToString(),
                                        UseShellExecute = true
                                    });
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"打开链接失败: {ex.Message}");
                                }
                            };
                            
                            textBlock.Inlines.Add(hyperlink);
                            index = urlEndIndex + 1;
                            continue;
                        }
                    }
                }

                // 普通字符
                var normalText = new string(text[index], 1);
                textBlock.Inlines.Add(new Run(normalText));
                index++;
            }

            return textBlock;
        }
    }
}

