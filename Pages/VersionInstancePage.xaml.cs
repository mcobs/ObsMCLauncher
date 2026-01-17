using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using ObsMCLauncher.Models;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;
using ObsMCLauncher.Windows;

namespace ObsMCLauncher.Pages
{
    public partial class VersionInstancePage : Page
    {
        private InstalledVersion? _version;
        private string _versionPath;
        private List<WorldInfo> _allWorlds = new List<WorldInfo>();
        private string _backupDirectory = "";
        public Action? OnBackRequested { get; set; }

        public VersionInstancePage(InstalledVersion version)
        {
            InitializeComponent();
            _version = version;
            _versionPath = version.Path;
            Loaded += VersionInstancePage_Loaded;
            InitializeBackupDirectory();
        }

        /// <summary>
        /// 初始化备份目录
        /// </summary>
        private void InitializeBackupDirectory()
        {
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

        private void VersionInstancePage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadVersionInfo();
            CheckGameFolders();
            
            // 查找ScrollViewer并监听滚动事件
            var scrollViewer = FindVisualChild<ScrollViewer>(this);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            }
        }

        /// <summary>
        /// 查找视觉子元素
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        /// <summary>
        /// 滚动时关闭Popup
        /// </summary>
        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (MoreActionsPopup.IsOpen)
            {
                MoreActionsPopup.IsOpen = false;
            }
        }

        /// <summary>
        /// 加载版本信息
        /// </summary>
        private void LoadVersionInfo()
        {
            if (_version == null) return;

            // 设置标题
            TitleText.Text = $"{_version.Id} - 实例管理";
            SubtitleText.Text = $"管理 {_version.Id} 的配置和信息";

            // 设置版本信息
            VersionIdText.Text = _version.Id;
            ActualVersionText.Text = _version.ActualVersionId;
            LastPlayedText.Text = _version.LastPlayed.ToString("yyyy-MM-dd HH:mm:ss");
            PathText.Text = _version.Path;

            // 计算版本大小
            CalculateVersionSize();

            // 获取安装日期
            GetInstallDate();

            // 设置版本隔离状态
            var config = LauncherConfig.Load();
            var versionIsolation = VersionConfigService.GetVersionIsolation(_version.Path);
            
            // 根据版本隔离设置选择下拉框项
            if (!versionIsolation.HasValue)
            {
                // 跟随全局设置
                IsolationComboBox.SelectedIndex = 0;
            }
            else if (versionIsolation.Value)
            {
                // 启用版本隔离
                IsolationComboBox.SelectedIndex = 1;
            }
            else
            {
                // 禁用版本隔离
                IsolationComboBox.SelectedIndex = 2;
            }
            
            // 更新图标
            bool useIsolation = versionIsolation.HasValue 
                ? versionIsolation.Value 
                : config.GameDirectoryType == GameDirectoryType.VersionFolder;
            
            if (useIsolation)
            {
                IsolationIcon.Kind = PackIconKind.FolderMultiple;
                IsolationIcon.Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // 绿色
            }
            else
            {
                IsolationIcon.Kind = PackIconKind.Folder;
                IsolationIcon.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)); // 灰色
            }

            // 设置版本类型
            var typeText = _version.Type == "release" ? "正式版" :
                          _version.Type == "snapshot" ? "快照版" :
                          _version.Type == "old_alpha" ? "远古Alpha" :
                          _version.Type == "old_beta" ? "远古Beta" : "其他";
            VersionTypeText.Text = typeText;

            // 设置加载器图标
            var loaderIcon = GetVersionLoaderIcon(_version);
            if (loaderIcon != null)
            {
                LoaderIcon.Kind = loaderIcon.Kind;
                LoaderIcon.Foreground = loaderIcon.Foreground;
                LoaderIcon.Visibility = Visibility.Visible;
            }
            else
            {
                LoaderIcon.Visibility = Visibility.Collapsed;
            }

            // 检查是否是当前版本，更新按钮状态
            if (_version.Id == config.SelectedVersion)
            {
                SetAsCurrentMenuItem.IsEnabled = false;
                var stackPanel = SetAsCurrentMenuItem.Content as StackPanel;
                if (stackPanel != null && stackPanel.Children.Count > 1 && stackPanel.Children[1] is TextBlock textBlock)
                {
                    textBlock.Text = "当前版本（已选中）";
                }
            }
        }

        /// <summary>
        /// 检查游戏文件夹
        /// </summary>
        private void CheckGameFolders()
        {
            if (_version == null) return;

            var config = LauncherConfig.Load();
            
            // 根据版本隔离设置获取正确的运行目录
            var runDirectory = config.GetRunDirectory(_version.Id);
            
            // 添加版本隔离状态说明
            string isolationNote = config.GameDirectoryType == GameDirectoryType.VersionFolder 
                ? "（版本独立）" 
                : "（所有版本共享）";

            // 检查Mods文件夹
            var modsPath = Path.Combine(runDirectory, "mods");
            if (Directory.Exists(modsPath))
            {
                var modFiles = Directory.GetFiles(modsPath, "*.jar");
                ModsCountText.Text = $"共 {modFiles.Length} 个 Mod 文件 {isolationNote}";
            }
            else
            {
                ModsCountText.Text = $"Mods 文件夹不存在 {isolationNote}";
            }

            // 检查存档文件夹
            var savesPath = Path.Combine(runDirectory, "saves");
            if (Directory.Exists(savesPath))
            {
                var savesFolders = Directory.GetDirectories(savesPath);
                SavesCountText.Text = $"共 {savesFolders.Length} 个存档 {isolationNote}";
            }
            else
            {
                SavesCountText.Text = $"存档文件夹不存在 {isolationNote}";
            }
        }

        /// <summary>
        /// 获取加载器图标
        /// </summary>
        private PackIcon? GetVersionLoaderIcon(InstalledVersion version)
        {
            try
            {
                var versionJsonPath = Path.Combine(version.Path, $"{version.ActualVersionId}.json");
                if (!File.Exists(versionJsonPath))
                {
                    return null;
                }

                var jsonContent = File.ReadAllText(versionJsonPath);

                PackIconKind iconKind = PackIconKind.Minecraft;
                Color iconColor = Colors.Green;

                if (jsonContent.Contains("net.minecraftforge") || jsonContent.Contains("forge"))
                {
                    iconKind = PackIconKind.Anvil;
                    iconColor = Color.FromRgb(205, 92, 92);
                }
                else if (jsonContent.Contains("fabric") || jsonContent.Contains("net.fabricmc"))
                {
                    iconKind = PackIconKind.AlphaFBox;
                    iconColor = Color.FromRgb(222, 184, 135);
                }
                else if (jsonContent.Contains("quilt") || jsonContent.Contains("org.quiltmc"))
                {
                    iconKind = PackIconKind.AlphaQBox;
                    iconColor = Color.FromRgb(138, 43, 226);
                }
                else if (jsonContent.Contains("neoforge") || jsonContent.Contains("net.neoforged"))
                {
                    iconKind = PackIconKind.Anvil;
                    iconColor = Color.FromRgb(255, 140, 0);
                }
                else if (jsonContent.Contains("optifine"))
                {
                    iconKind = PackIconKind.Sunglasses;
                    iconColor = Color.FromRgb(100, 149, 237);
                }

                return new PackIcon
                {
                    Kind = iconKind,
                    Width = 20,
                    Height = 20,
                    Foreground = new SolidColorBrush(iconColor)
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 返回按钮点击
        /// </summary>
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            OnBackRequested?.Invoke();
        }

        /// <summary>
        /// 打开文件夹按钮点击
        /// </summary>
        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_versionPath))
            {
                LocalVersionService.OpenVersionFolder(_versionPath);
            }
        }

        /// <summary>
        /// 启动游戏按钮点击
        /// </summary>
        private async void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            if (_version == null) return;

            var accounts = AccountService.Instance.GetAllAccounts();
            var defaultAccount = accounts.FirstOrDefault(a => a.IsDefault) ?? accounts.FirstOrDefault();

            if (defaultAccount == null)
            {
                NotificationManager.Instance.ShowNotification(
                    "无法启动",
                    "请先在账号管理中添加账号",
                    NotificationType.Error
                );
                return;
            }

            LaunchButton.IsEnabled = false;
            var originalContent = LaunchButton.Content;

            try
            {
                var progressRing = new ProgressBar
                {
                    Width = 20,
                    Height = 20,
                    IsIndeterminate = true,
                    Style = (Style)Application.Current.TryFindResource("MaterialDesignCircularProgressBar")
                };
                LaunchButton.Content = progressRing;

                var config = LauncherConfig.Load();

                var launchNotificationId = NotificationManager.Instance.ShowNotification(
                    "正在启动游戏",
                    $"版本: {_version.Id}",
                    NotificationType.Progress
                );

                bool hasIntegrityIssue = await GameLauncher.CheckGameIntegrityAsync(
                    _version.Id,
                    config,
                    (progress) => NotificationManager.Instance.UpdateNotification(launchNotificationId, progress)
                );

                if (hasIntegrityIssue && GameLauncher.MissingLibraries.Count > 0)
                {
                    NotificationManager.Instance.UpdateNotification(
                        launchNotificationId,
                        $"检测到 {GameLauncher.MissingLibraries.Count} 个缺失依赖，正在下载..."
                    );

                    var (successCount, failedCount) = await LibraryDownloader.DownloadMissingLibrariesAsync(
                        config.GameDirectory,
                        _version.Id,
                        GameLauncher.MissingLibraries,
                        (libName, current, total) =>
                        {
                            NotificationManager.Instance.UpdateNotification(
                                launchNotificationId,
                                $"下载依赖: {libName} ({current}/{total})"
                            );
                        }
                    );

                    if (failedCount > 0)
                    {
                        NotificationManager.Instance.ShowNotification(
                            "启动失败",
                            $"依赖库下载失败 ({failedCount}/{GameLauncher.MissingLibraries.Count})",
                            NotificationType.Error
                        );
                        return;
                    }
                }

                NotificationManager.Instance.UpdateNotification(launchNotificationId, "正在启动游戏...");
                
                // 创建日志窗口（如果配置启用）
                GameLogWindow? logWindow = null;
                if (config.ShowGameLogOnLaunch)
                {
                    logWindow = new GameLogWindow(_version.Id);
                    logWindow.Show();
                }
                
                await GameLauncher.LaunchGameAsync(
                    _version.Id, 
                    defaultAccount, 
                    config,
                    (progress) => NotificationManager.Instance.UpdateNotification(launchNotificationId, progress),
                    (output) => logWindow?.AppendGameOutput(output),
                    (exitCode) => 
                    {
                        logWindow?.OnGameExit(exitCode);
                        // 移除启动进度通知
                        NotificationManager.Instance.RemoveNotification(launchNotificationId);
                    }
                );

                NotificationManager.Instance.ShowNotification(
                    "启动成功",
                    $"游戏 {_version.Id} 已启动",
                    NotificationType.Success
                );
            }
            catch (Exception ex)
            {
                NotificationManager.Instance.ShowNotification(
                    "启动失败",
                    ex.Message,
                    NotificationType.Error
                );
            }
            finally
            {
                LaunchButton.Content = originalContent;
                LaunchButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// 更多操作按钮点击
        /// </summary>
        private void MoreActionsButton_Click(object sender, RoutedEventArgs e)
        {
            // 切换Popup显示状态
            MoreActionsPopup.IsOpen = !MoreActionsPopup.IsOpen;
        }

        /// <summary>
        /// 设为当前版本按钮点击
        /// </summary>
        private void SetAsCurrentButton_Click(object sender, RoutedEventArgs e)
        {
            if (_version == null) return;

            // 关闭Popup
            MoreActionsPopup.IsOpen = false;

            LocalVersionService.SetSelectedVersion(_version.Id);

            NotificationManager.Instance.ShowNotification(
                "设置成功",
                $"已将 {_version.Id} 设为当前版本",
                NotificationType.Success
            );

            // 更新按钮状态
            SetAsCurrentMenuItem.IsEnabled = false;
            var stackPanel = SetAsCurrentMenuItem.Content as StackPanel;
            if (stackPanel != null && stackPanel.Children.Count > 1 && stackPanel.Children[1] is TextBlock textBlock)
            {
                textBlock.Text = "当前版本（已选中）";
            }
        }

        /// <summary>
        /// 版本隔离下拉框选择改变事件
        /// </summary>
        private void IsolationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_version == null) return;
            
            // 避免初始化时触发
            if (!IsLoaded) return;
            
            var comboBox = sender as ComboBox;
            if (comboBox == null) return;
            
            var selectedItem = comboBox.SelectedItem as ComboBoxItem;
            if (selectedItem == null) return;

            // 当前设置（null=跟随全局）
            var currentSetting = VersionConfigService.GetVersionIsolation(_version.Path);
            
            var tag = selectedItem.Tag as string;
            bool? newSetting = null;
            string statusMessage = "";
            
            switch (tag)
            {
                case "global":
                    // 如果本来就是跟随全局，则不做任何事，也不弹提示
                    if (currentSetting == null) return;

                    newSetting = null;
                    statusMessage = "已设置为跟随全局设置";
                    IsolationIcon.Kind = PackIconKind.Folder;
                    break;

                case "enabled":
                    // 如果本来已经启用，则不做任何事，也不弹提示
                    if (currentSetting == true) return;

                    newSetting = true;
                    statusMessage = "已启用版本隔离";
                    IsolationIcon.Kind = PackIconKind.FolderMultiple;
                    IsolationIcon.Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // 绿色
                    break;

                case "disabled":
                    // 如果本来已经禁用，则不做任何事，也不弹提示
                    if (currentSetting == false) return;

                    newSetting = false;
                    statusMessage = "已禁用版本隔离";
                    IsolationIcon.Kind = PackIconKind.Folder;
                    IsolationIcon.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)); // 灰色
                    break;

                default:
                    return;
            }
            
            VersionConfigService.SetVersionIsolation(_version.Path, newSetting);
            
            NotificationManager.Instance.ShowNotification(
                "设置已更新",
                statusMessage,
                NotificationType.Success,
                2
            );

            // 设置变更后，刷新显示的文件夹统计，确保“（版本独立）/（共享）”提示同步
            CheckGameFolders();
        }

        /// <summary>
        /// 删除版本按钮点击
        /// </summary>
        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_version == null) return;

            // 关闭Popup
            MoreActionsPopup.IsOpen = false;

            var result = await DialogManager.Instance.ShowWarning(
                "确认删除",
                $"确定要删除版本 {_version.Id} 吗？\n\n此操作不可恢复！",
                DialogButtons.YesNo
            );

            if (result == DialogResult.Yes)
            {
                if (LocalVersionService.DeleteVersion(_version.Path))
                {
                    NotificationManager.Instance.ShowNotification(
                        "删除成功",
                        $"版本 {_version.Id} 已删除",
                        NotificationType.Success,
                        3
                    );

                    // 返回上一页
                    OnBackRequested?.Invoke();
                }
                else
                {
                    await DialogManager.Instance.ShowError(
                        "删除失败",
                        $"删除版本 {_version.Id} 失败，可能文件正在使用或没有权限"
                    );
                }
            }
        }

        /// <summary>
        /// 打开Mods文件夹按钮点击
        /// </summary>
        private void OpenModsFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_version == null) return;

            var config = LauncherConfig.Load();
            
            // 根据版本隔离设置获取正确的Mods目录
            var modsPath = config.GetModsDirectory(_version.Id);

            if (!Directory.Exists(modsPath))
            {
                Directory.CreateDirectory(modsPath);
            }

            LocalVersionService.OpenVersionFolder(modsPath);
        }

        /// <summary>
        /// 打开存档文件夹按钮点击
        /// </summary>
        private void OpenSavesFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_version == null) return;

            var config = LauncherConfig.Load();
            
            // 根据版本隔离设置获取正确的Saves目录
            var savesPath = config.GetSavesDirectory(_version.Id);

            if (!Directory.Exists(savesPath))
            {
                Directory.CreateDirectory(savesPath);
            }

            LocalVersionService.OpenVersionFolder(savesPath);
        }

        #region 世界管理功能

        /// <summary>
        /// 管理世界按钮点击
        /// </summary>
        private void ManageWorldsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_version == null) return;

            // 展开世界管理Expander
            WorldsManagementExpander.IsExpanded = true;
            
            // 加载世界列表
            LoadWorlds();
        }

        /// <summary>
        /// 计算版本大小
        /// </summary>
        private void CalculateVersionSize()
        {
            try
            {
                if (string.IsNullOrEmpty(_versionPath) || !Directory.Exists(_versionPath))
                {
                    VersionSizeText.Text = "未知";
                    return;
                }

                long totalSize = 0;
                var files = Directory.GetFiles(_versionPath, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        totalSize += new FileInfo(file).Length;
                    }
                    catch { /* 忽略无法访问的文件 */ }
                }

                // 格式化大小
                string formattedSize;
                if (totalSize < 1024)
                    formattedSize = $"{totalSize} B";
                else if (totalSize < 1024 * 1024)
                    formattedSize = $"{totalSize / 1024.0:F2} KB";
                else if (totalSize < 1024 * 1024 * 1024)
                    formattedSize = $"{totalSize / (1024.0 * 1024.0):F2} MB";
                else
                    formattedSize = $"{totalSize / (1024.0 * 1024.0 * 1024.0):F2} GB";

                VersionSizeText.Text = formattedSize;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VersionInstancePage] 计算版本大小失败: {ex.Message}");
                VersionSizeText.Text = "计算失败";
            }
        }

        /// <summary>
        /// 获取安装日期
        /// </summary>
        private void GetInstallDate()
        {
            try
            {
                if (string.IsNullOrEmpty(_versionPath) || !Directory.Exists(_versionPath))
                {
                    InstallDateText.Text = "未知";
                    return;
                }

                // 使用版本文件夹的创建时间作为安装日期
                var directoryInfo = new DirectoryInfo(_versionPath);
                var installDate = directoryInfo.CreationTime;
                InstallDateText.Text = installDate.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VersionInstancePage] 获取安装日期失败: {ex.Message}");
                InstallDateText.Text = "未知";
            }
        }

        /// <summary>
        /// 加载世界列表
        /// </summary>
        private void LoadWorlds()
        {
            if (_version == null) return;

            try
            {
                var config = LauncherConfig.Load();
                
                // 根据版本隔离设置获取正确的Saves目录
                var savesPath = config.GetSavesDirectory(_version.Id);
                
                // WorldManager.GetWorlds 可以接受 saves 目录路径
                _allWorlds = WorldManager.Instance.GetWorlds(savesPath);
                FilterWorlds();
            }
            catch (Exception ex)
            {
                NotificationManager.Instance.ShowNotification("错误", $"加载世界列表失败: {ex.Message}", NotificationType.Error);
                ShowWorldsEmptyState();
            }
        }

        /// <summary>
        /// 筛选世界
        /// </summary>
        private void FilterWorlds()
        {
            var searchText = WorldSearchBox?.Text?.Trim() ?? "";
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
                ShowWorldsEmptyState();
            }
            else
            {
                HideWorldsEmptyState();
            }
        }

        /// <summary>
        /// 显示空状态
        /// </summary>
        private void ShowWorldsEmptyState()
        {
            WorldsEmptyState.Visibility = Visibility.Visible;
            WorldsList.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 隐藏空状态
        /// </summary>
        private void HideWorldsEmptyState()
        {
            WorldsEmptyState.Visibility = Visibility.Collapsed;
            WorldsList.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 世界搜索框按键事件
        /// </summary>
        private void WorldSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FilterWorlds();
            }
        }

        /// <summary>
        /// 刷新世界列表
        /// </summary>
        private void RefreshWorldsButton_Click(object sender, RoutedEventArgs e)
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
        /// 查看世界详情
        /// </summary>
        private async void ViewWorldDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is WorldInfo world)
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

        #endregion
    }
}
