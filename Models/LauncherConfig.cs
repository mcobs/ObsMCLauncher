using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        /// 配置文件位置类型
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DirectoryLocation ConfigFileLocation { get; set; } = DirectoryLocation.AppData;

        /// <summary>
        /// 自定义配置文件路径
        /// </summary>
        public string CustomConfigPath { get; set; } = "";

        /// <summary>
        /// 账号文件位置类型
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DirectoryLocation AccountFileLocation { get; set; } = DirectoryLocation.AppData;

        /// <summary>
        /// Java路径
        /// </summary>
        public string JavaPath { get; set; } = "javaw.exe";

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
        /// 游戏启动后关闭启动器
        /// </summary>
        public bool CloseAfterLaunch { get; set; } = false;

        /// <summary>
        /// 自动检查更新
        /// </summary>
        public bool AutoCheckUpdate { get; set; } = true;

        /// <summary>
        /// 当前选中的版本
        /// </summary>
        public string? SelectedVersion { get; set; }

        /// <summary>
        /// 获取配置文件路径（静态方法，根据临时配置决定）
        /// </summary>
        public static string GetConfigFilePath(DirectoryLocation location = DirectoryLocation.AppData, string customPath = "")
        {
            return location switch
            {
                DirectoryLocation.AppData => Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ObsMCLauncher",
                    "config.json"),
                DirectoryLocation.RunningDirectory => Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "config.json"),
                DirectoryLocation.Custom => string.IsNullOrEmpty(customPath)
                    ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json")
                    : customPath,
                _ => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json")
            };
        }

        /// <summary>
        /// 当前使用的配置文件路径
        /// </summary>
        private static string _currentConfigPath = GetConfigFilePath(DirectoryLocation.AppData);

        /// <summary>
        /// 保存配置
        /// </summary>
        public void Save()
        {
            try
            {
                // 计算新的配置文件路径
                var newConfigPath = GetConfigFilePath(ConfigFileLocation, CustomConfigPath);
                
                var directory = Path.GetDirectoryName(newConfigPath);
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
                File.WriteAllText(newConfigPath, json);

                System.Diagnostics.Debug.WriteLine($"✅ 配置已保存到: {newConfigPath}");

                // 保存引导配置文件（固定位置，记录真实配置文件位置）
                SaveBootstrapConfig(newConfigPath);

                // 如果配置路径变了，删除旧的配置文件
                if (newConfigPath != _currentConfigPath && File.Exists(_currentConfigPath))
                {
                    try
                    {
                        File.Delete(_currentConfigPath);
                        System.Diagnostics.Debug.WriteLine($"✅ 已删除旧配置文件: {_currentConfigPath}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"删除旧配置文件失败: {ex.Message}");
                    }
                }

                // 更新当前配置路径
                _currentConfigPath = newConfigPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存配置失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 引导配置文件路径（固定在运行目录，用于记录真实配置文件位置）
        /// </summary>
        private static readonly string BootstrapConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "OMCL.ini");

        /// <summary>
        /// 加载配置
        /// </summary>
        public static LauncherConfig Load()
        {
            // 1. 首先尝试从引导配置文件读取真实配置文件位置
            string? actualConfigPath = null;
            try
            {
                if (File.Exists(BootstrapConfigPath))
                {
                    var bootstrapData = File.ReadAllText(BootstrapConfigPath);
                    var bootstrapInfo = JsonSerializer.Deserialize<BootstrapConfig>(bootstrapData);
                    if (bootstrapInfo != null && File.Exists(bootstrapInfo.ConfigFilePath))
                    {
                        actualConfigPath = bootstrapInfo.ConfigFilePath;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取引导配置失败: {ex.Message}");
            }

            // 2. 如果引导配置有效，从指定位置加载
            if (!string.IsNullOrEmpty(actualConfigPath))
            {
                try
                {
                    var json = File.ReadAllText(actualConfigPath);
                    var config = JsonSerializer.Deserialize<LauncherConfig>(json);
                    if (config != null)
                    {
                        _currentConfigPath = actualConfigPath;
                        System.Diagnostics.Debug.WriteLine($"✅ 从引导配置指定的位置加载: {actualConfigPath}");
                        return config;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"从引导配置指定位置 {actualConfigPath} 加载失败: {ex.Message}");
                }
            }

            // 3. 如果引导配置失败，按优先级尝试从默认位置加载
            var locations = new[]
            {
                GetConfigFilePath(DirectoryLocation.RunningDirectory),
                GetConfigFilePath(DirectoryLocation.AppData)
            };

            foreach (var path in locations)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        var json = File.ReadAllText(path);
                        var config = JsonSerializer.Deserialize<LauncherConfig>(json);
                        if (config != null)
                        {
                            _currentConfigPath = path;
                            System.Diagnostics.Debug.WriteLine($"✅ 从默认位置加载: {path}");
                            
                            // 保存引导配置，下次直接从这里加载
                            SaveBootstrapConfig(path);
                            
                            return config;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"从 {path} 加载配置失败: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine("⚠️ 未找到配置文件，使用默认配置");
            return new LauncherConfig();
        }

        /// <summary>
        /// 保存引导配置文件
        /// </summary>
        private static void SaveBootstrapConfig(string configPath)
        {
            try
            {
                var bootstrap = new BootstrapConfig { ConfigFilePath = configPath };
                var json = JsonSerializer.Serialize(bootstrap, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(BootstrapConfigPath, json);
                System.Diagnostics.Debug.WriteLine($"✅ 引导配置已保存: {BootstrapConfigPath} -> {configPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存引导配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 引导配置类
        /// </summary>
        private class BootstrapConfig
        {
            public string ConfigFilePath { get; set; } = string.Empty;
        }


        /// <summary>
        /// 获取账号文件路径
        /// </summary>
        public string GetAccountFilePath()
        {
            return AccountFileLocation switch
            {
                DirectoryLocation.AppData => Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ObsMCLauncher",
                    "accounts.json"),
                DirectoryLocation.RunningDirectory => Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "accounts.json"),
                DirectoryLocation.Custom => Path.Combine(
                    Path.GetDirectoryName(CustomConfigPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                    "accounts.json"),
                _ => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "accounts.json")
            };
        }
    }
}

