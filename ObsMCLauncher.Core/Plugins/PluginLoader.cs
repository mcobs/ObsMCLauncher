using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Plugins;

public class PluginLoader
{
    private readonly string _pluginsDirectory;
    private readonly List<LoadedPlugin> _loadedPlugins = new();

    public static Action<string>? OnPluginDisabled { get; set; }
    public static Action<string>? OnPluginEnabled { get; set; }
    public static Action<string>? OnPluginRemoved { get; set; }

    private static void CreateDisabledMarker(string pluginDirectory)
    {
        try
        {
            var disabledMarkerPath = Path.Combine(pluginDirectory, ".disabled");

            if (!File.Exists(disabledMarkerPath))
            {
                File.WriteAllText(disabledMarkerPath, DateTime.Now.ToString());
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginLoader", $"写入禁用标记失败: {ex.Message}");
        }
    }

    public PluginLoader(string pluginsDirectory)
    {
        _pluginsDirectory = pluginsDirectory;

        if (!Directory.Exists(_pluginsDirectory))
        {
            Directory.CreateDirectory(_pluginsDirectory);
            DebugLogger.Info("PluginLoader", $"创建插件目录: {_pluginsDirectory}");
        }
    }

    public IReadOnlyList<LoadedPlugin> LoadedPlugins => _loadedPlugins.AsReadOnly();

    public void LoadAllPlugins()
    {
        DebugLogger.Info("PluginLoader", "开始扫描插件...");

        try
        {
            _loadedPlugins.Clear();

            CleanupMarkedPlugins();

            var pluginDirs = Directory.GetDirectories(_pluginsDirectory);
            DebugLogger.Info("PluginLoader", $"发现 {pluginDirs.Length} 个插件文件夹");

            foreach (var pluginDir in pluginDirs)
            {
                try
                {
                    LoadPlugin(pluginDir);
                }
                catch (Exception ex)
                {
                    DebugLogger.Error("PluginLoader", $"加载插件失败 [{Path.GetFileName(pluginDir)}]: {ex.Message}");
                }
            }

            DebugLogger.Info("PluginLoader", $"插件加载完成，成功加载 {_loadedPlugins.Count(p => p.IsLoaded)} 个插件");
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginLoader", $"扫描插件目录失败: {ex.Message}");
        }
    }

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
                    DebugLogger.Info("PluginLoader", $"发现待删除插件: {Path.GetFileName(pluginDir)}");

                    try
                    {
                        Directory.Delete(pluginDir, true);
                        DebugLogger.Info("PluginLoader", $"已删除插件目录: {pluginDir}");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Warn("PluginLoader", $"删除插件失败: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginLoader", $"清理插件失败: {ex.Message}");
        }
    }

    private void LoadPlugin(string pluginDirectory)
    {
        var pluginDirName = Path.GetFileName(pluginDirectory);
        DebugLogger.Info("PluginLoader", $"正在加载插件: {pluginDirName}");

        var disabledMarkerPath = Path.Combine(pluginDirectory, ".disabled");
        var isDisabled = File.Exists(disabledMarkerPath);

        var metadataPath = Path.Combine(pluginDirectory, "plugin.json");
        if (!File.Exists(metadataPath))
        {
            DebugLogger.Warn("PluginLoader", $"插件缺少 plugin.json: {pluginDirName}");
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
            DebugLogger.Error("PluginLoader", $"解析 plugin.json 失败: {ex.Message}");
            return;
        }

        var readmePath = Path.Combine(pluginDirectory, "README.md");
        if (!File.Exists(readmePath))
        {
            DebugLogger.Warn("PluginLoader", $"插件缺少 README.md: {pluginDirName}");

            var missingReadme = new LoadedPlugin
            {
                Id = metadata.Id,
                Name = metadata.Name,
                Version = metadata.Version,
                Author = metadata.Author,
                Description = metadata.Description,
                DirectoryPath = pluginDirectory,
                IconPath = null,
                ReadmePath = null,
                Metadata = metadata,
                IsLoaded = false,
                ErrorMessage = "缺少 README.md",
                ErrorOutput = "缺少 README.md：插件必须在根目录提供 README.md 以显示说明文档。"
            };

            CreateDisabledMarker(pluginDirectory);
            _loadedPlugins.Add(missingReadme);
            return;
        }

        var dllPath = Path.Combine(pluginDirectory, $"{metadata.Id}.dll");
        if (!File.Exists(dllPath))
        {
            var dllFiles = Directory.GetFiles(pluginDirectory, "*.dll");
            if (dllFiles.Length == 0)
            {
                DebugLogger.Warn("PluginLoader", $"插件缺少 DLL 文件: {pluginDirName}");
                return;
            }
            dllPath = dllFiles[0];
        }

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
            ReadmePath = readmePath,
            Metadata = metadata,
            IsLoaded = false
        };

