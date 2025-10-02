using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ObsMCLauncher.Models;
using ObsMCLauncher.Services;

namespace ObsMCLauncher.Pages
{
    public partial class VersionDetailPage : Page
    {
        private string currentVersion;
        private CancellationTokenSource? _downloadCancellationToken;

        public VersionDetailPage(string version)
        {
            InitializeComponent();
            currentVersion = version;
            
            // 设置版本标题
            VersionTitle.Text = $"Minecraft {version}";
            VersionNumber.Text = version;
            
            // 更新选中的加载器显示
            UpdateSelectedLoaderText();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // 返回版本列表
            NavigationService?.GoBack();
        }

        private void LoaderRadio_Checked(object sender, RoutedEventArgs e)
        {
            // 禁用所有版本选择框
            if (ForgeVersionComboBox != null) ForgeVersionComboBox.IsEnabled = false;
            if (FabricVersionComboBox != null) FabricVersionComboBox.IsEnabled = false;
            if (OptiFineVersionComboBox != null) OptiFineVersionComboBox.IsEnabled = false;
            if (QuiltVersionComboBox != null) QuiltVersionComboBox.IsEnabled = false;

            // 根据选中的加载器启用对应的版本选择框
            if (sender == ForgeRadio && ForgeVersionComboBox != null)
            {
                ForgeVersionComboBox.IsEnabled = true;
            }
            else if (sender == FabricRadio && FabricVersionComboBox != null)
            {
                FabricVersionComboBox.IsEnabled = true;
            }
            else if (sender == OptiFineRadio && OptiFineVersionComboBox != null)
            {
                OptiFineVersionComboBox.IsEnabled = true;
            }
            else if (sender == QuiltRadio && QuiltVersionComboBox != null)
            {
                QuiltVersionComboBox.IsEnabled = true;
            }

            // 更新选中的加载器显示
            UpdateSelectedLoaderText();
        }

        private void UpdateSelectedLoaderText()
        {
            if (SelectedLoaderText == null) return;

            if (VanillaRadio?.IsChecked == true)
            {
                SelectedLoaderText.Text = "已选择：原版";
            }
            else if (ForgeRadio?.IsChecked == true)
            {
                var version = (ForgeVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "默认";
                SelectedLoaderText.Text = $"已选择：Forge {version}";
            }
            else if (FabricRadio?.IsChecked == true)
            {
                var version = (FabricVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "默认";
                SelectedLoaderText.Text = $"已选择：Fabric {version}";
            }
            else if (OptiFineRadio?.IsChecked == true)
            {
                var version = (OptiFineVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "默认";
                SelectedLoaderText.Text = $"已选择：OptiFine {version}";
            }
            else if (QuiltRadio?.IsChecked == true)
            {
                var version = (QuiltVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "默认";
                SelectedLoaderText.Text = $"已选择：Quilt {version}";
            }
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            // 确定加载器类型
            string loaderType = "原版";
            string loaderVersion = "";

            if (ForgeRadio?.IsChecked == true)
            {
                loaderType = "Forge";
                loaderVersion = (ForgeVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            }
            else if (FabricRadio?.IsChecked == true)
            {
                loaderType = "Fabric";
                loaderVersion = (FabricVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            }
            else if (OptiFineRadio?.IsChecked == true)
            {
                loaderType = "OptiFine";
                loaderVersion = (OptiFineVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            }
            else if (QuiltRadio?.IsChecked == true)
            {
                loaderType = "Quilt";
                loaderVersion = (QuiltVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            }

            // 确认安装
            var result = MessageBox.Show(
                $"准备安装：\n\nMinecraft 版本：{currentVersion}\n加载器：{loaderType}\n加载器版本：{loaderVersion}\n\n是否开始下载？",
                "安装确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result != MessageBoxResult.Yes)
                return;

            // 获取游戏目录
            var config = LauncherConfig.Load();
            var gameDirectory = config.GameDirectory;

            System.Diagnostics.Debug.WriteLine($"开始下载版本 {currentVersion} 到目录 {gameDirectory}");

            // 显示进度面板，隐藏安装按钮
            InstallButton.Visibility = Visibility.Collapsed;
            SelectedLoaderText.Visibility = Visibility.Collapsed;
            InstallHintText.Visibility = Visibility.Collapsed;
            DownloadProgressPanel.Visibility = Visibility.Visible;

            try
            {
                _downloadCancellationToken = new CancellationTokenSource();

                // 创建进度报告器
                var progress = new Progress<DownloadProgress>(p =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        DownloadProgressBar.Value = p.ProgressPercentage;
                        DownloadPercentageText.Text = $"{p.ProgressPercentage:F1}%";
                        DownloadStatusText.Text = p.Status;
                        CurrentFileText.Text = p.CurrentFile;
                    });
                });

                // 开始下载（目前仅支持原版）
                if (loaderType == "原版")
                {
                    var success = await DownloadService.DownloadMinecraftVersion(
                        currentVersion,
                        gameDirectory,
                        progress,
                        _downloadCancellationToken.Token
                    );

                    if (success)
                    {
                        MessageBox.Show(
                            $"Minecraft {currentVersion} 安装完成！",
                            "安装成功",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );

                        // 返回版本列表
                        NavigationService?.GoBack();
                    }
                    else
                    {
                        MessageBox.Show(
                            "版本下载失败，请查看日志了解详细信息。",
                            "安装失败",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    }
                }
                else
                {
                    MessageBox.Show(
                        $"{loaderType} 加载器的安装功能即将推出！",
                        "功能开发中",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"下载出错: {ex.Message}");
                MessageBox.Show(
                    $"下载过程中发生错误：\n{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                // 恢复界面
                InstallButton.Visibility = Visibility.Visible;
                SelectedLoaderText.Visibility = Visibility.Visible;
                InstallHintText.Visibility = Visibility.Visible;
                DownloadProgressPanel.Visibility = Visibility.Collapsed;
                
                _downloadCancellationToken?.Dispose();
                _downloadCancellationToken = null;
            }
        }
    }
}

