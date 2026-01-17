using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ObsMCLauncher.Models;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Pages
{
    public partial class SettingsPage : Page
    {
        private LauncherConfig _config = null!;
        private bool _isSaving = false;
        private bool _isInitialized = false;
        private System.Threading.CancellationTokenSource? _notificationCancellation;
        private System.Windows.Threading.DispatcherTimer? _saveDebounceTimer; // 防抖定时器
        private string _pendingSaveSettingName = ""; // 待保存的设置名称
        private bool _isUpdatingMemory = false; // 防止TextBox和Slider相互触发
        private List<JavaInfo> _detectedJavaList = new List<JavaInfo>(); // 检测到的Java列表
        private bool _isLoadingJava = false; // 标记是否正在加载Java

        // 主页卡片拖拽排序
        private Point _homeCardsDragStartPoint;
        private HomeCardViewModel? _draggingHomeCard;

        public SettingsPage()
        {
            InitializeComponent();
            
            // 设置内存滑块的最大值为系统总内存
            var totalMemoryMB = ObsMCLauncher.Utils.SystemInfo.GetTotalMemoryMB();
            MaxMemorySlider.Maximum = totalMemoryMB;
            
            LoadSettings();
            LoadJavaOptions(); // 加载Java选项
            LoadHomeCards(); // 加载主页卡片配置
            _isInitialized = true; // 初始化完成后才允许自动保存
        }

        /// <summary>
        /// 加载设置
        /// </summary>
        private void LoadSettings()
        {
            _config = LauncherConfig.Load();

            // 加载游戏目录设置
            GameDirectoryLocationComboBox.SelectedIndex = (int)_config.GameDirectoryLocation;
            GameDirectoryTextBox.Text = _config.CustomGameDirectory;
            UpdateGameDirectoryDisplay();

            // 加载其他游戏设置
            MaxMemorySlider.Value = _config.MaxMemory;
            MaxMemoryTextBox.Text = _config.MaxMemory.ToString();
            JvmArgumentsTextBox.Text = _config.JvmArguments;
            
            // 加载版本隔离设置
            GameDirectoryTypeComboBox.SelectedIndex = _config.GameDirectoryType == GameDirectoryType.VersionFolder ? 1 : 0;


            // 加载下载设置
            DownloadSourceComboBox.SelectedIndex = (int)_config.DownloadSource;
            MaxDownloadThreadsComboBox.SelectedIndex = GetThreadIndexFromValue(_config.MaxDownloadThreads);
            DownloadAssetsToggle.IsChecked = _config.DownloadAssetsWithGame;

            // 加载启动器设置
            CloseAfterLaunchToggle.IsChecked = _config.CloseAfterLaunch;
            AutoCheckUpdateToggle.IsChecked = _config.AutoCheckUpdate;
            ThemeModeComboBox.SelectedIndex = _config.ThemeMode;

            // MaxMemoryTextBox已在上面设置

            // 设置当前下载源
            DownloadSourceManager.Instance.SetDownloadSource(_config.DownloadSource);
        }

        /// <summary>
        /// 加载Java选项到下拉框
        /// </summary>
        private void LoadJavaOptions()
        {
            _isLoadingJava = true;
            try
            {
                JavaPathComboBox.Items.Clear();

                // 1. 添加"自动选择"选项
                var autoItem = new ComboBoxItem
                {
                    Content = "自动选择（根据游戏版本自动匹配）",
                    Tag = "AUTO"
                };
                JavaPathComboBox.Items.Add(autoItem);

                // 2. 检测并添加所有Java
                _detectedJavaList = JavaDetector.DetectAllJava();
                System.Diagnostics.Debug.WriteLine($"[SettingsPage] 检测到 {_detectedJavaList.Count} 个Java");

                foreach (var java in _detectedJavaList)
                {
                    var item = new ComboBoxItem
                    {
                        Content = $"☕ Java {java.MajorVersion} ({java.Architecture}) - {java.Source}",
                        Tag = java.Path,
                        ToolTip = $"版本: {java.Version}\n路径: {java.Path}"
                    };
                    JavaPathComboBox.Items.Add(item);
                }

                // 3. 添加"自定义"选项
                var customItem = new ComboBoxItem
                {
                    Content = "自定义路径...",
                    Tag = "CUSTOM"
                };
                JavaPathComboBox.Items.Add(customItem);

                // 4. 根据配置选中对应项
                SelectJavaByConfig();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载Java选项失败: {ex.Message}");
            }
            finally
            {
                _isLoadingJava = false;
            }
        }

        /// <summary>
        /// 根据配置选中Java
        /// </summary>
        private void SelectJavaByConfig()
        {
            _isLoadingJava = true;
            try
            {
                switch (_config.JavaSelectionMode)
                {
                    case 0: // 自动选择
                        JavaPathComboBox.SelectedIndex = 0;
                        CustomJavaPanel.Visibility = Visibility.Collapsed;
                        break;

                    case 1: // 指定路径
                        // 尝试找到匹配的Java路径
                        bool found = false;
                        for (int i = 1; i < JavaPathComboBox.Items.Count - 1; i++) // 跳过"自动选择"和"自定义"
                        {
                            var item = JavaPathComboBox.Items[i] as ComboBoxItem;
                            if (item?.Tag?.ToString() == _config.JavaPath)
                            {
                                JavaPathComboBox.SelectedIndex = i;
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            // 如果找不到，选择第一个检测到的Java
                            if (JavaPathComboBox.Items.Count > 1)
                            {
                                JavaPathComboBox.SelectedIndex = 1;
                            }
                        }
                        CustomJavaPanel.Visibility = Visibility.Collapsed;
                        break;

                    case 2: // 自定义
                        JavaPathComboBox.SelectedIndex = JavaPathComboBox.Items.Count - 1;
                        CustomJavaPanel.Visibility = Visibility.Visible;
                        CustomJavaTextBox.Text = _config.CustomJavaPath;
                        break;

                    default:
                        JavaPathComboBox.SelectedIndex = 0;
                        CustomJavaPanel.Visibility = Visibility.Collapsed;
                        break;
                }
            }
            finally
            {
                _isLoadingJava = false;
            }
        }

        /// <summary>
        /// Java路径下拉框选择变更
        /// </summary>
        private void JavaPathComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingJava || !_isInitialized) return;

            if (JavaPathComboBox.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag?.ToString();

                if (tag == "AUTO")
                {
                    // 自动选择
                    _config.JavaSelectionMode = 0;
                    _config.JavaPath = "";
                    CustomJavaPanel.Visibility = Visibility.Collapsed;
                    AutoSaveSettingsImmediately("Java选择");
                }
                else if (tag == "CUSTOM")
                {
                    // 自定义
                    _config.JavaSelectionMode = 2;
                    CustomJavaPanel.Visibility = Visibility.Visible;
                    // 保存在CustomJavaTextBox_LostFocus中
                }
                else
                {
                    // 指定路径
                    _config.JavaSelectionMode = 1;
                    _config.JavaPath = tag ?? "";
                    CustomJavaPanel.Visibility = Visibility.Collapsed;
                    AutoSaveSettingsImmediately("Java路径");
                }
            }
        }

        /// <summary>
        /// 自定义Java路径文本框失去焦点
        /// </summary>
        private void CustomJavaTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _config.CustomJavaPath = CustomJavaTextBox.Text;
            AutoSaveSettingsImmediately("自定义Java路径");
        }

        /// <summary>
        /// 自定义Java路径文本框内容改变
        /// </summary>
        private void CustomJavaTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 实时更新配置
            if (_isInitialized && CustomJavaPanel.Visibility == Visibility.Visible)
            {
                _config.CustomJavaPath = CustomJavaTextBox.Text;
            }
        }

        /// <summary>
        /// 浏览自定义Java路径
        /// </summary>
        private void BrowseCustomJavaPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Java可执行文件|javaw.exe;java.exe|所有文件|*.*",
                Title = "选择Java路径"
            };

            if (dialog.ShowDialog() == true)
            {
                CustomJavaTextBox.Text = dialog.FileName;
                _config.CustomJavaPath = dialog.FileName;
                AutoSaveSettingsImmediately("自定义Java路径");
            }
        }

        /// <summary>
        /// 自动保存设置（带防抖，仅用于内存滑块）
        /// </summary>
        private void AutoSaveSettings(string settingName = "设置")
        {
            if (!_isInitialized) return; // 未初始化完成时跳过
            
            // 保存待处理的设置名称
            _pendingSaveSettingName = settingName;
            
            // 取消之前的定时器
            _saveDebounceTimer?.Stop();
            
            // 创建新的防抖定时器（500ms）
            _saveDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _saveDebounceTimer.Tick += (s, e) =>
            {
                _saveDebounceTimer.Stop();
                DoSaveSettings(_pendingSaveSettingName);
            };
            _saveDebounceTimer.Start();
        }

        /// <summary>
        /// 立即保存设置（不使用防抖）
        /// </summary>
        private void AutoSaveSettingsImmediately(string settingName = "设置")
        {
            if (!_isInitialized) return;
            DoSaveSettings(settingName);
        }

        /// <summary>
        /// 执行实际的保存操作
        /// </summary>
        private void DoSaveSettings(string settingName)
        {
            if (_isSaving) return;
            _isSaving = true;

            try
            {
                // 保存游戏目录设置
                _config.GameDirectoryLocation = (DirectoryLocation)GameDirectoryLocationComboBox.SelectedIndex;
                _config.CustomGameDirectory = GameDirectoryTextBox.Text;

                // 保存其他游戏设置
                _config.MaxMemory = (int)MaxMemorySlider.Value;
                _config.MinMemory = 512; // 固定为512MB
                // Java配置已在选择时保存
                _config.JvmArguments = JvmArgumentsTextBox.Text;
                
                // 保存版本隔离设置
                _config.GameDirectoryType = GameDirectoryTypeComboBox.SelectedIndex == 1 
                    ? GameDirectoryType.VersionFolder 
                    : GameDirectoryType.RootFolder;


                // 保存下载设置
                _config.DownloadSource = (DownloadSource)DownloadSourceComboBox.SelectedIndex;
                _config.MaxDownloadThreads = GetThreadValueFromIndex(MaxDownloadThreadsComboBox.SelectedIndex);
                _config.DownloadAssetsWithGame = DownloadAssetsToggle.IsChecked ?? false;

                // 保存启动器设置
                _config.CloseAfterLaunch = CloseAfterLaunchToggle.IsChecked ?? false;
                _config.AutoCheckUpdate = AutoCheckUpdateToggle.IsChecked ?? false;
                _config.ThemeMode = ThemeModeComboBox.SelectedIndex;

                // 持久化配置
                _config.Save();

                // 更新下载源
                DownloadSourceManager.Instance.SetDownloadSource(_config.DownloadSource);

                // 重新加载账号（如果账号文件位置变了）
                AccountService.Instance.ReloadAccountsPath();

                System.Diagnostics.Debug.WriteLine($"✓ {settingName}已自动保存");

                // 使用新的通知系统（默认3秒）
                var mainWindow = Application.Current.MainWindow as MainWindow;
                mainWindow?.ShowNotification("设置已保存", $"{settingName}已自动保存", Utils.NotificationType.Success);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"自动保存失败: {ex.Message}");
            }
            finally
            {
                _isSaving = false;
            }
        }

        /// <summary>
        /// 显示保存成功通知和进度条动画
        /// </summary>
        private async Task ShowSaveNotification(string settingName)
        {
            // 取消之前的通知动画
            _notificationCancellation?.Cancel();
            _notificationCancellation = new System.Threading.CancellationTokenSource();
            var cancellationToken = _notificationCancellation.Token;

            try
            {
                // 更新通知文本
                SaveNotificationText.Text = $"{settingName}已自动保存";

                // 通知栏淡入动画
                var fadeIn = new DoubleAnimation
                {
                    From = SaveNotification.Opacity,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                SaveNotification.BeginAnimation(OpacityProperty, fadeIn);

                // 进度条动画（2秒从0到100）
                SaveProgressBar.Value = 0;
                var progressAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 100,
                    Duration = TimeSpan.FromMilliseconds(2000)
                };
                SaveProgressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, progressAnimation);

                // 等待2秒（可被取消）
                await Task.Delay(2000, cancellationToken);

                // 如果没被取消，淡出
                if (!cancellationToken.IsCancellationRequested)
                {
                    var fadeOut = new DoubleAnimation
                    {
                        From = 1,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(200),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    };
                    SaveNotification.BeginAnimation(OpacityProperty, fadeOut);

                    // 重置进度条
                    await Task.Delay(200, cancellationToken);
                    SaveProgressBar.Value = 0;
                }
            }
            catch (TaskCanceledException)
            {
                // 被取消，不做任何事（新的通知会立即开始）
            }
        }

        /// <summary>
        /// 恢复默认设置
        /// </summary>
        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var result = await DialogManager.Instance.ShowQuestion(
                "确认", 
                "确定要恢复默认设置吗？所有当前设置将会丢失。",
                DialogButtons.YesNo
            );

            if (result == DialogResult.Yes)
            {
                _config = new LauncherConfig();
                LoadSettings();
                NotificationManager.Instance.ShowNotification(
                    "成功",
                    "已恢复默认设置",
                    NotificationType.Success,
                    3
                );
            }
        }

        /// <summary>
        /// 最大内存滑块值改变
        /// </summary>
        private void MaxMemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingMemory) return;
            
            if (MaxMemoryTextBox != null)
            {
                _isUpdatingMemory = true;
                MaxMemoryTextBox.Text = ((int)e.NewValue).ToString();
                _isUpdatingMemory = false;
                AutoSaveSettings("最大内存");
            }
        }

        /// <summary>
        /// 最大内存文本框内容改变
        /// </summary>
        private void MaxMemoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingMemory || !_isInitialized) return;
            
            if (int.TryParse(MaxMemoryTextBox.Text, out int value))
            {
                var maxMemory = (int)MaxMemorySlider.Maximum;
                if (value >= 512 && value <= maxMemory)
                {
                    _isUpdatingMemory = true;
                    MaxMemorySlider.Value = value;
                    _isUpdatingMemory = false;
                    // 手动编辑文本框也需要保存（使用防抖）
                    AutoSaveSettings("最大内存");
                }
                else if (value > maxMemory)
                {
                    // 如果超过系统内存，自动调整为最大值
                    _isUpdatingMemory = true;
                    MaxMemoryTextBox.Text = maxMemory.ToString();
                    MaxMemorySlider.Value = maxMemory;
                    _isUpdatingMemory = false;
                    AutoSaveSettings("最大内存");
                }
            }
        }

        /// <summary>
        /// 最大内存文本框输入验证（只允许数字）
        /// </summary>
        private void MaxMemoryTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled = !IsTextNumeric(e.Text);
        }

        /// <summary>
        /// 检查文本是否为数字
        /// </summary>
        private bool IsTextNumeric(string text)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(text, "^[0-9]+$");
        }

        /// <summary>
        /// 下载源选择改变（自动保存）
        /// </summary>
        private void DownloadSourceComboBox_SelectionChanged_AutoSave(object sender, SelectionChangedEventArgs e)
        {
            if (DownloadSourceDescription != null && DownloadSourceComboBox.SelectedIndex >= 0)
            {
                var source = (DownloadSource)DownloadSourceComboBox.SelectedIndex;
                DownloadSourceDescription.Text = DownloadSourceManager.GetSourceDescription(source);
            }
            AutoSaveSettingsImmediately("下载源");
        }

        /// <summary>
        /// 浏览游戏目录
        /// </summary>
        private void BrowseGameDirectory_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "选择游戏目录";
                dialog.SelectedPath = GameDirectoryTextBox.Text;
                
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    GameDirectoryTextBox.Text = dialog.SelectedPath;
                    UpdateGameDirectoryDisplay();
                    // 浏览选择目录后自动保存
                    AutoSaveSettingsImmediately("游戏目录");
                }
            }
        }

        /// <summary>
        /// 游戏目录文本框失去焦点时自动保存
        /// </summary>
        private void GameDirectoryTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UpdateGameDirectoryDisplay();
            AutoSaveSettingsImmediately("游戏目录");
        }

        /// <summary>
        /// 游戏目录文本框内容改变时更新显示
        /// </summary>
        private void GameDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateGameDirectoryDisplay();
        }

        /// <summary>
        /// 测试下载源
        /// </summary>
        private async void TestDownloadSource_Click(object sender, RoutedEventArgs e)
        {
            var result = await DialogManager.Instance.ShowQuestion(
                "测试下载源",
                "是否要测试所有下载源的连接状态？\n\n这将测试：\n• BMCLAPI 镜像源\n• 官方源\n\n测试可能需要几秒钟。",
                DialogButtons.YesNo
            );

            if (result != DialogResult.Yes)
                return;

            var sb = new StringBuilder();
            sb.AppendLine("下载源测试结果：");
            sb.AppendLine();

            try
            {
                // 测试 BMCLAPI
                sb.AppendLine("【BMCLAPI 镜像源】");
                var bmclUrl = "https://bmclapi2.bangbang93.com/mc/game/version_manifest.json";
                sb.AppendLine($"URL: {bmclUrl}");
                var bmclResult = await ApiTester.TestApiAsync(bmclUrl);
                sb.AppendLine(bmclResult.Success ? $"✅ {bmclResult.Message}" : $"❌ {bmclResult.Message}");
                sb.AppendLine();

                // 测试官方源
                sb.AppendLine("【官方源】");
                var mojangUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
                sb.AppendLine($"URL: {mojangUrl}");
                var mojangResult = await ApiTester.TestApiAsync(mojangUrl);
                sb.AppendLine(mojangResult.Success ? $"✅ {mojangResult.Message}" : $"❌ {mojangResult.Message}");
                sb.AppendLine();

                sb.AppendLine("建议：");
                if (bmclResult.Success)
                {
                    sb.AppendLine("• BMCLAPI 可用，推荐中国大陆用户使用");
                }
                else
                {
                    sb.AppendLine("• BMCLAPI 不可用，可能需要检查网络");
                }

                if (mojangResult.Success)
                {
                    sb.AppendLine("• 官方源可用");
                }
                else
                {
                    sb.AppendLine("• 官方源不可用，可能需要代理");
                }

                await DialogManager.Instance.ShowInfo("测试结果", sb.ToString());
            }
            catch (Exception ex)
            {
                await DialogManager.Instance.ShowError("错误", $"测试过程中出现异常：\n{ex.Message}");
            }
        }

        private int GetThreadIndexFromValue(int value)
        {
            return value switch
            {
                4 => 0,
                8 => 1,
                16 => 2,
                32 => 3,
                _ => 1
            };
        }

        private int GetThreadValueFromIndex(int index)
        {
            return index switch
            {
                0 => 4,
                1 => 8,
                2 => 16,
                3 => 32,
                _ => 8
            };
        }

        /// <summary>
        /// JVM参数失去焦点时自动保存
        /// </summary>
        private void JvmArgumentsTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            AutoSaveSettingsImmediately("JVM参数");
        }

        /// <summary>
        /// 开关按钮改变时自动保存
        /// </summary>
        private void ToggleButton_Changed(object sender, RoutedEventArgs e)
        {
            AutoSaveSettingsImmediately("启动器设置");
        }

        /// <summary>
        /// 主题模式切换
        /// </summary>
        private void ThemeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;

            var themeMode = ThemeModeComboBox.SelectedIndex;
            _config.ThemeMode = themeMode;
            
            // 应用主题（带全局遮罩淡入淡出，避免闪烁）
            _ = ThemeTransitionManager.RunAsync(() => App.ApplyTheme(themeMode));
            
            // 保存配置
            AutoSaveSettingsImmediately("主题模式");
            
            // 显示提示
            var themeName = themeMode switch
            {
                0 => "深色模式",
                1 => "浅色模式",
                2 => "跟随系统",
                _ => "未知"
            };
            
            NotificationManager.Instance.ShowNotification(
                "主题已切换",
                $"已切换到{themeName}",
                NotificationType.Success,
                3
            );
        }

        /// <summary>
        /// 游戏目录位置选择改变（自动保存）
        /// </summary>
        private void GameDirectoryLocation_Changed_AutoSave(object sender, SelectionChangedEventArgs e)
        {
            if (GameDirectoryLocationComboBox == null) return;

            var location = (DirectoryLocation)GameDirectoryLocationComboBox.SelectedIndex;
            CustomGameDirectoryPanel.Visibility = location == DirectoryLocation.Custom ? Visibility.Visible : Visibility.Collapsed;
            UpdateGameDirectoryDisplay();
            AutoSaveSettingsImmediately("游戏目录");
        }


        /// <summary>
        /// 版本隔离设置改变（自动保存）
        /// </summary>
        private void GameDirectoryType_Changed_AutoSave(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            AutoSaveSettingsImmediately("版本隔离");
        }

        /// <summary>
        /// 通用ComboBox改变时自动保存
        /// </summary>
        private void ComboBox_Changed(object sender, SelectionChangedEventArgs e)
        {
            AutoSaveSettingsImmediately("下载设置");
        }

        /// <summary>
        /// 更新游戏目录显示
        /// </summary>
        private void UpdateGameDirectoryDisplay()
        {
            if (GameDirectoryDisplayText == null || _config == null) return;

            var location = (DirectoryLocation)GameDirectoryLocationComboBox.SelectedIndex;
            var tempConfig = new LauncherConfig
            {
                GameDirectoryLocation = location,
                CustomGameDirectory = GameDirectoryTextBox.Text
            };

            GameDirectoryDisplayText.Text = $"实际路径：{tempConfig.GameDirectory}";
        }


        /// <summary>
        /// 完整下载游戏资源设置改变
        /// </summary>
        private void DownloadAssetsToggle_Changed(object sender, RoutedEventArgs e)
        {
            AutoSaveSettingsImmediately("下载资源文件设置");
        }
        
        #region 主页卡片管理
        
        /// <summary>
        /// 加载主页卡片配置
        /// </summary>
        private void LoadHomeCards()
        {
            var config = LauncherConfig.Load();
            
            // 获取所有已注册的卡片（从HomePage获取）
            var homePage = Application.Current.Windows.OfType<MainWindow>()
                .FirstOrDefault()?.FindName("MainFrame") is System.Windows.Controls.Frame frame
                && frame.Content is HomePage hp ? hp : null;
            
            List<HomeCardViewModel> cardViewModels;
            
            if (homePage == null)
            {
                // 如果HomePage未加载，使用配置中的卡片列表
                cardViewModels = config.HomeCards
                    .OrderBy(hc => hc.Order)
                    .Select(hc => new HomeCardViewModel
                    {
                        CardId = hc.CardId,
                        Title = hc.CardId, // 临时使用CardId作为标题
                        Description = "卡片",
                        IsEnabled = hc.IsEnabled,
                        Order = hc.Order
                    })
                    .ToList();
            }
            else
            {
                // 从HomePage获取已注册的卡片
                var registeredCards = homePage.GetRegisteredCards();
                cardViewModels = registeredCards
                    .Select(card =>
                    {
                        var cardConfig = config.HomeCards.FirstOrDefault(hc => hc.CardId == card.CardId);
                        return new HomeCardViewModel
                        {
                            CardId = card.CardId,
                            Title = card.Title,
                            Description = card.Description,
                            IsEnabled = cardConfig?.IsEnabled ?? true,
                            Order = cardConfig?.Order ?? int.MaxValue
                        };
                    })
                    .OrderBy(cvm => cvm.Order)
                    .ToList();
            }
            
            // 为了稳定排序与按钮上下移/拖拽，先对列表重新编号
            for (int i = 0; i < cardViewModels.Count; i++)
            {
                cardViewModels[i].Order = i;
            }

            HomeCardsListControl.ItemsSource = cardViewModels;

            // 同步回配置（避免后续仅靠config排序导致标题回退到ID）
            var changed = false;
            foreach (var vm in cardViewModels)
            {
                var cc = config.HomeCards.FirstOrDefault(h => h.CardId == vm.CardId);
                if (cc == null)
                {
                    config.HomeCards.Add(new HomeCardConfig
                    {
                        CardId = vm.CardId,
                        IsEnabled = vm.IsEnabled,
                        Order = vm.Order
                    });
                    changed = true;
                }
                else if (cc.Order != vm.Order)
                {
                    cc.Order = vm.Order;
                    changed = true;
                }
            }

            if (changed)
            {
                config.Save();
            }
        }
        
        /// <summary>
        /// 主页卡片开关切换
        /// </summary>
        private void HomeCardToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            
            var toggle = sender as System.Windows.Controls.Primitives.ToggleButton;
            if (toggle?.DataContext is not HomeCardViewModel viewModel) return;
            
            var config = LauncherConfig.Load();
            var cardConfig = config.HomeCards.FirstOrDefault(hc => hc.CardId == viewModel.CardId);
            
            if (cardConfig == null)
            {
                cardConfig = new HomeCardConfig
                {
                    CardId = viewModel.CardId,
                    IsEnabled = toggle.IsChecked == true,
                    Order = config.HomeCards.Count
                };
                config.HomeCards.Add(cardConfig);
            }
            else
            {
                cardConfig.IsEnabled = toggle.IsChecked == true;
            }
            
            config.Save();
            
            // 刷新HomePage的卡片显示
            RefreshHomePageCards();
        }
        
        /// <summary>
        /// 上移卡片
        /// </summary>
        private void HomeCardMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not HomeCardViewModel viewModel) return;

            if (HomeCardsListControl.ItemsSource is not IList<HomeCardViewModel> list) return;

            var index = list.IndexOf(viewModel);
            if (index <= 0) return;

            list.RemoveAt(index);
            list.Insert(index - 1, viewModel);

            ApplyHomeCardsOrder(list);
        }
        
        /// <summary>
        /// 下移卡片
        /// </summary>
        private void HomeCardMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not HomeCardViewModel viewModel) return;

            if (HomeCardsListControl.ItemsSource is not IList<HomeCardViewModel> list) return;

            var index = list.IndexOf(viewModel);
            if (index < 0 || index >= list.Count - 1) return;

            list.RemoveAt(index);
            list.Insert(index + 1, viewModel);

            ApplyHomeCardsOrder(list);
        }
        
        private void ApplyHomeCardsOrder(IList<HomeCardViewModel> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                list[i].Order = i;
            }

            HomeCardsListControl.ItemsSource = null;
            HomeCardsListControl.ItemsSource = list;

            var config = LauncherConfig.Load();
            foreach (var vm in list)
            {
                var cc = config.HomeCards.FirstOrDefault(h => h.CardId == vm.CardId);
                if (cc == null)
                {
                    config.HomeCards.Add(new HomeCardConfig { CardId = vm.CardId, IsEnabled = vm.IsEnabled, Order = vm.Order });
                }
                else
                {
                    cc.Order = vm.Order;
                    cc.IsEnabled = vm.IsEnabled;
                }
            }
            config.Save();

            RefreshHomePageCards();
        }

        private void HomeCardsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _homeCardsDragStartPoint = e.GetPosition(null);
            _draggingHomeCard = GetHomeCardItemUnderMouse(e.GetPosition(HomeCardsListControl));
        }

        private void HomeCardsList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            if (_draggingHomeCard == null)
            {
                return;
            }

            var position = e.GetPosition(null);
            if (Math.Abs(position.X - _homeCardsDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - _homeCardsDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            DragDrop.DoDragDrop(HomeCardsListControl, _draggingHomeCard, DragDropEffects.Move);
        }

        private void HomeCardsList_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(HomeCardViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void HomeCardsList_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(HomeCardViewModel))) return;

            var droppedData = e.Data.GetData(typeof(HomeCardViewModel)) as HomeCardViewModel;
            if (droppedData == null) return;

            if (HomeCardsListControl.ItemsSource is not IList<HomeCardViewModel> list) return;

            var target = GetHomeCardItemUnderMouse(e.GetPosition(HomeCardsListControl));
            if (target == null || ReferenceEquals(target, droppedData)) return;

            var removedIdx = list.IndexOf(droppedData);
            var targetIdx = list.IndexOf(target);
            if (removedIdx < 0 || targetIdx < 0) return;

            list.RemoveAt(removedIdx);
            list.Insert(targetIdx, droppedData);

            ApplyHomeCardsOrder(list);

            _draggingHomeCard = null;
        }

        private HomeCardViewModel? GetHomeCardItemUnderMouse(Point position)
        {
            var element = HomeCardsListControl.InputHitTest(position) as DependencyObject;
            while (element != null && element is not ListBoxItem)
            {
                element = VisualTreeHelper.GetParent(element);
            }

            return (element as ListBoxItem)?.DataContext as HomeCardViewModel;
        }

        /// <summary>
        /// 刷新HomePage的卡片显示
        /// </summary>
        private void RefreshHomePageCards()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow?.FindName("MainFrame") is System.Windows.Controls.Frame frame
                && frame.Content is HomePage homePage)
            {
                homePage.RefreshCards();
            }
        }
        
        /// <summary>
        /// 主页卡片视图模型
        /// </summary>
        private class HomeCardViewModel
        {
            public string CardId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public bool IsEnabled { get; set; } = true;
            public int Order { get; set; } = 0;
        }
        
        #endregion
    }
}

