using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// 本地已安装版本信息
    /// </summary>
    public class InstalledVersion
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public DateTime ReleaseTime { get; set; }
        public DateTime LastPlayed { get; set; }
        public string Path { get; set; } = "";
        public bool IsSelected { get; set; }
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
                    System.Diagnostics.Debug.WriteLine($"版本目录不存在: {versionsPath}");
                    return installedVersions;
                }

                var versionDirs = Directory.GetDirectories(versionsPath);
                System.Diagnostics.Debug.WriteLine($"找到 {versionDirs.Length} 个版本文件夹");

                foreach (var versionDir in versionDirs)
                {
                    var versionId = System.IO.Path.GetFileName(versionDir);
                    var jsonPath = System.IO.Path.Combine(versionDir, $"{versionId}.json");
                    var jarPath = System.IO.Path.Combine(versionDir, $"{versionId}.jar");

                    // 检查必要文件是否存在
                    if (!File.Exists(jsonPath) || !File.Exists(jarPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"版本 {versionId} 不完整，跳过");
                        continue;
                    }

                    try
                    {
                        // 读取版本JSON
                        var jsonContent = File.ReadAllText(jsonPath);
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var versionData = JsonSerializer.Deserialize<VersionJsonData>(jsonContent, options);

                        if (versionData != null)
                        {
                            var lastPlayed = Directory.GetLastAccessTime(versionDir);
                            
                            installedVersions.Add(new InstalledVersion
                            {
                                Id = versionId,
                                Type = versionData.Type ?? "release",
                                ReleaseTime = versionData.ReleaseTime,
                                LastPlayed = lastPlayed,
                                Path = versionDir,
                                IsSelected = false
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"解析版本 {versionId} JSON 失败: {ex.Message}");
                    }
                }

                // 按最后游玩时间排序
                installedVersions = installedVersions.OrderByDescending(v => v.LastPlayed).ToList();
                System.Diagnostics.Debug.WriteLine($"✅ 成功加载 {installedVersions.Count} 个已安装版本");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 获取已安装版本失败: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"已选择版本: {versionId}");
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
                System.Diagnostics.Debug.WriteLine($"打开文件夹失败: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine($"✅ 已删除版本: {versionPath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 删除版本失败: {ex.Message}");
            }
            return false;
        }

        // JSON 数据模型
        private class VersionJsonData
        {
            public string? Type { get; set; }
            public DateTime ReleaseTime { get; set; }
        }
    }
}