        if (isDisabled)
        {
            loadedPlugin.ErrorMessage = "插件已被禁用";
            loadedPlugin.IsLoaded = false;
            _loadedPlugins.Add(loadedPlugin);
            DebugLogger.Info("PluginLoader", $"插件已禁用，跳过加载: {metadata.Name}");
            return;
        }

        try
        {
            var assembly = Assembly.LoadFrom(dllPath);

            var pluginType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(ILauncherPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            if (pluginType == null)
            {
                loadedPlugin.ErrorMessage = "未找到实现 ILauncherPlugin 接口的类";
                loadedPlugin.ErrorOutput = loadedPlugin.ErrorMessage;
                DebugLogger.Error("PluginLoader", $"{loadedPlugin.ErrorMessage}: {metadata.Name}");
                CreateDisabledMarker(pluginDirectory);
                _loadedPlugins.Add(loadedPlugin);
                return;
            }

            var pluginInstance = Activator.CreateInstance(pluginType) as ILauncherPlugin;
            if (pluginInstance == null)
            {
                loadedPlugin.ErrorMessage = "无法创建插件实例";
                loadedPlugin.ErrorOutput = loadedPlugin.ErrorMessage;
                DebugLogger.Error("PluginLoader", $"{loadedPlugin.ErrorMessage}: {metadata.Name}");
                CreateDisabledMarker(pluginDirectory);
                _loadedPlugins.Add(loadedPlugin);
                return;
            }

            var context = new PluginContext(metadata.Id);

            pluginInstance.OnLoad(context);

            loadedPlugin.Instance = pluginInstance;
            loadedPlugin.IsLoaded = true;
            loadedPlugin.ErrorMessage = null;
            loadedPlugin.ErrorOutput = null;

            DebugLogger.Info("PluginLoader", $"插件加载成功: {metadata.Name} v{metadata.Version}");
        }
        catch (Exception ex)
        {
            loadedPlugin.ErrorMessage = ex.Message;
            loadedPlugin.ErrorOutput = ex.ToString();
            DebugLogger.Error("PluginLoader", $"插件加载失败 [{metadata.Name}]: {ex.Message}");
            DebugLogger.Error("PluginLoader", $"堆栈跟踪: {ex.StackTrace}");

            CreateDisabledMarker(pluginDirectory);
        }

        _loadedPlugins.Add(loadedPlugin);
    }

    public void UnloadAllPlugins()
    {
        DebugLogger.Info("PluginLoader", "开始卸载插件...");

        foreach (var plugin in _loadedPlugins.Where(p => p.IsLoaded && p.Instance != null))
        {
            try
            {
                plugin.Instance!.OnUnload();
                DebugLogger.Info("PluginLoader", $"已卸载插件: {plugin.Name}");
            }
            catch (Exception ex)
            {
                DebugLogger.Error("PluginLoader", $"卸载插件失败 [{plugin.Name}]: {ex.Message}");
            }
        }

        _loadedPlugins.Clear();
    }

    public void ShutdownPlugins()
    {
        DebugLogger.Info("PluginLoader", "触发插件关闭事件...");

        foreach (var plugin in _loadedPlugins.Where(p => p.IsLoaded && p.Instance != null))
        {
            try
            {
                plugin.Instance!.OnShutdown();
            }
            catch (Exception ex)
            {
                DebugLogger.Error("PluginLoader", $"插件关闭事件处理失败 [{plugin.Name}]: {ex.Message}");
            }
        }
    }

    public bool DisablePluginImmediately(string pluginId)
    {
        try
        {
            var plugin = _loadedPlugins.FirstOrDefault(p => p.Id == pluginId);
            if (plugin == null)
            {
                DebugLogger.Warn("PluginLoader", $"未找到插件: {pluginId}");
                return false;
            }

            if (plugin.IsLoaded && plugin.Instance != null)
            {
                try
                {
                    plugin.Instance.OnUnload();
                    DebugLogger.Info("PluginLoader", $"已调用插件卸载方法: {plugin.Name}");
                }
                catch (Exception ex)
                {
                    DebugLogger.Error("PluginLoader", $"插件卸载方法执行失败: {ex.Message}");
                }

                plugin.Instance = null;
            }

            CreateDisabledMarker(plugin.DirectoryPath);

            plugin.IsLoaded = false;
            plugin.ErrorMessage = "插件已被禁用";

            OnPluginDisabled?.Invoke(pluginId);

            DebugLogger.Info("PluginLoader", $"已热禁用插件: {plugin.Name}");
            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginLoader", $"禁用插件失败: {ex.Message}");
            return false;
        }
    }

