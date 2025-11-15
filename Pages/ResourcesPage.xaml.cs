using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    public partial class ResourcesPage : Page
    {
        // 单个资源类型的状态
        private class ResourceTypeState
        {
            public string SearchKeyword { get; set; } = "";
            public int CurrentPage { get; set; } = 0;
            public List<CurseForgeMod>? CachedCurseForgeMods { get; set; } = null;
            public List<ModrinthMod>? CachedModrinthMods { get; set; } = null;
        }

        // 页面状态保存
        private static class PageState
        {
            public static ResourceSource CurrentSource { get; set; } = ResourceSource.CurseForge;
            public static string CurrentResourceType { get; set; } = "Mods";
            public static string? SelectedVersionId { get; set; } = null;
            public static int SelectedSourceIndex { get; set; } = 0;
            public static int SelectedSortIndex { get; set; } = 0;
            
            // 每个资源类型的独立状态
            public static Dictionary<string, ResourceTypeState> ResourceStates { get; set; } = new();
        }

        private ResourceSource _currentSource = ResourceSource.CurseForge;
        private string _currentResourceType = "Mods";  // 默认为Mods
        private int _currentPage = 0;
        private const int PAGE_SIZE = 20;
        private string? _selectedVersionId;
        private bool _isRestoringState = false;

        public ResourcesPage()
        {
            InitializeComponent();
            
            // 在 Loaded 事件中初始化 UI 状态
            Loaded += ResourcesPage_Loaded;
            Unloaded += ResourcesPage_Unloaded;
        }

        private void ResourcesPage_Unloaded(object sender, RoutedEventArgs e)
        {
            // 清理事件订阅，防止内存泄漏
            if (InstalledVersionComboBox != null)
            {
                InstalledVersionComboBox.SelectionChanged -= InstalledVersionComboBox_SelectionChanged;
            }
            if (SourceComboBox != null)
            {
                SourceComboBox.SelectionChanged -= SourceComboBox_SelectionChanged;
            }
            
            // 触发图片缓存清理
            ImageCacheManager.CleanupCache();
        }

        private void ResourcesPage_Loaded(object sender, RoutedEventArgs e)
        {
            // 加载已安装的版本列表
            LoadInstalledVersions();
            
            // 恢复页面状态
            RestorePageState();
            
            // 移除预加载逻辑以降低内存占用
            // 资源将在用户切换标签时按需加载
        }

        // 预加载功能已移除以降低内存占用
        // 资源将在用户切换标签时按需加载

        /// <summary>
        /// 加载已安装的版本列表
        /// </summary>
        private void LoadInstalledVersions()
        {
            try
            {
                // 清空现有项目，避免重复添加
                InstalledVersionComboBox.Items.Clear();
                _selectedVersionId = null;
                
                var config = LauncherConfig.Load();
                var versionsDir = Path.Combine(config.GameDirectory, "versions");
                
                if (!Directory.Exists(versionsDir))
                {
                    InstalledVersionComboBox.Items.Add(new ComboBoxItem 
                    { 
                        Content = "未找到已安装的版本", 
                        IsEnabled = false,
                        Tag = null
                    });
                    InstalledVersionComboBox.SelectedIndex = 0;
                    return;
                }
                
                var versionDirs = Directory.GetDirectories(versionsDir);
                var versionInfos = new List<(string name, string type)>();
                
                foreach (var versionDir in versionDirs)
                {
                    var versionName = Path.GetFileName(versionDir);
                    var versionJsonPath = Path.Combine(versionDir, $"{versionName}.json");
                    
                    if (File.Exists(versionJsonPath))
                    {
                        // 检测版本类型
                        var versionType = DetectVersionType(versionName);
                        versionInfos.Add((versionName, versionType));
                    }
                }
                
                // 排序：先按类型（支持mod的在前），再按版本名
                versionInfos = versionInfos
                    .OrderBy(v => v.type == "vanilla" || v.type == "optifine" ? 1 : 0)
                    .ThenBy(v => v.name)
                    .ToList();
                
                foreach (var (name, type) in versionInfos)
                {
                    var displayText = type switch
                    {
                        "vanilla" => $"{name} (原版)",
                        "optifine" => $"{name} (OptiFine)",
                        "forge" => $"{name} (Forge)",
                        "neoforge" => $"{name} (NeoForge)",
                        "fabric" => $"{name} (Fabric)",
                        "quilt" => $"{name} (Quilt)",
                        _ => name
                    };
                    
                    // 根据资源类型决定是否启用版本
                    bool isEnabled = true;
                    if (_currentResourceType == "Mods")
                    {
                        // MOD类型：只有模组加载器版本可用
                        isEnabled = type != "vanilla" && type != "optifine";
                    }
                    // 其他资源类型（材质包、光影、数据包、整合包）：所有版本都可用
                    
                    var item = new ComboBoxItem 
                    { 
                        Content = displayText, 
                        Tag = name,
                        IsEnabled = isEnabled
                    };
                    
                    InstalledVersionComboBox.Items.Add(item);
                }
                
                // 选择第一个可用的版本
                for (int i = 0; i < InstalledVersionComboBox.Items.Count; i++)
                {
                    if (InstalledVersionComboBox.Items[i] is ComboBoxItem item && item.IsEnabled)
                    {
                        InstalledVersionComboBox.SelectedIndex = i;
                        _selectedVersionId = item.Tag as string;
                        Debug.WriteLine($"[ResourcesPage] 默认选择版本: {_selectedVersionId}");
                        break;
                    }
                }
                
                if (InstalledVersionComboBox.SelectedIndex == -1 && InstalledVersionComboBox.Items.Count > 0)
                {
                    InstalledVersionComboBox.SelectedIndex = 0;
                    // 注意：这里选中的可能是不支持mod的版本，_selectedVersionId保持为null
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ResourcesPage] 加载版本列表失败: {ex.Message}");
                InstalledVersionComboBox.Items.Add(new ComboBoxItem 
                { 
                    Content = "加载失败", 
                    IsEnabled = false 
                });
                InstalledVersionComboBox.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// 检测版本类型
        /// </summary>
        private string DetectVersionType(string versionName)
        {
            var lowerName = versionName.ToLower();
            
            if (lowerName.Contains("forge") && !lowerName.Contains("neoforge"))
                return "forge";
            if (lowerName.Contains("neoforge"))
                return "neoforge";
            if (lowerName.Contains("fabric"))
                return "fabric";
            if (lowerName.Contains("quilt"))
                return "quilt";
            if (lowerName.Contains("optifine"))
                return "optifine";
            
            // 原版检测：不包含任何加载器标识
            return "vanilla";
        }

        /// <summary>
        /// 版本选择改变事件
        /// </summary>
        private void InstalledVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRestoringState) return; // 恢复状态时不处理
            
            if (InstalledVersionComboBox.SelectedItem is ComboBoxItem item)
            {
                if (item.Tag is string versionId)
                {
                    _selectedVersionId = versionId;
                    Debug.WriteLine($"[ResourcesPage] 选择的版本: {versionId}");
                    SavePageState(); // 保存选择
                }
                else
                {
                    // 选中的是不可用项（如"未找到已安装的版本"）
                    _selectedVersionId = null;
                    Debug.WriteLine($"[ResourcesPage] 未选择有效版本");
                }
            }
        }

        private void ResourceTab_Click(object sender, RoutedEventArgs e)
        {
            // 防止在初始化期间触发
            if (!IsLoaded || ResourceListPanel == null) return;
            
            if (sender is RadioButton button && button.Tag is string tag)
            {
                // 如果是同一个资源类型，不需要切换
                if (tag == _currentResourceType) return;
                
                // 保存当前资源类型的状态
                SaveCurrentResourceTypeState();
                
                // 切换资源类型
                _currentResourceType = tag;
                PageState.CurrentResourceType = tag;
                
                Debug.WriteLine($"[ResourcesPage] 切换到资源类型: {tag}");
                
                // 立即清空列表，避免显示旧内容
                ResourceListPanel.Children.Clear();
                
                // 重新加载版本列表（因为不同资源类型对版本的启用规则不同）
                LoadInstalledVersions();
                
                // 根据资源类型更新版本选择器的可见性和启用状态
                UpdateVersionComboBoxVisibility();
                
                // 恢复该资源类型的状态
                RestoreCurrentResourceTypeState();
            }
        }

        /// <summary>
        /// 保存当前资源类型的状态
        /// </summary>
        private void SaveCurrentResourceTypeState()
        {
            if (!PageState.ResourceStates.ContainsKey(_currentResourceType))
            {
                PageState.ResourceStates[_currentResourceType] = new ResourceTypeState();
            }

            var state = PageState.ResourceStates[_currentResourceType];
            state.SearchKeyword = SearchBox?.Text?.Trim() ?? "";
            state.CurrentPage = _currentPage;
            
            // 保存当前显示的资源列表
            if (_currentSource == ResourceSource.CurseForge)
            {
                // 从UI提取CurseForge资源
                state.CachedCurseForgeMods = ResourceListPanel.Children
                    .OfType<Border>()
                    .Select(b => b.Tag as CurseForgeMod)
                    .Where(m => m != null)
                    .Cast<CurseForgeMod>()
                    .ToList();
            }
            else
            {
                // 从UI提取Modrinth资源
                state.CachedModrinthMods = ResourceListPanel.Children
                    .OfType<Border>()
                    .Select(b => b.Tag as ModrinthMod)
                    .Where(m => m != null)
                    .Cast<ModrinthMod>()
                    .ToList();
            }

            Debug.WriteLine($"[ResourcesPage] 保存 {_currentResourceType} 状态: {state.CachedCurseForgeMods?.Count ?? 0} CF资源, {state.CachedModrinthMods?.Count ?? 0} Modrinth资源");
        }

        /// <summary>
        /// 恢复当前资源类型的状态
        /// </summary>
        private void RestoreCurrentResourceTypeState()
        {
            if (!PageState.ResourceStates.ContainsKey(_currentResourceType))
            {
                // 首次访问该资源类型，加载默认数据
                Debug.WriteLine($"[ResourcesPage] {_currentResourceType} 首次访问，异步加载数据");
                _ = LoadDefaultResourcesAsync();
                return;
            }

            var state = PageState.ResourceStates[_currentResourceType];
            
            // 恢复搜索关键词
            if (SearchBox != null)
            {
                SearchBox.Text = state.SearchKeyword;
            }
            
            _currentPage = state.CurrentPage;
            
            // 恢复显示的资源列表（列表已在ResourceTab_Click中清空，所以这里不需要再清空）
            if (_currentSource == ResourceSource.CurseForge && state.CachedCurseForgeMods != null && state.CachedCurseForgeMods.Count > 0)
            {
                DisplayCurseForgeMods(state.CachedCurseForgeMods, clearList: false);
                Debug.WriteLine($"[ResourcesPage] 恢复 {_currentResourceType} CurseForge资源: {state.CachedCurseForgeMods.Count} 个");
            }
            else if (_currentSource == ResourceSource.Modrinth && state.CachedModrinthMods != null && state.CachedModrinthMods.Count > 0)
            {
                DisplayModrinthMods(state.CachedModrinthMods, clearList: false);
                Debug.WriteLine($"[ResourcesPage] 恢复 {_currentResourceType} Modrinth资源: {state.CachedModrinthMods.Count} 个");
            }
            else
            {
                // 缓存为空，重新加载
                Debug.WriteLine($"[ResourcesPage] {_currentResourceType} 缓存为空，异步加载数据");
                _ = LoadDefaultResourcesAsync();
            }
        }

        /// <summary>
        /// 异步加载当前资源类型的默认数据
        /// </summary>
        private async System.Threading.Tasks.Task LoadDefaultResourcesAsync()
        {
            ShowLoading();
            
            try
            {
                // 使用默认排序和无搜索关键词加载
                await SearchModsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ResourcesPage] 加载默认资源失败: {ex.Message}");
                ShowEmptyState();
            }
        }

        /// <summary>
        /// 更新版本选择器的可见性和启用状态
        /// </summary>
        private void UpdateVersionComboBoxVisibility()
        {
            if (InstalledVersionComboBox != null)
            {
                // 所有资源类型都显示版本选择器
                InstalledVersionComboBox.Visibility = Visibility.Visible;
                
                // 整合包类型下禁用版本选择（整合包自带版本）
                InstalledVersionComboBox.IsEnabled = _currentResourceType != "Modpacks";
            }
        }

        private void SourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 防止在初始化期间触发
            if (!IsLoaded || SourceComboBox == null) return;
            
            if (SourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                _currentSource = tag == "Modrinth" ? ResourceSource.Modrinth : ResourceSource.CurseForge;
                _currentPage = 0;
                
                Debug.WriteLine($"[ResourcesPage] 切换下载源: {_currentSource}");
                
                // 清空当前搜索结果
                if (ResourceListPanel != null)
                {
                    ResourceListPanel.Children.Clear();
                    ShowEmptyState();
                }
            }
        }

        private void FilterChanged(object sender, SelectionChangedEventArgs e)
        {
            // 防止在初始化期间触发
            if (!IsLoaded || ResourceListPanel == null) return;
            
            // 筛选条件变化时，如果已经有搜索结果，重新搜索
            if (ResourceListPanel.Children.Count > 0)
            {
                _ = SearchModsAsync();
            }
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _ = SearchModsAsync();
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            _ = SearchModsAsync();
        }

        /// <summary>
        /// 搜索MOD（支持中英文）
        /// </summary>
        private async System.Threading.Tasks.Task SearchModsAsync()
        {
            var searchText = SearchBox.Text?.Trim() ?? "";
            bool isChineseSearch = !string.IsNullOrEmpty(searchText) && ContainsChinese(searchText);
            
            // 获取版本筛选
            string gameVersion = "";
            if (VersionFilterComboBox.SelectedItem is ComboBoxItem versionItem && versionItem.Tag is string version)
            {
                gameVersion = version;
            }

            // 获取排序方式
            int sortField = 2; // 默认按人气排序
            if (SortComboBox.SelectedItem is ComboBoxItem sortItem && sortItem.Tag is string sortTag)
            {
                int.TryParse(sortTag, out sortField);
            }

            Debug.WriteLine($"[ResourcesPage] 搜索MOD - 来源: {_currentSource}, 关键词: '{searchText}', 中文搜索: {isChineseSearch}, 版本: '{gameVersion}', 排序: {sortField}");

            ShowLoading();

            try
            {
                if (_currentSource == ResourceSource.CurseForge)
                {
                    await SearchCurseForgeModsAsync(searchText, isChineseSearch, gameVersion, sortField);
                }
                else if (_currentSource == ResourceSource.Modrinth)
                {
                    await SearchModrinthModsAsync(searchText, isChineseSearch, gameVersion);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ResourcesPage] ❌ 搜索失败: {ex.Message}");
                NotificationManager.Instance.ShowNotification("错误", $"搜索失败: {ex.Message}", NotificationType.Error);
                ShowEmptyState();
            }
        }

        /// <summary>
        /// 检查字符串是否包含中文字符
        /// </summary>
        private bool ContainsChinese(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return text.Any(c => c >= 0x4E00 && c <= 0x9FA5);
        }

        /// <summary>
        /// 获取CurseForge资源类型的ClassId
        /// </summary>
        private int GetCurseForgeClassId(string resourceType)
        {
            return resourceType switch
            {
                "Mods" => 6,
                "Textures" => 12,        // Resource Packs
                "Shaders" => 6552,       // Shader Packs
                "Modpacks" => 4471,      // Modpacks
                "Datapacks" => 6945,     // Datapacks (需要确认)
                _ => 6  // 默认为Mods
            };
        }

        /// <summary>
        /// 获取Modrinth资源类型的ProjectType
        /// </summary>
        private string GetModrinthProjectType(string resourceType)
        {
            return resourceType switch
            {
                "Mods" => "mod",
                "Textures" => "resourcepack",
                "Shaders" => "shader",
                "Modpacks" => "modpack",
                "Datapacks" => "datapack",
                _ => "mod"  // 默认为mod
            };
        }

        /// <summary>
        /// 搜索CurseForge模组
        /// </summary>
        private async System.Threading.Tasks.Task SearchCurseForgeModsAsync(string searchText, bool isChineseSearch, string gameVersion, int sortField)
        {
            List<CurseForgeMod> mods;
            int classId = GetCurseForgeClassId(_currentResourceType);

            if (isChineseSearch)
            {
                // 中文搜索：先获取更多结果，然后在本地过滤
                Debug.WriteLine("[ResourcesPage] 执行CurseForge中文搜索，获取更多结果进行过滤");
                
                var result = await CurseForgeService.SearchModsAsync(
                    searchFilter: "",  // 不使用英文搜索词
                    gameVersion: gameVersion,
                    categoryId: 0,
                    pageIndex: 0,
                    pageSize: 50,  // 获取更多结果
                    sortField: sortField,
                    sortOrder: "desc",
                    classId: classId
                );

                if (result?.Data != null && result.Data.Count > 0)
                {
                    // 在本地使用翻译服务进行中文匹配
                    mods = result.Data.Where(mod =>
                    {
                        // 优先使用slug查找翻译，如果找不到再使用数字ID
                        var translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(mod.Slug);
                        if (translation == null)
                        {
                            translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(mod.Id);
                        }
                        return ModTranslationService.Instance.MatchesSearch(mod.Name, translation, searchText);
                    }).ToList();

                    Debug.WriteLine($"[ResourcesPage] 中文搜索匹配到 {mods.Count} 个MOD");
                }
                else
                {
                    mods = new List<CurseForgeMod>();
                }
            }
            else
            {
                // 英文搜索：直接使用API搜索
                var result = await CurseForgeService.SearchModsAsync(
                    searchFilter: searchText,
                    gameVersion: gameVersion,
                    categoryId: 0,
                    pageIndex: _currentPage,
                    pageSize: PAGE_SIZE,
                    sortField: sortField,
                    sortOrder: "desc",
                    classId: classId
                );

                mods = result?.Data ?? new List<CurseForgeMod>();
            }

            if (mods.Count > 0)
                {
                DisplayCurseForgeMods(mods);
                Debug.WriteLine($"[ResourcesPage] ✅ 显示 {mods.Count} 个CurseForge MOD");
                }
                else
                {
                    ShowNoResults();
                Debug.WriteLine($"[ResourcesPage] ⚠️ 没有找到匹配的CurseForge MOD");
            }
        }

        /// <summary>
        /// 搜索Modrinth模组
        /// </summary>
        private async System.Threading.Tasks.Task SearchModrinthModsAsync(string searchText, bool isChineseSearch, string gameVersion)
        {
            var modrinthService = new ModrinthService(DownloadSourceManager.Instance);
            string projectType = GetModrinthProjectType(_currentResourceType);
            
            if (isChineseSearch)
            {
                // 中文搜索：先获取更多结果，然后在本地过滤
                Debug.WriteLine("[ResourcesPage] 执行Modrinth中文搜索，获取更多结果进行过滤");
                
                var result = await modrinthService.SearchModsAsync(
                    searchQuery: "",
                    gameVersion: gameVersion,
                    offset: 0,
                    limit: 100,  // 增加到100个以提高匹配率
                    sortBy: "downloads",  // 按下载量排序以获取更常用的MOD
                    projectType: projectType
                );

                if (result?.Hits != null && result.Hits.Count > 0)
                {
                    // 在本地使用翻译服务进行中文匹配
                    var filteredMods = result.Hits.Where(mod =>
                    {
                        // 优先使用slug查找翻译，如果找不到再使用数字ID
                        var translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(mod.Slug);
                        if (translation == null)
                        {
                            translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(mod.ProjectId);
                        }
                        return ModTranslationService.Instance.MatchesSearch(mod.Title, translation, searchText);
                    }).ToList();

                    Debug.WriteLine($"[ResourcesPage] 中文搜索匹配到 {filteredMods.Count} 个Modrinth MOD");
                    
                    if (filteredMods.Count > 0)
                    {
                        DisplayModrinthMods(filteredMods);
                    }
                    else
                    {
                        ShowNoResults();
                    }
                }
                else
                {
                    ShowNoResults();
                }
            }
            else
            {
                // 英文搜索：直接使用API搜索
                var result = await modrinthService.SearchModsAsync(
                    searchQuery: searchText,
                    gameVersion: gameVersion,
                    offset: _currentPage * PAGE_SIZE,
                    limit: PAGE_SIZE,
                    sortBy: "relevance",
                    projectType: projectType
                );

                if (result?.Hits != null && result.Hits.Count > 0)
                {
                    DisplayModrinthMods(result.Hits);
                    Debug.WriteLine($"[ResourcesPage] ✅ 显示 {result.Hits.Count} 个Modrinth MOD");
                }
                else
                {
                    ShowNoResults();
                    Debug.WriteLine($"[ResourcesPage] ⚠️ 没有找到匹配的Modrinth MOD");
                }
            }
        }

        /// <summary>
        /// 显示Modrinth MOD列表
        /// </summary>
        private void DisplayModrinthMods(List<ModrinthMod> mods, bool clearList = true)
        {
            // 缓存数据到当前资源类型的状态
            if (!PageState.ResourceStates.ContainsKey(_currentResourceType))
            {
                PageState.ResourceStates[_currentResourceType] = new ResourceTypeState();
            }
            PageState.ResourceStates[_currentResourceType].CachedModrinthMods = mods;
            SavePageState();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (clearList)
            {
                ResourceListPanel.Children.Clear();
                }
                HideLoading();
                HideEmptyState();

                foreach (var mod in mods)
                {
                    var modCard = CreateModrinthModCard(mod);
                    ResourceListPanel.Children.Add(modCard);
                }
            }));
        }

        /// <summary>
        /// 显示CurseForge MOD列表
        /// </summary>
        private void DisplayCurseForgeMods(List<CurseForgeMod> mods, bool clearList = true)
        {
            // 缓存数据到当前资源类型的状态
            if (!PageState.ResourceStates.ContainsKey(_currentResourceType))
            {
                PageState.ResourceStates[_currentResourceType] = new ResourceTypeState();
            }
            PageState.ResourceStates[_currentResourceType].CachedCurseForgeMods = mods;
            SavePageState();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (clearList)
                {
                    ResourceListPanel.Children.Clear();
                }
                HideLoading();
                HideEmptyState();

                foreach (var mod in mods)
                {
                    var modCard = CreateModCard(mod);
                    ResourceListPanel.Children.Add(modCard);
                }
            }));
        }

        /// <summary>
        /// 创建MOD卡片
        /// </summary>
        private Border CreateModCard(CurseForgeMod mod)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = Cursors.Hand,  // 鼠标悬停变成手型
                Tag = mod  // 保存mod数据
            };
            border.SetResourceReference(Border.BackgroundProperty, "SurfaceElevatedBrush");

            // 添加点击事件
            border.MouseLeftButtonDown += ModCard_Click;

            // 添加悬停效果
            border.MouseEnter += (s, e) =>
            {
                border.SetResourceReference(Border.BackgroundProperty, "SurfaceHoverBrush");
            };
            border.MouseLeave += (s, e) =>
            {
                border.SetResourceReference(Border.BackgroundProperty, "SurfaceElevatedBrush");
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 图标
            var iconBorder = new Border
            {
                Width = 60,
                Height = 60,
                CornerRadius = new CornerRadius(8),
                VerticalAlignment = VerticalAlignment.Top
            };
            iconBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");

            if (mod.Logo != null && !string.IsNullOrEmpty(mod.Logo.ThumbnailUrl))
            {
                try
                {
                    // 优化图片加载：限制解码尺寸以减少内存占用
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.UriSource = new Uri(mod.Logo.ThumbnailUrl);
                    bitmapImage.CacheOption = BitmapCacheOption.OnDemand; // 按需加载，不用时可被GC回收
                    bitmapImage.DecodePixelWidth = 96; // 限制解码宽度为96px
                    bitmapImage.EndInit();
                    
                    var image = new Image
                    {
                        Source = bitmapImage,
                        Stretch = Stretch.UniformToFill
                    };
                    iconBorder.Child = image;
                }
                catch
                {
                    // 如果加载图片失败，使用默认图标
                    iconBorder.Child = CreateDefaultIcon();
                }
            }
            else
            {
                iconBorder.Child = CreateDefaultIcon();
            }

            Grid.SetColumn(iconBorder, 0);
            grid.Children.Add(iconBorder);

            // 信息区
            var infoPanel = new StackPanel
            {
                Margin = new Thickness(15, 0, 15, 0)
            };

            // 标题（支持中文翻译）
            // 优先使用slug查找翻译，如果找不到再使用数字ID
            var translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(mod.Slug);
            if (translation == null)
            {
                translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(mod.Id);
            }
            
            // 调试：输出翻译查找结果
            if (translation != null)
            {
                Debug.WriteLine($"[ResourcesPage] 找到翻译: {mod.Name} -> {translation.ChineseName} (slug={mod.Slug}, id={mod.Id})");
            }
            
            var displayName = ModTranslationService.Instance.GetDisplayName(mod.Name, translation);
            
            var titleText = new TextBlock
            {
                Text = displayName,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            infoPanel.Children.Add(titleText);

            // 描述
            var descText = new TextBlock
            {
                Text = mod.Summary,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 40,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 5, 0, 8)
            };
            descText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            infoPanel.Children.Add(descText);

            // 标签区
            var tagsPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // 作者
            if (mod.Authors.Count > 0)
            {
                var authorBorder = CreateTagBorder(mod.Authors[0].Name, "#607D8B");
                tagsPanel.Children.Add(authorBorder);
            }

            // 分类
            if (mod.Categories.Count > 0)
            {
                var categoryBorder = CreateTagBorder(mod.Categories[0].Name, "#2196F3");
                tagsPanel.Children.Add(categoryBorder);
            }

            // 下载量
            var downloadIcon = new PackIcon
            {
                Kind = PackIconKind.Download,
                Width = 14,
                Height = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 3, 0)
            };
            tagsPanel.Children.Add(downloadIcon);

            var downloadText = new TextBlock
            {
                Text = CurseForgeService.FormatDownloadCount(mod.DownloadCount),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            downloadText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            tagsPanel.Children.Add(downloadText);

            // 支持的版本范围
            if (mod.LatestFilesIndexes != null && mod.LatestFilesIndexes.Count > 0)
            {
                // 只提取纯Minecraft版本号（排除Forge、Fabric等标识）
                var versions = VersionUtils.FilterAndSortVersions(
                    mod.LatestFilesIndexes
                        .Where(f => !string.IsNullOrEmpty(f.GameVersion))
                        .Select(f => f.GameVersion)
                );

                if (versions.Any())
                {
                    var versionIcon = new PackIcon
                    {
                        Kind = PackIconKind.Tag,
                        Width = 14,
                        Height = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(10, 0, 3, 0)
                    };
                    tagsPanel.Children.Add(versionIcon);

                    string versionDisplay;
                    if (versions.Count <= 5)
                    {
                        // 5个或更少版本：全部显示
                        versionDisplay = string.Join(", ", versions);
                    }
                    else
                    {
                        // 6个以上版本：显示前3个和最后1个
                        var displayVersions = versions.Take(3).Concat(new[] { "...", versions[versions.Count - 1] });
                        versionDisplay = string.Join(", ", displayVersions);
                    }

                    var versionText = new TextBlock
                    {
                        Text = versionDisplay,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    tagsPanel.Children.Add(versionText);
                }

                // 支持的加载器类型
                var loaders = mod.LatestFilesIndexes
                    .Where(f => f.ModLoader.HasValue && f.ModLoader.Value > 0)
                    .Select(f => GetModLoaderName(f.ModLoader!.Value))
                    .Distinct()
                    .ToList();

                if (loaders.Any())
                {
                    var loaderIcon = new PackIcon
                    {
                        Kind = PackIconKind.Cog,
                        Width = 14,
                        Height = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(10, 0, 3, 0)
                    };
                    tagsPanel.Children.Add(loaderIcon);

                    var loaderText = new TextBlock
                    {
                        Text = string.Join(", ", loaders),
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    loaderText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                    tagsPanel.Children.Add(loaderText);
                }
            }

            infoPanel.Children.Add(tagsPanel);

            Grid.SetColumn(infoPanel, 1);
            grid.Children.Add(infoPanel);

            border.Child = grid;
            return border;
        }

        /// <summary>
        /// 创建Modrinth MOD卡片
        /// </summary>
        private Border CreateModrinthModCard(ModrinthMod mod)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = Cursors.Hand,
                Tag = mod  // 保存mod数据
            };
            border.SetResourceReference(Border.BackgroundProperty, "SurfaceElevatedBrush");

            // 添加点击事件
            border.MouseLeftButtonDown += ModrinthModCard_Click;

            // 添加悬停效果
            border.MouseEnter += (s, e) =>
            {
                border.SetResourceReference(Border.BackgroundProperty, "SurfaceHoverBrush");
            };
            border.MouseLeave += (s, e) =>
            {
                border.SetResourceReference(Border.BackgroundProperty, "SurfaceElevatedBrush");
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 图标
            var iconBorder = new Border
            {
                Width = 60,
                Height = 60,
                CornerRadius = new CornerRadius(8),
                VerticalAlignment = VerticalAlignment.Top
            };
            iconBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");

            if (!string.IsNullOrEmpty(mod.IconUrl))
            {
                try
                {
                    // 优化图片加载：限制解码尺寸以减少内存占用
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.UriSource = new Uri(mod.IconUrl);
                    bitmapImage.CacheOption = BitmapCacheOption.OnDemand; // 按需加载，不用时可被GC回收
                    bitmapImage.DecodePixelWidth = 96; // 限制解码宽度为96px
                    bitmapImage.EndInit();
                    
                    var image = new Image
                    {
                        Source = bitmapImage,
                        Stretch = Stretch.UniformToFill
                    };
                    iconBorder.Child = image;
                }
                catch
                {
                    iconBorder.Child = CreateDefaultIcon();
                }
            }
            else
            {
                iconBorder.Child = CreateDefaultIcon();
            }

            Grid.SetColumn(iconBorder, 0);
            grid.Children.Add(iconBorder);

            // 信息区
            var infoPanel = new StackPanel
            {
                Margin = new Thickness(15, 0, 15, 0)
            };

            // 标题（支持中文翻译）
            var translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(mod.Slug);
            if (translation == null)
            {
                translation = ModTranslationService.Instance.GetTranslationByCurseForgeId(mod.ProjectId);
            }
            
            var displayName = ModTranslationService.Instance.GetDisplayName(mod.Title, translation);
            
            var titleText = new TextBlock
            {
                Text = displayName,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            infoPanel.Children.Add(titleText);

            // 描述
            var descText = new TextBlock
            {
                Text = mod.Description,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 40,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 5, 0, 8)
            };
            descText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            infoPanel.Children.Add(descText);

            // 标签区
            var tagsPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // 作者
            if (!string.IsNullOrEmpty(mod.Author))
            {
                var authorBorder = CreateTagBorder(mod.Author, "#607D8B");
                tagsPanel.Children.Add(authorBorder);
            }

            // 分类
            if (mod.Categories.Count > 0)
            {
                var categoryBorder = CreateTagBorder(mod.Categories[0], "#2196F3");
                tagsPanel.Children.Add(categoryBorder);
            }

            // 下载量
            var downloadIcon = new PackIcon
            {
                Kind = PackIconKind.Download,
                Width = 14,
                Height = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 3, 0)
            };
            tagsPanel.Children.Add(downloadIcon);

            var downloadText = new TextBlock
            {
                Text = CurseForgeService.FormatDownloadCount(mod.Downloads),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            downloadText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            tagsPanel.Children.Add(downloadText);

            // 支持的版本范围
            if (mod.Versions != null && mod.Versions.Count > 0)
            {
                var versions = VersionUtils.FilterAndSortVersions(mod.Versions);

                if (versions.Any())
                {
                    var versionIcon = new PackIcon
                    {
                        Kind = PackIconKind.Tag,
                        Width = 14,
                        Height = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(10, 0, 3, 0)
                    };
                    tagsPanel.Children.Add(versionIcon);

                    string versionDisplay;
                    if (versions.Count <= 5)
                    {
                        versionDisplay = string.Join(", ", versions);
                    }
                    else
                    {
                        var displayVersions = versions.Take(3).Concat(new[] { "...", versions[versions.Count - 1] });
                        versionDisplay = string.Join(", ", displayVersions);
                    }

                    var versionText = new TextBlock
                    {
                        Text = versionDisplay,
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    versionText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                    tagsPanel.Children.Add(versionText);
                }
            }

            infoPanel.Children.Add(tagsPanel);
            Grid.SetColumn(infoPanel, 1);
            grid.Children.Add(infoPanel);

            border.Child = grid;
            return border;
        }

        /// <summary>
        /// Modrinth MOD卡片点击事件
        /// </summary>
        private void ModrinthModCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is ModrinthMod mod)
            {
                Debug.WriteLine($"[ResourcesPage] 点击Modrinth资源: {mod.Title} (ID={mod.ProjectId}, 类型: {_currentResourceType})");
                
                // 检查是否选择了版本（整合包除外）
                if (_currentResourceType != "Modpacks" && string.IsNullOrEmpty(_selectedVersionId))
                {
                    NotificationManager.Instance.ShowNotification("提示", "请先选择要安装到的游戏版本", NotificationType.Warning);
                    return;
                }
                
                // 仅MOD类型需要检查版本是否支持模组加载器
                if (_currentResourceType == "Mods")
                {
                    var versionType = DetectVersionType(_selectedVersionId!);
                    if (versionType == "vanilla" || versionType == "optifine")
                    {
                        NotificationManager.Instance.ShowNotification("提示", "所选版本不支持安装MOD，请选择Forge、Fabric、NeoForge或Quilt版本", NotificationType.Warning);
                        return;
                    }
                }
                
                // 导航到资源详情页，传递资源类型
                var detailPage = new ModDetailPage(mod, _selectedVersionId ?? "", _currentResourceType);
                NavigationService?.Navigate(detailPage);
            }
        }

        /// <summary>
        /// 创建默认图标
        /// </summary>
        private UIElement CreateDefaultIcon()
        {
            return new PackIcon
            {
                Kind = PackIconKind.Cube,
                Width = 35,
                Height = 35,
                Foreground = (Brush)Application.Current.TryFindResource("PrimaryBrush")
                    ?? new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>
        /// 创建标签边框
        /// </summary>
        private Border CreateTagBorder(string text, string colorHex)
        {
            return new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 8, 0),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 11
                }
            };
        }

        /// <summary>
        /// MOD卡片点击事件 - 跳转到详情页面
        /// </summary>
        private void ModCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is CurseForgeMod mod)
            {
                Debug.WriteLine($"[ResourcesPage] 打开资源详情: {mod.Name} (类型: {_currentResourceType})");
                
                // 检查是否选择了版本（整合包除外）
                if (_currentResourceType != "Modpacks" && string.IsNullOrEmpty(_selectedVersionId))
                {
                    NotificationManager.Instance.ShowNotification("提示", "请先选择要安装到的游戏版本", NotificationType.Warning);
                    return;
                }
                
                // 仅MOD类型需要检查版本是否支持模组加载器
                if (_currentResourceType == "Mods")
                {
                    var versionType = DetectVersionType(_selectedVersionId!);
                    if (versionType == "vanilla" || versionType == "optifine")
                    {
                        NotificationManager.Instance.ShowNotification("提示", "所选版本不支持下载MOD，请选择Forge、Fabric、NeoForge或Quilt版本", NotificationType.Warning);
                        return;
                    }
                }
                
                // 跳转到详情页面，传递资源类型
                var detailPage = new ModDetailPage(mod, _selectedVersionId ?? "", _currentResourceType);
                this.NavigationService?.Navigate(detailPage);
            }
        }

        /// <summary>
        /// 显示加载状态
        /// </summary>
        private void ShowLoading()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LoadingIndicator.Visibility = Visibility.Visible;
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                ResourceScrollViewer.Visibility = Visibility.Collapsed;
            }));
        }

        /// <summary>
        /// 隐藏加载状态
        /// </summary>
        private void HideLoading()
        {
            LoadingIndicator.Visibility = Visibility.Collapsed;
            ResourceScrollViewer.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 显示空状态
        /// </summary>
        /// <summary>
        /// 恢复页面状态
        /// </summary>
        private void RestorePageState()
        {
            _isRestoringState = true;

            try
            {
                // 恢复下载源
                SourceComboBox.SelectedIndex = PageState.SelectedSourceIndex;
                
                // 恢复排序
                if (SortComboBox != null)
                    SortComboBox.SelectedIndex = PageState.SelectedSortIndex;
                
                // 恢复选择的版本
                bool versionRestored = false;
                if (!string.IsNullOrEmpty(PageState.SelectedVersionId) && InstalledVersionComboBox.Items.Count > 0)
                {
                    for (int i = 0; i < InstalledVersionComboBox.Items.Count; i++)
                    {
                        if (InstalledVersionComboBox.Items[i] is ComboBoxItem item &&
                            item.Tag as string == PageState.SelectedVersionId)
                        {
                            InstalledVersionComboBox.SelectedIndex = i;
                            _selectedVersionId = PageState.SelectedVersionId;
                            versionRestored = true;
                            break;
                        }
                    }
                }

                // 如果没有恢复成功，使用当前选中的版本
                if (!versionRestored && InstalledVersionComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    _selectedVersionId = selectedItem.Tag as string;
                }

                // 恢复其他状态
                _currentSource = PageState.CurrentSource;
                _currentResourceType = PageState.CurrentResourceType;

                // 恢复当前资源类型的状态（包括搜索关键词和缓存）
                RestoreCurrentResourceTypeState();
            }
            finally
            {
                _isRestoringState = false;
            }
        }

        /// <summary>
        /// 保存页面状态
        /// </summary>
        private void SavePageState()
        {
            if (_isRestoringState) return;

            PageState.CurrentSource = _currentSource;
            PageState.CurrentResourceType = _currentResourceType;
            PageState.SelectedVersionId = _selectedVersionId;
            PageState.SelectedSourceIndex = SourceComboBox?.SelectedIndex ?? 0;
            PageState.SelectedSortIndex = SortComboBox?.SelectedIndex ?? 0;
            
            // 保存当前资源类型的状态
            SaveCurrentResourceTypeState();
        }

        private void ShowEmptyState()
        {
            if (LoadingIndicator != null)
                LoadingIndicator.Visibility = Visibility.Collapsed;
            
            if (EmptyStatePanel != null)
                EmptyStatePanel.Visibility = Visibility.Visible;
            
            if (ResourceScrollViewer != null)
                ResourceScrollViewer.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 隐藏空状态
        /// </summary>
        private void HideEmptyState()
        {
            if (EmptyStatePanel != null)
                EmptyStatePanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 显示无结果状态
        /// </summary>
        private void ShowNoResults()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                HideLoading();
                ResourceListPanel.Children.Clear();
                
                var noResultsText = new TextBlock
                {
                    Text = "没有找到匹配的资源，请尝试其他关键词",
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 50, 0, 0)
                };
                noResultsText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                ResourceListPanel.Children.Add(noResultsText);
            }));
        }

        /// <summary>
        /// 获取模组加载器名称
        /// </summary>
        /// <param name="loaderId">加载器ID (0=Any, 1=Forge, 3=LiteLoader, 4=Fabric, 5=Quilt, 6=NeoForge)</param>
        private string GetModLoaderName(int loaderId)
        {
            return loaderId switch
            {
                1 => "Forge",
                3 => "LiteLoader",
                4 => "Fabric",
                5 => "Quilt",
                6 => "NeoForge",
                _ => ""
            };
        }
    }
}


