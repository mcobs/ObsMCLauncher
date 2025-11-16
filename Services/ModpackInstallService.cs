using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// 整合包安装服务
    /// </summary>
    public class ModpackInstallService
    {
        /// <summary>
        /// 安装整合包
        /// </summary>
        /// <param name="zipFilePath">整合包zip文件路径</param>
        /// <param name="versionName">版本名称</param>
        /// <param name="gameDirectory">游戏目录</param>
        /// <param name="progressCallback">进度回调</param>
        /// <returns>是否安装成功</returns>
        public static async Task<bool> InstallModpackAsync(
            string zipFilePath,
            string versionName,
            string gameDirectory,
            Action<string, double>? progressCallback = null)
        {
            try
            {
                Debug.WriteLine($"[ModpackInstall] 开始安装整合包: {versionName}");
                progressCallback?.Invoke("正在准备安装...", 0);

                // 创建版本目录
                var versionDir = Path.Combine(gameDirectory, "versions", versionName);
                if (Directory.Exists(versionDir))
                {
                    Debug.WriteLine($"[ModpackInstall] 版本目录已存在，删除旧版本");
                    Directory.Delete(versionDir, true);
                }
                Directory.CreateDirectory(versionDir);
                
                Debug.WriteLine($"[ModpackInstall] 整合包默认开启版本隔离，mods等文件将安装到版本目录");
                
                // 为整合包设置版本隔离
                VersionConfigService.SetVersionIsolation(versionDir, true);
                Debug.WriteLine($"[ModpackInstall] 已为整合包 {versionName} 启用版本隔离");

                progressCallback?.Invoke("正在解析整合包格式...", 10);

                // 检测整合包类型并安装
                using (var archive = ZipFile.OpenRead(zipFilePath))
                {
                    var modpackType = DetectModpackType(archive);
                    Debug.WriteLine($"[ModpackInstall] 检测到整合包类型: {modpackType}");

                    switch (modpackType)
                    {
                        case ModpackType.CurseForge:
                            await InstallCurseForgeModpackAsync(archive, versionName, versionDir, gameDirectory, progressCallback);
                            break;
                        case ModpackType.Modrinth:
                            await InstallModrinthModpackAsync(archive, versionName, versionDir, gameDirectory, progressCallback);
                            break;
                        case ModpackType.Manual:
                            await InstallManualModpackAsync(archive, versionName, versionDir, progressCallback);
                            break;
                        default:
                            throw new Exception("不支持的整合包格式");
                    }
                }

                progressCallback?.Invoke("安装完成", 100);
                Debug.WriteLine($"[ModpackInstall] 整合包安装完成: {versionName}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModpackInstall] 安装失败: {ex.Message}");
                throw new Exception($"整合包安装失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 检测整合包类型
        /// </summary>
        private static ModpackType DetectModpackType(ZipArchive archive)
        {
            // 检查 CurseForge 格式 (manifest.json)
            if (archive.GetEntry("manifest.json") != null)
            {
                return ModpackType.CurseForge;
            }

            // 检查 Modrinth 格式 (modrinth.index.json)
            if (archive.GetEntry("modrinth.index.json") != null)
            {
                return ModpackType.Modrinth;
            }

            // 手动创建的整合包（包含 .minecraft 或 versions 文件夹）
            if (archive.Entries.Any(e => e.FullName.Contains(".minecraft/") || e.FullName.StartsWith("versions/")))
            {
                return ModpackType.Manual;
            }

            return ModpackType.Unknown;
        }

        /// <summary>
        /// 安装 CurseForge 整合包
        /// </summary>
        private static async Task InstallCurseForgeModpackAsync(
            ZipArchive archive,
            string versionName,
            string versionDir,
            string gameDirectory,
            Action<string, double>? progressCallback)
        {
            progressCallback?.Invoke("正在解析 CurseForge 整合包...", 5);

            // 读取 manifest.json
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry == null)
                throw new Exception("找不到 manifest.json");

            using var manifestStream = manifestEntry.Open();
            using var reader = new StreamReader(manifestStream);
            var manifestJson = await reader.ReadToEndAsync();
            var manifest = JsonSerializer.Deserialize<CurseForgeManifest>(manifestJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (manifest == null)
                throw new Exception("无法解析 manifest.json");

            var minecraftVersion = manifest.Minecraft?.Version ?? "1.20.1";
            Debug.WriteLine($"[ModpackInstall] CurseForge 整合包: {manifest.Name}, Minecraft {minecraftVersion}");

            // 1. 先下载Minecraft核心版本 (5-35%)
            progressCallback?.Invoke($"正在下载 Minecraft {minecraftVersion}...", 10);
            
            var downloadProgress = new Progress<DownloadProgress>(p =>
            {
                // Minecraft下载占5-35%的进度
                var overallProgress = 5 + (p.OverallPercentage / 100.0 * 30);
                progressCallback?.Invoke($"正在下载 Minecraft {minecraftVersion}... {p.OverallPercentage:F0}%", overallProgress);
            });

            bool mcDownloaded = await DownloadService.DownloadMinecraftVersion(
                minecraftVersion,
                gameDirectory,
                versionName,
                downloadProgress
            );

            if (!mcDownloaded)
            {
                throw new Exception($"下载 Minecraft {minecraftVersion} 失败");
            }

            progressCallback?.Invoke("正在解压整合包文件...", 40);

            // 2. 解压 overrides 文件夹到版本目录 (40-70%)
            var overridesPrefix = manifest.Overrides ?? "overrides";
            var overrideEntries = archive.Entries.Where(e => e.FullName.StartsWith(overridesPrefix + "/")).ToList();
            
            int processedFiles = 0;
            int lastReportedPercent = 0;
            
            foreach (var entry in overrideEntries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // 跳过目录

                var relativePath = entry.FullName.Substring(overridesPrefix.Length + 1);
                var destPath = Path.Combine(versionDir, relativePath);
                
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, true);

                processedFiles++;
                var progress = 40 + (processedFiles / (double)overrideEntries.Count * 30);
                var currentPercent = (int)progress;
                
                // 每5%或每100个文件报告一次进度
                if (currentPercent - lastReportedPercent >= 5 || processedFiles % 100 == 0 || processedFiles == overrideEntries.Count)
                {
                    progressCallback?.Invoke($"正在解压文件... ({processedFiles}/{overrideEntries.Count})", progress);
                    lastReportedPercent = currentPercent;
                }
            }

            progressCallback?.Invoke("整合包安装完成", 100);
        }

        /// <summary>
        /// 安装 Modrinth 整合包
        /// </summary>
        private static async Task InstallModrinthModpackAsync(
            ZipArchive archive,
            string versionName,
            string versionDir,
            string gameDirectory,
            Action<string, double>? progressCallback)
        {
            progressCallback?.Invoke("正在解析 Modrinth 整合包...", 5);

            // 读取 modrinth.index.json
            var indexEntry = archive.GetEntry("modrinth.index.json");
            if (indexEntry == null)
                throw new Exception("找不到 modrinth.index.json");

            using var indexStream = indexEntry.Open();
            using var reader = new StreamReader(indexStream);
            var indexJson = await reader.ReadToEndAsync();
            var index = JsonSerializer.Deserialize<ModrinthIndex>(indexJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (index == null)
                throw new Exception("无法解析 modrinth.index.json");

            var minecraftVersion = index.Dependencies?.Minecraft ?? "1.20.1";
            Debug.WriteLine($"[ModpackInstall] Modrinth 整合包: {index.Name}, Minecraft {minecraftVersion}");

            // 1. 先下载Minecraft核心版本 (5-35%)
            progressCallback?.Invoke($"正在下载 Minecraft {minecraftVersion}...", 10);
            
            var downloadProgress = new Progress<DownloadProgress>(p =>
            {
                // Minecraft下载占5-35%的进度
                var overallProgress = 5 + (p.OverallPercentage / 100.0 * 30);
                progressCallback?.Invoke($"正在下载 Minecraft {minecraftVersion}... {p.OverallPercentage:F0}%", overallProgress);
            });

            bool mcDownloaded = await DownloadService.DownloadMinecraftVersion(
                minecraftVersion,
                gameDirectory,
                versionName,
                downloadProgress
            );

            if (!mcDownloaded)
            {
                throw new Exception($"下载 Minecraft {minecraftVersion} 失败");
            }

            progressCallback?.Invoke("正在解压整合包文件...", 40);

            // 2. 解压 overrides 文件夹到版本目录 (40-70%)
            var overrideEntries = archive.Entries.Where(e => e.FullName.StartsWith("overrides/") || e.FullName.StartsWith("client-overrides/")).ToList();
            
            int processedFiles = 0;
            int lastReportedPercent = 0;
            
            foreach (var entry in overrideEntries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                string relativePath;
                if (entry.FullName.StartsWith("overrides/"))
                {
                    relativePath = entry.FullName.Substring("overrides/".Length);
                }
                else
                {
                    relativePath = entry.FullName.Substring("client-overrides/".Length);
                }

                var destPath = Path.Combine(versionDir, relativePath);
                
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, true);

                processedFiles++;
                var progress = 40 + (processedFiles / (double)overrideEntries.Count * 30);
                var currentPercent = (int)progress;
                
                // 每5%或每100个文件报告一次进度
                if (currentPercent - lastReportedPercent >= 5 || processedFiles % 100 == 0 || processedFiles == overrideEntries.Count)
                {
                    progressCallback?.Invoke($"正在解压文件... ({processedFiles}/{overrideEntries.Count})", progress);
                    lastReportedPercent = currentPercent;
                }
            }

            progressCallback?.Invoke("整合包安装完成", 100);
        }

        /// <summary>
        /// 安装手动创建的整合包
        /// </summary>
        private static async Task InstallManualModpackAsync(
            ZipArchive archive,
            string versionName,
            string versionDir,
            Action<string, double>? progressCallback)
        {
            progressCallback?.Invoke("正在解压手动整合包...", 30);

            // 查找 .minecraft 目录或直接解压所有文件
            var minecraftDir = FindMinecraftDirectory(archive);
            var entries = minecraftDir != null
                ? archive.Entries.Where(e => e.FullName.StartsWith(minecraftDir)).ToList()
                : archive.Entries.ToList();

            int processedFiles = 0;
            int lastReportedPercent = 0;
            
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var relativePath = minecraftDir != null
                    ? entry.FullName.Substring(minecraftDir.Length)
                    : entry.FullName;

                var destPath = Path.Combine(versionDir, relativePath);
                
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, true);

                processedFiles++;
                var progress = 30 + (processedFiles / (double)entries.Count * 70);
                var currentPercent = (int)progress;
                
                // 每5%或每100个文件报告一次进度
                if (currentPercent - lastReportedPercent >= 5 || processedFiles % 100 == 0 || processedFiles == entries.Count)
                {
                    progressCallback?.Invoke($"正在解压文件... ({processedFiles}/{entries.Count})", progress);
                    lastReportedPercent = currentPercent;
                }
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 查找整合包中的 .minecraft 目录
        /// </summary>
        private static string? FindMinecraftDirectory(ZipArchive archive)
        {
            // 查找包含 versions 目录的路径
            var versionsEntry = archive.Entries.FirstOrDefault(e => e.FullName.Contains("versions/"));
            if (versionsEntry != null)
            {
                var parts = versionsEntry.FullName.Split('/');
                var versionIndex = Array.IndexOf(parts, "versions");
                if (versionIndex > 0)
                {
                    return string.Join("/", parts.Take(versionIndex)) + "/";
                }
            }

            return null;
        }

        #region 数据模型

        private enum ModpackType
        {
            Unknown,
            CurseForge,
            Modrinth,
            Manual
        }

        private class CurseForgeManifest
        {
            public string? Name { get; set; }
            public string? Version { get; set; }
            public string? Author { get; set; }
            public string? Overrides { get; set; }
            public MinecraftInfo? Minecraft { get; set; }
        }

        private class MinecraftInfo
        {
            public string? Version { get; set; }
        }

        private class ModrinthIndex
        {
            public string? Name { get; set; }
            public string? Summary { get; set; }
            public ModrinthDependencies? Dependencies { get; set; }
        }

        private class ModrinthDependencies
        {
            public string? Minecraft { get; set; }
        }

        #endregion
    }
}