    public bool EnablePlugin(string pluginId, out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            var plugin = _loadedPlugins.FirstOrDefault(p => p.Id == pluginId);
            if (plugin == null)
            {
                errorMessage = "未找到插件";
                DebugLogger.Warn("PluginLoader", $"未找到插件: {pluginId}");
                return false;
            }

            var readmePath = Path.Combine(plugin.DirectoryPath, "README.md");
            if (!File.Exists(readmePath))
            {
                errorMessage = "缺少 README.md，无法启用该插件";
                plugin.ErrorMessage = errorMessage;
                plugin.ErrorOutput = "缺少 README.md：插件必须在根目录提供 README.md 以显示说明文档。";
                plugin.IsLoaded = false;
                CreateDisabledMarker(plugin.DirectoryPath);
                return false;
            }

            plugin.ReadmePath = readmePath;

            var disabledMarkerPath = Path.Combine(plugin.DirectoryPath, ".disabled");
            if (File.Exists(disabledMarkerPath))
            {
                File.Delete(disabledMarkerPath);
            }

            if (plugin.IsLoaded && plugin.Instance != null)
            {
                DebugLogger.Info("PluginLoader", $"插件已经处于加载状态: {plugin.Name}");
                return true;
            }

            try
            {
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

                var assembly = Assembly.LoadFrom(dllPath);

                var pluginType = assembly.GetTypes()
                    .FirstOrDefault(t => typeof(ILauncherPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                if (pluginType == null)
                {
                    errorMessage = "未找到实现 ILauncherPlugin 接口的类";
                    plugin.ErrorMessage = errorMessage;
                    return false;
                }

                var pluginInstance = Activator.CreateInstance(pluginType) as ILauncherPlugin;
                if (pluginInstance == null)
                {
                    errorMessage = "无法创建插件实例";
                    plugin.ErrorMessage = errorMessage;
                    return false;
                }

                var context = new PluginContext(plugin.Id);

                pluginInstance.OnLoad(context);

                plugin.Instance = pluginInstance;
                plugin.IsLoaded = true;
                plugin.ErrorMessage = null;
                plugin.ErrorOutput = null;

                OnPluginEnabled?.Invoke(pluginId);

                DebugLogger.Info("PluginLoader", $"已热启用插件: {plugin.Name}");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                plugin.ErrorMessage = $"加载失败: {ex.Message}";
                plugin.IsLoaded = false;
                DebugLogger.Error("PluginLoader", $"热启用插件失败 [{plugin.Name}]: {ex.Message}");
                return false;
            }
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            DebugLogger.Error("PluginLoader", $"启用插件失败: {ex.Message}");
            return false;
        }
    }

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

            if (plugin.IsLoaded && plugin.Instance != null)
            {
                try
                {
                    plugin.Instance.OnUnload();
                    DebugLogger.Info("PluginLoader", $"已调用插件卸载方法: {plugin.Name}");
                }
                catch (Exception ex)
                {
                    DebugLogger.Error("PluginLoader", $"插件卸载方法执行失败: {ex.Message}");
                }

                plugin.Instance = null;
                plugin.IsLoaded = false;
            }

            System.Threading.Thread.Sleep(100);

            try
            {
                if (Directory.Exists(plugin.DirectoryPath))
                {
                    Directory.Delete(plugin.DirectoryPath, true);
                    DebugLogger.Info("PluginLoader", $"已删除插件目录: {plugin.DirectoryPath}");
                }
            }
            catch (UnauthorizedAccessException)
            {
                errorMessage = "无法删除插件文件，文件可能被占用。已热卸载插件实例，但文件将在下次重启后删除。";
                DebugLogger.Warn("PluginLoader", $"文件被占用，标记为待删除: {plugin.DirectoryPath}");

                try
                {
                    var deleteMarkerPath = Path.Combine(plugin.DirectoryPath, ".delete_on_restart");
                    File.WriteAllText(deleteMarkerPath, DateTime.Now.ToString());
                }
                catch
                {
                }

                OnPluginRemoved?.Invoke(pluginId);
                _loadedPlugins.Remove(plugin);
                return true;
            }

            OnPluginRemoved?.Invoke(pluginId);
            _loadedPlugins.Remove(plugin);
            DebugLogger.Info("PluginLoader", $"已热卸载插件: {plugin.Name}");

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            DebugLogger.Error("PluginLoader", $"删除插件失败: {ex.Message}");
            return false;
        }
    }
}
