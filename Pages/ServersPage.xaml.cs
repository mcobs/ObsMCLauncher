using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ObsMCLauncher.Models;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Pages
{
    public partial class ServersPage : Page
    {
        private List<ServerInfo> _allServers = new List<ServerInfo>();

        public ServersPage()
        {
            InitializeComponent();
            Loaded += ServersPage_Loaded;
        }

        private void ServersPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadServers();
        }

        /// <summary>
        /// 加载服务器列表
        /// </summary>
        private void LoadServers()
        {
            try
            {
                var config = LauncherConfig.Load();
                _allServers = config.Servers ?? new List<ServerInfo>();
                UpdateGroupFilter();
                FilterServers();
            }
            catch (Exception ex)
            {
                NotificationManager.Instance.ShowNotification("错误", $"加载服务器列表失败: {ex.Message}", NotificationType.Error);
                ShowEmptyState();
            }
        }

        /// <summary>
        /// 更新分组筛选下拉框
        /// </summary>
        private void UpdateGroupFilter()
        {
            var groups = ServerManager.Instance.GetServerGroups(_allServers);
            GroupFilterComboBox.Items.Clear();
            GroupFilterComboBox.Items.Add(new ComboBoxItem { Content = "全部分组", Tag = null });
            
            foreach (var group in groups)
            {
                GroupFilterComboBox.Items.Add(new ComboBoxItem { Content = group, Tag = group });
            }
            
            GroupFilterComboBox.SelectedIndex = 0;
        }

        /// <summary>
        /// 筛选服务器
        /// </summary>
        private void FilterServers()
        {
            var searchText = SearchBox?.Text?.Trim() ?? "";
            var selectedGroup = (GroupFilterComboBox?.SelectedItem as ComboBoxItem)?.Tag as string;

            var filtered = _allServers;

            // 按分组筛选
            if (selectedGroup != null)
            {
                filtered = ServerManager.Instance.FilterByGroup(filtered, selectedGroup);
            }

            // 按搜索文本筛选
            if (!string.IsNullOrEmpty(searchText))
            {
                filtered = filtered
                    .Where(s => s.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                               s.Address.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            ServersList.ItemsSource = filtered;

            // 显示/隐藏空状态
            if (filtered.Count == 0)
            {
                ShowEmptyState();
            }
            else
            {
                HideEmptyState();
            }
        }

        /// <summary>
        /// 显示空状态
        /// </summary>
        private void ShowEmptyState()
        {
            EmptyStatePanel.Visibility = Visibility.Visible;
            ServersList.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 隐藏空状态
        /// </summary>
        private void HideEmptyState()
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            ServersList.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 保存服务器列表
        /// </summary>
        private void SaveServers()
        {
            try
            {
                var config = LauncherConfig.Load();
                config.Servers = _allServers;
                config.Save();
            }
            catch (Exception ex)
            {
                NotificationManager.Instance.ShowNotification("错误", $"保存服务器列表失败: {ex.Message}", NotificationType.Error);
            }
        }

        /// <summary>
        /// 搜索框按键事件
        /// </summary>
        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FilterServers();
            }
        }

        /// <summary>
        /// 分组筛选变化事件
        /// </summary>
        private void GroupFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterServers();
        }

        /// <summary>
        /// 刷新按钮点击事件
        /// </summary>
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadServers();
        }

        /// <summary>
        /// 刷新状态按钮点击事件
        /// </summary>
        private async void RefreshStatusButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshStatusButton.IsEnabled = false;
            try
            {
                NotificationManager.Instance.ShowNotification("提示", "正在刷新服务器状态...", NotificationType.Info);
                var updatedServers = await ServerManager.Instance.QueryServersAsync(_allServers);
                _allServers = updatedServers;
                SaveServers();
                FilterServers();
                NotificationManager.Instance.ShowNotification("成功", "服务器状态已刷新", NotificationType.Success);
            }
            catch (Exception ex)
            {
                NotificationManager.Instance.ShowNotification("错误", $"刷新服务器状态失败: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                RefreshStatusButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// 添加服务器按钮点击事件
        /// </summary>
        private void AddServerButton_Click(object sender, RoutedEventArgs e)
        {
            ShowServerEditDialog(null);
        }

        /// <summary>
        /// 编辑服务器按钮点击事件
        /// </summary>
        private void EditServer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ServerInfo server)
            {
                ShowServerEditDialog(server);
            }
        }

        /// <summary>
        /// 删除服务器按钮点击事件
        /// </summary>
        private void DeleteServer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ServerInfo server)
            {
                var result = MessageBox.Show(
                    $"确定要删除服务器 \"{server.Name}\" 吗？",
                    "确认删除",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    _allServers.Remove(server);
                    SaveServers();
                    UpdateGroupFilter();
                    FilterServers();
                    NotificationManager.Instance.ShowNotification("成功", "服务器已删除", NotificationType.Success);
                }
            }
        }

        /// <summary>
        /// 连接服务器按钮点击事件
        /// </summary>
        private void ConnectServer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ServerInfo server)
            {
                // TODO: 实现连接到服务器的功能
                // 这需要启动游戏并传递服务器地址参数
                NotificationManager.Instance.ShowNotification("提示", $"连接到服务器: {server.FullAddress}\n（此功能待实现）", NotificationType.Info);
            }
        }

        /// <summary>
        /// 显示服务器编辑对话框
        /// </summary>
        private void ShowServerEditDialog(ServerInfo? server)
        {
            // 使用简单的输入对话框
            var dialog = new Windows.ServerEditDialog(server);
            dialog.Owner = Application.Current.MainWindow;
            var result = dialog.ShowDialog();

            if (result == true && dialog.ServerInfo != null)
            {
                if (server == null)
                {
                    // 添加新服务器
                    _allServers.Add(dialog.ServerInfo);
                }
                else
                {
                    // 更新现有服务器
                    var index = _allServers.IndexOf(server);
                    if (index >= 0)
                    {
                        _allServers[index] = dialog.ServerInfo;
                    }
                }

                SaveServers();
                UpdateGroupFilter();
                FilterServers();
                NotificationManager.Instance.ShowNotification("成功", server == null ? "服务器已添加" : "服务器已更新", NotificationType.Success);
            }
        }
    }
}

