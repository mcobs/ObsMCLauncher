using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
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

            var minecraftVersion = string.IsNullOrWhiteSpace(manifest.Minecraft?.Version) ? "1.20.1" : manifest.Minecraft.Version;
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

            // 2. 安装加载器（使用 manifest 中的 primary loader）
            await InstallCurseForgeModLoaderAsync(manifest, gameDirectory, versionName, progressCallback);

            // 3. 批量下载资源（CurseForge manifest.files）
            await DownloadCurseForgeModsAsync(manifest.Files, gameDirectory, versionName, progressCallback);

            progressCallback?.Invoke("正在解压整合包文件...", 70);

            // 4. 解压 overrides 文件夹到运行目录（确保 resourcepacks/shaderpacks/config 等位置正确）
            var config = LauncherConfig.Load();
            var runDir = config.GetRunDirectory(versionName);

            var overridesPrefix = string.IsNullOrWhiteSpace(manifest.Overrides) ? "overrides" : manifest.Overrides;
            var overrideEntries = archive.Entries.Where(e => e.FullName.StartsWith(overridesPrefix + "/")).ToList();
            
            int processedFiles = 0;
            int lastReportedPercent = 0;
            
            foreach (var entry in overrideEntries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // 跳过目录

                var relativePath = entry.FullName.Substring(overridesPrefix.Length + 1);
                var destPath = Path.Combine(runDir, relativePath);
                
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, true);

                processedFiles++;
                // 解压占70-95%
                var progress = 70 + (processedFiles / (double)Math.Max(1, overrideEntries.Count) * 25);
                var currentPercent = (int)progress;
                
                if (currentPercent - lastReportedPercent >= 5 || processedFiles % 100 == 0 || processedFiles == overrideEntries.Count)
                {
                    progressCallback?.Invoke($"正在解压文件... ({processedFiles}/{overrideEntries.Count})", progress);
                    lastReportedPercent = currentPercent;
                }
            }

            progressCallback?.Invoke("整合包安装完成", 100);
        }

        private static async Task InstallCurseForgeModLoaderAsync(
            CurseForgeManifest manifest,
            string gameDirectory,
            string versionName,
            Action<string, double>? progressCallback)
        {
            try
            {
                var primaryLoader = manifest.Minecraft?.ModLoaders?.FirstOrDefault(m => m.Primary) ?? manifest.Minecraft?.ModLoaders?.FirstOrDefault();
                if (primaryLoader == null || string.IsNullOrWhiteSpace(primaryLoader.Id))
                {
                    Debug.WriteLine("[ModpackInstall] 未找到 modLoaders，跳过加载器安装");
                    return;
                }

                var loaderId = primaryLoader.Id.Trim();
                // loaderId 形如: forge-40.2.0 / fabric-0.15.11 / quilt-0.24.0 / neoforge-20.4.234
                var parts = loaderId.Split('-', 2);
                if (parts.Length < 2)
                {
                    Debug.WriteLine($"[ModpackInstall] 无法解析加载器ID: {loaderId}");
                    return;
                }

                var loaderType = parts[0].ToLowerInvariant();
                var loaderVersion = parts[1];
                var mcVersion = manifest.Minecraft?.Version ?? "";

                progressCallback?.Invoke($"正在安装加载器 {loaderType}-{loaderVersion}...", 35);

                switch (loaderType)
                {
                    case "forge":
                        await ForgeService.InstallForgeAsync(mcVersion, loaderVersion, gameDirectory, versionName, progressCallback);
                        break;
                    case "fabric":
                        await FabricService.InstallFabricAsync(
                            mcVersion,
                            loaderVersion,
                            gameDirectory,
                            versionName,
                            (status, currentBytes, speed, totalBytes) =>
                            {
                                double percent = totalBytes > 0 ? (currentBytes * 100.0 / totalBytes) : 0;
                                progressCallback?.Invoke(status, 35 + percent * 0.05);
                            }
                        );
                        break;
                    case "quilt":
                        await QuiltService.InstallQuiltAsync(
                            mcVersion,
                            loaderVersion,
                            gameDirectory,
                            versionName,
                            (status, currentBytes, speed, totalBytes) =>
                            {
                                double percent = totalBytes > 0 ? (currentBytes * 100.0 / totalBytes) : 0;
                                progressCallback?.Invoke(status, 35 + percent * 0.05);
                            }
                        );
                        break;
                    case "neoforge":
                        await NeoForgeService.InstallNeoForgeAsync(
                            mcVersion,
                            loaderVersion,
                            gameDirectory,
                            versionName,
                            (status, percent) =>
                            {
                                progressCallback?.Invoke(status, 35 + percent * 0.05);
                            }
                        );
                        break;
                    default:
                        Debug.WriteLine($"[ModpackInstall] 未支持的加载器类型: {loaderType}，跳过加载器安装");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModpackInstall] 安装加载器失败: {ex.Message}");
                // 加载器安装失败通常会导致无法启动，这里选择继续抛出，让上层提示
                throw;
            }
        }

        private static async Task DownloadCurseForgeModsAsync(
            List<CurseForgeManifestFile> files,
            string gameDirectory,
            string versionName,
            Action<string, double>? progressCallback)
        {
            if (files == null || files.Count == 0)
            {
                Debug.WriteLine("[ModpackInstall] manifest.files 为空，跳过资源下载");
                return;
            }

            var config = LauncherConfig.Load();
            var runDir = config.GetRunDirectory(versionName);
            var modsDir = Path.Combine(runDir, "mods");
            var resourcepacksDir = Path.Combine(runDir, "resourcepacks");
            var shaderpacksDir = Path.Combine(runDir, "shaderpacks");
            Directory.CreateDirectory(modsDir);
            Directory.CreateDirectory(resourcepacksDir);
            Directory.CreateDirectory(shaderpacksDir);

            int total = files.Count;
            int completed = 0;
            int failed = 0;
            int modsCount = 0;
            int resourcepacksCount = 0;
            int shaderpacksCount = 0;

            progressCallback?.Invoke($"正在下载资源... (0/{total})", 40);

            // 控制并发，避免被 API/网络打爆
            using var semaphore = new SemaphoreSlim(4);
            var tasks = files.Select(async f =>
            {
                await semaphore.WaitAsync();
                try
                {
                    // 获取项目信息以确定类型
                    CurseForgeResponse<CurseForgeMod>? projectInfo = null;
                    try
                    {
                        projectInfo = await CurseForgeService.GetModAsync(f.ProjectID);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ModpackInstall] 获取项目信息异常: {f.ProjectID} - {ex.Message}");
                    }

                    if (projectInfo?.Data == null)
                    {
                        if (f.Required)
                        {
                            Interlocked.Increment(ref failed);
                            Debug.WriteLine($"[ModpackInstall] 获取项目信息失败（必需）: {f.ProjectID}");
                        }
                        else
                        {
                            Interlocked.Increment(ref completed);
                            Debug.WriteLine($"[ModpackInstall] 获取项目信息失败（可选）: {f.ProjectID}，跳过");
                        }
                        return;
                    }

                    // 获取文件信息
                    CurseForgeFile? fileInfo = null;
                    try
                    {
                        fileInfo = await CurseForgeService.GetModFileInfoAsync(f.ProjectID, f.FileID);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ModpackInstall] 获取文件信息异常: {f.ProjectID}/{f.FileID} - {ex.Message}");
                    }

                    if (fileInfo == null)
                    {
                        if (f.Required)
                        {
                            Interlocked.Increment(ref failed);
                            Debug.WriteLine($"[ModpackInstall] 获取文件信息失败（必需）: {f.ProjectID}/{f.FileID}");
                        }
                        else
                        {
                            Interlocked.Increment(ref completed);
                            Debug.WriteLine($"[ModpackInstall] 获取文件信息失败（可选）: {f.ProjectID}/{f.FileID}，跳过");
                        }
                        return;
                    }

                    // 根据ClassId和文件名确定目标目录
                    string destPath;
                    var safeFileName = string.IsNullOrWhiteSpace(fileInfo.FileName) ? $"{f.ProjectID}-{f.FileID}" : fileInfo.FileName;
                    var fileNameLower = safeFileName.ToLowerInvariant();

                    int classId = projectInfo.Data.ClassId;
                    
                    // 优先通过ClassId判断
                    if (classId == CurseForgeService.SECTION_MODS)
                    {
                        destPath = Path.Combine(modsDir, safeFileName);
                        Interlocked.Increment(ref modsCount);
                    }
                    else if (classId == CurseForgeService.SECTION_RESOURCE_PACKS)
                    {
                        // 资源包：进一步判断是否是光影包
                        if (fileNameLower.Contains("shader") || fileNameLower.Contains("shaders") || 
                            fileNameLower.Contains("optifine") || fileNameLower.Contains("iris") ||
                            fileNameLower.Contains("sodium") || fileNameLower.Contains("canvas"))
                        {
                            destPath = Path.Combine(shaderpacksDir, safeFileName);
                            Interlocked.Increment(ref shaderpacksCount);
                        }
                        else
                        {
                            destPath = Path.Combine(resourcepacksDir, safeFileName);
                            Interlocked.Increment(ref resourcepacksCount);
                        }
                    }
                    else
                    {
                        // 其他类型或ClassId未知：通过文件名和扩展名判断
                        if (fileNameLower.Contains("shader") || fileNameLower.Contains("shaders") || 
                            fileNameLower.Contains("optifine") || fileNameLower.Contains("iris") ||
                            fileNameLower.Contains("sodium") || fileNameLower.Contains("canvas"))
                        {
                            destPath = Path.Combine(shaderpacksDir, safeFileName);
                            Interlocked.Increment(ref shaderpacksCount);
                        }
                        else if (fileNameLower.EndsWith(".jar"))
                        {
                            // .jar文件通常是MOD
                            destPath = Path.Combine(modsDir, safeFileName);
                            Interlocked.Increment(ref modsCount);
                        }
                        else if (fileNameLower.EndsWith(".zip") || fileNameLower.EndsWith(".mcpack"))
                        {
                            // .zip或.mcpack文件通常是资源包
                            destPath = Path.Combine(resourcepacksDir, safeFileName);
                            Interlocked.Increment(ref resourcepacksCount);
                        }
                        else
                        {
                            // 默认归类为资源包
                            destPath = Path.Combine(resourcepacksDir, safeFileName);
                            Interlocked.Increment(ref resourcepacksCount);
                        }
                    }

                    // 获取SHA1哈希值用于校验
                    string? expectedSha1 = null;
                    var sha1Hash = fileInfo.Hashes?.FirstOrDefault(h => h.Algo == 1);
                    if (sha1Hash != null)
                    {
                        expectedSha1 = sha1Hash.Value;
                    }

                    // 检查文件是否已存在且SHA1匹配
                    if (File.Exists(destPath))
                    {
                        bool shouldSkip = false;
                        try
                        {
                            var fi = new FileInfo(destPath);
                            if (fi.Length == fileInfo.FileLength && fileInfo.FileLength > 0)
                            {
                                if (!string.IsNullOrEmpty(expectedSha1))
                                {
                                    if (VerifyFileHash(destPath, expectedSha1))
                                    {
                                        shouldSkip = true;
                                    }
                                }
                                else
                                {
                                    shouldSkip = true;
                                }
                            }
                        }
                        catch { }

                        if (shouldSkip)
                        {
                            Interlocked.Increment(ref completed);
                            return;
                        }
                    }

                    // 下载文件
                    bool ok = false;
                    try
                    {
                        ok = await CurseForgeService.DownloadModFileAsync(fileInfo, destPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ModpackInstall] 下载文件异常: {safeFileName} - {ex.Message}");
                        ok = false;
                    }

                    if (ok)
                    {
                        // 验证SHA1
                        if (!string.IsNullOrEmpty(expectedSha1))
                        {
                            if (!VerifyFileHash(destPath, expectedSha1))
                            {
                                Debug.WriteLine($"[ModpackInstall] SHA1校验失败: {safeFileName}");
                                try { File.Delete(destPath); } catch { }
                                if (f.Required)
                                {
                                    Interlocked.Increment(ref failed);
                                }
                                else
                                {
                                    Interlocked.Increment(ref completed);
                                }
                                return;
                            }
                        }

                        Interlocked.Increment(ref completed);
                        Debug.WriteLine($"[ModpackInstall] 成功下载: {safeFileName} -> {destPath}");
                    }
                    else
                    {
                        if (f.Required)
                        {
                            Interlocked.Increment(ref failed);
                            Debug.WriteLine($"[ModpackInstall] 下载失败（必需）: {safeFileName}");
                        }
                        else
                        {
                            Interlocked.Increment(ref completed);
                            Debug.WriteLine($"[ModpackInstall] 下载失败（可选）: {safeFileName}，跳过");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ModpackInstall] 下载文件时出错: {f.ProjectID}/{f.FileID} - {ex.Message}");
                    if (f.Required)
                    {
                        Interlocked.Increment(ref failed);
                    }
                    else
                    {
                        Interlocked.Increment(ref completed);
                    }
                }
                finally
                {
                    // 更新进度
                    var done = Volatile.Read(ref completed) + Volatile.Read(ref failed);
                    var mods = Volatile.Read(ref modsCount);
                    var resourcepacks = Volatile.Read(ref resourcepacksCount);
                    var shaderpacks = Volatile.Read(ref shaderpacksCount);
                    
                    // 资源下载占40-70%的进度
                    var percent = 40 + (done / (double)Math.Max(1, total) * 30);
                    var statusText = $"正在下载资源... ({done}/{total})";
                    if (mods > 0 || resourcepacks > 0 || shaderpacks > 0)
                    {
                        var parts = new List<string>();
                        if (mods > 0) parts.Add($"MODS: {mods}");
                        if (resourcepacks > 0) parts.Add($"资源包: {resourcepacks}");
                        if (shaderpacks > 0) parts.Add($"光影: {shaderpacks}");
                        statusText = $"正在下载资源... ({done}/{total}) [{string.Join(", ", parts)}]";
                    }
                    progressCallback?.Invoke(statusText, percent);

                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);

            var finalCompleted = Volatile.Read(ref completed);
            var finalFailed = Volatile.Read(ref failed);
            if (finalFailed > 0)
            {
                Debug.WriteLine($"[ModpackInstall] 资源下载完成，成功 {finalCompleted}/{total}，失败 {finalFailed}/{total}");
                if (finalFailed == total)
                {
                    throw new Exception("所有必需资源下载失败");
                }
            }
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

            // 2. 安装加载器（如果有）
            await InstallModrinthModLoaderAsync(index.Dependencies, gameDirectory, versionName, minecraftVersion, progressCallback);

            // 3. 下载文件（MODS、资源包、光影包）
            if (index.Files != null && index.Files.Count > 0)
            {
                await DownloadModrinthFilesAsync(index.Files, gameDirectory, versionName, progressCallback);
            }

            progressCallback?.Invoke("正在解压整合包文件...", 70);

            // 4. 解压 overrides 文件夹到运行目录 (70-95%)
            var config = LauncherConfig.Load();
            var runDir = config.GetRunDirectory(versionName);

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

                var destPath = Path.Combine(runDir, relativePath);
                
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, true);

                processedFiles++;
                var progress = 70 + (processedFiles / (double)Math.Max(1, overrideEntries.Count) * 25);
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

        private static async Task InstallModrinthModLoaderAsync(
            ModrinthDependencies? dependencies,
            string gameDirectory,
            string versionName,
            string minecraftVersion,
            Action<string, double>? progressCallback)
        {
            if (dependencies == null)
            {
                Debug.WriteLine("[ModpackInstall] 未找到依赖信息，跳过加载器安装");
                return;
            }

            try
            {
                string? loaderType = null;
                string? loaderVersion = null;

                if (!string.IsNullOrWhiteSpace(dependencies.Forge))
                {
                    loaderType = "forge";
                    loaderVersion = dependencies.Forge;
                }
                else if (!string.IsNullOrWhiteSpace(dependencies.FabricLoader))
                {
                    loaderType = "fabric";
                    loaderVersion = dependencies.FabricLoader;
                }
                else if (!string.IsNullOrWhiteSpace(dependencies.QuiltLoader))
                {
                    loaderType = "quilt";
                    loaderVersion = dependencies.QuiltLoader;
                }
                else if (!string.IsNullOrWhiteSpace(dependencies.NeoForge))
                {
                    loaderType = "neoforge";
                    loaderVersion = dependencies.NeoForge;
                }

                if (string.IsNullOrWhiteSpace(loaderType) || string.IsNullOrWhiteSpace(loaderVersion))
                {
                    Debug.WriteLine("[ModpackInstall] 未找到加载器依赖，跳过加载器安装");
                    return;
                }

                progressCallback?.Invoke($"正在安装加载器 {loaderType}-{loaderVersion}...", 35);

                switch (loaderType)
                {
                    case "forge":
                        await ForgeService.InstallForgeAsync(minecraftVersion, loaderVersion, gameDirectory, versionName, progressCallback);
                        break;
                    case "fabric":
                        await FabricService.InstallFabricAsync(
                            minecraftVersion,
                            loaderVersion,
                            gameDirectory,
                            versionName,
                            (status, currentBytes, speed, totalBytes) =>
                            {
                                double percent = totalBytes > 0 ? (currentBytes * 100.0 / totalBytes) : 0;
                                progressCallback?.Invoke(status, 35 + percent * 0.05);
                            }
                        );
                        break;
                    case "quilt":
                        await QuiltService.InstallQuiltAsync(
                            minecraftVersion,
                            loaderVersion,
                            gameDirectory,
                            versionName,
                            (status, currentBytes, speed, totalBytes) =>
                            {
                                double percent = totalBytes > 0 ? (currentBytes * 100.0 / totalBytes) : 0;
                                progressCallback?.Invoke(status, 35 + percent * 0.05);
                            }
                        );
                        break;
                    case "neoforge":
                        await NeoForgeService.InstallNeoForgeAsync(
                            minecraftVersion,
                            loaderVersion,
                            gameDirectory,
                            versionName,
                            (status, percent) =>
                            {
                                progressCallback?.Invoke(status, 35 + percent * 0.05);
                            }
                        );
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModpackInstall] 安装加载器失败: {ex.Message}");
                throw;
            }
        }

        private static async Task DownloadModrinthFilesAsync(
            List<ModrinthIndexFile> files,
            string gameDirectory,
            string versionName,
            Action<string, double>? progressCallback)
        {
            if (files == null || files.Count == 0)
            {
                Debug.WriteLine("[ModpackInstall] modrinth.index.json 中无文件列表，跳过文件下载");
                return;
            }

            var config = LauncherConfig.Load();
            var runDir = config.GetRunDirectory(versionName);
            var modsDir = Path.Combine(runDir, "mods");
            var resourcepacksDir = Path.Combine(runDir, "resourcepacks");
            var shaderpacksDir = Path.Combine(runDir, "shaderpacks");
            Directory.CreateDirectory(modsDir);
            Directory.CreateDirectory(resourcepacksDir);
            Directory.CreateDirectory(shaderpacksDir);

            int total = files.Count;
            int completed = 0;
            int failed = 0;
            int modsCount = 0;
            int resourcepacksCount = 0;
            int shaderpacksCount = 0;

            progressCallback?.Invoke($"正在下载资源... (0/{total})", 40);

            var modrinthService = new ModrinthService(DownloadSourceManager.Instance);
            using var semaphore = new SemaphoreSlim(4);
            var tasks = files.Select(async file =>
            {
                await semaphore.WaitAsync();
                try
                {
                    if (file.Downloads == null || file.Downloads.Count == 0)
                    {
                        Debug.WriteLine($"[ModpackInstall] 文件无下载链接: {file.Path}");
                        Interlocked.Increment(ref failed);
                        return;
                    }

                    // 根据path判断文件类型
                    var pathLower = file.Path.ToLowerInvariant();
                    string destPath;
                    string fileName = Path.GetFileName(file.Path);

                    if (pathLower.StartsWith("mods/"))
                    {
                        destPath = Path.Combine(modsDir, fileName);
                        Interlocked.Increment(ref modsCount);
                    }
                    else if (pathLower.StartsWith("resourcepacks/") || pathLower.StartsWith("textures/"))
                    {
                        destPath = Path.Combine(resourcepacksDir, fileName);
                        Interlocked.Increment(ref resourcepacksCount);
                    }
                    else if (pathLower.StartsWith("shaderpacks/") || pathLower.StartsWith("shaders/"))
                    {
                        destPath = Path.Combine(shaderpacksDir, fileName);
                        Interlocked.Increment(ref shaderpacksCount);
                    }
                    else
                    {
                        // 默认放到mods目录
                        destPath = Path.Combine(modsDir, fileName);
                        Interlocked.Increment(ref modsCount);
                    }

                    // 获取SHA1哈希值
                    string? expectedSha1 = null;
                    if (file.Hashes != null && file.Hashes.ContainsKey("sha1"))
                    {
                        expectedSha1 = file.Hashes["sha1"];
                    }

                    // 检查文件是否已存在且SHA1匹配
                    if (File.Exists(destPath))
                    {
                        bool shouldSkip = false;
                        try
                        {
                            var fi = new FileInfo(destPath);
                            if (fi.Length == file.FileSize && file.FileSize > 0)
                            {
                                if (!string.IsNullOrEmpty(expectedSha1))
                                {
                                    if (VerifyFileHash(destPath, expectedSha1))
                                    {
                                        shouldSkip = true;
                                    }
                                }
                                else
                                {
                                    shouldSkip = true;
                                }
                            }
                        }
                        catch { }

                        if (shouldSkip)
                        {
                            Interlocked.Increment(ref completed);
                            return;
                        }
                    }

                    // 下载文件（使用第一个下载链接）
                    var downloadUrl = file.Downloads[0];
                    var versionFile = new ModrinthVersionFile
                    {
                        Url = downloadUrl,
                        Filename = fileName,
                        Size = file.FileSize
                    };

                    var ok = await modrinthService.DownloadModFileAsync(versionFile, destPath, null);
                    if (ok)
                    {
                        // 验证SHA1
                        if (!string.IsNullOrEmpty(expectedSha1))
                        {
                            if (!VerifyFileHash(destPath, expectedSha1))
                            {
                                Debug.WriteLine($"[ModpackInstall] SHA1校验失败: {fileName}");
                                Interlocked.Increment(ref failed);
                                try { File.Delete(destPath); } catch { }
                                return;
                            }
                        }

                        Interlocked.Increment(ref completed);
                    }
                    else
                    {
                        Interlocked.Increment(ref failed);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ModpackInstall] 下载文件时出错: {file.Path} - {ex.Message}");
                    Interlocked.Increment(ref failed);
                }
                finally
                {
                    var done = Volatile.Read(ref completed) + Volatile.Read(ref failed);
                    var mods = Volatile.Read(ref modsCount);
                    var resourcepacks = Volatile.Read(ref resourcepacksCount);
                    var shaderpacks = Volatile.Read(ref shaderpacksCount);
                    
                    var percent = 40 + (done / (double)Math.Max(1, total) * 30);
                    var statusText = $"正在下载资源... ({done}/{total})";
                    if (mods > 0 || resourcepacks > 0 || shaderpacks > 0)
                    {
                        var parts = new List<string>();
                        if (mods > 0) parts.Add($"MODS: {mods}");
                        if (resourcepacks > 0) parts.Add($"资源包: {resourcepacks}");
                        if (shaderpacks > 0) parts.Add($"光影: {shaderpacks}");
                        statusText = $"正在下载资源... ({done}/{total}) [{string.Join(", ", parts)}]";
                    }
                    progressCallback?.Invoke(statusText, percent);

                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);

            var finalCompleted = Volatile.Read(ref completed);
            var finalFailed = Volatile.Read(ref failed);
            if (finalFailed > 0)
            {
                Debug.WriteLine($"[ModpackInstall] 资源下载完成，成功 {finalCompleted}/{total}，失败 {finalFailed}/{total}");
            }
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

        /// <summary>
        /// 验证文件的SHA1哈希值
        /// </summary>
        private static bool VerifyFileHash(string filePath, string expectedSha1)
        {
            try
            {
                if (!File.Exists(filePath) || string.IsNullOrWhiteSpace(expectedSha1))
                    return false;

                using var sha1 = SHA1.Create();
                using var fileStream = File.OpenRead(filePath);
                var hashBytes = sha1.ComputeHash(fileStream);
                var actualSha1 = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                var expectedSha1Lower = expectedSha1.ToLowerInvariant();

                return actualSha1 == expectedSha1Lower;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModpackInstall] SHA1校验失败: {ex.Message}");
                return false;
            }
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
            [JsonPropertyName("minecraft")]
            public CurseForgeMinecraft Minecraft { get; set; } = new();

            [JsonPropertyName("manifestType")]
            public string ManifestType { get; set; } = "";

            [JsonPropertyName("manifestVersion")]
            public int ManifestVersion { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; } = "";

            [JsonPropertyName("version")]
            public string Version { get; set; } = "";

            [JsonPropertyName("author")]
            public string Author { get; set; } = "";

            [JsonPropertyName("files")]
            public List<CurseForgeManifestFile> Files { get; set; } = new();

            [JsonPropertyName("overrides")]
            public string Overrides { get; set; } = "overrides";
        }

        private class CurseForgeMinecraft
        {
            [JsonPropertyName("version")]
            public string Version { get; set; } = "";

            [JsonPropertyName("modLoaders")]
            public List<CurseForgeModLoader> ModLoaders { get; set; } = new();
        }

        private class CurseForgeModLoader
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";  // 例如: "forge-40.2.0"

            [JsonPropertyName("primary")]
            public bool Primary { get; set; }
        }

        private class CurseForgeManifestFile
        {
            [JsonPropertyName("projectID")]
            public int ProjectID { get; set; }

            [JsonPropertyName("fileID")]
            public int FileID { get; set; }

            [JsonPropertyName("required")]
            public bool Required { get; set; } = true;
        }

        private class ModrinthIndex
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }
            
            [JsonPropertyName("summary")]
            public string? Summary { get; set; }
            
            [JsonPropertyName("dependencies")]
            public ModrinthDependencies? Dependencies { get; set; }
            
            [JsonPropertyName("files")]
            public List<ModrinthIndexFile>? Files { get; set; }
        }

        private class ModrinthDependencies
        {
            [JsonPropertyName("minecraft")]
            public string? Minecraft { get; set; }
            
            [JsonPropertyName("forge")]
            public string? Forge { get; set; }
            
            [JsonPropertyName("fabric-loader")]
            public string? FabricLoader { get; set; }
            
            [JsonPropertyName("quilt-loader")]
            public string? QuiltLoader { get; set; }
            
            [JsonPropertyName("neoforge")]
            public string? NeoForge { get; set; }
        }
        
        private class ModrinthIndexFile
        {
            [JsonPropertyName("path")]
            public string Path { get; set; } = "";
            
            [JsonPropertyName("downloads")]
            public List<string> Downloads { get; set; } = new();
            
            [JsonPropertyName("fileSize")]
            public long FileSize { get; set; }
            
            [JsonPropertyName("hashes")]
            public Dictionary<string, string>? Hashes { get; set; }
        }

        #endregion
    }
}

