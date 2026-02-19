using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Download;
using ObsMCLauncher.Core.Services.Installers;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services.Minecraft;

/// <summary>
/// 整合包安装服务 - 全流程在 .temp 中完成，成功后再迁移到正式目录
/// </summary>
public class ModpackInstallService
{
    public static async Task<bool> InstallModpackAsync(
        string zipFilePath,
        string versionName,
        string gameDirectory,
        Action<string, double>? progressCallback = null)
    {
        var tempRoot = Path.Combine(gameDirectory, ".temp");
        var tempVersionsDir = Path.Combine(tempRoot, "versions");
        var tempVersionDir = Path.Combine(tempVersionsDir, versionName ?? throw new ArgumentNullException(nameof(versionName)));

        var finalVersionsDir = Path.Combine(gameDirectory, "versions");
        var finalVersionDir = Path.Combine(finalVersionsDir, versionName);

        try
        {
            DebugLogger.Info("ModpackInstall", $"开始安装整合包: {versionName}");
            progressCallback?.Invoke("正在准备安装...", 0);

            if (!File.Exists(zipFilePath))
                throw new Exception("找不到整合包文件");

            Directory.CreateDirectory(tempVersionsDir);

            if (Directory.Exists(tempVersionDir))
            {
                try { Directory.Delete(tempVersionDir, true); } catch { }
            }
            Directory.CreateDirectory(tempVersionDir);

            // .temp 阶段也开启版本隔离（写入 temp 目录，最终迁移）
            VersionConfigService.SetVersionIsolation(tempVersionDir, true);

            progressCallback?.Invoke("正在解析整合包格式...", 10);

            using (var archive = ZipFile.OpenRead(zipFilePath))
            {
                var modpackType = DetectModpackType(archive);

                switch (modpackType)
                {
                    case ModpackType.CurseForge:
                        await InstallCurseForgeModpackAsync(archive, versionName, tempVersionDir, gameDirectory, progressCallback);
                        break;
                    case ModpackType.Modrinth:
                        await InstallModrinthModpackAsync(archive, versionName, tempVersionDir, gameDirectory, progressCallback);
                        break;
                    case ModpackType.Manual:
                        await InstallManualModpackAsync(archive, versionName, tempVersionDir, progressCallback);
                        break;
                    default:
                        throw new Exception("不支持的整合包格式");
                }
            }

            progressCallback?.Invoke("正在写入版本元数据...", 96);
            await EnsureVersionJsonAndJarAsync(versionName, gameDirectory, tempVersionDir);

            progressCallback?.Invoke("正在完成安装（迁移目录）...", 98);
            Directory.CreateDirectory(finalVersionsDir);

            // 增加重试机制的目录迁移逻辑
            await RobustMoveDirectoryAsync(tempVersionDir, finalVersionDir);

            // 彻底清理 .temp 目录（包括中间产生的原版目录等所有残留）
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    await RobustDeleteDirectoryAsync(tempRoot);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Warn("ModpackInstall", $"尝试清理临时目录失败: {ex.Message}");
            }

            progressCallback?.Invoke("安装完成", 100);
            DebugLogger.Info("ModpackInstall", $"整合包安装完成: {versionName}");

            var config = LauncherConfig.Load();
            // 如果开启了自动下载资源，则在后台启动下载任务
            if (config.DownloadAssetsWithGame)
            {
                _ = Task.Run(async () =>
                {
                    var cts = new CancellationTokenSource();
                    var task = Core.Services.Download.DownloadTaskManager.Instance.AddTask(
                        $"补全资源: {versionName}",
                        Core.Services.Download.DownloadTaskType.Resource,
                        cts);

                    try
                    {
                        await AssetsDownloadService.DownloadAndCheckAssetsAsync(
                            gameDirectory,
                            versionName,
                            (p, total, msg, speed) =>
                            {
                                Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(task.Id, p, msg);
                            },
                            cts.Token);

                        Core.Services.Download.DownloadTaskManager.Instance.CompleteTask(task.Id);
                    }
                    catch (Exception ex)
                    {
                        Core.Services.Download.DownloadTaskManager.Instance.FailTask(task.Id, ex.Message);
                    }
                });
            }

            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ModpackInstall", $"安装失败: {ex.Message}");
            try
            {
                if (Directory.Exists(tempRoot))
                    await RobustDeleteDirectoryAsync(tempRoot);
            }
            catch { }

            throw new Exception($"整合包安装失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 带重试机制的目录移动
    /// </summary>
    private static async Task RobustMoveDirectoryAsync(string source, string dest, int retries = 5, int delayMs = 500)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                if (Directory.Exists(dest))
                {
                    Directory.Delete(dest, true);
                }
                Directory.Move(source, dest);
                return;
            }
            catch (IOException ex) when (i < retries - 1)
            {
                DebugLogger.Warn("ModpackInstall", $"目录移动被锁定，正在重试 ({i + 1}/{retries}): {ex.Message}");
                await Task.Delay(delayMs);
            }
        }
        Directory.Move(source, dest); // 最后一次尝试，失败则抛出异常
    }

    /// <summary>
    /// 带重试机制的目录删除
    /// </summary>
    private static async Task RobustDeleteDirectoryAsync(string path, int retries = 3, int delayMs = 500)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
                return;
            }
            catch (IOException) when (i < retries - 1)
            {
                await Task.Delay(delayMs);
            }
        }
    }

    private static ModpackType DetectModpackType(ZipArchive archive)
    {
        if (archive.GetEntry("manifest.json") != null) return ModpackType.CurseForge;
        if (archive.GetEntry("modrinth.index.json") != null) return ModpackType.Modrinth;
        if (archive.Entries.Any(e => e.FullName.Contains(".minecraft/") || e.FullName.StartsWith("versions/"))) return ModpackType.Manual;
        return ModpackType.Unknown;
    }

    private static async Task InstallCurseForgeModpackAsync(
        ZipArchive archive,
        string versionName,
        string tempVersionDir,
        string gameDirectory,
        Action<string, double>? progressCallback)
    {
        progressCallback?.Invoke("正在解析 CurseForge 整合包...", 5);

        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new Exception("找不到 manifest.json");
        using var manifestStream = manifestEntry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<CurseForgeManifest>(manifestStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest == null) throw new Exception("无法解析 manifest.json");

        var minecraftVersion = string.IsNullOrWhiteSpace(manifest.Minecraft?.Version) ? "1.20.1" : manifest.Minecraft.Version;

        // 1) 下载原版到 .temp（复用旧 WPF 思路）
        progressCallback?.Invoke($"正在下载 Minecraft {minecraftVersion}...", 10);
        var tempGameDir = Path.Combine(gameDirectory, ".temp");
        var downloadProgress = new Progress<DownloadProgress>(p =>
        {
            var overallProgress = 10 + (p.OverallPercentage / 100.0 * 25);
            progressCallback?.Invoke($"正在下载 Minecraft {minecraftVersion}... {p.OverallPercentage:F0}%", overallProgress);
        });

        bool mcDownloaded = await DownloadService.DownloadMinecraftVersion(
            minecraftVersion,
            tempGameDir,
            minecraftVersion,
            downloadProgress);

        if (!mcDownloaded)
            throw new Exception($"下载 Minecraft {minecraftVersion} 失败");

        // 将 libraries 从 .temp 合并回真实 libraries（避免版本目录污染）
        var tempLibrariesDir = Path.Combine(tempGameDir, "libraries");
        if (Directory.Exists(tempLibrariesDir))
        {
            await MoveLibrariesToRealDirectory(tempLibrariesDir, Path.Combine(gameDirectory, "libraries"));
        }

        // 2) 安装加载器（NeoForge/Forge/Fabric 等）
        await InstallCurseForgeModLoaderAsync(manifest, gameDirectory, versionName, progressCallback);

        // 3) 下载资源（mods/resourcepacks/...）到 temp runDir（版本隔离下等同 tempVersionDir）
        await DownloadCurseForgeModsAsync(manifest.Files, gameDirectory, versionName, tempVersionDir, progressCallback);

        // 4) 解压 overrides 到 temp runDir
        progressCallback?.Invoke("正在解压整合包文件...", 70);
        var overridesPrefix = string.IsNullOrWhiteSpace(manifest.Overrides) ? "overrides" : manifest.Overrides;
        ExtractOverrides(archive, overridesPrefix, tempVersionDir, progressCallback);
    }

    private static async Task InstallCurseForgeModLoaderAsync(CurseForgeManifest manifest, string gameDir, string versionName, Action<string, double>? progress)
    {
        var primaryLoader = manifest.Minecraft?.ModLoaders?.FirstOrDefault(m => m.Primary) ?? manifest.Minecraft?.ModLoaders?.FirstOrDefault();
        if (primaryLoader == null || string.IsNullOrWhiteSpace(primaryLoader.Id)) return;

        var parts = primaryLoader.Id.Split('-', 2);
        if (parts.Length < 2) return;

        var loaderType = parts[0].ToLowerInvariant();
        var loaderVersion = parts[1];
        var mcVersion = manifest.Minecraft?.Version ?? "";
        var tempGameDir = Path.Combine(gameDir, ".temp");

        progress?.Invoke($"正在安装加载器 {loaderType}-{loaderVersion}...", 35);

        switch (loaderType)
        {
            case "forge":
                await ForgeService.InstallForgeForModpackAsync(mcVersion, loaderVersion, gameDir, tempGameDir, versionName, progress);
                break;
            case "fabric":
                await FabricService.InstallFabricForModpackAsync(mcVersion, loaderVersion, gameDir, tempGameDir, versionName,
                    (s, done, speed, total) => progress?.Invoke(s, 35 + (total > 0 ? done * 5.0 / total : 0)));
                break;
            case "quilt":
                await QuiltService.InstallQuiltForModpackAsync(mcVersion, loaderVersion, gameDir, tempGameDir, versionName,
                    (s, done, speed, total) => progress?.Invoke(s, 35 + (total > 0 ? done * 5.0 / total : 0)));
                break;
            case "neoforge":
                await NeoForgeService.InstallNeoForgeForModpackAsync(
                    mcVersion,
                    loaderVersion,
                    gameDir,
                    tempGameDir,
                    versionName,
                    (s, p) => progress?.Invoke(s, 35 + p * 0.05));
                break;
        }
    }

    private static async Task DownloadCurseForgeModsAsync(
        List<CurseForgeManifestFile> files,
        string gameDir,
        string versionName,
        string tempRunDir,
        Action<string, double>? progress)
    {
        if (files == null || files.Count == 0) return;

        var modsDir = Path.Combine(tempRunDir, "mods");
        var resourcepacksDir = Path.Combine(tempRunDir, "resourcepacks");
        var shaderpacksDir = Path.Combine(tempRunDir, "shaderpacks");
        Directory.CreateDirectory(modsDir);
        Directory.CreateDirectory(resourcepacksDir);
        Directory.CreateDirectory(shaderpacksDir);

        int total = files.Count;
        int completed = 0;
        int failed = 0;

        using var semaphore = new SemaphoreSlim(4);
        var tasks = files.Select(async f =>
        {
            await semaphore.WaitAsync();
            try
            {
                var fileInfo = await CurseForgeService.GetModFileInfoAsync(f.ProjectID, f.FileID);
                if (fileInfo == null)
                {
                    Interlocked.Increment(ref failed);
                    return;
                }

                // 简化：jar->mods，其它->resourcepacks
                var nameLower = (fileInfo.FileName ?? "").ToLowerInvariant();
                var destPath = nameLower.EndsWith(".jar")
                    ? Path.Combine(modsDir, fileInfo.FileName)
                    : Path.Combine(resourcepacksDir, fileInfo.FileName);

                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                await CurseForgeService.DownloadModFileAsync(fileInfo, destPath);

                Interlocked.Increment(ref completed);
            }
            catch
            {
                Interlocked.Increment(ref failed);
            }
            finally
            {
                var done = Volatile.Read(ref completed) + Volatile.Read(ref failed);
                progress?.Invoke($"正在下载资源... ({done}/{total})", 40 + (done * 30.0 / Math.Max(1, total)));
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);

        if (failed == total)
            throw new Exception("所有资源下载失败");
    }

    private static async Task InstallModrinthModpackAsync(
        ZipArchive archive,
        string versionName,
        string tempVersionDir,
        string gameDir,
        Action<string, double>? progress)
    {
        progress?.Invoke("正在解析 Modrinth 整合包...", 5);

        var indexEntry = archive.GetEntry("modrinth.index.json") ?? throw new Exception("找不到 modrinth.index.json");
        using var indexStream = indexEntry.Open();
        var index = await JsonSerializer.DeserializeAsync<ModrinthIndex>(indexStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (index == null) throw new Exception("无法解析 modrinth.index.json");

        var minecraftVersion = index.Dependencies?.Minecraft ?? "1.20.1";

        // 下载原版到 .temp
        var tempGameDir = Path.Combine(gameDir, ".temp");
        var downloadProgress = new Progress<DownloadProgress>(p =>
        {
            var overallProgress = 10 + (p.OverallPercentage / 100.0 * 25);
            progress?.Invoke($"正在下载 Minecraft {minecraftVersion}... {p.OverallPercentage:F0}%", overallProgress);
        });

        bool mcDownloaded = await DownloadService.DownloadMinecraftVersion(
            minecraftVersion,
            tempGameDir,
            minecraftVersion,
            downloadProgress);

        if (!mcDownloaded)
            throw new Exception($"下载 Minecraft {minecraftVersion} 失败");

        var tempLibrariesDir = Path.Combine(tempGameDir, "libraries");
        if (Directory.Exists(tempLibrariesDir))
        {
            await MoveLibrariesToRealDirectory(tempLibrariesDir, Path.Combine(gameDir, "libraries"));
        }

        // 安装加载器（Modrinth 依赖字段）
        await InstallModrinthModLoaderAsync(index.Dependencies, gameDir, versionName, minecraftVersion, progress);

        // 下载文件到 tempVersionDir
        if (index.Files != null)
        {
            int total = index.Files.Count;
            int completed = 0;
            using var semaphore = new SemaphoreSlim(4);
            var tasks = index.Files.Select(async file =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var destPath = Path.Combine(tempVersionDir, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    await HttpDownloadService.DownloadFileToPathAsync(file.Downloads[0], destPath, "MODPACK_DL", CancellationToken.None);
                    Interlocked.Increment(ref completed);
                    progress?.Invoke($"正在下载 Modrinth 资源... ({completed}/{total})", 40 + (completed * 30.0 / Math.Max(1, total)));
                }
                finally
                {
                    semaphore.Release();
                }
            });
            await Task.WhenAll(tasks);
        }

        // 解压 overrides 到 tempVersionDir
        ExtractOverrides(archive, "overrides", tempVersionDir, progress);
    }

    private static async Task InstallModrinthModLoaderAsync(
        ModrinthDependencies? dependencies,
        string gameDirectory,
        string versionName,
        string minecraftVersion,
        Action<string, double>? progressCallback)
    {
        if (dependencies == null) return;

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
            return;

        var tempGameDir = Path.Combine(gameDirectory, ".temp");
        progressCallback?.Invoke($"正在安装加载器 {loaderType}-{loaderVersion}...", 35);

        switch (loaderType)
        {
            case "forge":
                await ForgeService.InstallForgeForModpackAsync(minecraftVersion, loaderVersion, gameDirectory, tempGameDir, versionName, progressCallback);
                break;
            case "fabric":
                await FabricService.InstallFabricForModpackAsync(minecraftVersion, loaderVersion, gameDirectory, tempGameDir, versionName,
                    (s, done, speed, total) => progressCallback?.Invoke(s, 35 + (total > 0 ? done * 5.0 / total : 0)));
                break;
            case "quilt":
                await QuiltService.InstallQuiltForModpackAsync(minecraftVersion, loaderVersion, gameDirectory, tempGameDir, versionName,
                    (s, done, speed, total) => progressCallback?.Invoke(s, 35 + (total > 0 ? done * 5.0 / total : 0)));
                break;
            case "neoforge":
                await NeoForgeService.InstallNeoForgeForModpackAsync(minecraftVersion, loaderVersion, gameDirectory, tempGameDir, versionName,
                    (s, p) => progressCallback?.Invoke(s, 35 + p * 0.05));
                break;
        }
    }

    private static void ExtractOverrides(ZipArchive archive, string prefix, string versionDir, Action<string, double>? progress)
    {
        var entries = archive.Entries.Where(e => e.FullName.StartsWith(prefix + "/")).ToList();
        int count = 0;
        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var destPath = Path.Combine(versionDir, entry.FullName.Substring(prefix.Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, true);
            count++;
            progress?.Invoke($"正在解压文件... ({count}/{entries.Count})", 70 + (count * 25.0 / Math.Max(1, entries.Count)));
        }
    }

    private static async Task InstallManualModpackAsync(ZipArchive archive, string versionName, string versionDir, Action<string, double>? progress)
    {
        int count = 0;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var destPath = Path.Combine(versionDir, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, true);
            count++;
            progress?.Invoke($"正在解压手动整合包... ({count}/{archive.Entries.Count})", 30 + (count * 70.0 / Math.Max(1, archive.Entries.Count)));
        }
        await Task.CompletedTask;
    }

    private static Task EnsureVersionJsonAndJarAsync(string versionName, string gameDirectory, string tempVersionDir)
    {
        return Task.Run(() =>
        {
            // 1. 确保目录下存在 {versionName}.json（扫描器需要）
            var jsonPath = Path.Combine(tempVersionDir, $"{versionName}.json");
            if (!File.Exists(jsonPath))
            {
                // 尝试寻找目录内任意 json 并复制/重写 id
                var anyJson = Directory.GetFiles(tempVersionDir, "*.json", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => new FileInfo(f).Length) // 优先取内容多的
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(anyJson))
                {
                    try
                    {
                        var doc = JsonDocument.Parse(File.ReadAllText(anyJson));
                        var root = doc.RootElement;
                        var dict = new Dictionary<string, object>();
                        foreach (var p in root.EnumerateObject())
                        {
                            if (p.NameEquals("id")) dict["id"] = versionName;
                            else dict[p.Name] = p.Value.Clone();
                        }
                        if (!dict.ContainsKey("id")) dict["id"] = versionName;

                        var options = new JsonSerializerOptions { WriteIndented = true };
                        File.WriteAllText(jsonPath, JsonSerializer.Serialize(dict, options));
                    }
                    catch
                    {
                        File.Copy(anyJson, jsonPath, true);
                    }
                }
            }

            // 2. 确保目录下存在 {versionName}.jar（解决扫描器跳过版本的问题）
            var jarPath = Path.Combine(tempVersionDir, $"{versionName}.jar");
            if (!File.Exists(jarPath))
            {
                // 寻找加载器生成的 client.jar (NeoForge/Forge 常见)
                // 或者是原版复制过来的 jar
                var jarFiles = Directory.GetFiles(tempVersionDir, "*.jar", SearchOption.TopDirectoryOnly);
                
                // 优先级：包含 "client" 的 jar > 最大的 jar
                var bestJar = jarFiles.FirstOrDefault(f => f.Contains("-client.jar", StringComparison.OrdinalIgnoreCase))
                             ?? jarFiles.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();

                if (!string.IsNullOrEmpty(bestJar))
                {
                    try
                    {
                        File.Copy(bestJar, jarPath, true);
                        DebugLogger.Info("ModpackInstall", $"已自动补全版本 JAR: {Path.GetFileName(bestJar)} -> {versionName}.jar");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Warn("ModpackInstall", $"复制版本 JAR 失败: {ex.Message}");
                    }
                }
                else
                {
                    // 如果版本目录没找到，去正式库里找（针对 NeoForge 这种库里才有 JAR 的情况）
                    // 虽然 NeoForge 的 JSON 会指定正确的库路径，但启动器扫描器通常仍要求版本目录下有一个 jar 占位（或作为主文件）
                    DebugLogger.Warn("ModpackInstall", "未在版本目录找到可用的 JAR 文件");
                }
            }

            // 同步写入最终目录的版本隔离配置
            VersionConfigService.SetVersionIsolation(tempVersionDir, true);
        });
    }

    /// <summary>
    /// 将库文件从临时目录移动到真实的libraries目录（如果不存在）
    /// </summary>
    private static Task MoveLibrariesToRealDirectory(string tempLibrariesDir, string realLibrariesDir)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(tempLibrariesDir))
                    return;

                Directory.CreateDirectory(realLibrariesDir);

                var tempLibFiles = Directory.GetFiles(tempLibrariesDir, "*.*", SearchOption.AllDirectories);

                foreach (var tempFile in tempLibFiles)
                {
                    var relativePath = Path.GetRelativePath(tempLibrariesDir, tempFile);
                    var realFile = Path.Combine(realLibrariesDir, relativePath);

                    if (File.Exists(realFile))
                        continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(realFile)!);

                    try
                    {
                        File.Move(tempFile, realFile, true);
                    }
                    catch
                    {
                        File.Copy(tempFile, realFile, true);
                    }
                }

                DebugLogger.Info("ModpackInstall", "已合并临时库文件到真实libraries目录");
            }
            catch (Exception ex)
            {
                DebugLogger.Warn("ModpackInstall", $"合并库文件失败: {ex.Message}");
            }
        });
    }

    private enum ModpackType { Unknown, CurseForge, Modrinth, Manual }

    private class CurseForgeManifest
    {
        [JsonPropertyName("minecraft")] public CurseForgeMinecraft Minecraft { get; set; } = new();
        [JsonPropertyName("files")] public List<CurseForgeManifestFile> Files { get; set; } = new();
        [JsonPropertyName("overrides")] public string Overrides { get; set; } = "overrides";
    }

    private class CurseForgeMinecraft
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("modLoaders")] public List<CurseForgeModLoader> ModLoaders { get; set; } = new();
    }

    private class CurseForgeModLoader
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("primary")] public bool Primary { get; set; }
    }

    private class CurseForgeManifestFile
    {
        [JsonPropertyName("projectID")] public int ProjectID { get; set; }
        [JsonPropertyName("fileID")] public int FileID { get; set; }
        [JsonPropertyName("required")] public bool Required { get; set; } = true;
    }

    private class ModrinthIndex
    {
        [JsonPropertyName("dependencies")] public ModrinthDependencies? Dependencies { get; set; }
        [JsonPropertyName("files")] public List<ModrinthIndexFile>? Files { get; set; }
    }

    private class ModrinthDependencies
    {
        [JsonPropertyName("minecraft")] public string? Minecraft { get; set; }
        [JsonPropertyName("forge")] public string? Forge { get; set; }
        [JsonPropertyName("fabric-loader")] public string? FabricLoader { get; set; }
        [JsonPropertyName("quilt-loader")] public string? QuiltLoader { get; set; }
        [JsonPropertyName("neoforge")] public string? NeoForge { get; set; }
    }

    private class ModrinthIndexFile
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("downloads")] public List<string> Downloads { get; set; } = new();
    }
}
