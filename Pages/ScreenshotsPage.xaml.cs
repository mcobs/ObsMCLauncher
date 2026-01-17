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
    public partial class ScreenshotsPage : Page
    {
        private List<ScreenshotInfo> _allScreenshots = new List<ScreenshotInfo>();
        private string? _selectedVersion = null;

        public ScreenshotsPage()
        {
            InitializeComponent();
            Loaded += ScreenshotsPage_Loaded;
        }

        private void ScreenshotsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadVersionFilter();
            LoadScreenshots();
        }

        /// <summary>
        /// 加载版本筛选下拉框
        /// </summary>
        private void LoadVersionFilter()
        {
            try
            {
                var config = LauncherConfig.Load();
                var versions = ScreenshotManager.Instance.GetVersionsWithScreenshots(config.GameDirectory);
                
                VersionFilterComboBox.Items.Clear();
                
                // 添加版本选项（"全部"已经在列表中，如果有截图的话）
                foreach (var version in versions)
                {
                    VersionFilterComboBox.Items.Add(new ComboBoxItem 
                    { 
                        Content = version, 
                        Tag = version == "全部" ? null : version 
                    });
                }
                
                // 如果没有版本，至少添加"全部"选项
                if (VersionFilterComboBox.Items.Count == 0)
                {
                    VersionFilterComboBox.Items.Add(new ComboBoxItem { Content = "全部", Tag = null });
                }
                
                VersionFilterComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScreenshotsPage] 加载版本筛选失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载截图列表
        /// </summary>
        private void LoadScreenshots()
        {
            try
            {
                var config = LauncherConfig.Load();
                _allScreenshots = ScreenshotManager.Instance.GetScreenshots(config.GameDirectory, _selectedVersion);
                FilterScreenshots();
            }
            catch (Exception ex)
            {
                NotificationManager.Instance.ShowNotification("错误", $"加载截图列表失败: {ex.Message}", NotificationType.Error);
                ShowEmptyState();
            }
        }

        /// <summary>
        /// 筛选截图
        /// </summary>
        private void FilterScreenshots()
        {
            var searchText = SearchBox?.Text?.Trim() ?? "";
            var startDate = StartDatePicker?.SelectedDate;
            var endDate = EndDatePicker?.SelectedDate;

            var filtered = _allScreenshots;

            // 按日期筛选
            if (startDate.HasValue || endDate.HasValue)
            {
                filtered = ScreenshotManager.Instance.FilterByDate(filtered, startDate, endDate);
            }

            // 按搜索文本筛选
            if (!string.IsNullOrEmpty(searchText))
            {
                filtered = filtered
                    .Where(s => s.FileName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            ScreenshotsList.ItemsSource = filtered;

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
            ScreenshotsList.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 隐藏空状态
        /// </summary>
        private void HideEmptyState()
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            ScreenshotsList.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 搜索框按键事件
        /// </summary>
        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FilterScreenshots();
            }
        }

        /// <summary>
        /// 版本筛选变化事件
        /// </summary>
        private void VersionFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VersionFilterComboBox.SelectedItem is ComboBoxItem item)
            {
                _selectedVersion = item.Tag as string;
                LoadScreenshots(); // 重新加载截图列表
            }
        }

        /// <summary>
        /// 日期选择器变化事件
        /// </summary>
        private void DatePicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
        {
            FilterScreenshots();
        }

        /// <summary>
        /// 刷新按钮点击事件
        /// </summary>
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadVersionFilter();
            LoadScreenshots();
        }

        /// <summary>
        /// 截图卡片点击事件（查看大图）
        /// </summary>
        private void ScreenshotCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is ScreenshotInfo screenshot)
            {
                ViewScreenshot(screenshot);
            }
        }

        /// <summary>
        /// 查看截图按钮点击事件
        /// </summary>
        private void ViewScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ScreenshotInfo screenshot)
            {
                ViewScreenshot(screenshot);
            }
        }

        /// <summary>
        /// 查看截图（打开系统默认图片查看器）
        /// </summary>
        private void ViewScreenshot(ScreenshotInfo screenshot)
        {
            try
            {
                if (File.Exists(screenshot.FullPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = screenshot.FullPath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    NotificationManager.Instance.ShowNotification("错误", "截图文件不存在", NotificationType.Error);
                }
            }
            catch (Exception ex)
            {
                NotificationManager.Instance.ShowNotification("错误", $"打开截图失败: {ex.Message}", NotificationType.Error);
            }
        }

        /// <summary>
        /// 导出截图按钮点击事件
        /// </summary>
        private void ExportScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ScreenshotInfo screenshot)
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "选择导出目录"
                };

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    try
                    {
                        var exportedPath = ScreenshotManager.Instance.ExportScreenshot(screenshot.FullPath, dialog.SelectedPath);
                        if (exportedPath != null)
                        {
                            NotificationManager.Instance.ShowNotification("成功", $"截图已导出到: {exportedPath}", NotificationType.Success);
                        }
                        else
                        {
                            NotificationManager.Instance.ShowNotification("错误", "导出截图失败", NotificationType.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        NotificationManager.Instance.ShowNotification("错误", $"导出截图失败: {ex.Message}", NotificationType.Error);
                    }
                }
            }
        }

        /// <summary>
        /// 删除截图按钮点击事件
        /// </summary>
        private void DeleteScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ScreenshotInfo screenshot)
            {
                var result = MessageBox.Show(
                    $"确定要删除截图 \"{screenshot.FileName}\" 吗？\n此操作无法撤销。",
                    "确认删除",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        if (ScreenshotManager.Instance.DeleteScreenshot(screenshot.FullPath))
                        {
                            NotificationManager.Instance.ShowNotification("成功", "截图已删除", NotificationType.Success);
                            LoadScreenshots();
                        }
                        else
                        {
                            NotificationManager.Instance.ShowNotification("错误", "删除截图失败", NotificationType.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        NotificationManager.Instance.ShowNotification("错误", $"删除截图失败: {ex.Message}", NotificationType.Error);
                    }
                }
            }
        }
    }
}

