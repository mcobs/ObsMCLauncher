using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// 版本配置信息（保存在版本文件夹中的 version_config.json）
    /// </summary>
    public class VersionConfig
    {
        /// <summary>
        /// 版本隔离设置：true=启用版本隔离，false=共享文件夹，null=使用全局设置
        /// </summary>
        public bool? UseVersionIsolation { get; set; } = null;
    }

    /// <summary>
    /// 版本配置服务 - 管理每个版本的独立配置
    /// </summary>
    public class VersionConfigService
    {
        private const string CONFIG_FILE_NAME = "version_config.json";

        /// <summary>
        /// 获取版本配置文件路径
        /// </summary>
        private static string GetConfigPath(string versionPath)
        {
            return Path.Combine(versionPath, CONFIG_FILE_NAME);
        }

        /// <summary>
        /// 加载版本配置
        /// </summary>
        public static VersionConfig LoadVersionConfig(string versionPath)
        {
            var configPath = GetConfigPath(versionPath);
            
            if (!File.Exists(configPath))
            {
                // 配置文件不存在，返回默认配置
                return new VersionConfig();
            }

            try
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<VersionConfig>(json);
                return config ?? new VersionConfig();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VersionConfig] 加载配置失败: {ex.Message}");
                return new VersionConfig();
            }
        }

        /// <summary>
        /// 保存版本配置
        /// </summary>
        public static bool SaveVersionConfig(string versionPath, VersionConfig config)
        {
            try
            {
                var configPath = GetConfigPath(versionPath);
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(configPath, json);
                Debug.WriteLine($"[VersionConfig] 配置已保存: {versionPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VersionConfig] 保存配置失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 设置版本隔离
        /// </summary>
        public static bool SetVersionIsolation(string versionPath, bool? useIsolation)
        {
            var config = LoadVersionConfig(versionPath);
            config.UseVersionIsolation = useIsolation;
            return SaveVersionConfig(versionPath, config);
        }

        /// <summary>
        /// 获取版本隔离设置
        /// </summary>
        public static bool? GetVersionIsolation(string versionPath)
        {
            var config = LoadVersionConfig(versionPath);
            return config.UseVersionIsolation;
        }
    }
}
