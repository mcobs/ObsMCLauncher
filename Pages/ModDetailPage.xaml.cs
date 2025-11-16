using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using ObsMCLauncher.Models;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Pages
{
    public enum ModSource
    {
        CurseForge,
        Modrinth
    }

    public partial class ModDetailPage : Page
    {
        private readonly ModSource _source;
        private readonly CurseForgeMod? _curseForgeMod;
        private readonly ModrinthMod? _modrinthMod;
        private readonly string _selectedVersionId;
        private readonly string _resourceType; // 资源类型：Mods, Textures, Shaders, Datapacks, Modpacks
        private List<CurseForgeFile> _curseForgeFiles = new();
        private List<ModrinthVersion> _modrinthFiles = new();

        // CurseForge构造函数
        public ModDetailPage(CurseForgeMod mod, string selectedVersionId, string resourceType = "Mods")
        {
            InitializeComponent();
            _source = ModSource.CurseForge;
            _curseForgeMod = mod;
            _selectedVersionId = selectedVersionId;
            _resourceType = resourceType;

            LoadModInfo();
            _ = LoadModVersionsAsync();
        }

        // Modrinth构造函数
        public ModDetailPage(ModrinthMod mod, string selectedVersionId, string resourceType = "Mods")
        {
            InitializeComponent();
            _source = ModSource.Modrinth;
            _modrinthMod = mod;
            _selectedVersionId = selectedVersionId;
            _resourceType = resourceType;

            LoadModInfo();
            _ = LoadModVersionsAsync();
        }

        /// <summary>
        /// 加载MOD基本信息
        /// </summary>
        private void LoadModInfo()
        {
            if (_source == ModSource.CurseForge && _curseForgeMod != null)
            {
                LoadCurseForgeModInfo();
            }
            else if (_source == ModSource.Modrinth && _modrinthMod != null)
            {
                LoadModrinthModInfo();
            }
        }

        /// <summary>
        /// 加载CurseForge MOD信息
        /// </summary>
        private void LoadCurseForgeModInfo()
        {
            var mod = _curseForgeMod!;
            
            // 设置MOD图标
            if (mod.Logo != null && !string.IsNullOrEmpty(mod.Logo.Url))
            {
                try
                {
                    var image = new Image
                    {
                        Source = new BitmapImage(new Uri(mod.Logo.Url)),
                        Stretch = Stretch.UniformToFill
                    };
                    ModIcon.Child = image;
                }
                catch
                {
                    ModIcon.Child = CreateDefaultIcon();
                }
            }
            else
            {
                ModIcon.Child = CreateDefaultIcon();
            }

            // 设置MOD名称（支持翻译）
            var translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(mod.Slug);
            if (translation == null)
            {
                translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(mod.Id);
            }
            
            var displayName = ModTranslationService.Instance.GetDisplayName(mod.Name, translation);
            ModName.Text = displayName;

            // 设置MOD简介
            ModSummary.Text = mod.Summary;

            // 设置作者
            if (mod.Authors.Count > 0)
            {
                ModAuthor.Text = $"作者: {string.Join(", ", mod.Authors.Select(a => a.Name))}";
            }
            else
            {
                ModAuthor.Text = "作者: 未知";
            }

            // 设置下载量
            ModDownloads.Text = $"下载量: {CurseForgeService.FormatDownloadCount(mod.DownloadCount)}";

            // 设置更新时间
            ModLastUpdate.Text = $"更新: {mod.DateModified:yyyy-MM-dd}";
        }

        /// <summary>
        /// 加载Modrinth MOD信息
        /// </summary>
        private void LoadModrinthModInfo()
        {
            var mod = _modrinthMod!;
            
            // 设置MOD图标
            if (!string.IsNullOrEmpty(mod.IconUrl))
            {
                try
                {
                    var image = new Image
                    {
                        Source = new BitmapImage(new Uri(mod.IconUrl)),
                        Stretch = Stretch.UniformToFill
                    };
                    ModIcon.Child = image;
                }
                catch
                {
                    ModIcon.Child = CreateDefaultIcon();
                }
            }
            else
            {
                ModIcon.Child = CreateDefaultIcon();
            }

            // 设置MOD名称（支持翻译）
            var translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(mod.Slug);
            if (translation == null)
            {
                translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(mod.ProjectId);
            }
            
            var displayName = ModTranslationService.Instance.GetDisplayName(mod.Title, translation);
            ModName.Text = displayName;

            // 设置MOD简介
            ModSummary.Text = mod.Description;

            // 设置作者
            ModAuthor.Text = $"作者: {mod.Author}";

            // 设置下载量
            ModDownloads.Text = $"下载量: {CurseForgeService.FormatDownloadCount(mod.Downloads)}";

            // 设置更新时间
            ModLastUpdate.Text = $"更新: {mod.DateModified:yyyy-MM-dd}";
        }

        /// <summary>
        /// 创建默认图标
        /// </summary>
        private FrameworkElement CreateDefaultIcon()
        {
            return new PackIcon
            {
                Kind = PackIconKind.CubeOutline,
                Width = 60,
                Height = 60,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120))
            };
        }

        /// <summary>
        /// 加载MOD版本列表
        /// </summary>
        private async System.Threading.Tasks.Task LoadModVersionsAsync()
        {
            ShowLoading();

            try
            {
                if (_source == ModSource.CurseForge && _curseForgeMod != null)
                {
                    await LoadCurseForgeVersionsAsync();
                }
                else if (_source == ModSource.Modrinth && _modrinthMod != null)
                {
                    await LoadModrinthVersionsAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModDetailPage] 加载版本失败: {ex.Message}");
                ShowEmpty();
                NotificationManager.Instance.ShowNotification("错误", $"加载版本列表失败: {ex.Message}", NotificationType.Error);
            }
        }

        /// <summary>
        /// 加载CurseForge版本列表（支持异步增量分页加载）
        /// </summary>
        private async System.Threading.Tasks.Task LoadCurseForgeVersionsAsync()
        {
            int pageIndex = 0;
            int pageSize = 50;
            int totalCount = 0;
            bool isFirstPage = true;

            Debug.WriteLine($"[ModDetailPage] 开始加载CurseForge版本: {_curseForgeMod!.Name}");

            // 显示底部加载指示器
            ShowBottomLoading(0, 0);

            // 循环加载所有页
            do
            {
                var result = await CurseForgeService.GetModFilesAsync(_curseForgeMod.Id, pageIndex: pageIndex, pageSize: pageSize);

                if (result?.Data != null && result.Data.Count > 0)
                {
                    // 第一次获取总数
                    if (isFirstPage && result.Pagination != null)
                    {
                        totalCount = result.Pagination.TotalCount;
                        Debug.WriteLine($"[ModDetailPage] CurseForge版本总数: {totalCount}");
                        isFirstPage = false;
                    }

                    // 将新数据添加到现有列表
                    _curseForgeFiles.AddRange(result.Data);

                    pageIndex++;

                    // 更新底部加载进度
                    UpdateBottomLoading(_curseForgeFiles.Count, totalCount);

                    // 简化输出：只在特定页数输出进度
                    if (pageIndex % 10 == 0 || _curseForgeFiles.Count >= totalCount)
                    {
                        Debug.WriteLine($"[ModDetailPage] CurseForge加载进度: {_curseForgeFiles.Count}/{totalCount}");
                    }

                    // 每10页或完成时才刷新UI（大幅减少UI重建次数）
                    if (pageIndex % 10 == 0 || _curseForgeFiles.Count >= totalCount)
                    {
                        var sortedFiles = _curseForgeFiles.OrderByDescending(f => f.FileDate).ToList();
                        DisplayCurseForgeVersions(sortedFiles);
                    }

                    // 如果已加载所有版本，退出循环
                    if (_curseForgeFiles.Count >= totalCount)
                    {
                        break;
                    }

                    // 小延迟，避免过快请求
                    await System.Threading.Tasks.Task.Delay(50);
                }
                else
                {
                    break;
                }
            } while (true);

            // 隐藏底部加载指示器
            HideBottomLoading();

            if (_curseForgeFiles.Count == 0)
            {
                ShowEmpty();
                Debug.WriteLine($"[ModDetailPage] 未找到可用版本");
            }
            else
            {
                Debug.WriteLine($"[ModDetailPage] ✅ CurseForge版本加载完成: {_curseForgeFiles.Count} 个");
            }
        }

        /// <summary>
        /// 加载Modrinth版本列表
        /// </summary>
        private async System.Threading.Tasks.Task LoadModrinthVersionsAsync()
        {
            var modrinthService = new ModrinthService(DownloadSourceManager.Instance);
            var versions = await modrinthService.GetProjectVersionsAsync(_modrinthMod!.ProjectId);

            if (versions != null && versions.Count > 0)
            {
                _modrinthFiles = versions.OrderByDescending(v => v.DatePublished).ToList();
                DisplayModrinthVersions(_modrinthFiles);
                Debug.WriteLine($"[ModDetailPage] ✅ Modrinth版本加载完成: {_modrinthFiles.Count} 个");
            }
            else
            {
                ShowEmpty();
                Debug.WriteLine($"[ModDetailPage] 未找到可用版本");
            }
        }

        /// <summary>
        /// 显示CurseForge版本列表（按Minecraft版本分组）
        /// </summary>
        private void DisplayCurseForgeVersions(List<CurseForgeFile> files)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                VersionListPanel.Children.Clear();
                HideLoading();
                HideEmpty();

                // 按游戏版本分组（一个文件可以属于多个版本组）
                var groupedFiles = new Dictionary<string, List<CurseForgeFile>>();
                
                foreach (var file in files)
                {
                    if (file.GameVersions == null || file.GameVersions.Count == 0)
                    {
                        // 无版本信息的文件放入"未知"组
                        if (!groupedFiles.ContainsKey("未知版本"))
                            groupedFiles["未知版本"] = new List<CurseForgeFile>();
                        groupedFiles["未知版本"].Add(file);
                        continue;
                    }

                    // 提取所有纯Minecraft版本号（一个文件可能支持多个版本）
                    var minecraftVersions = VersionUtils.ExtractAllMinecraftVersions(file.GameVersions);
                    
                    if (minecraftVersions.Count == 0)
                    {
                        // 如果没有找到纯Minecraft版本，放入"其他版本"组
                        if (!groupedFiles.ContainsKey("其他版本"))
                            groupedFiles["其他版本"] = new List<CurseForgeFile>();
                        groupedFiles["其他版本"].Add(file);
                        continue;
                    }
                    
                    // 将文件添加到它支持的每个版本组
                    foreach (var version in minecraftVersions)
                    {
                        if (!groupedFiles.ContainsKey(version))
                            groupedFiles[version] = new List<CurseForgeFile>();
                        
                        groupedFiles[version].Add(file);
                    }
                }

                // 对版本进行排序（最新版本在前）
                var sortedVersions = groupedFiles.Keys
                    .OrderByDescending(v => v, new Utils.MinecraftVersionComparer())
                    .ToList();

                // 创建每个版本组的展开器
                for (int i = 0; i < sortedVersions.Count; i++)
                {
                    var version = sortedVersions[i];
                    var filesInGroup = groupedFiles[version];
                    var isLatest = i == 0 && version != "未知版本" && version != "其他版本"; // 第一个且不是特殊组才标记为最新

                    var expander = CreateVersionGroupExpander(version, filesInGroup, isLatest);
                    VersionListPanel.Children.Add(expander);
                }
            }));
        }

        /// <summary>
        /// 创建版本组展开器
        /// </summary>
        private Expander CreateVersionGroupExpander(string version, List<CurseForgeFile> files, bool isLatest)
        {
            var expander = new Expander
            {
                IsExpanded = isLatest, // 最新版本默认展开
                Margin = new Thickness(0, 0, 0, 6),
                Background = (Brush)Application.Current.TryFindResource("SurfaceElevatedBrush")
                    ?? new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                Foreground = (Brush)Application.Current.TryFindResource("TextPrimaryBrush")
                    ?? new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                BorderBrush = (Brush)Application.Current.TryFindResource("BorderBrush")
                    ?? new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                BorderThickness = new Thickness(1)
            };

            // 头部
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var headerStack = new StackPanel { Orientation = Orientation.Horizontal };

            // 版本号
            var versionText = new TextBlock
            {
                Text = version,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.TryFindResource("TextBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            headerStack.Children.Add(versionText);

            // 最新标签
            if (isLatest)
            {
                var latestBadge = new Border
                {
                    Background = (Brush)Application.Current.TryFindResource("PrimaryBrush")
                        ?? new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(10, 0, 0, 0),
                    Child = new TextBlock
                    {
                        Text = "最新",
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.White
                    }
                };
                headerStack.Children.Add(latestBadge);
            }

            Grid.SetColumn(headerStack, 0);
            headerGrid.Children.Add(headerStack);

            // 文件数量
            var countText = new TextBlock
            {
                Text = $"{files.Count} 个文件",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(countText, 1);
            headerGrid.Children.Add(countText);

            expander.Header = headerGrid;

            // 内容：文件列表
            var contentPanel = new StackPanel { Margin = new Thickness(6, 2, 6, 6) };
            
            foreach (var file in files)
            {
                var versionCard = CreateVersionCard(file);
                contentPanel.Children.Add(versionCard);
            }

            expander.Content = contentPanel;
            return expander;
        }

        /// <summary>
        /// 创建版本卡片
        /// </summary>
        private Border CreateVersionCard(CurseForgeFile file)
        {
            var border = new Border
            {
                Background = (Brush)Application.Current.TryFindResource("SurfaceElevatedBrush")
                    ?? new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                Tag = file
            };

            // 添加悬停效果
            border.MouseEnter += (s, e) =>
            {
                border.Background = (Brush)Application.Current.TryFindResource("SurfaceHoverBrush")
                    ?? new SolidColorBrush(Color.FromRgb(55, 55, 58));
            };
            border.MouseLeave += (s, e) =>
            {
                border.Background = (Brush)Application.Current.TryFindResource("SurfaceElevatedBrush")
                    ?? new SolidColorBrush(Color.FromRgb(45, 45, 48));
            };

            // 点击事件
            border.MouseLeftButtonDown += VersionCard_Click;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 版本信息
            var infoPanel = new StackPanel();

            // 文件名
            var fileName = new TextBlock
            {
                Text = file.DisplayName,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
            };
            infoPanel.Children.Add(fileName);

            // 版本标签
            var tagsPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // 发布时间
            var dateText = new TextBlock
            {
                Text = $"{file.FileDate:yyyy-MM-dd HH:mm}",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 15, 0)
            };
            tagsPanel.Children.Add(dateText);

            // 支持的游戏版本（只显示纯Minecraft版本）
            if (file.GameVersions != null && file.GameVersions.Count > 0)
            {
                var mcVersions = VersionUtils.ExtractAllMinecraftVersions(file.GameVersions);
                if (mcVersions.Count > 0)
                {
                    var versionDisplay = mcVersions.Count <= 3 
                        ? string.Join(", ", mcVersions) 
                        : $"{mcVersions[0]} ~ {mcVersions[mcVersions.Count - 1]}";
                    
                    var versionsText = new TextBlock
                    {
                        Text = $"适用: {versionDisplay}",
                        FontSize = 13,
                        Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 15, 0)
                    };
                    tagsPanel.Children.Add(versionsText);
                }
            }

            // 文件大小
            var sizeText = new TextBlock
            {
                Text = FormatFileSize(file.FileLength),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                VerticalAlignment = VerticalAlignment.Center
            };
            tagsPanel.Children.Add(sizeText);

            infoPanel.Children.Add(tagsPanel);

            Grid.SetColumn(infoPanel, 0);
            grid.Children.Add(infoPanel);

            // 下载图标
            var downloadIcon = new PackIcon
            {
                Kind = PackIconKind.Download,
                Width = 24,
                Height = 24,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.TryFindResource("PrimaryBrush")
                    ?? new SolidColorBrush(Color.FromRgb(33, 150, 243))
            };

            Grid.SetColumn(downloadIcon, 1);
            grid.Children.Add(downloadIcon);

            border.Child = grid;
            return border;
        }

        /// <summary>
        /// 版本卡片点击 - 显示下载确认对话框
        /// </summary>
        private async void VersionCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is CurseForgeFile file)
            {
                Debug.WriteLine($"[ModDetailPage] 选择版本: {file.DisplayName}");

                // 显示确认对话框
                var translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(_curseForgeMod?.Id ?? 0);
                var resourceDisplayName = ModTranslationService.Instance.GetDisplayName(_curseForgeMod?.Name ?? "", translation);
                
                var resourceTypeName = _resourceType switch
                {
                    "Mods" => "MOD",
                    "Textures" => "材质包",
                    "Shaders" => "光影包",
                    "Datapacks" => "数据包",
                    "Modpacks" => "整合包",
                    _ => "资源"
                };

                // 整合包需要先输入版本名称
                if (_resourceType == "Modpacks")
                {
                    var versionName = await DialogManager.Instance.ShowInputDialogAsync(
                        "安装整合包",
                        $"请为整合包命名：\n\n整合包: {resourceDisplayName}\n版本: {file.DisplayName}\n文件大小: {FormatFileSize(file.FileLength)}",
                        _curseForgeMod.Name);

                    if (!string.IsNullOrEmpty(versionName))
                    {
                        await DownloadAndInstallModpackAsync(file, versionName);
                    }
                }
                else
                {
                    var message = $"确认下载以下{resourceTypeName}吗？\n\n" +
                                $"名称: {resourceDisplayName}\n" +
                                $"版本: {file.DisplayName}\n" +
                                $"文件大小: {FormatFileSize(file.FileLength)}";
                    
                    // 数据包不显示安装位置
                    if (_resourceType != "Datapacks")
                    {
                        message += $"\n\n将安装到: {_selectedVersionId}";
                    }

                    var result = await DialogManager.Instance.ShowConfirmDialogAsync("确认下载", message);

                    if (result)
                    {
                        await DownloadModFileAsync(file);
                    }
                }
            }
        }

        /// <summary>
        /// 下载MOD文件
        /// </summary>
        private async System.Threading.Tasks.Task DownloadModFileAsync(CurseForgeFile file)
        {
            CancellationTokenSource? cts = null;
            string? taskId = null;
            
            try
            {
                var config = LauncherConfig.Load();
                string targetFolder;
                string savePath;

                // 根据资源类型确定下载目录
                if (_resourceType == "Datapacks" || _resourceType == "Modpacks")
                {
                    // 数据包和整合包：弹出文件选择对话框
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        FileName = file.FileName,
                        DefaultExt = Path.GetExtension(file.FileName),
                        Filter = _resourceType == "Datapacks" ? "数据包文件 (*.zip)|*.zip|所有文件 (*.*)|*.*" : "整合包文件 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                        // 数据包和整合包都默认打开对应版本文件夹
                        InitialDirectory = config.GetRunDirectory(_selectedVersionId)
                    };

                    if (dialog.ShowDialog() != true)
                    {
                        return; // 用户取消
                    }

                    savePath = dialog.FileName;
                    targetFolder = Path.GetDirectoryName(savePath) ?? "";
                }
                else
                {
                    // Mods、材质包、光影：自动下载到对应目录
                    targetFolder = _resourceType switch
                    {
                        "Mods" => config.GetModsDirectory(_selectedVersionId),
                        "Textures" => config.GetResourcePacksDirectory(_selectedVersionId),
                        "Shaders" => config.GetShaderPacksDirectory(_selectedVersionId),
                        _ => config.GetModsDirectory(_selectedVersionId)
                    };

                    Debug.WriteLine($"[ModDetailPage] 下载路径: {targetFolder}");

                    if (!Directory.Exists(targetFolder))
                    {
                        Directory.CreateDirectory(targetFolder);
                    }

                    savePath = Path.Combine(targetFolder, file.FileName);
                }

                var resourceName = _curseForgeMod?.Name ?? "资源";
                
                // 创建取消令牌并添加到下载管理器
                cts = new CancellationTokenSource();
                var downloadTask = DownloadTaskManager.Instance.AddTask(resourceName, DownloadTaskType.Resource, cts);
                taskId = downloadTask.Id;
                
                NotificationManager.Instance.ShowNotification("下载", $"开始下载: {resourceName}", NotificationType.Info);

                // 优化进度报告，避免频繁更新导致卡顿
                int lastReportedPercent = -1;
                var progress = new Progress<int>(percent =>
                {
                    // 每5%或达到100%时才报告一次，并更新下载管理器
                    if (percent >= 100 || percent - lastReportedPercent >= 5)
                    {
                        if (taskId != null)
                        {
                            DownloadTaskManager.Instance.UpdateTaskProgress(taskId, percent, file.FileName);
                        }
                        lastReportedPercent = percent;
                    }
                });

                var success = await CurseForgeService.DownloadModFileAsync(file, savePath, progress, cts.Token);

                if (cts.Token.IsCancellationRequested)
                {
                    Debug.WriteLine($"[ModDetailPage] 下载已取消: {resourceName}");
                    if (taskId != null)
                    {
                        DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 0, "已取消");
                    }
                    // 删除未完成的文件
                    if (File.Exists(savePath))
                    {
                        try { File.Delete(savePath); } catch { }
                    }
                    return;
                }

                if (success)
                {
                    if (taskId != null)
                    {
                        DownloadTaskManager.Instance.CompleteTask(taskId);
                    }
                    NotificationManager.Instance.ShowNotification("完成", $"下载完成: {resourceName}", NotificationType.Success, 5);
                    Debug.WriteLine($"[ModDetailPage] 下载成功: {savePath}");
                }
                else
                {
                    if (taskId != null)
                    {
                        DownloadTaskManager.Instance.FailTask(taskId, "下载失败");
                    }
                    NotificationManager.Instance.ShowNotification("错误", $"下载失败: {resourceName}", NotificationType.Error);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[ModDetailPage] 下载已取消");
                if (taskId != null)
                {
                    DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 0, "已取消");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModDetailPage] 下载失败: {ex.Message}");
                if (taskId != null)
                {
                    DownloadTaskManager.Instance.FailTask(taskId, ex.Message);
                }
                NotificationManager.Instance.ShowNotification("错误", $"下载失败: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                cts?.Dispose();
            }
        }

        /// <summary>
        /// 格式化文件大小
        /// </summary>
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void ShowLoading()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            VersionListPanel.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Collapsed;
        }

        private void HideLoading()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            VersionListPanel.Visibility = Visibility.Visible;
        }

        private void ShowEmpty()
        {
            EmptyPanel.Visibility = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Collapsed;
            VersionListPanel.Visibility = Visibility.Collapsed;
        }

        private void HideEmpty()
        {
            EmptyPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 显示Modrinth版本列表（按Minecraft版本分组）
        /// </summary>
        private void DisplayModrinthVersions(List<ModrinthVersion> versions)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                VersionListPanel.Children.Clear();
                HideLoading();
                HideEmpty();

                // 按游戏版本分组
                var groupedFiles = new Dictionary<string, List<ModrinthVersion>>();
                
                foreach (var version in versions)
                {
                    if (version.GameVersions == null || version.GameVersions.Count == 0)
                    {
                        if (!groupedFiles.ContainsKey("未知版本"))
                            groupedFiles["未知版本"] = new List<ModrinthVersion>();
                        groupedFiles["未知版本"].Add(version);
                        continue;
                    }

                    // 提取所有纯Minecraft版本号
                    var minecraftVersions = VersionUtils.ExtractAllMinecraftVersions(version.GameVersions);
                    
                    if (minecraftVersions.Count == 0)
                    {
                        if (!groupedFiles.ContainsKey("其他版本"))
                            groupedFiles["其他版本"] = new List<ModrinthVersion>();
                        groupedFiles["其他版本"].Add(version);
                        continue;
                    }
                    
                    // 将文件添加到它支持的每个版本组
                    foreach (var mcVersion in minecraftVersions)
                    {
                        if (!groupedFiles.ContainsKey(mcVersion))
                            groupedFiles[mcVersion] = new List<ModrinthVersion>();
                        
                        groupedFiles[mcVersion].Add(version);
                    }
                }

                // 对版本进行排序（最新版本在前）
                var sortedVersions = groupedFiles.Keys
                    .OrderByDescending(v => v, new Utils.MinecraftVersionComparer())
                    .ToList();

                // 为每个版本组创建展开器
                for (int i = 0; i < sortedVersions.Count; i++)
                {
                    var mcVersion = sortedVersions[i];
                    var filesInGroup = groupedFiles[mcVersion];
                    var isLatest = i == 0 && mcVersion != "未知版本" && mcVersion != "其他版本"; // 第一个且不是特殊组才标记为最新

                    var expander = CreateModrinthVersionExpander(mcVersion, filesInGroup, isLatest);
                    VersionListPanel.Children.Add(expander);
                }
            }));
        }

        /// <summary>
        /// 创建Modrinth版本组展开器
        /// </summary>
        private Expander CreateModrinthVersionExpander(string version, List<ModrinthVersion> files, bool isLatest)
        {
            var expander = new Expander
            {
                IsExpanded = isLatest, // 只展开最新版本
                Margin = new Thickness(0, 0, 0, 6),
                Background = (Brush)Application.Current.TryFindResource("SurfaceElevatedBrush")
                    ?? new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                Foreground = (Brush)Application.Current.TryFindResource("TextPrimaryBrush")
                    ?? new SolidColorBrush(Color.FromRgb(255, 255, 255))
            };

            // 标题
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = $"Minecraft {version}",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleText, 0);
            headerGrid.Children.Add(titleText);

            var countText = new TextBlock
            {
                Text = $"{files.Count} 个版本",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(countText, 1);
            headerGrid.Children.Add(countText);

            expander.Header = headerGrid;

            // 内容：文件列表
            var contentPanel = new StackPanel { Margin = new Thickness(6, 2, 6, 6) };
            
            foreach (var file in files)
            {
                var versionCard = CreateModrinthVersionCard(file);
                contentPanel.Children.Add(versionCard);
            }

            expander.Content = contentPanel;
            return expander;
        }

        /// <summary>
        /// 创建Modrinth版本卡片
        /// </summary>
        private Border CreateModrinthVersionCard(ModrinthVersion version)
        {
            var border = new Border
            {
                Background = (Brush)Application.Current.TryFindResource("SurfaceElevatedBrush")
                    ?? new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                Tag = version
            };

            border.MouseLeftButtonDown += ModrinthVersionCard_Click;

            border.MouseEnter += (s, e) =>
            {
                border.Background = (Brush)Application.Current.TryFindResource("SurfaceHoverBrush")
                    ?? new SolidColorBrush(Color.FromRgb(55, 55, 58));
            };
            border.MouseLeave += (s, e) =>
            {
                border.Background = (Brush)Application.Current.TryFindResource("SurfaceElevatedBrush")
                    ?? new SolidColorBrush(Color.FromRgb(45, 45, 48));
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 版本信息
            var infoPanel = new StackPanel();

            var nameText = new TextBlock
            {
                Text = version.Name,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
            };
            infoPanel.Children.Add(nameText);

            // 标签
            var tagsPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var dateText = new TextBlock
            {
                Text = version.DatePublished.ToString("yyyy-MM-dd"),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 15, 0)
            };
            tagsPanel.Children.Add(dateText);

            // 显示支持的游戏版本
            var mcVersions = VersionUtils.ExtractAllMinecraftVersions(version.GameVersions);
            if (mcVersions.Count > 0)
            {
                var versionDisplay = mcVersions.Count <= 3 
                    ? string.Join(", ", mcVersions) 
                    : $"{mcVersions[0]} ~ {mcVersions[mcVersions.Count - 1]}";
                
                var versionsText = new TextBlock
                {
                    Text = $"适用: {versionDisplay}",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 15, 0)
                };
                tagsPanel.Children.Add(versionsText);
            }

            // 文件大小
            if (version.Files.Count > 0)
            {
                var sizeText = new TextBlock
                {
                    Text = FormatFileSize(version.Files[0].Size),
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                tagsPanel.Children.Add(sizeText);
            }

            infoPanel.Children.Add(tagsPanel);

            Grid.SetColumn(infoPanel, 0);
            grid.Children.Add(infoPanel);

            // 下载图标
            var downloadIcon = new PackIcon
            {
                Kind = PackIconKind.Download,
                Width = 24,
                Height = 24,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.TryFindResource("PrimaryBrush")
                    ?? new SolidColorBrush(Color.FromRgb(33, 150, 243))
            };

            Grid.SetColumn(downloadIcon, 1);
            grid.Children.Add(downloadIcon);

            border.Child = grid;
            return border;
        }

        /// <summary>
        /// Modrinth版本卡片点击 - 显示下载确认对话框
        /// </summary>
        private async void ModrinthVersionCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is ModrinthVersion version)
            {
                Debug.WriteLine($"[ModDetailPage] 选择Modrinth版本: {version.Name}");

                if (version.Files.Count == 0)
                {
                    NotificationManager.Instance.ShowNotification("错误", "该版本没有可下载的文件", NotificationType.Error);
                    return;
                }

                var file = version.Files[0]; // 使用第一个文件

                // 显示确认对话框
                var translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(_modrinthMod!.Slug);
                if (translation == null)
                {
                    translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(_modrinthMod.ProjectId);
                }
                var resourceDisplayName = ModTranslationService.Instance.GetDisplayName(_modrinthMod.Title, translation);
                
                var resourceTypeName = _resourceType switch
                {
                    "Mods" => "MOD",
                    "Textures" => "材质包",
                    "Shaders" => "光影包",
                    "Datapacks" => "数据包",
                    "Modpacks" => "整合包",
                    _ => "资源"
                };

                // 整合包需要先输入版本名称
                if (_resourceType == "Modpacks")
                {
                    var versionName = await DialogManager.Instance.ShowInputDialogAsync(
                        "安装整合包",
                        $"请为整合包命名：\n\n整合包: {resourceDisplayName}\n版本: {version.Name}\n文件大小: {FormatFileSize(file.Size)}",
                        _modrinthMod.Title);

                    if (!string.IsNullOrEmpty(versionName))
                    {
                        await DownloadAndInstallModrinthModpackAsync(version, file, versionName);
                    }
                }
                else
                {
                    var message = $"确认下载以下{resourceTypeName}吗？\n\n" +
                                $"名称: {resourceDisplayName}\n" +
                                $"版本: {version.Name}\n" +
                                $"文件大小: {FormatFileSize(file.Size)}";
                    
                    // 数据包不显示安装位置
                    if (_resourceType != "Datapacks")
                    {
                        message += $"\n\n将安装到: {_selectedVersionId}";
                    }

                    var result = await DialogManager.Instance.ShowConfirmDialogAsync("确认下载", message);

                    if (result)
                    {
                        await DownloadModrinthFileAsync(version, file);
                    }
                }
            }
        }

        /// <summary>
        /// 下载Modrinth MOD文件
        /// </summary>
        private async System.Threading.Tasks.Task DownloadModrinthFileAsync(ModrinthVersion version, ModrinthVersionFile file)
        {
            CancellationTokenSource? cts = null;
            string? taskId = null;
            
            try
            {
                var config = LauncherConfig.Load();
                string targetFolder;
                string savePath;

                // 根据资源类型确定下载目录
                if (_resourceType == "Datapacks" || _resourceType == "Modpacks")
                {
                    // 数据包和整合包：弹出文件选择对话框
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        FileName = file.Filename,
                        DefaultExt = Path.GetExtension(file.Filename),
                        Filter = _resourceType == "Datapacks" ? "数据包文件 (*.zip)|*.zip|所有文件 (*.*)|*.*" : "整合包文件 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                        // 数据包和整合包都默认打开对应版本文件夹
                        InitialDirectory = config.GetRunDirectory(_selectedVersionId)
                    };

                    if (dialog.ShowDialog() != true)
                    {
                        return; // 用户取消
                    }

                    savePath = dialog.FileName;
                    targetFolder = Path.GetDirectoryName(savePath) ?? "";
                }
                else
                {
                    // Mods、材质包、光影：自动下载到对应目录
                    targetFolder = _resourceType switch
                    {
                        "Mods" => config.GetModsDirectory(_selectedVersionId),
                        "Textures" => config.GetResourcePacksDirectory(_selectedVersionId),
                        "Shaders" => config.GetShaderPacksDirectory(_selectedVersionId),
                        _ => config.GetModsDirectory(_selectedVersionId)
                    };

                    Debug.WriteLine($"[ModDetailPage] 下载路径: {targetFolder}");

                    if (!Directory.Exists(targetFolder))
                    {
                        Directory.CreateDirectory(targetFolder);
                    }

                    savePath = Path.Combine(targetFolder, file.Filename);
                }

                var resourceName = _modrinthMod?.Title ?? "资源";
                
                // 创建取消令牌并添加到下载管理器
                cts = new CancellationTokenSource();
                var downloadTask = DownloadTaskManager.Instance.AddTask(resourceName, DownloadTaskType.Resource, cts);
                taskId = downloadTask.Id;
                
                NotificationManager.Instance.ShowNotification("下载", $"开始下载: {resourceName}", NotificationType.Info);

                // 优化进度报告，避免频繁更新导致卡顿
                int lastReportedPercent = -1;
                var progress = new Progress<int>(percent =>
                {
                    // 每5%或达到100%时才报告一次，并更新下载管理器
                    if (percent >= 100 || percent - lastReportedPercent >= 5)
                    {
                        if (taskId != null)
                        {
                            DownloadTaskManager.Instance.UpdateTaskProgress(taskId, percent, file.Filename);
                        }
                        lastReportedPercent = percent;
                    }
                });

                var modrinthService = new ModrinthService(DownloadSourceManager.Instance);
                var success = await modrinthService.DownloadModFileAsync(file, savePath, progress, cts.Token);

                if (cts.Token.IsCancellationRequested)
                {
                    Debug.WriteLine($"[ModDetailPage] 下载已取消: {resourceName}");
                    if (taskId != null)
                    {
                        DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 0, "已取消");
                    }
                    // 删除未完成的文件
                    if (File.Exists(savePath))
                    {
                        try { File.Delete(savePath); } catch { }
                    }
                    return;
                }

                if (success)
                {
                    if (taskId != null)
                    {
                        DownloadTaskManager.Instance.CompleteTask(taskId);
                    }
                    NotificationManager.Instance.ShowNotification("完成", $"下载完成: {resourceName}", NotificationType.Success, 5);
                    Debug.WriteLine($"[ModDetailPage] 下载成功: {savePath}");
                }
                else
                {
                    if (taskId != null)
                    {
                        DownloadTaskManager.Instance.FailTask(taskId, "下载失败");
                    }
                    NotificationManager.Instance.ShowNotification("失败", $"下载失败: {resourceName}", NotificationType.Error);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[ModDetailPage] 下载已取消");
                if (taskId != null)
                {
                    DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 0, "已取消");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModDetailPage] 下载失败: {ex.Message}");
                if (taskId != null)
                {
                    DownloadTaskManager.Instance.FailTask(taskId, ex.Message);
                }
                NotificationManager.Instance.ShowNotification("错误", $"下载失败: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                cts?.Dispose();
            }
        }

        /// <summary>
        /// 显示底部加载指示器
        /// </summary>
        private void ShowBottomLoading(int current, int total)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (BottomLoadingPanel != null)
                {
                    BottomLoadingPanel.Visibility = Visibility.Visible;
                    
                    if (total > 0)
                    {
                        var percentage = (int)((double)current / total * 100);
                        BottomLoadingProgressBar.Value = percentage;
                        BottomLoadingPercentage.Text = $"{percentage}%";
                        BottomLoadingText.Text = $"正在加载版本... ({current}/{total})";
                    }
                    else
                    {
                        BottomLoadingProgressBar.Value = 0;
                        BottomLoadingPercentage.Text = "0%";
                        BottomLoadingText.Text = "正在加载版本...";
                    }
                }
            }));
        }

        /// <summary>
        /// 更新底部加载进度
        /// </summary>
        private void UpdateBottomLoading(int current, int total)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (BottomLoadingPanel != null && total > 0)
                {
                    var percentage = (int)((double)current / total * 100);
                    BottomLoadingProgressBar.Value = percentage;
                    BottomLoadingPercentage.Text = $"{percentage}%";
                    BottomLoadingText.Text = $"正在加载版本... ({current}/{total})";
                }
            }));
        }

        /// <summary>
        /// 隐藏底部加载指示器
        /// </summary>
        private void HideBottomLoading()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (BottomLoadingPanel != null)
                {
                    BottomLoadingPanel.Visibility = Visibility.Collapsed;
                }
            }));
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService != null && NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
        }

        /// <summary>
        /// 下载并安装CurseForge整合包
        /// </summary>
        private async System.Threading.Tasks.Task DownloadAndInstallModpackAsync(CurseForgeFile file, string versionName)
        {
            CancellationTokenSource? cts = null;
            string? taskId = null;
            
            try
            {
                var config = LauncherConfig.Load();
                
                // 下载到 versions 目录
                var versionsDir = Path.Combine(config.GameDirectory, "versions");
                Directory.CreateDirectory(versionsDir);

                var tempZipPath = Path.Combine(versionsDir, file.FileName);

                // 创建取消令牌并添加到下载管理器
                cts = new CancellationTokenSource();
                var downloadTask = DownloadTaskManager.Instance.AddTask($"{_curseForgeMod?.Name} (整合包)", DownloadTaskType.Resource, cts);
                taskId = downloadTask.Id;
                
                NotificationManager.Instance.ShowNotification("下载", $"正在下载整合包: {_curseForgeMod?.Name}", NotificationType.Info);

                // 下载整合包文件 (0-50%)
                Debug.WriteLine($"[ModDetailPage] 开始下载整合包: {file.FileName} 到 {tempZipPath}");

                int lastReportedPercent = -1;
                var progress = new Progress<int>(percent =>
                {
                    if (percent >= 100 || percent - lastReportedPercent >= 5)
                    {
                        // 下载占总进度的50%
                        var totalPercent = percent / 2.0;
                        if (taskId != null)
                        {
                            DownloadTaskManager.Instance.UpdateTaskProgress(taskId, totalPercent, "正在下载...");
                        }
                        lastReportedPercent = percent;
                    }
                });

                var downloadSuccess = await CurseForgeService.DownloadModFileAsync(file, tempZipPath, progress, cts.Token);

                if (cts.Token.IsCancellationRequested)
                {
                    Debug.WriteLine($"[ModDetailPage] 整合包下载已取消");
                    if (taskId != null)
                    {
                        DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 0, "已取消");
                    }
                    // 删除未完成的下载文件
                    if (File.Exists(tempZipPath))
                    {
                        try 
                        { 
                            File.Delete(tempZipPath);
                            Debug.WriteLine($"[ModDetailPage] 已删除未完成的下载文件: {tempZipPath}");
                        } 
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[ModDetailPage] 删除未完成文件失败: {ex.Message}");
                        }
                    }
                    return;
                }

                if (!downloadSuccess)
                {
                    if (taskId != null)
                    {
                        DownloadTaskManager.Instance.FailTask(taskId, "下载失败");
                    }
                    NotificationManager.Instance.ShowNotification("错误", "整合包下载失败", NotificationType.Error);
                    return;
                }

                Debug.WriteLine($"[ModDetailPage] 整合包下载完成，开始安装");

                // 安装整合包 (50-100%)
                NotificationManager.Instance.ShowNotification("安装", $"正在安装整合包: {versionName}", NotificationType.Info);

                var installSuccess = await ModpackInstallService.InstallModpackAsync(
                    tempZipPath,
                    versionName,
                    config.GameDirectory,
                    (status, percent) =>
                    {
                        // 安装占总进度的50%，所以要加上前50%的下载进度
                        var totalPercent = 50 + (percent / 2.0);
                        if (taskId != null)
                        {
                            DownloadTaskManager.Instance.UpdateTaskProgress(taskId, totalPercent, status);
                        }
                    });

                if (installSuccess)
                {
                    if (taskId != null)
                    {
                        DownloadTaskManager.Instance.CompleteTask(taskId);
                    }
                    NotificationManager.Instance.ShowNotification("完成", $"整合包安装成功: {versionName}", NotificationType.Success, 5);
                    Debug.WriteLine($"[ModDetailPage] 整合包安装成功: {versionName}");

                    // 清理整合包文件
                    try
                    {
                        File.Delete(tempZipPath);
                        Debug.WriteLine($"[ModDetailPage] 已删除整合包文件: {tempZipPath}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ModDetailPage] 删除整合包文件失败: {ex.Message}");
                    }
                }
                else
                {
                    if (taskId != null)
                    {
                        DownloadTaskManager.Instance.FailTask(taskId, "安装失败");
                    }
                    NotificationManager.Instance.ShowNotification("错误", "整合包安装失败", NotificationType.Error);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[ModDetailPage] 整合包安装已取消");
                if (taskId != null)
                {
                    DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 0, "已取消");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModDetailPage] 整合包安装失败: {ex.Message}");
                if (taskId != null)
                {
                    DownloadTaskManager.Instance.FailTask(taskId, ex.Message);
                }
                NotificationManager.Instance.ShowNotification("错误", $"整合包安装失败: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                cts?.Dispose();
            }
        }

        /// <summary>
        /// 下载并安装Modrinth整合包
        /// </summary>
        private async System.Threading.Tasks.Task DownloadAndInstallModrinthModpackAsync(ModrinthVersion version, ModrinthVersionFile file, string versionName)
        {
            CancellationTokenSource? cts = null;
            string? taskId = null;
            
            try
            {
                var config = LauncherConfig.Load();
                
                // 下载到 versions 目录
                var versionsDir = Path.Combine(config.GameDirectory, "versions");
                Directory.CreateDirectory(versionsDir);

                var tempZipPath = Path.Combine(versionsDir, file.Filename);

                // 创建取消令牌并添加到下载管理器
                cts = new CancellationTokenSource();
                var downloadTask = DownloadTaskManager.Instance.AddTask($"{_modrinthMod?.Title} (整合包)", DownloadTaskType.Resource, cts);
                taskId = downloadTask.Id;
                
                NotificationManager.Instance.ShowNotification("下载", $"正在下载整合包: {_modrinthMod?.Title}", NotificationType.Info);

                // 下载整合包文件 (0-50%)
                Debug.WriteLine($"[ModDetailPage] 开始下载Modrinth整合包: {file.Filename} 到 {tempZipPath}");

                var modrinthService = new ModrinthService(DownloadSourceManager.Instance);

                int lastReportedPercent = -1;
                var progress = new Progress<int>(percent =>
                {
                    if (percent >= 100 || percent - lastReportedPercent >= 5)
                    {
                        // 下载占总进度的50%
                        var totalPercent = percent / 2.0;
                        if (taskId != null)
                        {
                            DownloadTaskManager.Instance.UpdateTaskProgress(taskId, totalPercent, "正在下载...");
                        }
                        lastReportedPercent = percent;
                    }
                });

                var downloadSuccess = await modrinthService.DownloadModFileAsync(file, tempZipPath, progress, cts.Token);

                if (cts.Token.IsCancellationRequested)
                {
                    Debug.WriteLine($"[ModDetailPage] Modrinth整合包下载已取消");
                    if (taskId != null)
                    {
                        DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 0, "已取消");
                    }
                    // 删除未完成的下载文件
                    if (File.Exists(tempZipPath))
                    {
                        try 
                        { 
                            File.Delete(tempZipPath);
                            Debug.WriteLine($"[ModDetailPage] 已删除未完成的下载文件: {tempZipPath}");
                        } 
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[ModDetailPage] 删除未完成文件失败: {ex.Message}");
                        }
                    }
                    return;
                }

                if (!downloadSuccess)
                {
                    if (taskId != null)
                    {
                        DownloadTaskManager.Instance.FailTask(taskId, "下载失败");
                    }
                    NotificationManager.Instance.ShowNotification("错误", "整合包下载失败", NotificationType.Error);
                    return;
                }

                Debug.WriteLine($"[ModDetailPage] Modrinth整合包下载完成，开始安装");

                // 安装整合包 (50-100%)
                NotificationManager.Instance.ShowNotification("安装", $"正在安装整合包: {versionName}", NotificationType.Info);

                var installSuccess = await ModpackInstallService.InstallModpackAsync(
                    tempZipPath,
                    versionName,
                    config.GameDirectory,
                    (status, percent) =>
                    {
                        // 安装占总进度的50%，所以要加上前50%的下载进度
                        var totalPercent = 50 + (percent / 2.0);
                        if (taskId != null)
                        {
                            DownloadTaskManager.Instance.UpdateTaskProgress(taskId, totalPercent, status);
                        }
                    });

                if (installSuccess)
                {
                    if (taskId != null)
                    {
                        DownloadTaskManager.Instance.CompleteTask(taskId);
                    }
                    NotificationManager.Instance.ShowNotification("完成", $"整合包安装成功: {versionName}", NotificationType.Success, 5);
                    Debug.WriteLine($"[ModDetailPage] Modrinth整合包安装成功: {versionName}");

                    // 清理整合包文件
                    try
                    {
                        File.Delete(tempZipPath);
                        Debug.WriteLine($"[ModDetailPage] 已删除整合包文件: {tempZipPath}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ModDetailPage] 删除整合包文件失败: {ex.Message}");
                    }
                }
                else
                {
                    if (taskId != null)
                    {
                        DownloadTaskManager.Instance.FailTask(taskId, "安装失败");
                    }
                    NotificationManager.Instance.ShowNotification("错误", "整合包安装失败", NotificationType.Error);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[ModDetailPage] Modrinth整合包安装已取消");
                if (taskId != null)
                {
                    DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 0, "已取消");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModDetailPage] Modrinth整合包安装失败: {ex.Message}");
                if (taskId != null)
                {
                    DownloadTaskManager.Instance.FailTask(taskId, ex.Message);
                }
                NotificationManager.Instance.ShowNotification("错误", $"整合包安装失败: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                cts?.Dispose();
            }
        }
    }
}

