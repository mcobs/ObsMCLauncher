using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// 游戏截图管理器
    /// </summary>
    public class ScreenshotManager
    {
        private static ScreenshotManager? _instance;
        public static ScreenshotManager Instance => _instance ??= new ScreenshotManager();

        private ScreenshotManager() { }

        /// <summary>
        /// 获取所有截图列表（包括主目录和版本隔离目录）
        /// </summary>
        /// <param name="gameDirectory">游戏目录</param>
        /// <param name="versionName">版本名称（可选，如果指定则只扫描该版本的截图）</param>
        /// <returns>截图列表</returns>
        public List<ScreenshotInfo> GetScreenshots(string gameDirectory, string? versionName = null)
        {
            var screenshots = new List<ScreenshotInfo>();
            var config = Models.LauncherConfig.Load();

            // 1. 扫描主目录的截图（如果未指定版本或版本未启用隔离）
            var mainScreenshotsDir = Path.Combine(gameDirectory, "screenshots");
            if (Directory.Exists(mainScreenshotsDir) && (versionName == null || !IsVersionIsolated(config, versionName)))
            {
                screenshots.AddRange(ScanScreenshotsFromDirectory(mainScreenshotsDir, null));
            }

            // 2. 扫描版本隔离目录的截图
            if (versionName == null)
            {
                // 扫描所有版本的截图目录
                var versionsDir = Path.Combine(gameDirectory, "versions");
                if (Directory.Exists(versionsDir))
                {
                    var versionDirs = Directory.GetDirectories(versionsDir);
                    foreach (var versionDir in versionDirs)
                    {
                        var vName = Path.GetFileName(versionDir);
                        if (IsVersionIsolated(config, vName))
                        {
                            var versionScreenshotsDir = Path.Combine(versionDir, "screenshots");
                            if (Directory.Exists(versionScreenshotsDir))
                            {
                                screenshots.AddRange(ScanScreenshotsFromDirectory(versionScreenshotsDir, vName));
                            }
                        }
                    }
                }
            }
            else
            {
                // 只扫描指定版本的截图目录
                var versionDir = Path.Combine(gameDirectory, "versions", versionName);
                if (Directory.Exists(versionDir) && IsVersionIsolated(config, versionName))
                {
                    var versionScreenshotsDir = Path.Combine(versionDir, "screenshots");
                    if (Directory.Exists(versionScreenshotsDir))
                    {
                        screenshots.AddRange(ScanScreenshotsFromDirectory(versionScreenshotsDir, versionName));
                    }
                }
            }

            // 按创建时间排序（最新的在前）
            return screenshots.OrderByDescending(s => s.CreatedTime).ToList();
        }

        /// <summary>
        /// 扫描指定目录的截图
        /// </summary>
        private List<ScreenshotInfo> ScanScreenshotsFromDirectory(string screenshotsDir, string? versionName)
        {
            var screenshots = new List<ScreenshotInfo>();

            try
            {
                var imageExtensions = new[] { ".png", ".jpg", ".jpeg" };
                var files = Directory.GetFiles(screenshotsDir)
                    .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        screenshots.Add(new ScreenshotInfo
                        {
                            FileName = Path.GetFileName(file),
                            FullPath = file,
                            Size = fileInfo.Length,
                            CreatedTime = fileInfo.CreationTime,
                            LastModified = fileInfo.LastWriteTime,
                            VersionName = versionName
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ScreenshotManager] 读取截图文件失败: {file}, 错误: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScreenshotManager] 扫描截图目录失败: {screenshotsDir}, 错误: {ex.Message}");
            }

            return screenshots;
        }

        /// <summary>
        /// 检查版本是否启用了版本隔离
        /// </summary>
        private bool IsVersionIsolated(Models.LauncherConfig config, string versionName)
        {
            var versionPath = Path.Combine(config.GameDirectory, "versions", versionName);
            var versionIsolation = Services.VersionConfigService.GetVersionIsolation(versionPath);
            
            // 如果版本有独立设置，使用版本设置；否则使用全局设置
            if (versionIsolation.HasValue)
            {
                return versionIsolation.Value;
            }
            else
            {
                return config.GameDirectoryType == Models.GameDirectoryType.VersionFolder;
            }
        }

        /// <summary>
        /// 获取所有有截图的版本列表
        /// </summary>
        public List<string> GetVersionsWithScreenshots(string gameDirectory)
        {
            var versions = new List<string>();
            var config = Models.LauncherConfig.Load();

            // 检查主目录是否有截图（无论是否启用版本隔离，主目录都可能存在截图）
            var mainScreenshotsDir = Path.Combine(gameDirectory, "screenshots");
            bool hasMainScreenshots = Directory.Exists(mainScreenshotsDir) && HasScreenshots(mainScreenshotsDir);

            // 检查版本隔离目录
            var versionsWithScreenshots = new List<string>();
            var versionsDir = Path.Combine(gameDirectory, "versions");
            if (Directory.Exists(versionsDir))
            {
                var versionDirs = Directory.GetDirectories(versionsDir);
                foreach (var versionDir in versionDirs)
                {
                    var versionName = Path.GetFileName(versionDir);
                    if (IsVersionIsolated(config, versionName))
                    {
                        var versionScreenshotsDir = Path.Combine(versionDir, "screenshots");
                        if (Directory.Exists(versionScreenshotsDir) && HasScreenshots(versionScreenshotsDir))
                        {
                            versionsWithScreenshots.Add(versionName);
                        }
                    }
                }
            }

            // 如果有任何截图（主目录或版本隔离目录），添加"全部"选项
            if (hasMainScreenshots || versionsWithScreenshots.Count > 0)
            {
                versions.Add("全部");
            }

            // 添加有截图的版本
            versions.AddRange(versionsWithScreenshots);

            return versions;
        }

        /// <summary>
        /// 检查目录是否有截图文件
        /// </summary>
        private bool HasScreenshots(string screenshotsDir)
        {
            try
            {
                var imageExtensions = new[] { ".png", ".jpg", ".jpeg" };
                return Directory.GetFiles(screenshotsDir)
                    .Any(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 删除截图
        /// </summary>
        /// <param name="screenshotPath">截图路径</param>
        /// <returns>是否成功</returns>
        public bool DeleteScreenshot(string screenshotPath)
        {
            try
            {
                if (File.Exists(screenshotPath))
                {
                    File.Delete(screenshotPath);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScreenshotManager] 删除截图失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 导出截图到指定目录
        /// </summary>
        /// <param name="screenshotPath">截图路径</param>
        /// <param name="targetDirectory">目标目录</param>
        /// <returns>导出后的文件路径，失败返回null</returns>
        public string? ExportScreenshot(string screenshotPath, string targetDirectory)
        {
            try
            {
                if (!File.Exists(screenshotPath))
                    return null;

                if (!Directory.Exists(targetDirectory))
                    Directory.CreateDirectory(targetDirectory);

                var fileName = Path.GetFileName(screenshotPath);
                var targetPath = Path.Combine(targetDirectory, fileName);

                // 如果目标文件已存在，添加序号
                int counter = 1;
                while (File.Exists(targetPath))
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    targetPath = Path.Combine(targetDirectory, $"{nameWithoutExt}_{counter}{ext}");
                    counter++;
                }

                File.Copy(screenshotPath, targetPath, true);
                return targetPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScreenshotManager] 导出截图失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 按日期筛选截图
        /// </summary>
        /// <param name="screenshots">截图列表</param>
        /// <param name="startDate">开始日期（可选）</param>
        /// <param name="endDate">结束日期（可选）</param>
        /// <returns>筛选后的截图列表</returns>
        public List<ScreenshotInfo> FilterByDate(List<ScreenshotInfo> screenshots, DateTime? startDate = null, DateTime? endDate = null)
        {
            var filtered = screenshots.AsEnumerable();

            if (startDate.HasValue)
                filtered = filtered.Where(s => s.CreatedTime >= startDate.Value);

            if (endDate.HasValue)
                filtered = filtered.Where(s => s.CreatedTime <= endDate.Value.AddDays(1)); // 包含结束日期当天

            return filtered.ToList();
        }
    }
}

