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
        /// 游戏目录
        /// </summary>
        public string GameDirectory { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            ".minecraft");

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
        /// 游戏启动后关闭启动器
        /// </summary>
        public bool CloseAfterLaunch { get; set; } = false;

        /// <summary>
        /// 自动检查更新
        /// </summary>
        public bool AutoCheckUpdate { get; set; } = true;

        /// <summary>
        /// 配置文件路径
        /// </summary>
        private static readonly string ConfigFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ObsMCLauncher",
            "config.json");

        /// <summary>
        /// 保存配置
        /// </summary>
        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory!);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载配置
        /// </summary>
        public static LauncherConfig Load()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var json = File.ReadAllText(ConfigFilePath);
                    var config = JsonSerializer.Deserialize<LauncherConfig>(json);
                    return config ?? new LauncherConfig();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载配置失败: {ex.Message}");
            }

            return new LauncherConfig();
        }
    }
}

