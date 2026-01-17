using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ObsMCLauncher.Models;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Pages
{
    public partial class WorldsPage : Page
    {
        private List<WorldInfo> _allWorlds = new List<WorldInfo>();
        private string _backupDirectory = "";
        
        /// <summary>
        /// 返回回调（当从其他页面导航进入时使用）
        /// </summary>
        public Action? OnBackRequested { get; set; }

        public WorldsPage()
        {
            InitializeComponent();
            Loaded += WorldsPage_Loaded;
        }
        
        /// <summary>
        /// 页面加载完成后检查是否需要显示返回按钮
        /// </summary>
        private void WorldsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadWorlds();
            InitializeBackupDirectory();
            
            // 如果设置了返回回调，显示返回按钮
            if (OnBackRequested != null)
            {
                BackButton.Visibility = Visibility.Visible;
            }
            else if (NavigationService != null && NavigationService.CanGoBack)
            {
                BackButton.Visibility = Visibility.Visible;
            }
        }
        
        /// <summary>
        /// 返回按钮点击事件
        /// </summary>
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (OnBackRequested != null)
            {
                OnBackRequested.Invoke();
            }
            else if (NavigationService != null && NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
        }


        /// <summary>
        /// 初始化备份目录
        /// </summary>
        private void InitializeBackupDirectory()
        {
            var config = LauncherConfig.Load();
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ObsMCLauncher",
                "backups",
                "worlds"
            );
            _backupDirectory = appDataDir;
            
            if (!Directory.Exists(_backupDirectory))
            {
                Directory.CreateDirectory(_backupDirectory);
            }
        }

        /// <summary>
        /// 加载世界列表
        /// </summary>
        private void LoadWorlds()
        {
            try
            {
                var config = LauncherConfig.Load();
                _allWorlds = WorldManager.Instance.GetWorlds(config.GameDirectory);
                FilterWorlds();
            }
            catch (Exception ex)
            {
                NotificationManager.Instance.ShowNotification("错误", $"加载世界列表失败: {ex.Message}", NotificationType.Error);
                ShowEmptyState();
            }
        }

        /// <summary>
        /// 筛选世界
        /// </summary>
        private void FilterWorlds()
        {
            var searchText = SearchBox?.Text?.Trim() ?? "";
            var filteredWorlds = _allWorlds;

            if (!string.IsNullOrEmpty(searchText))
            {
                filteredWorlds = _allWorlds
                    .Where(w => w.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            WorldsList.ItemsSource = filteredWorlds;

            // 显示/隐藏空状态
            if (filteredWorlds.Count == 0)
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
            WorldsList.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 隐藏空状态
        /// </summary>
        private void HideEmptyState()
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            WorldsList.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 搜索框按键事件
        /// </summary>
        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FilterWorlds();
            }
        }

        /// <summary>
        /// 刷新按钮点击
        /// </summary>
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadWorlds();
        }

        /// <summary>
        /// 打开备份目录
        /// </summary>
        private void OpenBackupFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(_backupDirectory))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _backupDirectory,
                        UseShellExecute = true
                    });
                }
                else
                {
                    NotificationManager.Instance.ShowNotification("提示", "备份目录不存在", NotificationType.Info);
                }
            }
            catch (Exception ex)
            {
                NotificationManager.Instance.ShowNotification("错误", $"打开备份目录失败: {ex.Message}", NotificationType.Error);
            }
        }

        /// <summary>
        /// 备份世界
        /// </summary>
        private void BackupWorld_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is WorldInfo world)
            {
                try
                {
                    var result = WorldManager.Instance.BackupWorld(world, _backupDirectory);
                    if (result)
                    {
                        NotificationManager.Instance.ShowNotification("成功", $"世界 \"{world.Name}\" 已备份", NotificationType.Success);
                    }
                    else
                    {
                        NotificationManager.Instance.ShowNotification("错误", "备份失败", NotificationType.Error);
                    }
                }
                catch (Exception ex)
                {
                    NotificationManager.Instance.ShowNotification("错误", $"备份失败: {ex.Message}", NotificationType.Error);
                }
            }
        }

        /// <summary>
        /// 查看详情
        /// </summary>
        private void ViewDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is WorldInfo world)
            {
                ShowWorldDetails(world);
            }
        }

        /// <summary>
        /// 显示世界详情对话框
        /// </summary>
        private async void ShowWorldDetails(WorldInfo world)
        {
            var details = $@"世界名称: {world.Name}
路径: {world.FullPath}
大小: {world.FormattedSize}
最后游玩: {world.FormattedLastPlayed}
游戏模式: {world.GameMode ?? "未知"}
难度: {world.Difficulty ?? "未知"}
种子: {world.Seed?.ToString() ?? "未知"}
游戏版本: {world.GameVersion ?? "未知"}";

            await DialogManager.Instance.ShowInfo("世界详情", details);
        }

        /// <summary>
        /// 删除世界
        /// </summary>
        private async void DeleteWorld_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is WorldInfo world)
            {
                var confirmed = await DialogManager.Instance.Confirm(
                    "确认删除",
                    $"确定要删除世界 \"{world.Name}\" 吗？\n\n此操作不可恢复！"
                );

                if (confirmed)
                {
                    try
                    {
                        var deleteResult = WorldManager.Instance.DeleteWorld(world);
                        if (deleteResult)
                        {
                            NotificationManager.Instance.ShowNotification("成功", $"世界 \"{world.Name}\" 已删除", NotificationType.Success);
                            LoadWorlds(); // 重新加载列表
                        }
                        else
                        {
                            NotificationManager.Instance.ShowNotification("错误", "删除失败", NotificationType.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        NotificationManager.Instance.ShowNotification("错误", $"删除失败: {ex.Message}", NotificationType.Error);
                    }
                }
            }
        }
    }
}

