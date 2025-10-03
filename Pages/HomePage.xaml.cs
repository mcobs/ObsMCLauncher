using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using MaterialDesignThemes.Wpf;
using ObsMCLauncher.Services;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Pages
{
    public partial class HomePage : Page
    {
        public HomePage()
        {
            InitializeComponent();
            Loaded += HomePage_Loaded;
        }

        private void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAccounts();
            LoadVersions();
        }

        private void LoadAccounts()
        {
            AccountComboBox.Items.Clear();

            var accounts = AccountService.Instance.GetAllAccounts();

            if (accounts.Count == 0)
            {
                // 没有账号时显示提示
                var emptyItem = new ComboBoxItem
                {
                    Content = "请先添加账号",
                    IsEnabled = false
                };
                AccountComboBox.Items.Add(emptyItem);
                AccountComboBox.SelectedIndex = 0;
                return;
            }

            // 添加所有账号
            foreach (var account in accounts)
            {
                var item = new ComboBoxItem
                {
                    Tag = account.Id
                };

                var panel = new StackPanel { Orientation = Orientation.Horizontal };

                var icon = new PackIcon
                {
                    Kind = account.Type == AccountType.Offline ? PackIconKind.Account : PackIconKind.Microsoft,
                    Width = 20,
                    Height = 20,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };

                var text = new TextBlock
                {
                    Text = account.DisplayName,
                    VerticalAlignment = VerticalAlignment.Center
                };

                panel.Children.Add(icon);
                panel.Children.Add(text);
                item.Content = panel;

                AccountComboBox.Items.Add(item);

                // 选中默认账号
                if (account.IsDefault)
                {
                    AccountComboBox.SelectedItem = item;
                }
            }

            // 如果没有默认账号，选中第一个
            if (AccountComboBox.SelectedIndex == -1 && AccountComboBox.Items.Count > 0)
            {
                AccountComboBox.SelectedIndex = 0;
            }
        }

        private void LoadVersions()
        {
            VersionComboBox.Items.Clear();

            var config = LauncherConfig.Load();
            var installedVersions = LocalVersionService.GetInstalledVersions(config.GameDirectory);

            if (installedVersions.Count == 0)
            {
                // 没有版本时显示提示
                var emptyItem = new ComboBoxItem
                {
                    Content = "请先下载版本",
                    IsEnabled = false
                };
                VersionComboBox.Items.Add(emptyItem);
                VersionComboBox.SelectedIndex = 0;
                return;
            }

            // 添加所有版本
            foreach (var version in installedVersions)
            {
                var item = new ComboBoxItem
                {
                    Content = version.Id, // 显示自定义名称
                    Tag = version.Id,
                    ToolTip = version.Id != version.ActualVersionId ? $"版本: {version.ActualVersionId}" : null
                };

                VersionComboBox.Items.Add(item);

                // 选中配置中保存的版本
                if (version.Id == config.SelectedVersion)
                {
                    VersionComboBox.SelectedItem = item;
                }
            }

            // 如果没有选中的版本，选中第一个
            if (VersionComboBox.SelectedIndex == -1 && VersionComboBox.Items.Count > 0)
            {
                VersionComboBox.SelectedIndex = 0;
                // 保存选中的版本
                if (VersionComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string versionId)
                {
                    LocalVersionService.SetSelectedVersion(versionId);
                }
            }

            // 监听版本选择变化
            VersionComboBox.SelectionChanged += VersionComboBox_SelectionChanged;
        }

        private void VersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VersionComboBox.SelectedItem is ComboBoxItem item && item.Tag is string versionId)
            {
                LocalVersionService.SetSelectedVersion(versionId);
                System.Diagnostics.Debug.WriteLine($"版本已切换到: {versionId}");
            }
        }

        /// <summary>
        /// 启动游戏按钮点击事件
        /// </summary>
        private void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. 检查是否选择了版本
                if (VersionComboBox.SelectedItem is not ComboBoxItem versionItem || versionItem.Tag is not string versionId)
                {
                    MessageBox.Show("请先选择一个游戏版本！", "提示", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 2. 获取账号
                GameAccount? account = null;
                if (AccountComboBox.SelectedItem is ComboBoxItem accountItem && accountItem.Tag is string accountId)
                {
                    var accounts = AccountService.Instance.GetAllAccounts();
                    account = accounts.FirstOrDefault(a => a.Id == accountId);
                }

                if (account == null)
                {
                    var result = MessageBox.Show(
                        "未找到游戏账号，是否前往账号管理添加账号？", 
                        "提示", 
                        MessageBoxButton.YesNo, 
                        MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        NavigationService?.Navigate(new Uri("/Pages/AccountManagementPage.xaml", UriKind.Relative));
                    }
                    return;
                }

                // 3. 加载配置
                var config = LauncherConfig.Load();

                // 4. 禁用启动按钮，防止重复点击
                LaunchButton.IsEnabled = false;
                LaunchButton.Content = "启动中...";

                // 5. 启动游戏
                Debug.WriteLine($"========== 准备启动游戏 ==========");
                Debug.WriteLine($"版本: {versionId}");
                Debug.WriteLine($"账号: {account.Username} ({account.Type})");
                
                bool success = GameLauncher.LaunchGame(versionId, account, config);

                if (success)
                {
                    // 更新账号最后使用时间
                    AccountService.Instance.UpdateLastUsed(account.Id);

                    MessageBox.Show(
                        $"游戏已启动！\n\n版本: {versionId}\n账号: {account.Username}",
                        "启动成功",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        "游戏启动失败，请检查：\n\n" +
                        "1. Java路径是否正确\n" +
                        "2. 版本文件是否完整\n" +
                        "3. 查看启动器日志了解详细错误",
                        "启动失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 启动游戏异常: {ex.Message}");
                MessageBox.Show(
                    $"启动游戏时发生错误：\n\n{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                // 恢复启动按钮
                LaunchButton.IsEnabled = true;
                LaunchButton.Content = "启动游戏";
            }
        }
    }
}

