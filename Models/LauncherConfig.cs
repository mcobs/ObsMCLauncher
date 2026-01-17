using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Models
{
    /// <summary>
    /// 启动器配置
    /// </summary>
    public class LauncherConfig
    {
        /// <summary>
        /// 下载源
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DownloadSource DownloadSource { get; set; } = DownloadSource.BMCLAPI;

        /// <summary>
        /// 最大内存（MB）
        /// </summary>
        public int MaxMemory { get; set; } = 4096;

        /// <summary>
        /// 最小内存（MB）
        /// </summary>
        public int MinMemory { get; set; } = 1024;

        /// <summary>
        /// 游戏目录位置类型
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DirectoryLocation GameDirectoryLocation { get; set; } = DirectoryLocation.AppData;

        /// <summary>
        /// 自定义游戏目录（当GameDirectoryLocation为Custom时使用）
        /// </summary>
        public string CustomGameDirectory { get; set; } = "";

        /// <summary>
        /// 游戏目录（根据位置类型计算）
        /// </summary>
        [JsonIgnore]
        public string GameDirectory
        {
            get
            {
                return GameDirectoryLocation switch
                {
                    DirectoryLocation.AppData => Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        ".minecraft"),
                    DirectoryLocation.RunningDirectory => Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        ".minecraft"),
                    DirectoryLocation.Custom => string.IsNullOrEmpty(CustomGameDirectory)
                        ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft")
                        : CustomGameDirectory,
                    _ => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft")
                };
            }
        }


        /// <summary>
        /// Java选择模式：0=自动选择，1=指定路径，2=自定义路径
        /// </summary>
        public int JavaSelectionMode { get; set; } = 0;

        /// <summary>
        /// Java路径（当JavaSelectionMode为1或2时使用）
        /// </summary>
        public string JavaPath { get; set; } = "javaw.exe";

        /// <summary>
        /// 自定义Java路径（当JavaSelectionMode为2时由用户输入）
        /// </summary>
        public string CustomJavaPath { get; set; } = "";

        /// <summary>
        /// JVM参数
        /// </summary>
        public string JvmArguments { get; set; } = "-XX:+UseG1GC -XX:+UnlockExperimentalVMOptions";

        /// <summary>
        /// 最大下载线程数
        /// </summary>
        public int MaxDownloadThreads { get; set; } = 8;

        /// <summary>
        /// 下载游戏时是否完整下载资源文件（Assets）
        /// </summary>
        public bool DownloadAssetsWithGame { get; set; } = true;

        /// <summary>
        /// 版本隔离模式：决定每个版本使用独立的mods文件夹还是共享
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GameDirectoryType GameDirectoryType { get; set; } = GameDirectoryType.RootFolder;

        /// <summary>
        /// 游戏启动后关闭启动器
        /// </summary>
        public bool CloseAfterLaunch { get; set; } = false;

        /// <summary>
        /// 启动时显示游戏日志窗口
        /// </summary>
        public bool ShowGameLogOnLaunch { get; set; } = false;

        /// <summary>
        /// 自动检查更新
        /// </summary>
        public bool AutoCheckUpdate { get; set; } = true;

        /// <summary>
        /// 主题模式：0=深色，1=浅色，2=跟随系统
        /// </summary>
        public int ThemeMode { get; set; } = 0;

        /// <summary>
        /// 当前选中的版本
        /// </summary>
        public string? SelectedVersion { get; set; }

        /// <summary>
        /// 侧边导航栏是否折叠
        /// </summary>
        public bool IsNavCollapsed { get; set; } = false;

        /// <summary>
        /// 服务器收藏列表
        /// </summary>
        public List<ServerInfo> Servers { get; set; } = new List<ServerInfo>();
        
        /// <summary>
        /// 主页卡片配置列表
        /// </summary>
        public List<HomeCardConfig> HomeCards { get; set; } = new List<HomeCardConfig>();

        /// <summary>
        /// 获取配置文件路径（固定路径：当前目录\OMCL\config\config.json）
        /// </summary>
        public static string GetConfigFilePath()
        {
            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "OMCL",
                "config",
                "config.json");
        }

        /// <summary>
        /// 当前使用的配置文件路径
        /// </summary>
        private static string _currentConfigPath = GetConfigFilePath();

        /// <summary>
        /// 保存配置
        /// </summary>
        public void Save()
        {
            try
            {
                // 使用固定路径
                var configPath = GetConfigFilePath();
                
                // 确保目录存在
                var directory = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(configPath, json);

                System.Diagnostics.Debug.WriteLine($"✅ 配置已保存到: {configPath}");

                // 如果配置路径变了，尝试迁移旧配置文件
                if (configPath != _currentConfigPath && File.Exists(_currentConfigPath))
                {
                    try
                    {
                        // 迁移旧配置文件到新位置
                        var oldConfigDir = Path.GetDirectoryName(_currentConfigPath);
                        if (!string.IsNullOrEmpty(oldConfigDir) && Directory.Exists(oldConfigDir))
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ 检测到旧配置文件位置: {_currentConfigPath}");
                            System.Diagnostics.Debug.WriteLine($"✅ 配置文件已迁移到新位置: {configPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"迁移旧配置文件时出错: {ex.Message}");
                    }
                }

                // 更新当前配置路径
                _currentConfigPath = configPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存配置失败: {ex.Message}");
                throw;
            }
        }


        /// <summary>
        /// 加载配置
        /// </summary>
        public static LauncherConfig Load()
        {
            // 固定路径：当前目录\OMCL\config\config.json
            var configPath = GetConfigFilePath();
            
            // 确保目录存在
            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 尝试从固定路径加载
            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize<LauncherConfig>(json);
                    if (config != null)
                    {
                        _currentConfigPath = configPath;
                        System.Diagnostics.Debug.WriteLine($"✅ 从固定路径加载配置: {configPath}");
                        
                        // 迁移旧配置文件（如果存在）
                        MigrateOldConfigFiles(config);
                        
                        return config;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"从固定路径 {configPath} 加载配置失败: {ex.Message}");
                }
            }

            // 尝试从旧位置迁移配置
            var oldLocations = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OMCL", "config", "config.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ObsMCLauncher", "config", "config.json")
            };

            foreach (var oldPath in oldLocations)
            {
                if (File.Exists(oldPath) && oldPath != configPath)
                {
                    try
                    {
                        var json = File.ReadAllText(oldPath);
                        var config = JsonSerializer.Deserialize<LauncherConfig>(json);
                        if (config != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"✅ 从旧位置迁移配置: {oldPath} -> {configPath}");
                            
                            // 保存到新位置
                            var newDir = Path.GetDirectoryName(configPath);
                            if (!string.IsNullOrEmpty(newDir) && !Directory.Exists(newDir))
                            {
                                Directory.CreateDirectory(newDir);
                            }
                            
                            var options = new JsonSerializerOptions
                            {
                                WriteIndented = true,
                                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                            };
                            File.WriteAllText(configPath, JsonSerializer.Serialize(config, options));
                            
                            _currentConfigPath = configPath;
                            
                            // 迁移账号文件
                            MigrateAccountFile(oldPath, configPath);
                            
                            return config;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"从旧位置 {oldPath} 迁移配置失败: {ex.Message}");
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine("⚠️ 未找到配置文件，使用默认配置");
            var defaultConfig = new LauncherConfig();
            _currentConfigPath = configPath;
            return defaultConfig;
        }

        /// <summary>
        /// 迁移旧配置文件
        /// </summary>
        private static void MigrateOldConfigFiles(LauncherConfig config)
        {
            // 如果配置中有旧的文件存储设置，清理它们（向后兼容）
            // 这些属性已经不存在了，但JSON中可能还有，会被忽略
        }

        /// <summary>
        /// 迁移账号文件
        /// </summary>
        private static void MigrateAccountFile(string oldConfigPath, string newConfigPath)
        {
            try
            {
                var oldConfigDir = Path.GetDirectoryName(oldConfigPath);
                var newConfigDir = Path.GetDirectoryName(newConfigPath);
                
                if (string.IsNullOrEmpty(oldConfigDir) || string.IsNullOrEmpty(newConfigDir))
                    return;

                var oldAccountPath = Path.Combine(oldConfigDir, "accounts.json");
                var newAccountPath = Path.Combine(newConfigDir, "accounts.json");

                // 如果新位置已有账号文件，不覆盖
                if (File.Exists(newAccountPath))
                {
                    System.Diagnostics.Debug.WriteLine($"账号文件已存在于新位置: {newAccountPath}");
                    return;
                }

                // 如果旧位置有账号文件，迁移它
                if (File.Exists(oldAccountPath))
                {
                    if (!Directory.Exists(newConfigDir))
                    {
                        Directory.CreateDirectory(newConfigDir);
                    }
                    File.Copy(oldAccountPath, newAccountPath, true);
                    System.Diagnostics.Debug.WriteLine($"✅ 账号文件已迁移: {oldAccountPath} -> {newAccountPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"迁移账号文件失败: {ex.Message}");
            }
        }



        /// <summary>
        /// 获取账号文件路径（固定路径：当前目录\OMCL\config\accounts.json）
        /// </summary>
        public string GetAccountFilePath()
        {
            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "OMCL",
                "config",
                "accounts.json");
        }

        /// <summary>
        /// 获取插件目录路径（固定路径：当前目录\OMCL\plugins\）
        /// </summary>
        public string GetPluginDirectory()
        {
            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "OMCL",
                "plugins");
        }

        /// <summary>
        /// 获取数据目录路径（用于存放 Libraries 等数据）
        /// </summary>
        public string GetDataDirectory()
        {
            // 数据目录固定在运行目录的 OMCL 文件夹下
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OMCL");
        }

        /// <summary>
        /// 获取实际应该使用的Java路径
        /// </summary>
        /// <param name="minecraftVersion">Minecraft版本号，用于自动选择模式</param>
        /// <returns>Java可执行文件路径</returns>
        public string GetActualJavaPath(string? minecraftVersion = null)
        {
            switch (JavaSelectionMode)
            {
                case 0: // 自动选择
                    if (!string.IsNullOrEmpty(minecraftVersion))
                    {
                        var autoPath = Services.JavaDetector.SelectJavaForMinecraftVersion(minecraftVersion);
                        if (!string.IsNullOrEmpty(autoPath))
                        {
                            return autoPath;
                        }
                    }
                    // 如果自动选择失败，降级使用最佳Java
                    var bestJava = Services.JavaDetector.SelectBestJava();
                    return bestJava?.Path ?? "javaw.exe";

                case 1: // 指定路径（从检测列表选择）
                    return string.IsNullOrEmpty(JavaPath) ? "javaw.exe" : JavaPath;

                case 2: // 自定义路径
                    return string.IsNullOrEmpty(CustomJavaPath) ? JavaPath : CustomJavaPath;

                default:
                    return JavaPath;
            }
        }

        /// <summary>
        /// 获取指定版本的运行目录（根据版本隔离设置）
        /// 优先使用版本自己的设置，如果没有则使用全局设置
        /// </summary>
        /// <param name="versionName">版本名称</param>
        /// <returns>运行目录路径</returns>
        public string GetRunDirectory(string versionName)
        {
            // 获取版本路径
            var versionPath = Path.Combine(GameDirectory, "versions", versionName);
            
            // 尝试加载版本配置
            var versionIsolation = Services.VersionConfigService.GetVersionIsolation(versionPath);
            
            // 如果版本有独立设置，使用版本设置；否则使用全局设置
            bool useIsolation;
            if (versionIsolation.HasValue)
            {
                useIsolation = versionIsolation.Value;
            }
            else
            {
                useIsolation = GameDirectoryType == GameDirectoryType.VersionFolder;
            }
            
            return useIsolation 
                ? Path.Combine(GameDirectory, "versions", versionName)
                : GameDirectory;
        }

        /// <summary>
        /// 获取指定版本的Mods目录（根据版本隔离设置）
        /// </summary>
        /// <param name="versionName">版本名称</param>
        /// <returns>Mods目录路径</returns>
        public string GetModsDirectory(string versionName)
        {
            return Path.Combine(GetRunDirectory(versionName), "mods");
        }

        /// <summary>
        /// 获取指定版本的材质包目录（根据版本隔离设置）
        /// </summary>
        /// <param name="versionName">版本名称</param>
        /// <returns>材质包目录路径</returns>
        public string GetResourcePacksDirectory(string versionName)
        {
            return Path.Combine(GetRunDirectory(versionName), "resourcepacks");
        }

        /// <summary>
        /// 获取指定版本的光影目录（根据版本隔离设置）
        /// </summary>
        /// <param name="versionName">版本名称</param>
        /// <returns>光影目录路径</returns>
        public string GetShaderPacksDirectory(string versionName)
        {
            return Path.Combine(GetRunDirectory(versionName), "shaderpacks");
        }

        /// <summary>
        /// 获取指定版本的数据包目录（根据版本隔离设置）
        /// 通常在saves/[世界名]/datapacks，这里返回版本目录下的saves文件夹
        /// </summary>
        /// <param name="versionName">版本名称</param>
        /// <returns>Saves目录路径</returns>
        public string GetSavesDirectory(string versionName)
        {
            return Path.Combine(GetRunDirectory(versionName), "saves");
        }
    }
}

