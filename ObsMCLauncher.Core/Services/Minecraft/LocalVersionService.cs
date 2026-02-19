using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services.Minecraft
{
    /// <summary>
    /// 自定义DateTime转换器，支持多种时间格式
    /// </summary>
    public class FlexibleDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var dateString = reader.GetString();
            if (string.IsNullOrEmpty(dateString))
                return DateTime.MinValue;

            // 尝试标准解析
            if (DateTime.TryParse(dateString, out DateTime result))
                return result;

            // 处理Fabric的特殊格式: "2025-10-07T18:03:46+0000" (缺少冒号)
            try
            {
                if (dateString.Length >= 24 && dateString.Contains("+"))
                {
                    // 找到 + 或 - 的位置
                    var tzIndex = dateString.LastIndexOf('+');
                    if (tzIndex < 0)
                        tzIndex = dateString.LastIndexOf('-');

                    if (tzIndex > 0 && dateString.Length - tzIndex == 5)
                    {
                        // 格式: +0000 或 -0000，需要转换为 +00:00 或 -00:00
                        var fixedString = dateString.Substring(0, tzIndex + 3) + ":" + dateString.Substring(tzIndex + 3);
                        if (DateTime.TryParse(fixedString, out result))
                            return result;
                    }
                }
            }
            catch { }

            DebugLogger.Warn("LocalVersion", $"无法解析时间格式: {dateString}");
            return DateTime.MinValue;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString("yyyy-MM-ddTHH:mm:ss+00:00"));
        }
    }

    /// <summary>
    /// 本地已安装版本信息
    /// </summary>
    public class InstalledVersion
    {
        public string Id { get; set; } = ""; // 文件夹名称（自定义名称）
        public string ActualVersionId { get; set; } = ""; // JSON中的实际版本ID
        public string Type { get; set; } = "";
        public DateTime ReleaseTime { get; set; }
        public DateTime LastPlayed { get; set; }
        public string Path { get; set; } = "";
        public bool IsSelected { get; set; }
        
        /// <summary>
        /// 加载器类型：null/空=原版, "Forge", "Fabric", "OptiFine", "Quilt"
        /// </summary>
        public string? LoaderType { get; set; }
        
        /// <summary>
        /// 版本隔离设置：true=启用版本隔离（独立文件夹），false=共享文件夹
        /// 整合包默认为 true，普通版本默认使用全局设置
        /// </summary>
        public bool? UseVersionIsolation { get; set; } = null; // null表示使用全局设置

        /// <summary>
        /// UI 绑定属性：获取详情描述文本
        /// </summary>
        public string DetailText
        {
            get
            {
                var typeStr = Type switch
                {
                    "release" => "正式版",
                    "snapshot" => "快照版",
                    _ => "其他"
                };
                var loaderStr = string.IsNullOrEmpty(LoaderType) ? "" : $" · {LoaderType}";
                return $"{typeStr}{loaderStr} · 上次游玩: {LastPlayed:yyyy-MM-dd HH:mm}";
            }
        }

        /// <summary>
        /// UI 绑定属性：获取加载器图标路径
        /// </summary>
        public string LoaderIconPath
        {
            get
            {
                var iconName = LoaderType?.ToLower() switch
                {
                    "forge" => "forge.png",
                    "fabric" => "fabric.png",
                    "neoforge" => "neoforged.png",
                    "quilt" => "quilt.png",
                    "optifine" => "optifine.png",
                    _ => Type == "snapshot" ? "vanilia_snapshot.png" : 
                         (IsVersionLessThanOrEqual(ActualVersionId, "1.12.2") ? "vanilla_old.png" : "vanilla.png")
                };
                return $"avares://ObsMCLauncher.Desktop/Assets/LoaderIcons/{iconName}";
            }
        }

        private static bool IsVersionLessThanOrEqual(string version, string targetVersion)
        {
            try
            {
                var vParts = version.Split('.');
                var tParts = targetVersion.Split('.');
                for (int i = 0; i < Math.Min(vParts.Length, tParts.Length); i++)
                {
                    if (int.TryParse(vParts[i], out int v) && int.TryParse(tParts[i], out int t))
                    {
                        if (v < t) return true;
                        if (v > t) return false;
                    }
                }
                return vParts.Length <= tParts.Length;
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// 本地版本管理服务
    /// </summary>
    public class LocalVersionService
    {
        /// <summary>
        /// 获取所有已安装的版本
        /// </summary>
        public static List<InstalledVersion> GetInstalledVersions(string gameDirectory)
        {
            var installedVersions = new List<InstalledVersion>();
            
            try
            {
                var versionsPath = System.IO.Path.Combine(gameDirectory, "versions");
                
                if (!Directory.Exists(versionsPath))
                {
                    DebugLogger.Warn("LocalVersion", $"版本目录不存在: {versionsPath}");
                    return installedVersions;
                }

                var versionDirs = Directory.GetDirectories(versionsPath);
                DebugLogger.Info("LocalVersion", $"找到 {versionDirs.Length} 个版本文件夹");

                foreach (var versionDir in versionDirs)
                {
                    var versionId = System.IO.Path.GetFileName(versionDir);
                    var jsonPath = System.IO.Path.Combine(versionDir, $"{versionId}.json");
                    var jarPath = System.IO.Path.Combine(versionDir, $"{versionId}.jar");

                    // 检查JSON文件是否存在（必须）
                    if (!File.Exists(jsonPath))
                    {
                        DebugLogger.Warn("LocalVersion", $"版本 {versionId} 缺少JSON文件，跳过");
                        continue;
                    }
                    
                    // 读取JSON以判断是否为Forge等Mod加载器
                    string jsonContent = "";
                    try
                    {
                        jsonContent = File.ReadAllText(jsonPath);
                    }
                    catch
                    {
                        DebugLogger.Warn("LocalVersion", $"版本 {versionId} JSON读取失败，跳过");
                        continue;
                    }
                    
                    // 检测是否为Mod加载器版本（Forge/Fabric等的JAR在libraries中，不需要检查版本文件夹的JAR）
                    var loaderType = DetectLoaderType(jsonContent);
                    bool isModLoader = !string.IsNullOrEmpty(loaderType);
                    
                    // 对于原版，必须有JAR文件；对于Mod加载器，JAR在libraries中
                    if (!isModLoader && !File.Exists(jarPath))
                    {
                        DebugLogger.Warn("LocalVersion", $"版本 {versionId} 缺少JAR文件，跳过");
                        continue;
                    }

                    try
                    {
                        // 解析版本JSON，使用自定义DateTime转换器
                        var options = new JsonSerializerOptions 
                        { 
                            PropertyNameCaseInsensitive = true,
                            Converters = { new FlexibleDateTimeConverter() }
                        };
                        var versionData = JsonSerializer.Deserialize<VersionJsonData>(jsonContent, options);

                        if (versionData != null)
                        {
                            var lastPlayed = Directory.GetLastAccessTime(versionDir);
                            
                            // 加载版本配置
                            var versionConfig = VersionConfigService.LoadVersionConfig(versionDir);
                            
                            installedVersions.Add(new InstalledVersion
                            {
                                Id = versionId, // 文件夹名称（显示名称）
                                ActualVersionId = versionData.Id ?? versionId, // JSON中的实际版本ID
                                Type = versionData.Type ?? "release",
                                ReleaseTime = versionData.ReleaseTime,
                                LastPlayed = lastPlayed,
                                Path = versionDir,
                                IsSelected = false,
                                LoaderType = loaderType,
                                UseVersionIsolation = versionConfig.UseVersionIsolation
                            });
                            
                            var loaderInfo = string.IsNullOrEmpty(loaderType) ? "" : $" [{loaderType}]";
                            var isolationInfo = versionConfig.UseVersionIsolation.HasValue ? 
                                (versionConfig.UseVersionIsolation.Value ? " [版本隔离]" : " [共享]") : "";
                            DebugLogger.Info("LocalVersion", $"找到版本: {versionId} (实际版本: {versionData.Id}){loaderInfo}{isolationInfo}");
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Error("LocalVersion", $"解析版本 {versionId} JSON 失败: {ex.Message}");
                    }
                }

                // 按选中状态和最后游玩时间排序（选中的在最前面）
                var selectedVersionId = GetSelectedVersion();
                installedVersions = installedVersions
                    .OrderByDescending(v => v.Id == selectedVersionId)
                    .ThenByDescending(v => v.LastPlayed)
                    .ToList();
                DebugLogger.Info("LocalVersion", $"成功加载 {installedVersions.Count} 个已安装版本");
            }
            catch (Exception ex)
            {
                DebugLogger.Error("LocalVersion", $"获取已安装版本失败: {ex.Message}");
            }

            return installedVersions;
        }

        /// <summary>
        /// 获取当前选中的版本
        /// </summary>
        public static string? GetSelectedVersion()
        {
            var config = LauncherConfig.Load();
            return config.SelectedVersion;
        }

        /// <summary>
        /// 设置选中的版本
        /// </summary>
        public static void SetSelectedVersion(string versionId)
        {
            var config = LauncherConfig.Load();
            config.SelectedVersion = versionId;
            config.Save();
            DebugLogger.Info("LocalVersion", $"已选择版本: {versionId}");
        }

        /// <summary>
        /// 打开版本文件夹
        /// </summary>
        public static void OpenVersionFolder(string versionPath)
        {
            try
            {
                if (Directory.Exists(versionPath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", versionPath);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error("LocalVersion", $"打开文件夹失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 删除版本
        /// </summary>
        public static bool DeleteVersion(string versionPath)
        {
            try
            {
                if (Directory.Exists(versionPath))
                {
                    Directory.Delete(versionPath, true);
                    DebugLogger.Info("LocalVersion", $"已删除版本: {versionPath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error("LocalVersion", $"删除版本失败: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// 检测版本的加载器类型
        /// </summary>
        private static string? DetectLoaderType(string jsonContent)
        {
            try
            {
                // 转换为小写以便不区分大小写匹配
                var jsonLower = jsonContent.ToLower();
                
                // 检测NeoForge
                if (jsonLower.Contains("\"neoforged\"") || 
                    jsonLower.Contains("\"net.neoforged\""))
                {
                    return "NeoForge";
                }

                // 检测Forge - 通过mainClass或libraries
                if (jsonLower.Contains("\"minecraftforge\"") || 
                    jsonLower.Contains("\"net.minecraftforge\"") ||
                    (jsonLower.Contains("forge") && jsonLower.Contains("\"mainclass\"")))
                {
                    return "Forge";
                }
                
                // 检测Fabric
                if (jsonLower.Contains("\"fabricmc\"") || 
                    jsonLower.Contains("\"net.fabricmc\"") ||
                    jsonLower.Contains("fabric") && jsonLower.Contains("\"mainclass\""))
                {
                    return "Fabric";
                }
                
                // 检测OptiFine
                if (jsonLower.Contains("optifine"))
                {
                    return "OptiFine";
                }
                
                // 检测Quilt
                if (jsonLower.Contains("quilt"))
                {
                    return "Quilt";
                }
                
                return null; // 原版
            }
            catch
            {
                return null;
            }
        }

        // JSON 数据模型
        private class VersionJsonData
        {
            public string? Id { get; set; }
            public string? Type { get; set; }
            public DateTime ReleaseTime { get; set; }
        }
    }
}
