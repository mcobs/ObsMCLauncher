using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

public class ScreenshotManager
{
    private static ScreenshotManager? _instance;
    public static ScreenshotManager Instance => _instance ??= new ScreenshotManager();

    private ScreenshotManager() { }

    public List<ScreenshotInfo> GetScreenshots(string gameDirectory, string? versionName = null)
    {
        var screenshots = new List<ScreenshotInfo>();
        var config = LauncherConfig.Load();

        if (string.IsNullOrEmpty(versionName) || versionName == "全部")
        {
            var mainScreenshotsDir = Path.Combine(gameDirectory, "screenshots");
            if (Directory.Exists(mainScreenshotsDir))
            {
                screenshots.AddRange(ScanScreenshotsFromDirectory(mainScreenshotsDir, "主目录"));
            }

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
            var versionDir = Path.Combine(gameDirectory, "versions", versionName);
            if (Directory.Exists(versionDir))
            {
                if (IsVersionIsolated(config, versionName))
                {
                    var versionScreenshotsDir = Path.Combine(versionDir, "screenshots");
                    if (Directory.Exists(versionScreenshotsDir))
                    {
                        screenshots.AddRange(ScanScreenshotsFromDirectory(versionScreenshotsDir, versionName));
                    }
                }
                else
                {
                    var mainScreenshotsDir = Path.Combine(gameDirectory, "screenshots");
                    if (Directory.Exists(mainScreenshotsDir))
                    {
                        screenshots.AddRange(ScanScreenshotsFromDirectory(mainScreenshotsDir, "主目录"));
                    }
                }
            }
        }

        return screenshots.OrderByDescending(s => s.CreatedTime).ToList();
    }

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
                    DebugLogger.Warn("Screenshot", $"读取截图文件失败: {file}, 错误: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("Screenshot", $"扫描截图目录失败: {screenshotsDir}, 错误: {ex.Message}");
        }

        return screenshots;
    }

    private bool IsVersionIsolated(LauncherConfig config, string versionName)
    {
        var versionPath = Path.Combine(config.GameDirectory, "versions", versionName);
        var versionIsolation = VersionConfigService.GetVersionIsolation(versionPath);

        if (versionIsolation.HasValue)
        {
            return versionIsolation.Value;
        }
        else
        {
            return config.GameDirectoryType == GameDirectoryType.VersionFolder;
        }
    }

    public List<string> GetVersionsWithScreenshots(string gameDirectory)
    {
        var versions = new List<string>();
        var config = LauncherConfig.Load();

        var mainScreenshotsDir = Path.Combine(gameDirectory, "screenshots");
        bool hasMainScreenshots = Directory.Exists(mainScreenshotsDir) && HasScreenshots(mainScreenshotsDir);

        var versionsWithScreenshots = new List<string>();
        var versionsDir = Path.Combine(gameDirectory, "versions");
        if (Directory.Exists(versionsDir))
        {
            var versionDirs = Directory.GetDirectories(versionsDir);
            foreach (var versionDir in versionDirs)
            {
                var versionName = Path.GetFileName(versionDir);
                var versionScreenshotsDir = Path.Combine(versionDir, "screenshots");
                if (Directory.Exists(versionScreenshotsDir) && HasScreenshots(versionScreenshotsDir))
                {
                    versionsWithScreenshots.Add(versionName);
                }
            }
        }

        if (hasMainScreenshots || versionsWithScreenshots.Count > 0)
        {
            versions.Add("全部");
        }

        if (hasMainScreenshots)
        {
            versions.Add("主目录");
        }

        versions.AddRange(versionsWithScreenshots);

        return versions;
    }

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
            DebugLogger.Error("Screenshot", $"删除截图失败: {ex.Message}");
            return false;
        }
    }

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
            DebugLogger.Error("Screenshot", $"导出截图失败: {ex.Message}");
            return null;
        }
    }

    public List<ScreenshotInfo> FilterByDate(List<ScreenshotInfo> screenshots, DateTime? startDate = null, DateTime? endDate = null)
    {
        var filtered = screenshots.AsEnumerable();

        if (startDate.HasValue)
            filtered = filtered.Where(s => s.CreatedTime >= startDate.Value);

        if (endDate.HasValue)
            filtered = filtered.Where(s => s.CreatedTime <= endDate.Value.AddDays(1));

        return filtered.ToList();
    }
}
