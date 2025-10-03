using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

        public SettingsPage()
        {
            InitializeComponent();
            
            // 设置内存滑块的最大值为系统总内存
            var totalMemoryMB = ObsMCLauncher.Utils.SystemInfo.GetTotalMemoryMB();
            MaxMemorySlider.Maximum = totalMemoryMB;
            
            LoadSettings();
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
            JavaPathTextBox.Text = _config.JavaPath;
            JvmArgumentsTextBox.Text = _config.JvmArguments;

            // 加载文件存储设置
            ConfigFileLocationComboBox.SelectedIndex = (int)_config.ConfigFileLocation;
            ConfigFileTextBox.Text = _config.CustomConfigPath;
            UpdateConfigFileDisplay();

            AccountFileLocationComboBox.SelectedIndex = (int)_config.AccountFileLocation;
            UpdateAccountFileDisplay();

            // 加载下载设置
            DownloadSourceComboBox.SelectedIndex = (int)_config.DownloadSource;
            MaxDownloadThreadsComboBox.SelectedIndex = GetThreadIndexFromValue(_config.MaxDownloadThreads);

            // 加载启动器设置
            CloseAfterLaunchToggle.IsChecked = _config.CloseAfterLaunch;
            AutoCheckUpdateToggle.IsChecked = _config.AutoCheckUpdate;

            // MaxMemoryTextBox已在上面设置

            // 设置当前下载源
            DownloadSourceManager.Instance.SetDownloadSource(_config.DownloadSource);
        }

        /// <summary>
        /// 自动保存设置
        /// </summary>
        private void AutoSaveSettings(string settingName = "设置")
        {
            if (!_isInitialized || _isSaving) return; // 未初始化完成或正在保存时跳过
            _isSaving = true;

            try
            {
                // 保存游戏目录设置
                _config.GameDirectoryLocation = (DirectoryLocation)GameDirectoryLocationComboBox.SelectedIndex;
                _config.CustomGameDirectory = GameDirectoryTextBox.Text;

                // 保存其他游戏设置
                _config.MaxMemory = (int)MaxMemorySlider.Value;
                _config.MinMemory = 512; // 固定为512MB
                _config.JavaPath = JavaPathTextBox.Text;
                _config.JvmArguments = JvmArgumentsTextBox.Text;

                // 保存文件存储设置
                _config.ConfigFileLocation = (DirectoryLocation)ConfigFileLocationComboBox.SelectedIndex;
                _config.CustomConfigPath = ConfigFileTextBox.Text;
                _config.AccountFileLocation = (DirectoryLocation)AccountFileLocationComboBox.SelectedIndex;

                // 保存下载设置
                _config.DownloadSource = (DownloadSource)DownloadSourceComboBox.SelectedIndex;
                _config.MaxDownloadThreads = GetThreadValueFromIndex(MaxDownloadThreadsComboBox.SelectedIndex);

                // 保存启动器设置
                _config.CloseAfterLaunch = CloseAfterLaunchToggle.IsChecked ?? false;
                _config.AutoCheckUpdate = AutoCheckUpdateToggle.IsChecked ?? false;

                // 持久化配置
                _config.Save();

                // 更新下载源
                DownloadSourceManager.Instance.SetDownloadSource(_config.DownloadSource);

                // 重新加载账号（如果账号文件位置变了）
                AccountService.Instance.ReloadAccountsPath();

                System.Diagnostics.Debug.WriteLine($"✓ {settingName}已自动保存");

                // 使用新的通知系统
                var mainWindow = Application.Current.MainWindow as MainWindow;
                mainWindow?.ShowNotification("设置已保存", $"{settingName}已自动保存", Utils.NotificationType.Success, 2);
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
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "确定要恢复默认设置吗？所有当前设置将会丢失。", 
                "确认", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _config = new LauncherConfig();
                LoadSettings();
                MessageBox.Show("已恢复默认设置", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// 最大内存滑块值改变
        /// </summary>
        private bool _isUpdatingMemory = false;

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
                }
                else if (value > maxMemory)
                {
                    // 如果超过系统内存，自动调整为最大值
                    _isUpdatingMemory = true;
                    MaxMemoryTextBox.Text = maxMemory.ToString();
                    MaxMemorySlider.Value = maxMemory;
                    _isUpdatingMemory = false;
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
            AutoSaveSettings("下载源");
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
                }
            }
        }

        /// <summary>
        /// 浏览Java路径
        /// </summary>
        private void BrowseJavaPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Java可执行文件|javaw.exe;java.exe|所有文件|*.*",
                Title = "选择Java路径"
            };

            if (dialog.ShowDialog() == true)
            {
                JavaPathTextBox.Text = dialog.FileName;
            }
        }

        /// <summary>
        /// 自动检测Java
        /// </summary>
        private void AutoDetectJava_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("开始自动检测Java...");
                
                var javaList = JavaDetector.DetectAllJava();

                if (javaList.Count == 0)
                {
                    MessageBox.Show(
                        "未检测到任何Java安装！\n\n" +
                        "请手动安装Java后重试，或手动指定Java路径。\n\n" +
                        "推荐Java版本：\n" +
                        "• Minecraft 1.17+: Java 17或更高\n" +
                        "• Minecraft 1.13-1.16: Java 8或更高\n" +
                        "• Minecraft 1.12及以下: Java 8",
                        "未检测到Java",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // 构建选择对话框内容
                var sb = new StringBuilder();
                sb.AppendLine($"检测到 {javaList.Count} 个Java安装：");
                sb.AppendLine();

                for (int i = 0; i < javaList.Count; i++)
                {
                    var java = javaList[i];
                    sb.AppendLine($"[{i + 1}] Java {java.MajorVersion} ({java.Architecture})");
                    sb.AppendLine($"    版本: {java.Version}");
                    sb.AppendLine($"    来源: {java.Source}");
                    sb.AppendLine($"    路径: {java.Path}");
                    sb.AppendLine();
                }

                sb.AppendLine("推荐使用第 [1] 个Java（版本最高）");
                sb.AppendLine();
                sb.AppendLine("是否使用推荐的Java？");

                var result = MessageBox.Show(
                    sb.ToString(),
                    "Java自动检测",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    // 使用推荐的Java（第一个，版本最高）
                    var bestJava = javaList[0];
                    JavaPathTextBox.Text = bestJava.Path;
                    
                    // ✅ 自动保存设置
                    AutoSaveSettings("Java路径");
                    
                    MessageBox.Show(
                        $"已选择 Java {bestJava.MajorVersion}！\n\n" +
                        $"版本: {bestJava.Version}\n" +
                        $"架构: {bestJava.Architecture}\n" +
                        $"路径: {bestJava.Path}\n\n" +
                        "设置已自动保存。",
                        "设置成功",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    System.Diagnostics.Debug.WriteLine($"已选择Java: {bestJava.Path}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 自动检测Java失败: {ex.Message}");
                MessageBox.Show(
                    $"自动检测Java时出现错误：\n\n{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 测试下载源
        /// </summary>
        private async void TestDownloadSource_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "是否要测试所有下载源的连接状态？\n\n" +
                "这将测试：\n" +
                "• BMCLAPI 镜像源\n" +
                "• Mojang 官方源\n\n" +
                "测试可能需要几秒钟。",
                "测试下载源",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
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

                // 测试 Mojang 官方源
                sb.AppendLine("【Mojang 官方源】");
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
                    sb.AppendLine("• Mojang 官方源可用");
                }
                else
                {
                    sb.AppendLine("• Mojang 官方源不可用，可能需要代理");
                }

                MessageBox.Show(sb.ToString(), "测试结果", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"测试过程中出现异常：\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
        /// Java路径失去焦点时自动保存
        /// </summary>
        private void JavaPathTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            AutoSaveSettings("Java路径");
        }

        /// <summary>
        /// JVM参数失去焦点时自动保存
        /// </summary>
        private void JvmArgumentsTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            AutoSaveSettings("JVM参数");
        }

        /// <summary>
        /// 开关按钮改变时自动保存
        /// </summary>
        private void ToggleButton_Changed(object sender, RoutedEventArgs e)
        {
            AutoSaveSettings("启动器设置");
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
            AutoSaveSettings("游戏目录");
        }

        /// <summary>
        /// 配置文件位置选择改变（自动保存）
        /// </summary>
        private void ConfigFileLocation_Changed_AutoSave(object sender, SelectionChangedEventArgs e)
        {
            if (ConfigFileLocationComboBox == null) return;

            var location = (DirectoryLocation)ConfigFileLocationComboBox.SelectedIndex;
            CustomConfigFilePanel.Visibility = location == DirectoryLocation.Custom ? Visibility.Visible : Visibility.Collapsed;
            UpdateConfigFileDisplay();
            AutoSaveSettings("配置文件位置");
        }

        /// <summary>
        /// 账号文件位置选择改变（自动保存）
        /// </summary>
        private void AccountFileLocation_Changed_AutoSave(object sender, SelectionChangedEventArgs e)
        {
            UpdateAccountFileDisplay();
            AutoSaveSettings("账号文件位置");
        }

        /// <summary>
        /// 通用ComboBox改变时自动保存
        /// </summary>
        private void ComboBox_Changed(object sender, SelectionChangedEventArgs e)
        {
            AutoSaveSettings("下载设置");
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
        /// 更新配置文件路径显示
        /// </summary>
        private void UpdateConfigFileDisplay()
        {
            if (ConfigFileDisplayText == null) return;

            var location = (DirectoryLocation)ConfigFileLocationComboBox.SelectedIndex;
            var path = LauncherConfig.GetConfigFilePath(location, ConfigFileTextBox.Text);

            ConfigFileDisplayText.Text = $"实际路径：{path}";
        }

        /// <summary>
        /// 更新账号文件路径显示
        /// </summary>
        private void UpdateAccountFileDisplay()
        {
            if (AccountFileDisplayText == null) return;

            var location = (DirectoryLocation)AccountFileLocationComboBox.SelectedIndex;
            var tempConfig = new LauncherConfig
            {
                AccountFileLocation = location,
                CustomConfigPath = ConfigFileTextBox.Text
            };

            AccountFileDisplayText.Text = $"实际路径：{tempConfig.GetAccountFilePath()}";
        }

        /// <summary>
        /// 浏览配置文件
        /// </summary>
        private void BrowseConfigFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.SaveFileDialog
            {
                Title = "选择配置文件保存位置",
                Filter = "JSON文件|*.json",
                FileName = "config.json",
                InitialDirectory = System.IO.Path.GetDirectoryName(ConfigFileTextBox.Text)
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ConfigFileTextBox.Text = dialog.FileName;
                UpdateConfigFileDisplay();
            }
        }
    }
}

