using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

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
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                
                if (version != null)
                {
                    VersionText.Text = $"版本 {version.Major}.{version.Minor}.{version.Build}";
                }
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
    }
}
