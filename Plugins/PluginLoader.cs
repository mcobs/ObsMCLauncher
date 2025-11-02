using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace ObsMCLauncher.Plugins
{
    /// <summary>
    /// 插件加载器
    /// 负责扫描、加载和管理插件
    /// </summary>
    public class PluginLoader
    {
        private readonly string _pluginsDirectory;
        private readonly List<LoadedPlugin> _loadedPlugins = new();
        
        public PluginLoader(string pluginsDirectory)
        {
            _pluginsDirectory = pluginsDirectory;
            
            // 确保插件目录存在
            if (!Directory.Exists(_pluginsDirectory))
            {
                Directory.CreateDirectory(_pluginsDirectory);
                Debug.WriteLine($"[PluginLoader] 创建插件目录: {_pluginsDirectory}");
            }
        }
        
        /// <summary>
        /// 获取所有已加载的插件
        /// </summary>
        public IReadOnlyList<LoadedPlugin> LoadedPlugins => _loadedPlugins.AsReadOnly();
        
        /// <summary>
        /// 扫描并加载所有插件
        /// </summary>
        public void LoadAllPlugins()
        {
            Debug.WriteLine("[PluginLoader] 开始扫描插件...");
            
            try
            {
                // 先处理待删除的插件
                CleanupMarkedPlugins();
                
                var pluginDirs = Directory.GetDirectories(_pluginsDirectory);
                Debug.WriteLine($"[PluginLoader] 发现 {pluginDirs.Length} 个插件文件夹");
                
                foreach (var pluginDir in pluginDirs)
                {
                    try
                    {
                        LoadPlugin(pluginDir);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[PluginLoader] 加载插件失败 [{Path.GetFileName(pluginDir)}]: {ex.Message}");
                    }
                }
                
                Debug.WriteLine($"[PluginLoader] 插件加载完成，成功加载 {_loadedPlugins.Count(p => p.IsLoaded)} 个插件");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginLoader] 扫描插件目录失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 清理标记为待删除的插件
        /// </summary>
        private void CleanupMarkedPlugins()
        {
            try
            {
                if (!Directory.Exists(_pluginsDirectory))
                    return;
                
                var pluginDirs = Directory.GetDirectories(_pluginsDirectory);
                foreach (var pluginDir in pluginDirs)
                {
                    var deleteMarkerPath = Path.Combine(pluginDir, ".delete_on_restart");
                    if (File.Exists(deleteMarkerPath))
                    {
                        Debug.WriteLine($"[PluginLoader] 发现待删除插件: {Path.GetFileName(pluginDir)}");
                        
                        try
                        {
                            Directory.Delete(pluginDir, true);
                            Debug.WriteLine($"[PluginLoader] ✅ 已删除插件目录: {pluginDir}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[PluginLoader] ⚠️ 删除插件失败: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginLoader] 清理插件失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 加载单个插件
        /// </summary>
        private void LoadPlugin(string pluginDirectory)
        {
            var pluginDirName = Path.GetFileName(pluginDirectory);
            Debug.WriteLine($"[PluginLoader] 正在加载插件: {pluginDirName}");
            
            // 检查插件是否被禁用
            var disabledMarkerPath = Path.Combine(pluginDirectory, ".disabled");
            var isDisabled = File.Exists(disabledMarkerPath);
            
            // 读取 plugin.json
            var metadataPath = Path.Combine(pluginDirectory, "plugin.json");
            if (!File.Exists(metadataPath))
            {
                Debug.WriteLine($"[PluginLoader] 插件缺少 plugin.json: {pluginDirName}");
                return;
            }
            
            PluginMetadata metadata;
            try
            {
                var json = File.ReadAllText(metadataPath);
                metadata = JsonSerializer.Deserialize<PluginMetadata>(json) 
                    ?? throw new Exception("无法解析 plugin.json");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginLoader] 解析 plugin.json 失败: {ex.Message}");
                return;
            }
            
            // 查找DLL文件（优先查找与插件ID同名的DLL）
            var dllPath = Path.Combine(pluginDirectory, $"{metadata.Id}.dll");
            if (!File.Exists(dllPath))
            {
                // 尝试查找任意DLL
                var dllFiles = Directory.GetFiles(pluginDirectory, "*.dll");
                if (dllFiles.Length == 0)
                {
                    Debug.WriteLine($"[PluginLoader] 插件缺少 DLL 文件: {pluginDirName}");
                    return;
                }
                dllPath = dllFiles[0];
            }
            
            // 查找图标
            string? iconPath = null;
            var iconFile = Path.Combine(pluginDirectory, "icon.png");
            if (File.Exists(iconFile))
            {
                iconPath = iconFile;
            }
            
            var loadedPlugin = new LoadedPlugin
            {
                Id = metadata.Id,
                Name = metadata.Name,
                Version = metadata.Version,
                Author = metadata.Author,
                Description = metadata.Description,
                DirectoryPath = pluginDirectory,
                IconPath = iconPath,
                Metadata = metadata,
                IsLoaded = false
            };
            
            // 如果插件被禁用，不加载但保留在列表中
            if (isDisabled)
            {
                loadedPlugin.ErrorMessage = "插件已被禁用";
                loadedPlugin.IsLoaded = false;
                _loadedPlugins.Add(loadedPlugin);
                Debug.WriteLine($"[PluginLoader] 插件已禁用，跳过加载: {metadata.Name}");
                return;
            }
            
            try
            {
                // 加载程序集
                var assembly = Assembly.LoadFrom(dllPath);
                
                // 查找实现 ILauncherPlugin 的类
                var pluginType = assembly.GetTypes()
                    .FirstOrDefault(t => typeof(ILauncherPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                
                if (pluginType == null)
                {
                    loadedPlugin.ErrorMessage = "未找到实现 ILauncherPlugin 接口的类";
                    Debug.WriteLine($"[PluginLoader] {loadedPlugin.ErrorMessage}: {metadata.Name}");
                    _loadedPlugins.Add(loadedPlugin);
                    return;
                }
                
                // 创建插件实例
                var pluginInstance = Activator.CreateInstance(pluginType) as ILauncherPlugin;
                if (pluginInstance == null)
                {
                    loadedPlugin.ErrorMessage = "无法创建插件实例";
                    Debug.WriteLine($"[PluginLoader] {loadedPlugin.ErrorMessage}: {metadata.Name}");
                    _loadedPlugins.Add(loadedPlugin);
                    return;
                }
                
                // 创建插件上下文
                var context = new PluginContext(metadata.Id);
                
                // 调用插件的 OnLoad 方法
                pluginInstance.OnLoad(context);
                
                loadedPlugin.Instance = pluginInstance;
                loadedPlugin.IsLoaded = true;
                
                Debug.WriteLine($"[PluginLoader] ✅ 插件加载成功: {metadata.Name} v{metadata.Version}");
            }
            catch (Exception ex)
            {
                loadedPlugin.ErrorMessage = ex.Message;
                Debug.WriteLine($"[PluginLoader] ❌ 插件加载失败 [{metadata.Name}]: {ex.Message}");
                Debug.WriteLine($"[PluginLoader] 堆栈跟踪: {ex.StackTrace}");
            }
            
            _loadedPlugins.Add(loadedPlugin);
        }
        
        /// <summary>
        /// 卸载所有插件
        /// </summary>
        public void UnloadAllPlugins()
        {
            Debug.WriteLine("[PluginLoader] 开始卸载插件...");
            
            foreach (var plugin in _loadedPlugins.Where(p => p.IsLoaded && p.Instance != null))
            {
                try
                {
                    plugin.Instance!.OnUnload();
                    Debug.WriteLine($"[PluginLoader] 已卸载插件: {plugin.Name}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PluginLoader] 卸载插件失败 [{plugin.Name}]: {ex.Message}");
                }
            }
            
            _loadedPlugins.Clear();
        }
        
        /// <summary>
        /// 触发插件关闭事件
        /// </summary>
        public void ShutdownPlugins()
        {
            Debug.WriteLine("[PluginLoader] 触发插件关闭事件...");
            
            foreach (var plugin in _loadedPlugins.Where(p => p.IsLoaded && p.Instance != null))
            {
                try
                {
                    plugin.Instance!.OnShutdown();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PluginLoader] 插件关闭事件处理失败 [{plugin.Name}]: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 立即禁用插件（热卸载实例并标记）
        /// </summary>
        public bool DisablePluginImmediately(string pluginId)
        {
            try
            {
                var plugin = _loadedPlugins.FirstOrDefault(p => p.Id == pluginId);
                if (plugin == null)
                {
                    Debug.WriteLine($"[PluginLoader] 未找到插件: {pluginId}");
                    return false;
                }
                
                // 如果插件已加载，先卸载实例
                if (plugin.IsLoaded && plugin.Instance != null)
                {
                    try
                    {
                        plugin.Instance.OnUnload();
                        Debug.WriteLine($"[PluginLoader] 已调用插件卸载方法: {plugin.Name}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[PluginLoader] 插件卸载方法执行失败: {ex.Message}");
                    }
                    
                    plugin.Instance = null;
                }
                
                // 移除插件的UI标签页
                try
                {
                    ObsMCLauncher.Pages.MorePage.RemovePluginTab(pluginId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PluginLoader] 移除插件标签页失败: {ex.Message}");
                }
                
                // 创建禁用标记文件
                var disabledMarkerPath = Path.Combine(plugin.DirectoryPath, ".disabled");
                File.WriteAllText(disabledMarkerPath, DateTime.Now.ToString());
                
                // 立即更新插件状态
                plugin.IsLoaded = false;
                plugin.ErrorMessage = "插件已被禁用";
                
                Debug.WriteLine($"[PluginLoader] ✅ 已热禁用插件: {plugin.Name}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginLoader] 禁用插件失败: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 启用插件（热加载插件实例）
        /// </summary>
        public bool EnablePlugin(string pluginId, out string? errorMessage)
        {
            errorMessage = null;
            try
            {
                var plugin = _loadedPlugins.FirstOrDefault(p => p.Id == pluginId);
                if (plugin == null)
                {
                    errorMessage = "未找到插件";
                    Debug.WriteLine($"[PluginLoader] 未找到插件: {pluginId}");
                    return false;
                }
                
                // 删除禁用标记文件
                var disabledMarkerPath = Path.Combine(plugin.DirectoryPath, ".disabled");
                if (File.Exists(disabledMarkerPath))
                {
                    File.Delete(disabledMarkerPath);
                }
                
                // 如果插件已经加载，直接返回
                if (plugin.IsLoaded && plugin.Instance != null)
                {
                    Debug.WriteLine($"[PluginLoader] 插件已经处于加载状态: {plugin.Name}");
                    return true;
                }
                
                // 热加载插件
                try
                {
                    // 查找DLL文件
                    var dllPath = Path.Combine(plugin.DirectoryPath, $"{plugin.Id}.dll");
                    if (!File.Exists(dllPath))
                    {
                        var dllFiles = Directory.GetFiles(plugin.DirectoryPath, "*.dll");
                        if (dllFiles.Length == 0)
                        {
                            errorMessage = "未找到插件DLL文件";
                            plugin.ErrorMessage = errorMessage;
                            return false;
                        }
                        dllPath = dllFiles[0];
                    }
                    
                    // 加载程序集
                    var assembly = Assembly.LoadFrom(dllPath);
                    
                    // 查找实现 ILauncherPlugin 的类
                    var pluginType = assembly.GetTypes()
                        .FirstOrDefault(t => typeof(ILauncherPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                    
                    if (pluginType == null)
                    {
                        errorMessage = "未找到实现 ILauncherPlugin 接口的类";
                        plugin.ErrorMessage = errorMessage;
                        return false;
                    }
                    
                    // 创建插件实例
                    var pluginInstance = Activator.CreateInstance(pluginType) as ILauncherPlugin;
                    if (pluginInstance == null)
                    {
                        errorMessage = "无法创建插件实例";
                        plugin.ErrorMessage = errorMessage;
                        return false;
                    }
                    
                    // 创建插件上下文
                    var context = new PluginContext(plugin.Id);
                    
                    // 初始化插件
                    pluginInstance.OnLoad(context);
                    
                    // 更新插件状态
                    plugin.Instance = pluginInstance;
                    plugin.IsLoaded = true;
                    plugin.ErrorMessage = null;
                    
                    Debug.WriteLine($"[PluginLoader] ✅ 已热启用插件: {plugin.Name}");
                    return true;
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                    plugin.ErrorMessage = $"加载失败: {ex.Message}";
                    plugin.IsLoaded = false;
                    Debug.WriteLine($"[PluginLoader] 热启用插件失败 [{plugin.Name}]: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                Debug.WriteLine($"[PluginLoader] 启用插件失败: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 删除插件（热卸载并从文件系统中删除）
        /// </summary>
        public bool RemovePlugin(string pluginId, out string? errorMessage)
        {
            errorMessage = null;
            try
            {
                var plugin = _loadedPlugins.FirstOrDefault(p => p.Id == pluginId);
                if (plugin == null)
                {
                    errorMessage = "未找到插件";
                    return false;
                }
                
                // 如果插件已加载，先热卸载
                if (plugin.IsLoaded && plugin.Instance != null)
                {
                    try
                    {
                        plugin.Instance.OnUnload();
                        Debug.WriteLine($"[PluginLoader] 已调用插件卸载方法: {plugin.Name}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[PluginLoader] 插件卸载方法执行失败: {ex.Message}");
                    }
                    
                    plugin.Instance = null;
                    plugin.IsLoaded = false;
                }
                
                // 移除插件的UI标签页
                try
                {
                    ObsMCLauncher.Pages.MorePage.RemovePluginTab(pluginId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PluginLoader] 移除插件标签页失败: {ex.Message}");
                }
                
                // 延迟一下，确保资源被释放
                System.Threading.Thread.Sleep(100);
                
                // 尝试删除插件目录
                try
                {
                    if (Directory.Exists(plugin.DirectoryPath))
                    {
                        Directory.Delete(plugin.DirectoryPath, true);
                        Debug.WriteLine($"[PluginLoader] 已删除插件目录: {plugin.DirectoryPath}");
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    errorMessage = "无法删除插件文件，文件可能被占用。已热卸载插件实例，但文件将在下次重启后删除。";
                    Debug.WriteLine($"[PluginLoader] ⚠️ 文件被占用，标记为待删除: {plugin.DirectoryPath}");
                    
                    // 创建待删除标记
                    try
                    {
                        var deleteMarkerPath = Path.Combine(plugin.DirectoryPath, ".delete_on_restart");
                        File.WriteAllText(deleteMarkerPath, DateTime.Now.ToString());
                    }
                    catch { }
                    
                    // 仍然从列表中移除
                    _loadedPlugins.Remove(plugin);
                    return true;
                }
                
                // 从列表中移除
                _loadedPlugins.Remove(plugin);
                Debug.WriteLine($"[PluginLoader] ✅ 已热卸载插件: {plugin.Name}");
                
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                Debug.WriteLine($"[PluginLoader] 删除插件失败: {ex.Message}");
                return false;
            }
        }
    }
}

