using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Pages
{
    /// <summary>
    /// MorePage.xaml 的交互逻辑
    /// </summary>
    public partial class MorePage : Page
    {
        public MorePage()
        {
            InitializeComponent();
            
            // 加载版本信息
            LoadVersionInfo();
        }

        /// <summary>
        /// 加载版本信息
        /// </summary>
        private void LoadVersionInfo()
        {
            try
            {
                // 使用全局版本信息
                VersionText.Text = $"版本 {VersionInfo.DisplayVersion}";
                
                // 在调试模式下输出详细版本信息
                Debug.WriteLine("========== 版本信息 ==========");
                Debug.WriteLine(VersionInfo.GetDetailedVersionInfo());
                Debug.WriteLine("=============================");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取版本信息失败: {ex.Message}");
                VersionText.Text = "版本 1.0.0";
            }
        }

        /// <summary>
        /// 打开 GitHub 仓库
        /// </summary>
        private void OpenGitHub_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/mcobs/ObsMCLauncher");
        }

        /// <summary>
        /// 打开黑曜石论坛
        /// </summary>
        private void OpenForum_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://mcobs.cn/");
        }

        /// <summary>
        /// 打开 bangbang93 的爱发电页面
        /// </summary>
        private void OpenBangBang93_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://afdian.com/a/bangbang93");
        }

        /// <summary>
        /// 在默认浏览器中打开 URL
        /// </summary>
        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                
                Debug.WriteLine($"已打开链接: {url}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"打开链接失败: {ex.Message}");
                MessageBox.Show(
                    $"无法打开链接：{url}\n\n错误：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        /// <summary>
        /// 检查更新按钮点击
        /// </summary>
        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null)
            {
                button.IsEnabled = false;
                
                // 显示检查中状态
                var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };
                var progressRing = new MaterialDesignThemes.Wpf.PackIcon
                {
                    Kind = MaterialDesignThemes.Wpf.PackIconKind.Loading,
                    Width = 16,
                    Height = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                // 使用DynamicResource绑定前景色
                progressRing.SetResourceReference(MaterialDesignThemes.Wpf.PackIcon.ForegroundProperty, "TextBrush");
                
                var textBlock = new TextBlock
                {
                    Text = "检查中...",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13
                };
                // 使用DynamicResource绑定前景色
                textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                
                stackPanel.Children.Add(progressRing);
                stackPanel.Children.Add(textBlock);
                button.Content = stackPanel;
            }

            try
            {
                var newRelease = await UpdateService.CheckForUpdatesAsync();
                
                if (newRelease != null)
                {
                    await UpdateService.ShowUpdateDialogAsync(newRelease);
                }
                else
                {
                    NotificationManager.Instance.ShowNotification(
                        "已是最新版本",
                        $"当前版本 {VersionInfo.DisplayVersion} 已是最新版本",
                        NotificationType.Success,
                        3
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MorePage] 检查更新失败: {ex.Message}");
                NotificationManager.Instance.ShowNotification(
                    "检查更新失败",
                    "无法连接到更新服务器，请检查网络连接",
                    NotificationType.Error,
                    5
                );
            }
            finally
            {
                // 恢复按钮状态
                if (button != null)
                {
                    var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };
                    var icon = new MaterialDesignThemes.Wpf.PackIcon
                    {
                        Kind = MaterialDesignThemes.Wpf.PackIconKind.Update,
                        Width = 16,
                        Height = 16,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    // 使用DynamicResource绑定前景色
                    icon.SetResourceReference(MaterialDesignThemes.Wpf.PackIcon.ForegroundProperty, "TextBrush");
                    
                    var textBlock = new TextBlock
                    {
                        Text = "检查更新",
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 13
                    };
                    // 使用DynamicResource绑定前景色
                    textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                    
                    stackPanel.Children.Add(icon);
                    stackPanel.Children.Add(textBlock);
                    button.Content = stackPanel;
                    button.IsEnabled = true;
                }
            }
        }
    }
}
