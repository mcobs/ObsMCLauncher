using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using MaterialDesignThemes.Wpf;
using ObsMCLauncher.Models;
using ObsMCLauncher.Services;

namespace ObsMCLauncher.Utils
{
    /// <summary>
    /// Yggdrasil 外置登录悬浮框管理器
    /// </summary>
    public class YggdrasilPanelManager
    {
        private static readonly Lazy<YggdrasilPanelManager> _instance = new(() => new YggdrasilPanelManager());
        public static YggdrasilPanelManager Instance => _instance.Value;

        private Panel? _container;
        private Grid? _overlay;
        private Border? _panelBorder;
        private TaskCompletionSource<GameAccount?>? _currentTaskSource;

        private YggdrasilPanelManager() { }

        /// <summary>
        /// 初始化容器
        /// </summary>
        public void Initialize(Panel container)
        {
            _container = container;
        }

        /// <summary>
        /// 显示外置登录面板
        /// </summary>
        public Task<GameAccount?> ShowLoginPanelAsync()
        {
            if (_container == null)
            {
                System.Diagnostics.Debug.WriteLine("YggdrasilPanelManager 容器未初始化");
                return Task.FromResult<GameAccount?>(null);
            }

            _currentTaskSource = new TaskCompletionSource<GameAccount?>();

            _container.Dispatcher.Invoke(() =>
            {
                // 创建遮罩层（优先级低于 DialogManager 和 NotificationManager）
                _overlay = new Grid
                {
                    Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), // 更透明
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Opacity = 0
                };
                // 不设置 ZIndex，使用默认值 0

                // 创建登录面板
                _panelBorder = CreateLoginPanel();
                _overlay.Children.Add(_panelBorder);

                // 添加到容器
                _container.Children.Add(_overlay);

                // 淡入动画
                AnimateIn();
            });

            return _currentTaskSource.Task;
        }

