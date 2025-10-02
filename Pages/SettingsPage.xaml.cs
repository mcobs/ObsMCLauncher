using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ObsMCLauncher.Models;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Pages
{
    public partial class SettingsPage : Page
    {
        private LauncherConfig _config = null!;

        public SettingsPage()
        {
            InitializeComponent();
            LoadSettings();
        }

        /// <summary>
        /// 加载设置
        /// </summary>
        private void LoadSettings()
        {
            _config = LauncherConfig.Load();

            // 加载游戏设置
            GameDirectoryTextBox.Text = _config.GameDirectory;
            MaxMemorySlider.Value = _config.MaxMemory;
            MinMemorySlider.Value = _config.MinMemory;
            JavaPathTextBox.Text = _config.JavaPath;
            JvmArgumentsTextBox.Text = _config.JvmArguments;

            // 加载下载设置
            DownloadSourceComboBox.SelectedIndex = (int)_config.DownloadSource;
            MaxDownloadThreadsComboBox.SelectedIndex = GetThreadIndexFromValue(_config.MaxDownloadThreads);

            // 加载启动器设置
            CloseAfterLaunchToggle.IsChecked = _config.CloseAfterLaunch;
            AutoCheckUpdateToggle.IsChecked = _config.AutoCheckUpdate;

            // 更新内存显示
            MaxMemoryText.Text = $"{_config.MaxMemory} MB";
            MinMemoryText.Text = $"{_config.MinMemory} MB";

            // 设置当前下载源
            DownloadSourceManager.Instance.SetDownloadSource(_config.DownloadSource);
        }

        /// <summary>
        /// 保存设置
        /// </summary>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 保存游戏设置
                _config.GameDirectory = GameDirectoryTextBox.Text;
                _config.MaxMemory = (int)MaxMemorySlider.Value;
                _config.MinMemory = (int)MinMemorySlider.Value;
                _config.JavaPath = JavaPathTextBox.Text;
                _config.JvmArguments = JvmArgumentsTextBox.Text;

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

                MessageBox.Show("设置已保存！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
        private void MaxMemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MaxMemoryText != null)
            {
                MaxMemoryText.Text = $"{(int)e.NewValue} MB";
            }
        }

        /// <summary>
        /// 最小内存滑块值改变
        /// </summary>
        private void MinMemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MinMemoryText != null)
            {
                MinMemoryText.Text = $"{(int)e.NewValue} MB";
            }
        }

        /// <summary>
        /// 下载源选择改变
        /// </summary>
        private void DownloadSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DownloadSourceDescription != null && DownloadSourceComboBox.SelectedIndex >= 0)
            {
                var source = (DownloadSource)DownloadSourceComboBox.SelectedIndex;
                DownloadSourceDescription.Text = DownloadSourceManager.GetSourceDescription(source);
            }
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
            MessageBox.Show("Java自动检测功能将在后续版本中实现", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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
    }
}

