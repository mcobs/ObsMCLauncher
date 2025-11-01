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
        /// 加载单个插件
        /// </summary>
        private void LoadPlugin(string pluginDirectory)
        {
            var pluginDirName = Path.GetFileName(pluginDirectory);
            Debug.WriteLine($"[PluginLoader] 正在加载插件: {pluginDirName}");
            
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
    }
}