        /// <summary>
        /// 创建登录面板
        /// </summary>
        private Border CreateLoginPanel()
        {
            var border = new Border
            {
                Width = 500,
                MaxHeight = 600,
                Background = (Brush)Application.Current.FindResource("SurfaceBrush"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(30),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 30,
                    ShadowDepth = 0,
                    Opacity = 0.5
                }
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 标题
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 内容
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 按钮

            // 标题
            var titlePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            titlePanel.Children.Add(new TextBlock
            {
                Text = "外置登录",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.FindResource("TextBrush")
            });
            titlePanel.Children.Add(new TextBlock
            {
                Text = "使用 Yggdrasil 认证服务器登录",
                FontSize = 13,
                Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetRow(titlePanel, 0);
            mainGrid.Children.Add(titlePanel);

            // 内容区域
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var contentPanel = new StackPanel();

            // 服务器选择
            contentPanel.Children.Add(new TextBlock
            {
                Text = "认证服务器",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource("TextBrush"),
                Margin = new Thickness(0, 0, 0, 8)
            });

            var serverComboBox = new ComboBox
            {
                Name = "ServerComboBox",
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 15)
            };

            try
            {
                serverComboBox.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedComboBox");
            }
            catch { }

            // 加载服务器列表
            var servers = YggdrasilServerService.Instance.GetAllServers();
            serverComboBox.ItemsSource = servers;
            if (servers.Count > 0)
            {
                serverComboBox.SelectedIndex = 0;
            }

            // 服务器项模板
            var itemTemplate = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(StackPanel));
            factory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var iconFactory = new FrameworkElementFactory(typeof(PackIcon));
            iconFactory.SetValue(PackIcon.KindProperty, PackIconKind.Server);
            iconFactory.SetValue(PackIcon.WidthProperty, 16.0);
            iconFactory.SetValue(PackIcon.HeightProperty, 16.0);
            iconFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            iconFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            factory.AppendChild(iconFactory);

            var textFactory = new FrameworkElementFactory(typeof(TextBlock));
            textFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
            textFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            factory.AppendChild(textFactory);

            itemTemplate.VisualTree = factory;
            serverComboBox.ItemTemplate = itemTemplate;

            contentPanel.Children.Add(serverComboBox);

            // 服务器管理按钮
            var manageButton = new Button
            {
                Content = "管理服务器",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 20)
            };

            try
            {
                manageButton.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedButton");
            }
            catch { }

            manageButton.Click += (s, e) => ShowServerManagementView(serverComboBox, scrollViewer);
            contentPanel.Children.Add(manageButton);

            // 登录表单
            var formBorder = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceElevatedBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 15)
            };

            var formPanel = new StackPanel();
            formPanel.Children.Add(new TextBlock
            {
                Text = "账号信息",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource("TextBrush"),
                Margin = new Thickness(0, 0, 0, 15)
            });

            formPanel.Children.Add(new TextBlock
            {
                Text = "用户名/邮箱",
                FontSize = 13,
                Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 8)
            });

            var usernameBox = new TextBox
            {
                Name = "UsernameTextBox",
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 15)
            };

            try
            {
                usernameBox.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedTextBox");
                MaterialDesignThemes.Wpf.HintAssist.SetHint(usernameBox, "请输入用户名或邮箱");
            }
            catch { }

            formPanel.Children.Add(usernameBox);

            formPanel.Children.Add(new TextBlock
            {
                Text = "密码",
                FontSize = 13,
                Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 8)
            });

            var passwordBox = new PasswordBox
            {
                Name = "PasswordBox",
                FontSize = 14
            };

            try
            {
                passwordBox.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedPasswordBox");
                MaterialDesignThemes.Wpf.HintAssist.SetHint(passwordBox, "请输入密码");
            }
            catch { }

            formPanel.Children.Add(passwordBox);

            // OAuth 登录按钮（仅 LittleSkin）
            var oauthButton = new Button
            {
                Content = "🔐 OAuth 登录",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 12, 0, 0),
                Visibility = Visibility.Collapsed // 默认隐藏
            };

            try
            {
                oauthButton.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedButton");
            }
            catch { }

            oauthButton.Click += async (s, e) =>
            {
                await DialogManager.Instance.ShowInfo(
                    "功能开发中",
                    "OAuth 登录功能正在开发中，敬请期待！\n\n目前请使用账号密码登录。"
                );
            };

            formPanel.Children.Add(oauthButton);

            // 根据服务器选择显示/隐藏 OAuth 按钮
            Action updateOAuthButtonVisibility = () =>
            {
                var selectedServer = serverComboBox.SelectedItem as YggdrasilServer;
                if (selectedServer != null && selectedServer.Name == "LittleSkin")
                {
                    oauthButton.Visibility = Visibility.Visible;
                }
                else
                {
                    oauthButton.Visibility = Visibility.Collapsed;
                }
            };

            serverComboBox.SelectionChanged += (s, e) => updateOAuthButtonVisibility();
            
            // 初始化时也检查一次
            serverComboBox.Loaded += (s, e) => updateOAuthButtonVisibility();

            formBorder.Child = formPanel;
            contentPanel.Children.Add(formBorder);

            // 提示信息
            var tipBorder = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceBrush"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 15)
            };

            var tipPanel = new StackPanel { Orientation = Orientation.Horizontal };
            tipPanel.Children.Add(new PackIcon
            {
                Kind = PackIconKind.Information,
                Width = 20,
                Height = 20,
                Foreground = (Brush)Application.Current.FindResource("PrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 10, 0)
            });

            var tipText = new TextBlock
            {
                FontSize = 12,
                Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            };
            tipText.Inlines.Add("外置登录需要在第三方认证服务器注册账号。");
            tipText.Inlines.Add(new System.Windows.Documents.LineBreak());
            tipText.Inlines.Add("推荐使用 LittleSkin (littleskin.cn) 服务。");
            tipPanel.Children.Add(tipText);

            tipBorder.Child = tipPanel;
            contentPanel.Children.Add(tipBorder);

            // 进度面板
            var progressPanel = new Border
            {
                Name = "ProgressPanel",
                Background = (Brush)Application.Current.FindResource("SurfaceElevatedBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Visibility = Visibility.Collapsed
            };

            var progressStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var progressBar = new ProgressBar
            {
                Style = (Style)Application.Current.FindResource("MaterialDesignCircularProgressBar"),
                IsIndeterminate = true,
                Width = 48,
                Height = 48,
                Margin = new Thickness(0, 0, 0, 15)
            };
            progressStack.Children.Add(progressBar);

            var progressText = new TextBlock
            {
                Name = "ProgressText",
                Text = "正在登录...",
                FontSize = 14,
                Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            progressStack.Children.Add(progressText);

            progressPanel.Child = progressStack;
            contentPanel.Children.Add(progressPanel);

            scrollViewer.Content = contentPanel;
            Grid.SetRow(scrollViewer, 1);
            mainGrid.Children.Add(scrollViewer);

            // 底部按钮
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var cancelButton = new Button
            {
                Content = "取消",
                Width = 100,
                Margin = new Thickness(0, 0, 15, 0)
            };

            try
            {
                cancelButton.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedButton");
            }
            catch { }

            cancelButton.Click += (s, e) => ClosePanel(null);

            var loginButton = new Button
            {
                Name = "LoginButton",
                Content = "登录",
                Width = 100
            };

            try
            {
                loginButton.Style = (Style)Application.Current.FindResource("MaterialDesignRaisedButton");
                loginButton.Background = (Brush)Application.Current.FindResource("PrimaryBrush");
            }
            catch { }

            loginButton.Click += async (s, e) => await HandleLogin(serverComboBox, usernameBox, passwordBox, progressPanel, progressText, loginButton, manageButton, serverComboBox);

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(loginButton);

            Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            border.Child = mainGrid;
            return border;
        }

        /// <summary>
        /// 处理登录
        /// </summary>
        private async Task HandleLogin(ComboBox serverComboBox, TextBox usernameBox, PasswordBox passwordBox, 
            Border progressPanel, TextBlock progressText, Button loginButton, Button manageButton, ComboBox serverBox)
        {
            var server = serverComboBox.SelectedItem as YggdrasilServer;
            if (server == null)
            {
                await DialogManager.Instance.ShowWarning("提示", "请选择认证服务器");
                return;
            }

            var username = usernameBox.Text.Trim();
            var password = passwordBox.Password;

            if (string.IsNullOrEmpty(username))
            {
                await DialogManager.Instance.ShowWarning("提示", "请输入用户名或邮箱");
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                await DialogManager.Instance.ShowWarning("提示", "请输入密码");
                return;
            }

            // 禁用控件
            loginButton.IsEnabled = false;
            manageButton.IsEnabled = false;
            serverBox.IsEnabled = false;
            usernameBox.IsEnabled = false;
            passwordBox.IsEnabled = false;

            // 显示进度
            progressPanel.Visibility = Visibility.Visible;

            var authService = new YggdrasilAuthService();
            authService.OnProgressUpdate = (message) =>
            {
                _container?.Dispatcher.Invoke(() =>
                {
                    progressText.Text = message;
                });
            };

            try
            {
                var account = await authService.LoginAsync(server, username, password);

                if (account != null)
                {
                    ClosePanel(account);
                }
                else
                {
                    await DialogManager.Instance.ShowError("登录失败", "无法登录到认证服务器，请检查账号密码是否正确");
                }
            }
            catch (MultipleProfilesException mpEx)
            {
                // 用户有多个角色，让用户选择
                progressPanel.Visibility = Visibility.Collapsed;
                
                var selectedProfile = await ShowProfileSelectionDialog(mpEx.Profiles);
                if (selectedProfile.HasValue)
                {
                    var account = authService.CreateAccountFromProfile(
                        server, 
                        selectedProfile.Value.id, 
                        selectedProfile.Value.name,
                        mpEx.AccessToken,
                        mpEx.ClientToken
                    );
                    ClosePanel(account);
                }
            }
            catch (Exception ex)
            {
                await DialogManager.Instance.ShowError("登录错误", ex.Message);
            }
            finally
            {
                // 恢复控件
                loginButton.IsEnabled = true;
                manageButton.IsEnabled = true;
                serverBox.IsEnabled = true;
                usernameBox.IsEnabled = true;
                passwordBox.IsEnabled = true;
                progressPanel.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 下载 authlib-injector.jar
        /// </summary>
        private async Task<bool> DownloadAuthlibInjectorAsync()
        {
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
                    _container?.Dispatcher.Invoke(() =>
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
                    $"authlib-injector.jar 下载成功！文件大小: {AuthlibInjectorService.GetFileSizeFormatted()}",
                    NotificationType.Success,
                    3
                );

                return true;
            }
            catch (Exception ex)
            {
                NotificationManager.Instance.RemoveNotification(notificationId);
                await DialogManager.Instance.ShowError(
                    "下载失败",
                    $"下载 authlib-injector.jar 失败：\n\n{ex.Message}\n\n请检查网络连接或稍后重试。"
                );
                return false;
            }
        }

        /// <summary>
        /// 在同一窗口内显示服务器管理视图
        /// </summary>
        private void ShowServerManagementView(ComboBox serverComboBox, ScrollViewer scrollViewer)
        {
            // 保存原始内容
            var originalContent = scrollViewer.Content;
            
            // 查找并隐藏底部按钮面板
            var mainGrid = _panelBorder?.Child as Grid;
            StackPanel? bottomButtonPanel = null;
            if (mainGrid != null)
            {
                foreach (var child in mainGrid.Children)
                {
                    if (child is StackPanel panel && Grid.GetRow(panel) == 2)
                    {
                        bottomButtonPanel = panel;
                        panel.Visibility = Visibility.Collapsed;
                        break;
                    }
                }
            }
            
            // 创建服务器管理视图
            var managementPanel = new StackPanel();
            
            // 返回按钮
            var backButton = new Button
            {
                Content = "← 返回登录",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 15)
            };
            
            try
            {
                backButton.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedButton");
            }
            catch { }
            
            backButton.Click += (s, e) =>
            {
                scrollViewer.Content = originalContent;
                RefreshServerComboBox(serverComboBox);
                
                // 恢复底部按钮的显示
                if (bottomButtonPanel != null)
                {
                    bottomButtonPanel.Visibility = Visibility.Visible;
                }
            };
            managementPanel.Children.Add(backButton);
            
            // 标题
            managementPanel.Children.Add(new TextBlock
            {
                Text = "管理认证服务器",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.FindResource("TextBrush"),
                Margin = new Thickness(0, 0, 0, 8)
            });
            
            managementPanel.Children.Add(new TextBlock
            {
                Text = "添加、编辑或删除自定义认证服务器",
                FontSize = 13,
                Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 20)
            });
            
            // 添加服务器按钮
            var addButton = new Button
            {
                Content = "➕ 添加新服务器",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 15)
            };
            
            try
            {
                addButton.Style = (Style)Application.Current.FindResource("MaterialDesignRaisedButton");
                addButton.Background = (Brush)Application.Current.FindResource("PrimaryBrush");
            }
            catch { }
            
            var serverListPanel = new StackPanel();
            
            addButton.Click += (s, e) =>
            {
                ShowAddServerInlineDialog(serverListPanel, serverComboBox, managementPanel);
            };
            managementPanel.Children.Add(addButton);
            
            // 服务器列表
            LoadServerListInline(serverListPanel, serverComboBox, managementPanel);
            managementPanel.Children.Add(serverListPanel);
            
            // 切换到管理视图
            scrollViewer.Content = managementPanel;
        }
        
        /// <summary>
        /// 加载服务器列表（内联版本）
        /// </summary>
        private void LoadServerListInline(StackPanel serverListPanel, ComboBox serverComboBox, StackPanel managementPanel)
        {
            serverListPanel.Children.Clear();
            
            var servers = YggdrasilServerService.Instance.GetAllServers();
            
            foreach (var server in servers)
            {
                var serverCard = CreateServerCardInline(server, serverListPanel, serverComboBox, managementPanel);
                serverListPanel.Children.Add(serverCard);
            }
        }
        
        /// <summary>
        /// 创建服务器卡片（内联版本）
        /// </summary>
        private Border CreateServerCardInline(YggdrasilServer server, StackPanel serverListPanel, ComboBox serverComboBox, StackPanel managementPanel)
        {
            var card = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceElevatedBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };
            
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            
            // 服务器信息
            var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            
            var nameText = new TextBlock
            {
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource("TextBrush")
            };
            nameText.Inlines.Add(server.Name);
            if (server.IsBuiltIn)
            {
                nameText.Inlines.Add(new System.Windows.Documents.Run(" [内置]")
                {
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.FindResource("PrimaryBrush")
                });
            }
            
            var urlText = new TextBlock
            {
                Text = server.ApiUrl,
                FontSize = 12,
                Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 4, 0, 0)
            };
            
            infoPanel.Children.Add(nameText);
            infoPanel.Children.Add(urlText);
            Grid.SetColumn(infoPanel, 0);
            
            // 操作按钮
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            if (!server.IsBuiltIn)
            {
                var editButton = new Button
                {
                    ToolTip = "编辑",
                    Margin = new Thickness(0, 0, 4, 0)
                };
                
                try
                {
                    editButton.Style = (Style)Application.Current.FindResource("MaterialDesignIconButton");
                }
                catch { }
                
                editButton.Content = new PackIcon
                {
                    Kind = PackIconKind.Pencil,
                    Width = 20,
                    Height = 20
                };
                
                editButton.Click += (s, e) => ShowEditServerInlineDialog(server, serverListPanel, serverComboBox, managementPanel);
                
                var deleteButton = new Button
                {
                    ToolTip = "删除",
                    Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68))
                };
                
                try
                {
                    deleteButton.Style = (Style)Application.Current.FindResource("MaterialDesignIconButton");
                }
                catch { }
                
                deleteButton.Content = new PackIcon
                {
                    Kind = PackIconKind.Delete,
                    Width = 20,
                    Height = 20
                };
                
                deleteButton.Click += async (s, e) => await DeleteServerInline(server, serverListPanel, serverComboBox, managementPanel);
                
                buttonPanel.Children.Add(editButton);
                buttonPanel.Children.Add(deleteButton);
            }
            
            Grid.SetColumn(buttonPanel, 1);
            
            grid.Children.Add(infoPanel);
            grid.Children.Add(buttonPanel);
            
            card.Child = grid;
            return card;
        }
        
        /// <summary>
        /// 显示添加服务器表单（内联版本）
        /// </summary>
        private void ShowAddServerInlineDialog(StackPanel serverListPanel, ComboBox serverComboBox, StackPanel managementPanel)
        {
            ShowServerEditForm(null, serverListPanel, serverComboBox, managementPanel);
        }
        
        /// <summary>
        /// 显示编辑服务器表单（内联版本）
        /// </summary>
        private void ShowEditServerInlineDialog(YggdrasilServer server, StackPanel serverListPanel, ComboBox serverComboBox, StackPanel managementPanel)
        {
            ShowServerEditForm(server, serverListPanel, serverComboBox, managementPanel);
        }
        
        /// <summary>
        /// 显示服务器编辑表单（在同一窗口内）
        /// </summary>
        private void ShowServerEditForm(YggdrasilServer? existingServer, StackPanel serverListPanel, ComboBox serverComboBox, StackPanel managementPanel)
        {
            // 保存管理面板的原始内容
            var originalContent = managementPanel.Children.Cast<UIElement>().ToList();
            managementPanel.Children.Clear();
            
            // 返回按钮
            var backButton = new Button
            {
                Content = "← 返回",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 15)
            };
            
            try
            {
                backButton.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedButton");
            }
            catch { }
            
            backButton.Click += (s, e) =>
            {
                managementPanel.Children.Clear();
                foreach (var child in originalContent)
                {
                    managementPanel.Children.Add(child);
                }
            };
            managementPanel.Children.Add(backButton);
            
            // 标题
            var isEdit = existingServer != null;
            managementPanel.Children.Add(new TextBlock
            {
                Text = isEdit ? "编辑服务器" : "添加新服务器",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.FindResource("TextBrush"),
                Margin = new Thickness(0, 0, 0, 8)
            });
            
            managementPanel.Children.Add(new TextBlock
            {
                Text = isEdit ? "修改服务器信息" : "添加自定义认证服务器",
                FontSize = 13,
                Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 20)
            });
            
            // 表单
            var formBorder = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceElevatedBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 20)
            };
            
            var formPanel = new StackPanel();
            
            // 服务器名称
            formPanel.Children.Add(new TextBlock
            {
                Text = "服务器名称",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource("TextBrush"),
                Margin = new Thickness(0, 0, 0, 8)
            });
            
            var nameBox = new TextBox
            {
                Name = "ServerNameBox",
                Text = existingServer?.Name ?? "",
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 15)
            };
            
            try
            {
                nameBox.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedTextBox");
                MaterialDesignThemes.Wpf.HintAssist.SetHint(nameBox, "例如：LittleSkin");
            }
            catch { }
            
            formPanel.Children.Add(nameBox);
            
            // 服务器地址
            formPanel.Children.Add(new TextBlock
            {
                Text = "服务器地址",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource("TextBrush"),
                Margin = new Thickness(0, 0, 0, 8)
            });
            
            var urlBox = new TextBox
            {
                Name = "ServerUrlBox",
                Text = existingServer?.ApiUrl ?? "",
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 8)
            };
            
            try
            {
                urlBox.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedTextBox");
                MaterialDesignThemes.Wpf.HintAssist.SetHint(urlBox, "例如：littleskin.cn");
            }
            catch { }
            
            formPanel.Children.Add(urlBox);
            
            // 提示信息
            var tipBorder = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceBrush"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 0)
            };
            
            var tipPanel = new StackPanel { Orientation = Orientation.Horizontal };
            tipPanel.Children.Add(new PackIcon
            {
                Kind = PackIconKind.Information,
                Width = 18,
                Height = 18,
                Foreground = (Brush)Application.Current.FindResource("PrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 8, 0)
            });
            
            var tipText = new TextBlock
            {
                FontSize = 12,
                Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            };
            tipText.Inlines.Add("支持两种地址格式：");
            tipText.Inlines.Add(new System.Windows.Documents.LineBreak());
            tipText.Inlines.Add("• 简化地址：littleskin.cn");
            tipText.Inlines.Add(new System.Windows.Documents.LineBreak());
            tipText.Inlines.Add("• 完整地址：https://littleskin.cn/api/yggdrasil");
            tipPanel.Children.Add(tipText);
            
            tipBorder.Child = tipPanel;
            formPanel.Children.Add(tipBorder);
            
            formBorder.Child = formPanel;
            managementPanel.Children.Add(formBorder);
            
            // 按钮区域
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            
            var cancelButton = new Button
            {
                Content = "取消",
                Width = 100,
                Margin = new Thickness(0, 0, 15, 0)
            };
            
            try
            {
                cancelButton.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedButton");
            }
            catch { }
            
            cancelButton.Click += (s, e) =>
            {
                managementPanel.Children.Clear();
                foreach (var child in originalContent)
                {
                    managementPanel.Children.Add(child);
                }
            };
            
            var saveButton = new Button
            {
                Content = isEdit ? "保存" : "添加",
                Width = 100
            };
            
            try
            {
                saveButton.Style = (Style)Application.Current.FindResource("MaterialDesignRaisedButton");
                saveButton.Background = (Brush)Application.Current.FindResource("PrimaryBrush");
            }
            catch { }
            
            saveButton.Click += async (s, e) =>
            {
                var name = nameBox.Text.Trim();
                var url = urlBox.Text.Trim();
                
                if (string.IsNullOrEmpty(name))
                {
                    await DialogManager.Instance.ShowWarning("提示", "请输入服务器名称");
                    nameBox.Focus();
                    return;
                }
                
                if (string.IsNullOrEmpty(url))
                {
                    await DialogManager.Instance.ShowWarning("提示", "请输入服务器地址");
                    urlBox.Focus();
                    return;
                }
                
                try
                {
                    if (isEdit && existingServer != null)
                    {
                        YggdrasilServerService.Instance.UpdateServer(existingServer.Id, name, url);
                        NotificationManager.Instance.ShowNotification(
                            "更新成功",
                            $"服务器 '{name}' 已更新",
                            NotificationType.Success,
                            3
                        );
                    }
                    else
                    {
                        YggdrasilServerService.Instance.AddServer(name, url);
                        NotificationManager.Instance.ShowNotification(
                            "添加成功",
                            $"服务器 '{name}' 已添加",
                            NotificationType.Success,
                            3
                        );
                    }
                    
                    // 返回管理界面
                    managementPanel.Children.Clear();
                    foreach (var child in originalContent)
                    {
                        managementPanel.Children.Add(child);
                    }
                    
                    // 刷新服务器列表
                    LoadServerListInline(serverListPanel, serverComboBox, managementPanel);
                    RefreshServerComboBox(serverComboBox);
                }
                catch (Exception ex)
                {
                    await DialogManager.Instance.ShowError("错误", ex.Message);
                }
            };
            
            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(saveButton);
            managementPanel.Children.Add(buttonPanel);
        }
        
        /// <summary>
        /// 删除服务器（内联版本）
        /// </summary>
        private async Task DeleteServerInline(YggdrasilServer server, StackPanel serverListPanel, ComboBox serverComboBox, StackPanel managementPanel)
        {
            var result = await DialogManager.Instance.ShowQuestion(
                "确认删除",
                $"确定要删除服务器 '{server.Name}' 吗？",
                DialogButtons.YesNo
            );
            
            if (result == DialogResult.Yes)
            {
                try
                {
                    YggdrasilServerService.Instance.DeleteServer(server.Id);
                    LoadServerListInline(serverListPanel, serverComboBox, managementPanel);
                    RefreshServerComboBox(serverComboBox);
                    
                    NotificationManager.Instance.ShowNotification(
                        "删除成功",
                        $"服务器 '{server.Name}' 已删除",
                        NotificationType.Success,
                        3
                    );
                }
                catch (Exception ex)
                {
                    await DialogManager.Instance.ShowError("错误", ex.Message);
                }
            }
        }

        /// <summary>
        /// 显示服务器管理面板（旧版本，保留以防需要）
        /// </summary>
        private void ShowServerManagementPanel_Old(ComboBox serverComboBox)
        {
            if (_container == null) return;

            var managementOverlay = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Opacity = 0
            };
            Panel.SetZIndex(managementOverlay, 110); // 高于登录面板

            var managementBorder = CreateServerManagementPanel(serverComboBox, managementOverlay);
            managementOverlay.Children.Add(managementBorder);

            _container.Children.Add(managementOverlay);

            // 淡入动画
            var overlayFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            managementOverlay.BeginAnimation(UIElement.OpacityProperty, overlayFadeIn);

            var dialogFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var scaleTransform = new ScaleTransform(0.9, 0.9);
            managementBorder.RenderTransform = scaleTransform;
            managementBorder.RenderTransformOrigin = new Point(0.5, 0.5);

            var scaleXAnimation = new DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
            };
            var scaleYAnimation = new DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
            };

            managementBorder.BeginAnimation(UIElement.OpacityProperty, dialogFadeIn);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnimation);
        }

        /// <summary>
        /// 创建服务器管理面板
        /// </summary>
        private Border CreateServerManagementPanel(ComboBox serverComboBox, Grid overlay)
        {
            var border = new Border
            {
                Width = 600,
                MaxHeight = 500,
                Background = (Brush)Application.Current.FindResource("SurfaceBrush"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(30),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 30,
                    ShadowDepth = 0,
                    Opacity = 0.5
                }
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 标题
            var titlePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            titlePanel.Children.Add(new TextBlock
            {
                Text = "管理认证服务器",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.FindResource("TextBrush")
            });
            titlePanel.Children.Add(new TextBlock
            {
                Text = "添加、编辑或删除自定义认证服务器",
                FontSize = 13,
                Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetRow(titlePanel, 0);
            mainGrid.Children.Add(titlePanel);

            // 服务器列表
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var serverListPanel = new StackPanel { Name = "ServerListPanel" };
            LoadServerList(serverListPanel, serverComboBox, overlay);

            scrollViewer.Content = serverListPanel;
            Grid.SetRow(scrollViewer, 1);
            mainGrid.Children.Add(scrollViewer);

            // 底部按钮
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var addButton = new Button
            {
                Content = "添加服务器",
                Width = 120,
                Margin = new Thickness(0, 0, 15, 0)
            };

            try
            {
                addButton.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedButton");
            }
            catch { }

            addButton.Click += async (s, e) => await ShowAddServerDialog(serverListPanel, serverComboBox, overlay);

            var closeButton = new Button
            {
                Content = "关闭",
                Width = 100
            };

            try
            {
                closeButton.Style = (Style)Application.Current.FindResource("MaterialDesignRaisedButton");
                closeButton.Background = (Brush)Application.Current.FindResource("PrimaryBrush");
            }
            catch { }

            closeButton.Click += (s, e) => CloseServerManagementPanel(overlay);

            buttonPanel.Children.Add(addButton);
            buttonPanel.Children.Add(closeButton);

            Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            border.Child = mainGrid;
            return border;
        }

        /// <summary>
        /// 加载服务器列表
        /// </summary>
        private void LoadServerList(StackPanel serverListPanel, ComboBox serverComboBox, Grid overlay)
        {
            serverListPanel.Children.Clear();

            var servers = YggdrasilServerService.Instance.GetAllServers();

            foreach (var server in servers)
            {
                var serverCard = CreateServerCard(server, serverListPanel, serverComboBox, overlay);
                serverListPanel.Children.Add(serverCard);
            }
        }

        /// <summary>
        /// 创建服务器卡片
        /// </summary>
        private Border CreateServerCard(YggdrasilServer server, StackPanel serverListPanel, ComboBox serverComboBox, Grid overlay)
        {
            var card = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceElevatedBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 服务器信息
            var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var nameText = new TextBlock
            {
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource("TextBrush")
            };
            nameText.Inlines.Add(server.Name);
            if (server.IsBuiltIn)
            {
                nameText.Inlines.Add(new System.Windows.Documents.Run(" [内置]")
                {
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.FindResource("PrimaryBrush")
                });
            }

            var urlText = new TextBlock
            {
                Text = server.ApiUrl,
                FontSize = 12,
                Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 4, 0, 0)
            };

            infoPanel.Children.Add(nameText);
            infoPanel.Children.Add(urlText);
            Grid.SetColumn(infoPanel, 0);

            // 操作按钮
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (!server.IsBuiltIn)
            {
                var editButton = new Button
                {
                    ToolTip = "编辑",
                    Margin = new Thickness(0, 0, 4, 0)
                };

                try
                {
                    editButton.Style = (Style)Application.Current.FindResource("MaterialDesignIconButton");
                }
                catch { }

                editButton.Content = new PackIcon
                {
                    Kind = PackIconKind.Pencil,
                    Width = 20,
                    Height = 20
                };

                editButton.Click += async (s, e) => await ShowEditServerDialog(server, serverListPanel, serverComboBox, overlay);

                var deleteButton = new Button
                {
                    ToolTip = "删除",
                    Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68))
                };

                try
                {
                    deleteButton.Style = (Style)Application.Current.FindResource("MaterialDesignIconButton");
                }
                catch { }

                deleteButton.Content = new PackIcon
                {
                    Kind = PackIconKind.Delete,
                    Width = 20,
                    Height = 20
                };

                deleteButton.Click += async (s, e) => await DeleteServer(server, serverListPanel, serverComboBox, overlay);

                buttonPanel.Children.Add(editButton);
                buttonPanel.Children.Add(deleteButton);
            }

            Grid.SetColumn(buttonPanel, 1);

            grid.Children.Add(infoPanel);
            grid.Children.Add(buttonPanel);

            card.Child = grid;
            return card;
        }

        /// <summary>
        /// 显示添加服务器对话框
        /// </summary>
        private async Task ShowAddServerDialog(StackPanel serverListPanel, ComboBox serverComboBox, Grid overlay)
        {
            var name = await DialogManager.Instance.ShowInputDialogAsync("添加服务器", "请输入服务器名称：", "");
            if (string.IsNullOrEmpty(name)) return;

            var url = await DialogManager.Instance.ShowInputDialogAsync("添加服务器", "请输入服务器地址（支持简化地址）：", "");
            if (string.IsNullOrEmpty(url)) return;

            try
            {
                YggdrasilServerService.Instance.AddServer(name, url);
                LoadServerList(serverListPanel, serverComboBox, overlay);
                RefreshServerComboBox(serverComboBox);

                NotificationManager.Instance.ShowNotification(
                    "添加成功",
                    $"服务器 '{name}' 已添加",
                    NotificationType.Success,
                    3
                );
            }
            catch (Exception ex)
            {
                await DialogManager.Instance.ShowError("错误", ex.Message);
            }
        }

        /// <summary>
        /// 显示编辑服务器对话框
        /// </summary>
        private async Task ShowEditServerDialog(YggdrasilServer server, StackPanel serverListPanel, ComboBox serverComboBox, Grid overlay)
        {
            var name = await DialogManager.Instance.ShowInputDialogAsync("编辑服务器", "请输入服务器名称：", server.Name);
            if (string.IsNullOrEmpty(name)) return;

            var url = await DialogManager.Instance.ShowInputDialogAsync("编辑服务器", "请输入服务器地址：", server.ApiUrl);
            if (string.IsNullOrEmpty(url)) return;

            try
            {
                YggdrasilServerService.Instance.UpdateServer(server.Id, name, url);
                LoadServerList(serverListPanel, serverComboBox, overlay);
                RefreshServerComboBox(serverComboBox);

                NotificationManager.Instance.ShowNotification(
                    "更新成功",
                    $"服务器 '{name}' 已更新",
                    NotificationType.Success,
                    3
                );
            }
            catch (Exception ex)
            {
                await DialogManager.Instance.ShowError("错误", ex.Message);
            }
        }

        /// <summary>
        /// 删除服务器
        /// </summary>
        private async Task DeleteServer(YggdrasilServer server, StackPanel serverListPanel, ComboBox serverComboBox, Grid overlay)
        {
            var result = await DialogManager.Instance.ShowQuestion(
                "确认删除",
                $"确定要删除服务器 '{server.Name}' 吗？",
                DialogButtons.YesNo
            );

            if (result == DialogResult.Yes)
            {
                try
                {
                    YggdrasilServerService.Instance.DeleteServer(server.Id);
                    LoadServerList(serverListPanel, serverComboBox, overlay);
                    RefreshServerComboBox(serverComboBox);

                    NotificationManager.Instance.ShowNotification(
                        "删除成功",
                        $"服务器 '{server.Name}' 已删除",
                        NotificationType.Success,
                        3
                    );
                }
                catch (Exception ex)
                {
                    await DialogManager.Instance.ShowError("错误", ex.Message);
                }
            }
        }

        /// <summary>
        /// 刷新服务器下拉框
        /// </summary>
        private void RefreshServerComboBox(ComboBox serverComboBox)
        {
            var selectedServer = serverComboBox.SelectedItem as YggdrasilServer;
            var servers = YggdrasilServerService.Instance.GetAllServers();
            serverComboBox.ItemsSource = servers;

            if (selectedServer != null)
            {
                var server = servers.FirstOrDefault(s => s.Id == selectedServer.Id);
                if (server != null)
                {
                    serverComboBox.SelectedItem = server;
                }
                else if (servers.Count > 0)
                {
                    serverComboBox.SelectedIndex = 0;
                }
            }
            else if (servers.Count > 0)
            {
                serverComboBox.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// 关闭服务器管理面板
        /// </summary>
        private void CloseServerManagementPanel(Grid overlay)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, e) =>
            {
                _container?.Children.Remove(overlay);
            };
            overlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        /// <summary>
        /// 显示角色选择对话框
        /// </summary>
        private async Task<(string id, string name)?> ShowProfileSelectionDialog(List<(string id, string name)> profiles)
        {
            var tcs = new TaskCompletionSource<(string id, string name)?>();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new Border
                {
                    Background = (Brush)Application.Current.FindResource("SurfaceBrush"),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(24),
                    MaxWidth = 400,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var panel = new StackPanel();

                // 标题
                panel.Children.Add(new TextBlock
                {
                    Text = "选择角色",
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)Application.Current.FindResource("TextBrush"),
                    Margin = new Thickness(0, 0, 0, 8)
                });

                panel.Children.Add(new TextBlock
                {
                    Text = "您的账号有多个角色，请选择一个角色登录：",
                    FontSize = 13,
                    Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 20)
                });

                // 角色列表
                foreach (var profile in profiles)
                {
                    var profileButton = new Button
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(0, 0, 0, 8),
                        Padding = new Thickness(16, 12, 16, 12),
                        MinHeight = 48,
                        HorizontalContentAlignment = HorizontalAlignment.Left
                    };

                    // 使用 TextBlock 确保文字完整显示
                    var textBlock = new TextBlock
                    {
                        Text = profile.name,
                        FontSize = 14,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    profileButton.Content = textBlock;

                    try
                    {
                        profileButton.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedButton");
                    }
                    catch { }

                    profileButton.Click += (s, e) =>
                    {
                        tcs.TrySetResult(profile);
                    };

                    panel.Children.Add(profileButton);
                }

                // 取消按钮
                var cancelButton = new Button
                {
                    Content = "取消",
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 12, 0, 0),
                    Width = 100
                };

                try
                {
                    cancelButton.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedButton");
                }
                catch { }

                cancelButton.Click += (s, e) =>
                {
                    tcs.TrySetResult(null);
                };

                panel.Children.Add(cancelButton);

                dialog.Child = panel;

                // 添加到 overlay
                if (_overlay != null)
                {
                    _overlay.Children.Add(dialog);
                }
            });

            var result = await tcs.Task;

            // 移除对话框
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (_overlay != null && _overlay.Children.Count > 1)
                {
                    _overlay.Children.RemoveAt(_overlay.Children.Count - 1);
                }
            });

            return result;
        }

        /// <summary>
        /// 关闭面板
        /// </summary>
        private void ClosePanel(GameAccount? result)
        {
            if (_overlay != null && _panelBorder != null)
            {
                var overlayFadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                var dialogFadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));

                dialogFadeOut.Completed += (s, e) =>
                {
                    _container?.Children.Remove(_overlay);
                    _currentTaskSource?.TrySetResult(result);
                };

                _overlay.BeginAnimation(UIElement.OpacityProperty, overlayFadeOut);
                _panelBorder.BeginAnimation(UIElement.OpacityProperty, dialogFadeOut);
            }
        }

        /// <summary>
        /// 淡入动画
        /// </summary>
        private void AnimateIn()
        {
            if (_overlay != null && _panelBorder != null)
            {
                var overlayFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
                _overlay.BeginAnimation(UIElement.OpacityProperty, overlayFadeIn);

                var dialogFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                var scaleTransform = new ScaleTransform(0.9, 0.9);
                _panelBorder.RenderTransform = scaleTransform;
                _panelBorder.RenderTransformOrigin = new Point(0.5, 0.5);

                var scaleXAnimation = new DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                };
                var scaleYAnimation = new DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                };

                _panelBorder.BeginAnimation(UIElement.OpacityProperty, dialogFadeIn);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnimation);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnimation);
            }
        }
    }
}
