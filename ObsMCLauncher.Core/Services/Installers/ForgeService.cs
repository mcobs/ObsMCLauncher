using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Minecraft;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services.Installers
{
    /// <summary>
    /// Forge版本信息
    /// </summary>
    public class ForgeVersion
    {
        [JsonPropertyName("build")]
        public int Build { get; set; }

        [JsonPropertyName("mcversion")]
        public string McVersion { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("modified")]
        public string Modified { get; set; } = "";

        /// <summary>
        /// 完整版本号 (例如: 1.20.1-47.2.0)
        /// </summary>
        public string FullVersion => $"{McVersion}-{Version}";

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName => $"Forge {Version}";
    }

    /// <summary>
    /// Forge版本列表响应
    /// </summary>
    public class ForgeListResponse
    {
        [JsonPropertyName("mcversion")]
        public string McVersion { get; set; } = "";

        [JsonPropertyName("builds")]
        public List<ForgeVersion> Builds { get; set; } = new();
    }

    /// <summary>
    /// Forge支持的Minecraft版本
    /// </summary>
    public class ForgeSupportedMinecraft
    {
        public List<string> Versions { get; set; } = new();
    }

    /// <summary>
    /// Forge install_profile.json 结构
    /// </summary>
    public class ForgeInstallProfile
    {
        [JsonPropertyName("spec")]
        public int Spec { get; set; }

        [JsonPropertyName("profile")]
        public string Profile { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("minecraft")]
        public string Minecraft { get; set; } = "";

        [JsonPropertyName("libraries")]
        public List<ForgeLibrary> Libraries { get; set; } = new();

        [JsonPropertyName("data")]
        public Dictionary<string, ForgeData>? Data { get; set; }

        [JsonPropertyName("processors")]
        public List<ForgeProcessor>? Processors { get; set; }
    }

    public class ForgeLibrary
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("downloads")]
        public ForgeLibraryDownloads? Downloads { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    public class ForgeLibraryDownloads
    {
        [JsonPropertyName("artifact")]
        public ForgeArtifact? Artifact { get; set; }
    }

    public class ForgeArtifact
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("sha1")]
        public string Sha1 { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public class ForgeData
    {
        [JsonPropertyName("client")]
        public string Client { get; set; } = "";

        [JsonPropertyName("server")]
        public string Server { get; set; } = "";
    }

    public class ForgeProcessor
    {
        [JsonPropertyName("jar")]
        public string Jar { get; set; } = "";

        [JsonPropertyName("classpath")]
        public List<string> Classpath { get; set; } = new();

        [JsonPropertyName("args")]
        public List<string> Args { get; set; } = new();

        [JsonPropertyName("outputs")]
        public Dictionary<string, string>? Outputs { get; set; }
    }

    /// <summary>
    /// Forge服务 - 处理Forge版本查询和下载
    /// </summary>
    public class ForgeService
    {
        public static async Task InstallForgeForModpackAsync(
            string mcVersion,
            string forgeVersion,
            string realGameDirectory,
            string tempGameDirectory,
            string versionName,
            Action<string, double>? progressCallback = null)
        {
            var ok = await InstallForgeAsync(
                mcVersion, 
                forgeVersion, 
                tempGameDirectory, 
                versionName, 
                progressCallback, 
                null, 
                isModpackMode: true,
                realGameDirectory: realGameDirectory);

            if (!ok)
                throw new Exception($"Forge {forgeVersion} 安装失败");
        }

        public static async Task<bool> InstallForgeAsync(
            string mcVersion,
            string forgeVersion,
            string gameDirectory,
            string customVersionName,
            Action<string, double>? progressCallback = null,
            Action<ObsMCLauncher.Core.Services.Minecraft.DownloadProgress>? detailedProgressCallback = null,
            bool isModpackMode = false,
            string? realGameDirectory = null,
            Action<string>? onInstallerOutput = null,
            CancellationToken cancellationToken = default)
        {
            var finalRealGameDir = realGameDirectory ?? gameDirectory;
            var config = LauncherConfig.Load();
            var forgeFullVersion = $"{mcVersion}-{forgeVersion}";
            var installerPath = Path.Combine(Path.GetTempPath(), $"forge-installer-{forgeFullVersion}.jar");

            IProgress<DownloadProgress> progress = new Progress<DownloadProgress>(p =>
            {
                detailedProgressCallback?.Invoke(p);

                double percent = 0;
                if (p.TotalBytes > 0)
                    percent = p.TotalDownloadedBytes * 100.0 / p.TotalBytes;
                else if (p.CurrentFileTotalBytes > 0)
                    percent = p.CurrentFileBytes * 100.0 / p.CurrentFileTotalBytes;

                progressCallback?.Invoke(string.IsNullOrWhiteSpace(p.Status) ? "正在安装Forge..." : p.Status, percent);
            });

            string tempGameDir;
            bool ownsTempDir;

            if (isModpackMode)
            {
                tempGameDir = gameDirectory;
                ownsTempDir = false;
            }
            else
            {
                tempGameDir = Path.Combine(gameDirectory, ".temp");
                ownsTempDir = true;
            }

            string tempVersionsDir = Path.Combine(tempGameDir, "versions");
            string tempVanillaDir = Path.Combine(tempVersionsDir, mcVersion);

            try
            {
                // === 阶段1: 下载Forge安装器 (0-30%) ===
                progressCallback?.Invoke("正在下载Forge安装器...", 0);

                if (!await DownloadForgeInstallerWithDetailsAsync(
                        forgeFullVersion,
                        installerPath,
                        (currentBytes, speed, totalBytes) =>
                        {
                            double percentage = totalBytes > 0 ? (double)currentBytes / totalBytes * 100 : 0;
                            progressCallback?.Invoke("正在下载Forge安装器...", Math.Min(30, percentage * 0.3));
                        },
                        cancellationToken))
                {
                    throw new Exception("Forge安装器下载失败，请检查网络连接");
                }

                // === 阶段2: 在.temp中准备原版MC (30-45%) ===
                Directory.CreateDirectory(tempVersionsDir);
                progressCallback?.Invoke("正在下载原版Minecraft...", 30);
                await DownloadVanillaForForge(tempGameDir, mcVersion, tempVanillaDir, progress, cancellationToken);

                // === 阶段3: 运行官方安装器 (45-80%) ===
                progressCallback?.Invoke("执行Forge安装器...", 45);

                string tempProfilesPath = Path.Combine(tempGameDir, "launcher_profiles.json");
                if (!File.Exists(tempProfilesPath))
                {
                    string realProfilesPath = Path.Combine(finalRealGameDir, "launcher_profiles.json");
                    if (File.Exists(realProfilesPath))
                    {
                        File.Copy(realProfilesPath, tempProfilesPath, true);
                    }
                    else
                    {
                        var defaultProfiles = new
                        {
                            profiles = new { },
                            selectedProfile = (string?)null,
                            clientToken = Guid.NewGuid().ToString(),
                            authenticationDatabase = new { },
                            launcherVersion = new { name = "ObsMCLauncher", format = 21 }
                        };
                        await File.WriteAllTextAsync(tempProfilesPath, JsonSerializer.Serialize(defaultProfiles, new JsonSerializerOptions { WriteIndented = true }), default);
                    }
                }

                bool installSuccess;
                string officialForgeId = $"{mcVersion}-forge-{forgeVersion}";
                string tempForgeDir = Path.Combine(tempVersionsDir, officialForgeId);

                bool isNewVersion = IsForgeInstallerNewVersion(mcVersion);
                bool isVeryOldVersion = IsVeryOldForgeVersion(mcVersion);

                if (isNewVersion)
                {
                    // 新版Forge(1.13+): 先下载install_profile中的库，再运行安装器
                    progressCallback?.Invoke("正在准备Forge依赖库...", 48);
                    await DownloadInstallerLibrariesAsync(installerPath, tempGameDir, progressCallback, cancellationToken);

                    progressCallback?.Invoke("正在运行Forge安装器...", 55);
                    installSuccess = await RunForgeInstallerAsync(installerPath, tempGameDir, mcVersion, forgeVersion, config, cancellationToken, onInstallerOutput);
                }
                else
                {
                    // 旧版Forge: 尝试运行安装器，失败则手动安装
                    progressCallback?.Invoke("正在运行Forge安装器...", 55);
                    installSuccess = await RunForgeInstallerAsync(installerPath, tempGameDir, mcVersion, forgeVersion, config, cancellationToken, onInstallerOutput);

                    if (!installSuccess && (isVeryOldVersion || !isNewVersion))
                    {
                        DebugLogger.Info("Forge", $"安装器执行失败，尝试手动安装: {mcVersion}");
                        progressCallback?.Invoke("正在手动安装Forge...", 60);
                        installSuccess = await ManualInstallVeryOldForgeClient(installerPath, tempGameDir, mcVersion, officialForgeId, tempForgeDir, config, cancellationToken);
                    }
                }

                if (!installSuccess)
                {
                    throw new Exception("Forge安装器执行失败。可能原因：Java版本不兼容、安装器损坏或缺少依赖。请检查Java配置后重试。");
                }

                // === 阶段4: 验证安装结果 (80-85%) ===
                progressCallback?.Invoke("正在验证安装结果...", 80);

                if (!Directory.Exists(tempForgeDir))
                {
                    throw new Exception($"Forge安装后未找到版本目录: {officialForgeId}。安装器可能未正确执行。");
                }

                string tempForgeJson = Path.Combine(tempForgeDir, $"{officialForgeId}.json");
                if (!File.Exists(tempForgeJson))
                {
                    var jsonCandidates = Directory.GetFiles(tempForgeDir, "*.json");
                    if (jsonCandidates.Length > 0)
                    {
                        tempForgeJson = jsonCandidates[0];
                        DebugLogger.Info("Forge", $"使用替代JSON文件: {Path.GetFileName(tempForgeJson)}");
                    }
                    else
                    {
                        throw new Exception($"Forge安装后未找到版本JSON文件。安装可能不完整。");
                    }
                }

                // === 阶段5: 在.temp中完成所有配置 (85-92%) ===
                progressCallback?.Invoke("正在配置版本信息...", 85);

                if (isModpackMode)
                {
                    string newJson = Path.Combine(tempForgeDir, $"{customVersionName}.json");
                    if (File.Exists(Path.Combine(tempForgeDir, $"{officialForgeId}.json")))
                        File.Move(Path.Combine(tempForgeDir, $"{officialForgeId}.json"), newJson, true);

                    string oldJar = Path.Combine(tempForgeDir, $"{officialForgeId}.jar");
                    string newJar = Path.Combine(tempForgeDir, $"{customVersionName}.jar");
                    if (File.Exists(oldJar) && !File.Exists(newJar))
                        File.Move(oldJar, newJar, true);

                    if (File.Exists(newJson))
                    {
                        var jsonText = await File.ReadAllTextAsync(newJson, default);
                        var node = JsonNode.Parse(jsonText) as JsonObject;
                        if (node != null)
                        {
                            node["id"] = customVersionName;
                            await File.WriteAllTextAsync(newJson, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), default);
                        }
                    }
                }
                else
                {
                    string finalVersionDir = Path.Combine(finalRealGameDir, "versions", customVersionName);

                    // 先在.temp中完成重命名
                    string renamedJson = Path.Combine(tempForgeDir, $"{customVersionName}.json");
                    string renamedJar = Path.Combine(tempForgeDir, $"{customVersionName}.jar");

                    if (File.Exists(tempForgeJson) && Path.GetFileName(tempForgeJson) != $"{customVersionName}.json")
                        File.Move(tempForgeJson, renamedJson, true);

                    string oldJar = Path.Combine(tempForgeDir, $"{officialForgeId}.jar");
                    if (File.Exists(oldJar) && !File.Exists(renamedJar))
                        File.Move(oldJar, renamedJar, true);

                    // 修正json内的id字段
                    if (File.Exists(renamedJson))
                    {
                        try
                        {
                            var jsonText = await File.ReadAllTextAsync(renamedJson, default);
                            var node = JsonNode.Parse(jsonText) as JsonObject;
                            if (node != null)
                            {
                                node["id"] = customVersionName;
                                await File.WriteAllTextAsync(renamedJson, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), default);
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.Warn("Forge", $"修改版本JSON的id字段失败: {ex.Message}");
                        }
                    }

                    // 确保主JAR存在
                    if (!File.Exists(renamedJar))
                    {
                        var jarInForgeDir = Path.Combine(tempForgeDir, $"{mcVersion}.jar");
                        var jarInVanillaDir = Path.Combine(tempVanillaDir, $"{mcVersion}.jar");

                        if (File.Exists(jarInForgeDir))
                            File.Copy(jarInForgeDir, renamedJar, true);
                        else if (File.Exists(jarInVanillaDir))
                            File.Copy(jarInVanillaDir, renamedJar, true);
                        else
                            DebugLogger.Warn("Forge", "未找到可复制的原版JAR，新版Forge可能不需要独立JAR");
                    }
                }

                // 合并父版本信息（在.temp中完成）
                string targetDir = isModpackMode ? tempForgeDir : Path.Combine(tempForgeDir);
                string finalJsonPath = Path.Combine(targetDir, $"{customVersionName}.json");
                if (File.Exists(finalJsonPath))
                {
                    try
                    {
                        await MergeVanillaIntoForgeJson(finalJsonPath, customVersionName, finalRealGameDir, mcVersion, cancellationToken);
                    }
                    catch (Exception mergeEx)
                    {
                        DebugLogger.Warn("Forge", $"合并父版本信息失败: {mergeEx.Message}");
                    }
                }

                // === 阶段6: 验证完整性后迁移到最终位置 (92-100%) ===
                progressCallback?.Invoke("正在验证安装完整性...", 92);

                // 验证JSON可解析
                string verifyJsonPath = Path.Combine(targetDir, $"{customVersionName}.json");
                if (File.Exists(verifyJsonPath))
                {
                    try
                    {
                        var verifyContent = await File.ReadAllTextAsync(verifyJsonPath, default);
                        var verifyNode = JsonNode.Parse(verifyContent);
                        if (verifyNode == null)
                            throw new Exception("版本JSON解析结果为空");
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"安装结果验证失败：版本JSON文件损坏 - {ex.Message}");
                    }
                }

                if (!isModpackMode)
                {
                    // 迁移Forge版本到最终位置
                    progressCallback?.Invoke("正在完成安装...", 95);
                    string finalVersionDir = Path.Combine(finalRealGameDir, "versions", customVersionName);

                    if (Directory.Exists(finalVersionDir))
                    {
                        try { Directory.Delete(finalVersionDir, true); }
                        catch (Exception ex)
                        {
                            DebugLogger.Warn("Forge", $"删除已有版本目录失败: {ex.Message}");
                            throw new Exception($"无法删除已有的版本目录 {customVersionName}，可能被占用。请关闭游戏后重试。");
                        }
                    }

                    try
                    {
                        Directory.Move(tempForgeDir, finalVersionDir);
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Error("Forge", $"移动版本目录失败: {ex.Message}");
                        throw new Exception($"无法将安装结果移动到最终位置: {ex.Message}。临时文件保留在 {tempForgeDir}。");
                    }

                    // 迁移安装器下载的库文件到正式libraries目录
                    MoveLibrariesFromTemp(tempGameDir, finalRealGameDir);
                }

                progressCallback?.Invoke("Forge安装完成", 100);
                DebugLogger.Info("Forge", $"Forge {forgeVersion} 安装成功: {customVersionName}");
                return true;
            }
            catch (OperationCanceledException)
            {
                DebugLogger.Warn("Forge", "Forge安装被取消");
                throw;
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Forge", $"Forge安装失败: {ex.Message}");
                throw;
            }
            finally
            {
                // 清理安装器临时文件
                try { if (File.Exists(installerPath)) File.Delete(installerPath); } catch { }

                // 非整合包模式下清理.temp目录
                if (ownsTempDir && Directory.Exists(tempGameDir))
                {
                    try { Directory.Delete(tempGameDir, true); }
                    catch (Exception ex) { DebugLogger.Warn("Forge", $"清理临时目录失败: {ex.Message}"); }
                }
            }
        }

        private static void MoveLibrariesFromTemp(string tempGameDir, string realGameDir)
        {
            var tempLibDir = Path.Combine(tempGameDir, "libraries");
            var realLibDir = Path.Combine(realGameDir, "libraries");

            if (!Directory.Exists(tempLibDir)) return;

            Directory.CreateDirectory(realLibDir);

            foreach (var srcFile in Directory.GetFiles(tempLibDir, "*", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(tempLibDir, srcFile);
                var destFile = Path.Combine(realLibDir, relPath);

                if (File.Exists(destFile)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                try
                {
                    File.Move(srcFile, destFile, false);
                }
                catch (IOException)
                {
                    try { File.Copy(srcFile, destFile, false); } catch { }
                }
            }

            DebugLogger.Info("Forge", "已将临时库文件迁移到正式目录");
        }

        private static async Task DownloadInstallerLibrariesAsync(
            string installerPath,
            string gameDirectory,
            Action<string, double>? progressCallback,
            CancellationToken cancellationToken)
        {
            try
            {
                var profile = await ExtractInstallProfileAsync(installerPath);
                if (profile == null)
                {
                    DebugLogger.Warn("Forge", "无法解析install_profile.json，跳过库预下载");
                    return;
                }

                if (profile.Libraries != null && profile.Libraries.Count > 0)
                {
                    await DownloadInstallProfileLibrariesAsync(profile.Libraries, gameDirectory, progressCallback, cancellationToken);
                }

                // 检查是否有下载失败的库
                var failedLibs = new List<string>();
                if (profile.Libraries != null)
                {
                    foreach (var lib in profile.Libraries)
                    {
                        if (lib.Downloads?.Artifact != null && !string.IsNullOrWhiteSpace(lib.Downloads.Artifact.Path))
                        {
                            var libPath = Path.Combine(gameDirectory, "libraries", lib.Downloads.Artifact.Path.Replace('/', Path.DirectorySeparatorChar));
                            if (!File.Exists(libPath))
                                failedLibs.Add(lib.Name);
                        }
                    }
                }

                if (failedLibs.Count > 0)
                {
                    DebugLogger.Warn("Forge", $"以下库下载失败（将交给安装器处理）: {string.Join(", ", failedLibs.Take(5))}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Warn("Forge", $"预下载install_profile库时出错: {ex.Message}，将交给安装器处理");
            }
        }

        private static readonly HttpClient _httpClient = new HttpClient();

        private static async Task ExecuteInstallProfileProcessorsAsync(
            ForgeInstallProfile profile,
            string gameDirectory,
            string installerPath,
            Action<string, double>? progressCallback,
            CancellationToken cancellationToken = default)
        {
            // 这里暂时实现 processors 执行的最小版本：逐个执行 processor.jar 主类
            // 完整逻辑需要解析 install_profile 的 data/args 占位符等
            var config = LauncherConfig.Load();
            var javaPath = config.JavaPath;
            if (string.IsNullOrEmpty(javaPath) || !File.Exists(javaPath))
                javaPath = "java";

            if (profile.Processors == null || profile.Processors.Count == 0)
                return;

            var librariesDir = Path.Combine(gameDirectory, "libraries");
            Directory.CreateDirectory(librariesDir);

            int total = profile.Processors.Count;

            for (int i = 0; i < total; i++)
            {
                var proc = profile.Processors[i];
                progressCallback?.Invoke($"正在执行 Forge 处理器... ({i + 1}/{total})", 55 + (i / (double)Math.Max(1, total) * 20)); // 55-75

                // processor jar 路径（maven坐标 -> 本地 libraries 路径）
                var procJarPathRel = MavenToPath(proc.Jar);
                var procJarPath = Path.Combine(librariesDir, procJarPathRel.Replace('/', Path.DirectorySeparatorChar));

                // 先确保 processor 本体和其 classpath 依赖都已下载
                var mavenToEnsure = new List<string>();
                if (!string.IsNullOrWhiteSpace(proc.Jar)) mavenToEnsure.Add(proc.Jar);
                if (proc.Classpath != null && proc.Classpath.Count > 0) mavenToEnsure.AddRange(proc.Classpath);
                await EnsureMavenLibrariesDownloadedAsync(mavenToEnsure, gameDirectory, progressCallback, cancellationToken);

                // classpath = processor jar + 其 classpath 依赖（全部使用本地路径）
                var cpParts = new List<string>();
                if (File.Exists(procJarPath)) cpParts.Add(procJarPath);

                if (proc.Classpath != null)
                {
                    foreach (var cp in proc.Classpath)
                    {
                        var cpRel = MavenToPath(cp);
                        var cpPath = Path.Combine(librariesDir, cpRel.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(cpPath)) cpParts.Add(cpPath);
                    }
                }

                var classpath = string.Join(Path.PathSeparator, cpParts);

                // args 占位符替换（增强版）
                var replacements = BuildProcessorReplacements(profile, gameDirectory, installerPath);
                var args = proc.Args.Select(a => ReplacePlaceholders(a, replacements)).ToList();

                // 读取 Main-Class，并用 -cp Main-Class 执行（比 -jar 更符合 processor 需求）
                var mainClass = ReadJarMainClass(procJarPath);
                if (string.IsNullOrWhiteSpace(mainClass))
                {
                    // 如果取不到 mainClass，退回 -jar（但更容易失败）
                    mainClass = null;
                }

                var argString = string.Join(" ", args.Select(x => $"\"{x}\""));

                var psi = new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = mainClass == null
                        ? $"-jar \"{procJarPath}\" {argString}"
                        : $"-cp \"{classpath}\" {mainClass} {argString}",
                    WorkingDirectory = gameDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var p = new Process { StartInfo = psi };
                p.Start();
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync(cancellationToken);

                var outText = await outTask;
                var errText = await errTask;
                if (!string.IsNullOrWhiteSpace(outText)) DebugLogger.Info("Forge", $"Processor: {outText}");
                if (!string.IsNullOrWhiteSpace(errText)) DebugLogger.Error("Forge", $"Processor ERROR: {errText}");

                if (p.ExitCode != 0)
                {
                    throw new Exception($"Forge processor 执行失败: {proc.Jar} (ExitCode={p.ExitCode})");
                }
            }
        }

        private static Dictionary<string, string> BuildProcessorReplacements(ForgeInstallProfile profile, string gameDirectory, string installerPath)
        {
            var root = gameDirectory;
            var librariesDir = Path.Combine(gameDirectory, "libraries");
            var versionsDir = Path.Combine(gameDirectory, "versions");
            var mcVersion = profile.Minecraft;

            // Forge processors 期望输入是“bundler jar”（通常是带时间戳的 slim/client jar），
            // 而不是 versions/<mc>/<mc>.jar。
            // 优先使用 libraries/net/minecraft/client/<ver>/client-<ver>-*-slim.jar 作为 MINECRAFT_JAR。
            string minecraftJarCandidate = Path.Combine(versionsDir, mcVersion, $"{mcVersion}.jar");
            try
            {
                var clientDir = Path.Combine(librariesDir, "net", "minecraft", "client", mcVersion);
                if (Directory.Exists(clientDir))
                {
                    var best = Directory.GetFiles(clientDir, $"client-{mcVersion}-*-slim.jar")
                        .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
                        .FirstOrDefault();
                    if (!string.IsNullOrEmpty(best))
                    {
                        minecraftJarCandidate = best;
                        DebugLogger.Info("Forge", $"已为 processor 定位到 slim jar: {Path.GetFileName(best)}");
                    }
                }
            }
            catch (Exception ex) { DebugLogger.Warn("Forge", $"查找 slim jar 失败: {ex.Message}"); }

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SIDE"] = "client",
                ["ROOT"] = root,
                ["LIBRARY_DIR"] = librariesDir,
                ["MINECRAFT_VERSION"] = mcVersion,
                ["MINECRAFT_JAR"] = minecraftJarCandidate,
                ["INSTALLER"] = installerPath
            };

            // install_profile.data 中可能包含额外替换项（client/server 路径等）
            if (profile.Data != null)
            {
                foreach (var kv in profile.Data)
                {
                    // 这里先把 client 字段当成可替换值
                    // 真实格式可能更复杂（带 []），后续可继续按 VersionDetailPage 完整实现
                    if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null && !string.IsNullOrWhiteSpace(kv.Value.Client))
                    {
                        dict[kv.Key] = kv.Value.Client;
                    }
                }
            }

            return dict;
        }

        private static string ReplacePlaceholders(string input, Dictionary<string, string> dict)
        {
            if (string.IsNullOrEmpty(input)) return input;

            string result = input;
            foreach (var kv in dict)
            {
                result = result.Replace("{" + kv.Key + "}", kv.Value);
            }
            return result;
        }

        private static string? ReadJarMainClass(string jarPath)
        {
            try
            {
                if (!File.Exists(jarPath)) return null;
                using var zip = ZipFile.OpenRead(jarPath);
                var entry = zip.GetEntry("META-INF/MANIFEST.MF");
                if (entry == null) return null;
                using var s = entry.Open();
                using var r = new StreamReader(s);
                var text = r.ReadToEnd();
                foreach (var line in text.Split('\n'))
                {
                    var l = line.Trim();
                    if (l.StartsWith("Main-Class:", StringComparison.OrdinalIgnoreCase))
                    {
                        return l.Substring("Main-Class:".Length).Trim();
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static async Task EnsureMavenLibrariesDownloadedAsync(
            List<string> mavenCoords,
            string gameDirectory,
            Action<string, double>? progressCallback,
            CancellationToken cancellationToken = default)
        {
            if (mavenCoords == null || mavenCoords.Count == 0) return;

            var downloadService = DownloadSourceManager.Instance.CurrentService;
            var librariesDir = Path.Combine(gameDirectory, "libraries");
            Directory.CreateDirectory(librariesDir);

            var list = mavenCoords.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();

            using var semaphore = new SemaphoreSlim(8);
            int done = 0;
            var failedMavens = new List<string>();

            var tasks = list.Select(async maven =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var rel = MavenToPath(maven);
                    if (string.IsNullOrWhiteSpace(rel)) return;

                    var savePath = Path.Combine(librariesDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(savePath)) return;

                    Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                    var url = downloadService.GetLibraryUrl(rel);

                    using var resp = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    resp.EnsureSuccessStatusCode();
                    var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
                    await File.WriteAllBytesAsync(savePath, bytes, cancellationToken);
                }
                catch (Exception ex)
                {
                    DebugLogger.Error("Forge", $"processor 依赖下载失败: {maven} - {ex.Message}");
                    lock (failedMavens) { failedMavens.Add(maven); }
                }
                finally
                {
                    var c = Interlocked.Increment(ref done);
                    progressCallback?.Invoke($"正在准备 Forge 处理器依赖... ({c}/{list.Count})", 52);
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);

            if (failedMavens.Count > 0)
            {
                DebugLogger.Warn("Forge", $"processor 依赖下载失败 {failedMavens.Count}/{list.Count} 个: {string.Join(", ", failedMavens.Take(5))}");
            }
        }

        private static async Task DownloadInstallProfileLibrariesAsync(
            List<ForgeLibrary> libraries,
            string gameDirectory,
            Action<string, double>? progressCallback,
            CancellationToken cancellationToken = default)
        {
            if (libraries == null || libraries.Count == 0)
                return;

            var downloadService = DownloadSourceManager.Instance.CurrentService;
            var librariesDir = Path.Combine(gameDirectory, "libraries");
            Directory.CreateDirectory(librariesDir);

            int total = libraries.Count;
            int completed = 0;
            var failedLibs = new List<string>();

            using var semaphore = new SemaphoreSlim(8);
            var tasks = libraries.Select(async lib =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    string? downloadUrl = null;
                    string? savePath = null;

                    if (lib.Downloads?.Artifact != null)
                    {
                        var artifact = lib.Downloads.Artifact;
                        if (!string.IsNullOrWhiteSpace(artifact.Path))
                        {
                            downloadUrl = !string.IsNullOrWhiteSpace(artifact.Url)
                                ? artifact.Url
                                : downloadService.GetLibraryUrl(artifact.Path);
                            savePath = Path.Combine(librariesDir, artifact.Path.Replace("/", "\\"));
                        }
                    }

                    if ((string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(savePath)) && !string.IsNullOrWhiteSpace(lib.Name))
                    {
                        var mavenPath = MavenToPath(lib.Name);
                        if (!string.IsNullOrWhiteSpace(mavenPath))
                        {
                            downloadUrl = !string.IsNullOrWhiteSpace(lib.Url)
                                ? $"{lib.Url}{mavenPath}"
                                : downloadService.GetLibraryUrl(mavenPath);
                            savePath = Path.Combine(librariesDir, mavenPath.Replace("/", "\\"));
                        }
                    }

                    if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(savePath))
                    {
                        DebugLogger.Warn("Forge", $"跳过 install_profile 库（无法构建URL）: {lib.Name}");
                        return;
                    }

                    if (File.Exists(savePath))
                        return;

                    Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

                    using var resp = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    resp.EnsureSuccessStatusCode();
                    var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
                    await File.WriteAllBytesAsync(savePath, bytes, cancellationToken);

                    // SHA1校验
                    if (lib.Downloads?.Artifact != null && !string.IsNullOrWhiteSpace(lib.Downloads.Artifact.Sha1))
                    {
                        using var sha1 = SHA1.Create();
                        var hash = string.Concat(sha1.ComputeHash(bytes).Select(b => b.ToString("x2")));
                        if (!string.Equals(hash, lib.Downloads.Artifact.Sha1, StringComparison.OrdinalIgnoreCase))
                        {
                            DebugLogger.Warn("Forge", $"库文件SHA1校验失败: {lib.Name} (期望: {lib.Downloads.Artifact.Sha1}, 实际: {hash})");
                            try { File.Delete(savePath); } catch { }
                            lock (failedLibs) { failedLibs.Add(lib.Name); }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Error("Forge", $"install_profile 库下载失败: {lib.Name} - {ex.Message}");
                    lock (failedLibs) { failedLibs.Add(lib.Name); }
                }
                finally
                {
                    var done = Interlocked.Increment(ref completed);
                    progressCallback?.Invoke($"正在下载 Forge 依赖库... ({done}/{total})", 45 + (done / (double)Math.Max(1, total) * 10));
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);

            if (failedLibs.Count > 0)
            {
                DebugLogger.Warn("Forge", $"install_profile 库下载失败 {failedLibs.Count}/{total} 个: {string.Join(", ", failedLibs.Take(5))}");
            }
        }

        private class MojangVersionJson
        {
            [JsonPropertyName("downloads")]
            public MojangDownloads? Downloads { get; set; }
        }

        private class MojangDownloads
        {
            [JsonPropertyName("client")]
            public MojangDownloadItem? Client { get; set; }
        }

        private class MojangDownloadItem
        {
            [JsonPropertyName("url")]
            public string? Url { get; set; }

            [JsonPropertyName("size")]
            public long Size { get; set; }
        }

        private static async Task DownloadVanillaForForge(
            string gameDirectory,
            string version,
            string targetDirectory,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // 关键修复：Forge processors（如 BundlerExtract）需要 Mojang 官方的 bundler client.jar
            // 某些镜像源/第三方源可能提供“非 bundler”的 1.xx.jar，导致 "Invalid bundler archive"。
            // 因此这里强制走 Mojang 官方 version url -> downloads.client.url。

            Directory.CreateDirectory(targetDirectory);

            string jsonPath = Path.Combine(targetDirectory, $"{version}.json");
            string jarPath = Path.Combine(targetDirectory, $"{version}.jar");

            // 1) 获取官方 version url
            var manifest = await MinecraftVersionService.GetVersionListAsync();
            var v = manifest?.Versions?.FirstOrDefault(x => x.Id == version);
            if (v == null || string.IsNullOrWhiteSpace(v.Url))
                throw new Exception($"找不到版本 {version} 的官方信息 URL");

            // 2) 下载版本 json（强制用 v.Url，不走 DownloadSource 替换）
            if (!File.Exists(jsonPath))
            {
                progress?.Report(new DownloadProgress { Status = "正在下载官方版本JSON...", CurrentFile = $"{version}.json" });
                var jsonText = await _httpClient.GetStringAsync(v.Url, cancellationToken);
                await File.WriteAllTextAsync(jsonPath, jsonText, cancellationToken);
            }

            // 3) 解析 downloads.client.url（官方）
            var parsed = JsonSerializer.Deserialize<MojangVersionJson>(await File.ReadAllTextAsync(jsonPath, cancellationToken), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var clientUrl = parsed?.Downloads?.Client?.Url;
            var clientSize = parsed?.Downloads?.Client?.Size ?? 0;
            if (string.IsNullOrWhiteSpace(clientUrl))
                throw new Exception($"版本 {version} JSON 中缺少 downloads.client.url");

            // 4) 下载 bundler client.jar 到 versions/{version}/{version}.jar
            if (!File.Exists(jarPath))
            {
                progress?.Report(new DownloadProgress { Status = "正在下载官方客户端JAR...", CurrentFile = $"{version}.jar" });

                using var resp = await _httpClient.GetAsync(clientUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                resp.EnsureSuccessStatusCode();

                var totalBytes = resp.Content.Headers.ContentLength ?? clientSize;
                using var contentStream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(jarPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;
                var lastReport = DateTime.UtcNow;
                long lastReportedBytes = 0;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalRead += bytesRead;

                    var now = DateTime.UtcNow;
                    if ((now - lastReport).TotalMilliseconds >= 200)
                    {
                        var elapsed = (now - lastReport).TotalSeconds;
                        var speed = elapsed > 0 ? (totalRead - lastReportedBytes) / elapsed : 0;
                        progress?.Report(new DownloadProgress
                        {
                            Status = $"正在下载官方客户端JAR {version}.jar",
                            CurrentFile = $"{version}.jar",
                            CurrentFileBytes = totalRead,
                            CurrentFileTotalBytes = totalBytes,
                            DownloadSpeed = speed
                        });
                        lastReport = now;
                        lastReportedBytes = totalRead;
                    }
                }

                progress?.Report(new DownloadProgress
                {
                    Status = "官方客户端JAR下载完成",
                    CurrentFile = $"{version}.jar",
                    CurrentFileBytes = totalRead,
                    CurrentFileTotalBytes = totalBytes
                });
            }
        }

        private static async Task<bool> RunForgeInstallerAsync(
            string installerPath,
            string gameDirectory,
            string mcVersion,
            string forgeVersion,
            LauncherConfig config,
            CancellationToken cancellationToken = default,
            Action<string>? onInstallerOutput = null)
        {
            // 这里实现 VersionDetailPage 同款参数尝试逻辑的最小可用版
            // 说明：完整逻辑很长（包含旧版本手动安装等），可后续继续补齐。

            // 确保 launcher_profiles.json 存在
            string profilesPath = Path.Combine(gameDirectory, "launcher_profiles.json");
            if (!File.Exists(profilesPath))
            {
                var defaultProfiles = new
                {
                    profiles = new { },
                    selectedProfile = (string?)null,
                    clientToken = Guid.NewGuid().ToString(),
                    authenticationDatabase = new { },
                    launcherVersion = new { name = "ObsMCLauncher", format = 21 }
                };
                await File.WriteAllTextAsync(profilesPath, JsonSerializer.Serialize(defaultProfiles, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            }

            bool isNewVersion = IsForgeInstallerNewVersion(mcVersion);
            bool isVeryOldVersion = IsVeryOldForgeVersion(mcVersion);

            var argsList = new List<string>();

            if (isNewVersion)
            {
                // 1.13+: --installClient <path>
                argsList.Add($"--installClient \"{gameDirectory}\"");
            }
            else if (isVeryOldVersion)
            {
                // <1.9: 先尝试不带目录参数，再尝试各种格式
                argsList.Add("--installClient");
                argsList.Add($"--installClient \"{gameDirectory}\"");
                argsList.Add("--install-client");
                argsList.Add("-installClient");
            }
            else
            {
                // 1.9~1.12.2: 旧版安装器--installClient通常不接受目录参数
                argsList.Add("--installClient");
                argsList.Add($"--installClient \"{gameDirectory}\"");
                argsList.Add("--install-client");
            }

            foreach (var args in argsList)
            {
                if (await TryRunForgeInstallerWithArgs(installerPath, gameDirectory, mcVersion, args, cancellationToken, onInstallerOutput))
                    return true;
            }

            // 对于 1.12.2 及更早的版本，安装器参数可能无效，需手动安装（完整迁移 VersionDetailPage 逻辑）
            if (isVeryOldVersion || !isNewVersion)
            {
                DebugLogger.Info("Forge", $"安装器参数不适用，尝试手动安装客户端... {mcVersion}");
                string targetVersionName = $"{mcVersion}-forge-{forgeVersion}";
                string targetVersionDir = Path.Combine(gameDirectory, "versions", targetVersionName);
                var ok = await ManualInstallVeryOldForgeClient(installerPath, gameDirectory, mcVersion, targetVersionName, targetVersionDir, config, cancellationToken);
                return ok;
            }

            return false;
        }

        private static async Task<bool> TryRunForgeInstallerWithArgs(
            string installerPath,
            string gameDirectory,
            string mcVersion,
            string arguments,
            CancellationToken cancellationToken = default,
            Action<string>? onInstallerOutput = null)
        {
            // 根据Minecraft版本选择Java，这对于旧版本Forge兼容性至关重要
            var config = LauncherConfig.Load();
            string javaPath;
            
            // 根据Minecraft版本选择Java
            if (!string.IsNullOrEmpty(mcVersion))
            {
                javaPath = config.GetActualJavaPath(mcVersion);
                DebugLogger.Info("Forge", $"根据Minecraft版本 {mcVersion} 选择Java: {javaPath}");
            }
            else
            {
                javaPath = config.JavaPath ?? "java";
            }

            if (string.IsNullOrEmpty(javaPath) || !File.Exists(javaPath))
            {
                javaPath = "java"; // 回退到系统 PATH
                DebugLogger.Warn("Forge", "未配置有效Java路径，将使用系统 PATH 中的 'java'。这可能导致安装失败！");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = $"-jar \"{installerPath}\" {arguments}",
                WorkingDirectory = gameDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };

            var stdout = new System.Text.StringBuilder();
            var stderr = new System.Text.StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    stdout.AppendLine(e.Data);
                    DebugLogger.Info("Forge", $"Installer: {e.Data}");
                    onInstallerOutput?.Invoke(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    stderr.AppendLine(e.Data);
                    DebugLogger.Error("Forge", $"Installer ERROR: {e.Data}");
                    if (!e.Data.Contains("joptsimple") && !e.Data.Contains("Exception in thread"))
                        onInstallerOutput?.Invoke(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                var waitTask = process.WaitForExitAsync(cancellationToken);
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
                var completed = await Task.WhenAny(waitTask, timeoutTask);

                if (completed == timeoutTask)
                {
                    try { if (!process.HasExited) process.Kill(true); } catch { }
                    DebugLogger.Warn("Forge", "安装器超时（10分钟），已终止进程");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                DebugLogger.Info("Forge", "安装器已取消，进程已终止");
                throw;
            }

            DebugLogger.Info("Forge", $"安装器退出码: {process.ExitCode}");

            if (process.ExitCode != 0)
            {
                // 汇总输出，便于定位原因
                var outText = stdout.ToString().Trim();
                var errText = stderr.ToString().Trim();
                if (!string.IsNullOrEmpty(outText))
                    DebugLogger.Info("Forge", $"安装器输出汇总:\n{outText}");
                if (!string.IsNullOrEmpty(errText))
                    DebugLogger.Error("Forge", $"安装器错误汇总:\n{errText}");
            }

            return process.ExitCode == 0;
        }

        private static async Task MergeVanillaIntoForgeJson(
            string forgeJsonPath,
            string customVersionName,
            string gameDirectory,
            string vanillaVersion,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(forgeJsonPath))
                {
                    DebugLogger.Warn("Forge", $"MergeVanillaIntoForgeJson: Forge JSON不存在: {forgeJsonPath}");
                    return;
                }

                var forgeJsonContent = await File.ReadAllTextAsync(forgeJsonPath, cancellationToken);
                var forgeJson = JsonNode.Parse(forgeJsonContent);
                if (forgeJson == null)
                {
                    DebugLogger.Warn("Forge", "MergeVanillaIntoForgeJson: Forge JSON解析失败");
                    return;
                }
                var forgeObj = forgeJson.AsObject();

                forgeObj["id"] = customVersionName;

                // 优先从.temp读取原版JSON，如果不存在则从标准位置读取
                string tempVanillaJsonPath = Path.Combine(gameDirectory, ".temp", "versions", vanillaVersion, $"{vanillaVersion}.json");
                string standardVanillaJsonPath = Path.Combine(gameDirectory, "versions", vanillaVersion, $"{vanillaVersion}.json");
                string vanillaJsonPath = File.Exists(tempVanillaJsonPath) ? tempVanillaJsonPath : standardVanillaJsonPath;
                
                if (!File.Exists(vanillaJsonPath))
                {
                    DebugLogger.Warn("Forge", $"原版JSON不存在（.temp和标准位置都未找到），保留inheritsFrom: {vanillaVersion}");
                    forgeObj["inheritsFrom"] = vanillaVersion;
                    await File.WriteAllTextAsync(forgeJsonPath, forgeObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
                    return;
                }

                DebugLogger.Info("Forge", $"从{(vanillaJsonPath == tempVanillaJsonPath ? "临时目录" : "标准位置")}读取原版JSON: {vanillaJsonPath}");
                var vanillaJsonContent = await File.ReadAllTextAsync(vanillaJsonPath, cancellationToken);
                var vanillaJson = JsonNode.Parse(vanillaJsonContent);
                if (vanillaJson == null)
                {
                    DebugLogger.Warn("Forge", "原版JSON解析失败，保留inheritsFrom");
                    forgeObj["inheritsFrom"] = vanillaVersion;
                    await File.WriteAllTextAsync(forgeJsonPath, forgeObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
                    return;
                }
                var vanillaObj = vanillaJson.AsObject();

                var forgeLibraries = forgeObj["libraries"]?.AsArray() ?? new JsonArray();
                var vanillaLibraries = vanillaObj["libraries"]?.AsArray() ?? new JsonArray();

                var existingLibs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var lib in forgeLibraries)
                {
                    if (lib?["name"] != null)
                    {
                        existingLibs.Add(GetLibraryKeyFromName(lib["name"]!.ToString()));
                    }
                }

                int addedCount = 0;
                foreach (var vlib in vanillaLibraries)
                {
                    bool isNativesLib = vlib?["natives"] != null;
                    if (isNativesLib)
                    {
                        forgeLibraries.Add(vlib?.DeepClone());
                        addedCount++;
                        continue;
                    }

                    var name = vlib?["name"]?.ToString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        var key = GetLibraryKeyFromName(name);
                        if (!existingLibs.Contains(key))
                        {
                            forgeLibraries.Add(vlib!.DeepClone());
                            existingLibs.Add(key);
                            addedCount++;
                        }
                    }
                }

                forgeObj["libraries"] = forgeLibraries;

                if (!forgeObj.ContainsKey("assetIndex") && vanillaObj.ContainsKey("assetIndex"))
                    forgeObj["assetIndex"] = vanillaObj["assetIndex"]!.DeepClone();
                if (!forgeObj.ContainsKey("assets") && vanillaObj.ContainsKey("assets"))
                    forgeObj["assets"] = vanillaObj["assets"]!.DeepClone();
                if (!forgeObj.ContainsKey("arguments") && vanillaObj.ContainsKey("arguments"))
                    forgeObj["arguments"] = vanillaObj["arguments"]!.DeepClone();

                // 移除inheritsFrom，使版本自包含
                forgeObj.Remove("inheritsFrom");

                await File.WriteAllTextAsync(forgeJsonPath, forgeObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

                DebugLogger.Info("Forge", $"MergeVanillaIntoForgeJson 完成，新增库 {addedCount} 个");
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Forge", $"MergeVanillaIntoForgeJson 失败: {ex.Message}");
                throw;
            }
        }

        private static string GetLibraryKeyFromName(string libraryName)
        {
            if (string.IsNullOrEmpty(libraryName)) return string.Empty;
            var parts = libraryName.Split(':');
            if (parts.Length >= 4)
                return $"{parts[0]}:{parts[1]}:{parts[3]}";
            if (parts.Length >= 2)
                return $"{parts[0]}:{parts[1]}";
            return libraryName;
        }

        private static async Task<bool> ManualInstallVeryOldForgeClient(
            string installerPath,
            string gameDirectory,
            string mcVersion,
            string targetVersionName,
            string targetVersionDir,
            LauncherConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                DebugLogger.Info("Forge", $"开始手动安装旧版Forge: {mcVersion} -> {targetVersionName}");

                using var zip = ZipFile.OpenRead(installerPath);
                var profileEntry = zip.GetEntry("install_profile.json");
                if (profileEntry == null)
                {
                    DebugLogger.Warn("Forge", "安装器中找不到 install_profile.json");
                    return false;
                }

                string profileJson;
                using (var stream = profileEntry.Open())
                using (var reader = new StreamReader(stream))
                    profileJson = await reader.ReadToEndAsync();

                var profile = JsonDocument.Parse(profileJson);

                Directory.CreateDirectory(targetVersionDir);

                // 尝试从install_profile.json中获取版本JSON
                string? versionJsonContent = null;

                // 方式1: 旧格式 - install_profile.json 中的 versionInfo 字段
                if (profile.RootElement.TryGetProperty("versionInfo", out var versionInfo))
                {
                    versionJsonContent = versionInfo.GetRawText();
                    DebugLogger.Info("Forge", "从install_profile.versionInfo提取版本JSON");
                }

                // 方式2: 新格式 - 从安装器jar中提取 version.json
                if (string.IsNullOrEmpty(versionJsonContent))
                {
                    var versionEntry = zip.GetEntry("version.json");
                    if (versionEntry != null)
                    {
                        using var vstream = versionEntry.Open();
                        using var vreader = new StreamReader(vstream);
                        versionJsonContent = await vreader.ReadToEndAsync();
                        DebugLogger.Info("Forge", "从安装器中提取version.json");
                    }
                }

                // 方式3: 尝试从install_profile的data字段中提取
                if (string.IsNullOrEmpty(versionJsonContent))
                {
                    DebugLogger.Warn("Forge", "install_profile.json 中缺少 versionInfo 字段，且未找到 version.json");
                    return false;
                }

                var versionJsonPath = Path.Combine(targetVersionDir, $"{targetVersionName}.json");
                var versionJson = JsonNode.Parse(versionJsonContent)!.AsObject();
                versionJson["id"] = targetVersionName;

                await File.WriteAllTextAsync(versionJsonPath, versionJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

                // 下载install_profile中的libraries
                if (profile.RootElement.TryGetProperty("install", out var installSection))
                {
                    if (installSection.TryGetProperty("libraries", out var libsElement))
                    {
                        var forgeLibraries = JsonSerializer.Deserialize<List<ForgeLibrary>>(libsElement.GetRawText());
                        if (forgeLibraries != null && forgeLibraries.Count > 0)
                        {
                            DebugLogger.Info("Forge", $"旧版Forge: 需要下载 {forgeLibraries.Count} 个库文件");
                            await DownloadInstallProfileLibrariesAsync(forgeLibraries, gameDirectory, null, cancellationToken);
                        }
                    }
                }

                // 新格式install_profile中也可能有顶层libraries
                if (profile.RootElement.TryGetProperty("libraries", out var topLibs))
                {
                    var topForgeLibs = JsonSerializer.Deserialize<List<ForgeLibrary>>(topLibs.GetRawText());
                    if (topForgeLibs != null && topForgeLibs.Count > 0)
                    {
                        DebugLogger.Info("Forge", $"install_profile顶层: 需要下载 {topForgeLibs.Count} 个库文件");
                        await DownloadInstallProfileLibrariesAsync(topForgeLibs, gameDirectory, null, cancellationToken);
                    }
                }

                // 尝试合并原版信息
                var vanillaJsonPath = Path.Combine(gameDirectory, "versions", mcVersion, $"{mcVersion}.json");
                if (File.Exists(vanillaJsonPath))
                {
                    try
                    {
                        await MergeVanillaIntoForgeJson(versionJsonPath, targetVersionName, gameDirectory, mcVersion, cancellationToken);
                    }
                    catch (Exception mergeEx)
                    {
                        DebugLogger.Warn("Forge", $"旧版Forge合并原版信息失败: {mergeEx.Message}");
                    }
                }
                else
                {
                    DebugLogger.Warn("Forge", $"原版JSON不存在: {vanillaJsonPath}，跳过合并");
                }

                // 复制原版jar到版本目录
                var vanillaJar = Path.Combine(gameDirectory, "versions", mcVersion, $"{mcVersion}.jar");
                var jarInTarget = Path.Combine(targetVersionDir, $"{mcVersion}.jar");
                if (File.Exists(vanillaJar) && !File.Exists(jarInTarget))
                {
                    File.Copy(vanillaJar, jarInTarget, true);
                    DebugLogger.Info("Forge", $"已复制原版JAR到版本目录: {mcVersion}.jar");
                }
                else if (!File.Exists(vanillaJar))
                {
                    DebugLogger.Warn("Forge", $"原版JAR不存在: {vanillaJar}，旧版Forge可能无法正常启动");
                }

                // 验证安装结果
                if (!File.Exists(versionJsonPath))
                {
                    DebugLogger.Error("Forge", "手动安装后版本JSON文件不存在");
                    return false;
                }

                DebugLogger.Info("Forge", $"旧版Forge手动安装完成: {targetVersionName}");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Forge", $"手动安装旧版Forge失败: {ex.Message}");
                return false;
            }
        }



        private static async Task RenameForgeVersionAsync(
            string gameDirectory,
            string gameVersion,
            string forgeVersion,
            string customVersionName,
            CancellationToken cancellationToken = default)
        {
            string officialForgeId = $"{gameVersion}-forge-{forgeVersion}";
            string officialDir = Path.Combine(gameDirectory, "versions", officialForgeId);
            string customDir = Path.Combine(gameDirectory, "versions", customVersionName);

            if (!Directory.Exists(officialDir))
                throw new Exception($"找不到Forge安装目录: {officialForgeId}");

            if (Directory.Exists(customDir))
                Directory.Delete(customDir, true);

            Directory.Move(officialDir, customDir);

            var oldJson = Path.Combine(customDir, $"{officialForgeId}.json");
            var newJson = Path.Combine(customDir, $"{customVersionName}.json");
            if (File.Exists(oldJson)) File.Move(oldJson, newJson, true);

            var oldJar = Path.Combine(customDir, $"{officialForgeId}.jar");
            var newJar = Path.Combine(customDir, $"{customVersionName}.jar");
            if (File.Exists(oldJar)) File.Move(oldJar, newJar, true);

            if (File.Exists(newJson))
            {
                try
                {
                    var jsonText = await File.ReadAllTextAsync(newJson, cancellationToken);
                    var node = JsonNode.Parse(jsonText) as JsonObject;
                    if (node != null)
                    {
                        node["id"] = customVersionName;
                        await File.WriteAllTextAsync(newJson, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Warn("Forge", $"修改版本JSON的id字段失败: {ex.Message}");
                }
            }

            var customJar = Path.Combine(customDir, $"{customVersionName}.jar");
            if (!File.Exists(customJar))
            {
                try
                {
                    var jarInForgeDir = Path.Combine(customDir, $"{gameVersion}.jar");
                    var jarInVanillaDir = Path.Combine(gameDirectory, "versions", gameVersion, $"{gameVersion}.jar");

                    if (File.Exists(jarInForgeDir))
                    {
                        File.Copy(jarInForgeDir, customJar, true);
                        DebugLogger.Info("Forge", $"已从Forge目录内复制主JAR: {gameVersion}.jar -> {customVersionName}.jar");
                    }
                    else if (File.Exists(jarInVanillaDir))
                    {
                        File.Copy(jarInVanillaDir, customJar, true);
                        DebugLogger.Info("Forge", $"已从原版目录复制主JAR: {gameVersion}.jar -> {customVersionName}.jar");
                    }
                    else
                    {
                        DebugLogger.Warn("Forge", $"未找到可复制的原版JAR（{gameVersion}.jar），新版Forge可能不需要独立JAR");
                    }
                }
                catch (Exception jarEx)
                {
                    DebugLogger.Warn("Forge", $"复制主JAR失败: {jarEx.Message}");
                }
            }
        }
        
        // 使用镜像源获取Forge支持版本列表
        private const string BMCL_FORGE_SUPPORT = "https://bmclapi2.bangbang93.com/forge/minecraft";
        private const string BMCL_FORGE_LIST = "https://bmclapi2.bangbang93.com/forge/minecraft/{0}";
        // Forge下载使用Maven格式
        private const string BMCL_FORGE_DOWNLOAD = "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/{0}/forge-{0}-installer.jar";
        
        // 官方源（Forge官方文件服务器）
        private const string OFFICIAL_FORGE_MAVEN = "https://maven.minecraftforge.net/net/minecraftforge/forge/";
        private const string OFFICIAL_FORGE_PROMO = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";

        /// <summary>
        /// 获取Forge支持的Minecraft版本列表
        /// </summary>
        public static async Task<List<string>> GetSupportedMinecraftVersionsAsync()
        {
            try
            {
                var config = LauncherConfig.Load();
                DebugLogger.Info("ForgeService", $"获取Forge支持的MC版本列表... (源: {config.DownloadSource})");
                
                if (config.DownloadSource == DownloadSource.BMCLAPI)
                {
                    // 使用镜像源
                    var response = await _httpClient.GetAsync(BMCL_FORGE_SUPPORT);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    var versions = JsonSerializer.Deserialize<List<string>>(json);

                    DebugLogger.Info("ForgeService", $"从镜像源获取到 {versions?.Count ?? 0} 个支持的MC版本");
                    return versions ?? new List<string>();
                }
                else
                {
                    // 使用官方源 - 通过解析promotions文件获取支持的版本
                    var response = await _httpClient.GetAsync(OFFICIAL_FORGE_PROMO);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    var promoData = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                    
                    if (promoData != null && promoData.ContainsKey("promos"))
                    {
                        var promosJson = promoData["promos"].ToString();
                        var promos = JsonSerializer.Deserialize<Dictionary<string, string>>(promosJson ?? "{}");
                        
                        // 从promos中提取MC版本号
                        var versions = promos?.Keys
                            .Select(k => k.Split('-')[0])
                            .Distinct()
                            .OrderByDescending(v => v)
                            .ToList() ?? new List<string>();
                        
                        DebugLogger.Info("ForgeService", $"从官方源获取到 {versions.Count} 个支持的MC版本");
                        return versions;
                    }
                    
                    return new List<string>();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error("ForgeService", $"获取Forge支持版本失败: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// 获取指定Minecraft版本的Forge版本列表
        /// </summary>
        public static async Task<List<ForgeVersion>> GetForgeVersionsAsync(string mcVersion)
        {
            try
            {
                var config = LauncherConfig.Load();
                DebugLogger.Info("ForgeService", $"获取 MC {mcVersion} 的Forge版本列表... (源: {config.DownloadSource})");
                
                if (config.DownloadSource == DownloadSource.BMCLAPI)
                {
                    // 使用镜像源
                    var url = string.Format(BMCL_FORGE_LIST, mcVersion);
                    var response = await _httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    var forgeList = JsonSerializer.Deserialize<List<ForgeVersion>>(json);

                    if (forgeList != null)
                    {
                        // 按build号降序排序（最新的在前）
                        forgeList = forgeList.OrderByDescending(f => f.Build).ToList();
                        DebugLogger.Info("ForgeService", $"从镜像源获取到 {forgeList.Count} 个Forge版本");
                    }

                    return forgeList ?? new List<ForgeVersion>();
                }
                else
                {
                    // 使用官方源 - 从Maven仓库获取版本列表
                    var response = await _httpClient.GetAsync(OFFICIAL_FORGE_MAVEN + "maven-metadata.xml");
                    response.EnsureSuccessStatusCode();
                    
                    var xml = await response.Content.ReadAsStringAsync();
                    var forgeList = ParseForgeMavenMetadata(xml, mcVersion);
                    
                    DebugLogger.Info("ForgeService", $"从官方源获取到 {forgeList.Count} 个Forge版本");
                    return forgeList;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error("ForgeService", $"获取Forge版本列表失败: {ex.Message}");
                return new List<ForgeVersion>();
            }
        }

        /// <summary>
        /// 解析Forge Maven元数据XML
        /// </summary>
        private static List<ForgeVersion> ParseForgeMavenMetadata(string xml, string mcVersion)
        {
            var forgeList = new List<ForgeVersion>();
            
            try
            {
                // 简单的XML解析，提取<version>标签
                var lines = xml.Split('\n');
                int build = 1000; // 从高数字开始，确保排序正确
                
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("<version>") && trimmed.EndsWith("</version>"))
                    {
                        var versionText = trimmed.Replace("<version>", "").Replace("</version>", "");
                        
                        // 格式: 1.20.1-47.2.0 或 1.20.1-47.2.0-1.20.1
                        if (versionText.StartsWith(mcVersion + "-"))
                        {
                            // 移除MC版本号前缀，获取Forge版本号
                            var forgeVer = versionText.Substring(mcVersion.Length + 1);
                            
                            // 如果有额外的后缀（如 47.2.0-1.20.1），只取前面部分
                            var firstDashIndex = forgeVer.IndexOf('-');
                            if (firstDashIndex > 0)
                            {
                                // 检查是否是类似 "47.2.0-1.20.1" 的格式
                                var afterDash = forgeVer.Substring(firstDashIndex + 1);
                                if (afterDash.StartsWith(mcVersion))
                                {
                                    forgeVer = forgeVer.Substring(0, firstDashIndex);
                                }
                            }
                            
                            forgeList.Add(new ForgeVersion
                            {
                                Build = build--,
                                McVersion = mcVersion,
                                Version = forgeVer,
                                Modified = DateTime.Now.ToString("yyyy-MM-dd")
                            });
                        }
                    }
                }
                
                // 按build号降序排序
                forgeList = forgeList.OrderByDescending(f => f.Build).ToList();
            }
            catch (Exception ex)
            {
                DebugLogger.Error("ForgeService", $"解析Maven元数据失败: {ex.Message}");
            }
            
            return forgeList;
        }

        /// <summary>
        /// 下载Forge安装器（带详细进度信息）
        /// </summary>
        /// <param name="forgeVersion">Forge版本 (格式: mcVersion-forgeVersion, 例如: 1.20.1-47.2.0)</param>
        /// <param name="savePath">保存路径</param>
        /// <param name="progressCallback">进度回调（当前字节数, 速度, 总字节数）</param>
        /// <param name="cancellationToken">取消令牌</param>
        public static async Task<bool> DownloadForgeInstallerWithDetailsAsync(
            string forgeVersion,
            string savePath,
            Action<long, double, long>? progressCallback = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                var config = LauncherConfig.Load();
                DebugLogger.Info("ForgeService", $"开始下载Forge安装器: {forgeVersion} (源: {config.DownloadSource})");
                
                // 准备多个可能的URL格式（与原方法相同的逻辑）
                var urlsToTry = new List<string>();
                
                // 提取MC版本号，用于判断是否是旧版本
                string mcVersion = "";
                if (forgeVersion.Contains("-"))
                {
                    mcVersion = forgeVersion.Split('-')[0];
                }
                
                // 判断是否是旧版本Forge（1.12.2及之前）
                bool isOldVersion = IsOldForgeVersion(mcVersion);
                
                if (config.DownloadSource == DownloadSource.BMCLAPI)
                {
                    // 使用镜像源 - Maven格式
                    if (isOldVersion)
                    {
                        // 旧版本格式：1.8.9-11.15.1.2318-1.8.9
                        string oldFormatVersion = $"{forgeVersion}-{mcVersion}";
                        urlsToTry.Add(string.Format(BMCL_FORGE_DOWNLOAD, oldFormatVersion));
                        // 也尝试不带后缀的格式
                        urlsToTry.Add(string.Format(BMCL_FORGE_DOWNLOAD, forgeVersion));
                    }
                    else
                    {
                        urlsToTry.Add(string.Format(BMCL_FORGE_DOWNLOAD, forgeVersion));
                    }
                    
                    // 添加官方源作为备用
                    if (isOldVersion)
                    {
                        string oldFormatVersion = $"{forgeVersion}-{mcVersion}";
                        urlsToTry.Add($"{OFFICIAL_FORGE_MAVEN}{oldFormatVersion}/forge-{oldFormatVersion}-installer.jar");
                        urlsToTry.Add($"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{oldFormatVersion}/forge-{oldFormatVersion}-installer.jar");
                    }
                    urlsToTry.Add($"{OFFICIAL_FORGE_MAVEN}{forgeVersion}/forge-{forgeVersion}-installer.jar");
                    urlsToTry.Add($"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{forgeVersion}/forge-{forgeVersion}-installer.jar");
                }
                else
                {
                    // 使用官方源
                    if (isOldVersion)
                    {
                        // 格式1: 1.8.9-11.15.1.2318-1.8.9
                        string oldFormatVersion = $"{forgeVersion}-{mcVersion}";
                        urlsToTry.Add($"{OFFICIAL_FORGE_MAVEN}{oldFormatVersion}/forge-{oldFormatVersion}-installer.jar");
                        urlsToTry.Add($"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{oldFormatVersion}/forge-{oldFormatVersion}-installer.jar");
                    }
                    // 格式2: 标准格式
                    urlsToTry.Add($"{OFFICIAL_FORGE_MAVEN}{forgeVersion}/forge-{forgeVersion}-installer.jar");
                    // 格式3: files.minecraftforge.net (标准格式)
                    urlsToTry.Add($"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{forgeVersion}/forge-{forgeVersion}-installer.jar");
                    // 格式4: 镜像源作为最终备用
                    if (isOldVersion)
                    {
                        string oldFormatVersion = $"{forgeVersion}-{mcVersion}";
                        urlsToTry.Add(string.Format(BMCL_FORGE_DOWNLOAD, oldFormatVersion));
                    }
                    urlsToTry.Add(string.Format(BMCL_FORGE_DOWNLOAD, forgeVersion));
                }
                
                // 确保目录存在
                var directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                HttpResponseMessage? response = null;
                Exception? lastException = null;
                
                // 尝试所有可能的URL
                foreach (var url in urlsToTry)
                {
                    try
                    {
                        DebugLogger.Info("ForgeService", $"尝试下载URL: {url}");
                        response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        response.EnsureSuccessStatusCode();
                        DebugLogger.Info("ForgeService", $"成功找到Forge安装器: {url}");
                        break; // 成功，跳出循环
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Warn("ForgeService", $"URL失败: {url} - {ex.Message}");
                        lastException = ex;
                        response?.Dispose();
                        response = null;
                    }
                }
                
                // 如果所有URL都失败了
                if (response == null || !response.IsSuccessStatusCode)
                {
                    DebugLogger.Warn("ForgeService", "所有URL都无法下载Forge安装器");
                    return false;
                }

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;
                
                // 用于计算下载速度
                var startTime = DateTime.Now;
                var lastReportTime = startTime;
                var lastReportedBytes = 0L;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalRead += bytesRead;

                    // 每100ms报告一次进度
                    var now = DateTime.Now;
                    if ((now - lastReportTime).TotalMilliseconds >= 100 || totalRead == totalBytes)
                    {
                        var elapsedSeconds = (now - lastReportTime).TotalSeconds;
                        var bytesInPeriod = totalRead - lastReportedBytes;
                        var speed = elapsedSeconds > 0 ? bytesInPeriod / elapsedSeconds : 0;
                        
                        progressCallback?.Invoke(totalRead, speed, totalBytes);
                        
                        lastReportTime = now;
                        lastReportedBytes = totalRead;
                    }
                }
                
                // 最后再报告一次
                progressCallback?.Invoke(totalRead, 0, totalBytes);

                DebugLogger.Info("ForgeService", $"Forge安装器下载完成: {savePath}");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Error("ForgeService", $"下载Forge安装器失败: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 下载Forge安装器
        /// </summary>
        /// <param name="forgeVersion">Forge版本 (格式: mcVersion-forgeVersion, 例如: 1.20.1-47.2.0)</param>
        /// <param name="savePath">保存路径</param>
        /// <param name="progress">进度回调</param>
        public static async Task<bool> DownloadForgeInstallerAsync(
            string forgeVersion,
            string savePath,
            IProgress<double>? progress = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                var config = LauncherConfig.Load();
                DebugLogger.Info("ForgeService", $"开始下载Forge安装器: {forgeVersion} (源: {config.DownloadSource})");
                
                // 准备多个可能的URL格式
                var urlsToTry = new List<string>();
                
                // 提取MC版本号，用于判断是否是旧版本
                string mcVersion = "";
                if (forgeVersion.Contains("-"))
                {
                    mcVersion = forgeVersion.Split('-')[0];
                }
                
                // 判断是否是旧版本Forge（1.12.2及之前）
                bool isOldVersion = IsOldForgeVersion(mcVersion);
                
                if (config.DownloadSource == DownloadSource.BMCLAPI)
                {
                    // 使用镜像源 - Maven格式
                    if (isOldVersion)
                    {
                        // 旧版本格式：1.8.9-11.15.1.2318-1.8.9
                        string oldFormatVersion = $"{forgeVersion}-{mcVersion}";
                        urlsToTry.Add(string.Format(BMCL_FORGE_DOWNLOAD, oldFormatVersion));
                        // 也尝试不带后缀的格式
                        urlsToTry.Add(string.Format(BMCL_FORGE_DOWNLOAD, forgeVersion));
                    }
                    else
                    {
                        urlsToTry.Add(string.Format(BMCL_FORGE_DOWNLOAD, forgeVersion));
                    }
                    
                    // 添加官方源作为备用
                    if (isOldVersion)
                    {
                        string oldFormatVersion = $"{forgeVersion}-{mcVersion}";
                        urlsToTry.Add($"{OFFICIAL_FORGE_MAVEN}{oldFormatVersion}/forge-{oldFormatVersion}-installer.jar");
                        urlsToTry.Add($"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{oldFormatVersion}/forge-{oldFormatVersion}-installer.jar");
                    }
                    urlsToTry.Add($"{OFFICIAL_FORGE_MAVEN}{forgeVersion}/forge-{forgeVersion}-installer.jar");
                    urlsToTry.Add($"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{forgeVersion}/forge-{forgeVersion}-installer.jar");
                }
                else
                {
                    // 使用官方源
                    if (isOldVersion)
                    {
                        // 旧版本格式：1.8.9-11.15.1.2318-1.8.9
                        string oldFormatVersion = $"{forgeVersion}-{mcVersion}";
                        // 格式1: Maven 仓库（旧版本格式）
                        urlsToTry.Add($"{OFFICIAL_FORGE_MAVEN}{oldFormatVersion}/forge-{oldFormatVersion}-installer.jar");
                        // 格式2: files.minecraftforge.net（旧版本格式）
                        urlsToTry.Add($"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{oldFormatVersion}/forge-{oldFormatVersion}-installer.jar");
                    }
                    
                    // 格式3: Maven 仓库 (标准格式)
                    urlsToTry.Add($"{OFFICIAL_FORGE_MAVEN}{forgeVersion}/forge-{forgeVersion}-installer.jar");
                    // 格式4: files.minecraftforge.net (标准格式)
                    urlsToTry.Add($"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{forgeVersion}/forge-{forgeVersion}-installer.jar");
                    // 格式5: 镜像源作为最终备用
                    if (isOldVersion)
                    {
                        string oldFormatVersion = $"{forgeVersion}-{mcVersion}";
                        urlsToTry.Add(string.Format(BMCL_FORGE_DOWNLOAD, oldFormatVersion));
                    }
                    urlsToTry.Add(string.Format(BMCL_FORGE_DOWNLOAD, forgeVersion));
                }

                // 确保目录存在
                var directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                HttpResponseMessage? response = null;
                string successUrl = "";
                Exception? lastException = null;
                
                // 尝试所有可能的URL
                foreach (var url in urlsToTry)
                {
                    try
                    {
                        DebugLogger.Info("ForgeService", $"尝试下载URL: {url}");
                        response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        response.EnsureSuccessStatusCode();
                        successUrl = url;
                        DebugLogger.Info("ForgeService", $"成功找到Forge安装器: {url}");
                        break; // 成功，跳出循环
                    }
                    catch (HttpRequestException ex)
                    {
                        DebugLogger.Warn("ForgeService", $"URL失败 ({ex.StatusCode}): {url}");
                        lastException = ex;
                        response?.Dispose();
                        response = null;
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Warn("ForgeService", $"URL错误: {url} - {ex.Message}");
                        lastException = ex;
                        response?.Dispose();
                        response = null;
                    }
                }
                
                // 如果所有URL都失败了
                if (response == null || !response.IsSuccessStatusCode)
                {
                    DebugLogger.Warn("ForgeService", "所有URL都无法下载Forge安装器");
                    if (lastException != null)
                    {
                        throw new Exception($"无法下载Forge安装器，已尝试 {urlsToTry.Count} 个URL", lastException);
                    }
                    return false;
                }

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;
                
                // 用于计算下载速度
                var startTime = DateTime.Now;
                var lastReportTime = startTime;
                var lastReportedBytes = 0L;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalRead += bytesRead;

                    // 每100ms报告一次进度
                    var now = DateTime.Now;
                    if ((now - lastReportTime).TotalMilliseconds >= 100 || totalRead == totalBytes)
                    {
                        if (totalBytes > 0)
                        {
                            var percentage = (double)totalRead / totalBytes * 100;
                            progress?.Report(percentage);
                        }
                        
                        lastReportTime = now;
                        lastReportedBytes = totalRead;
                    }
                }

                DebugLogger.Info("ForgeService", $"Forge安装器下载完成: {savePath}");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Error("ForgeService", $"下载Forge安装器失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从Forge安装器中提取install_profile.json
        /// </summary>
        public static async Task<ForgeInstallProfile?> ExtractInstallProfileAsync(string installerPath)
        {
            try
            {
                DebugLogger.Info("ForgeService", $"解析Forge安装器: {installerPath}");

                using var zip = ZipFile.OpenRead(installerPath);
                var profileEntry = zip.GetEntry("install_profile.json");

                if (profileEntry == null)
                {
                    DebugLogger.Warn("ForgeService", "未找到install_profile.json");
                    return null;
                }

                using var stream = profileEntry.Open();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();

                var profile = JsonSerializer.Deserialize<ForgeInstallProfile>(json);
                DebugLogger.Info("ForgeService", "成功解析install_profile.json");

                return profile;
            }
            catch (Exception ex)
            {
                DebugLogger.Error("ForgeService", $"解析install_profile失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从Forge安装器中提取version.json
        /// </summary>
        public static async Task<string?> ExtractVersionJsonAsync(string installerPath, string versionId)
        {
            try
            {
                DebugLogger.Info("ForgeService", $"从安装器提取version.json: {versionId}");

                using var zip = ZipFile.OpenRead(installerPath);
                
                // 尝试多种可能的路径
                var possiblePaths = new[]
                {
                    $"version.json",
                    $"{versionId}.json",
                    $"versions/{versionId}/{versionId}.json"
                };

                ZipArchiveEntry? versionEntry = null;
                foreach (var path in possiblePaths)
                {
                    versionEntry = zip.GetEntry(path);
                    if (versionEntry != null)
                    {
                        DebugLogger.Info("ForgeService", $"找到version.json: {path}");
                        break;
                    }
                }

                if (versionEntry == null)
                {
                    DebugLogger.Warn("ForgeService", "未找到version.json");
                    return null;
                }

                using var stream = versionEntry.Open();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();

                DebugLogger.Info("ForgeService", "成功提取version.json");
                return json;
            }
            catch (Exception ex)
            {
                DebugLogger.Error("ForgeService", $"提取version.json失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 判断是否是旧版本Forge（1.12.2及之前）
        /// 旧版本使用 1.8.9-11.15.1.2318-1.8.9 格式
        /// 新版本使用 1.16.5-36.2.34 格式
        /// </summary>
        private static bool IsOldForgeVersion(string mcVersion)
        {
            try
            {
                if (string.IsNullOrEmpty(mcVersion)) return false;
                
                // 解析版本号
                var versionParts = mcVersion.Split('.');
                if (versionParts.Length < 2) return false;
                
                if (!int.TryParse(versionParts[0], out int major)) return false;
                if (!int.TryParse(versionParts[1], out int minor)) return false;
                
                // 1.12.2 及之前的版本
                if (major == 1 && minor <= 12)
                {
                    return true;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 解析Maven坐标为路径
        /// 例如: net.minecraftforge:forge:1.20.1-47.2.0 => net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0.jar
        /// </summary>
        public static string MavenToPath(string maven)
        {
            var parts = maven.Split(':');
            if (parts.Length < 3) return "";

            var group = parts[0].Replace('.', '/');
            var artifact = parts[1];
            var version = parts[2];
            var classifier = parts.Length > 3 ? $"-{parts[3]}" : "";

            return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.jar";
        }

        /// <summary>
        /// 判断Forge安装器是否是新版本（1.13+需要 --installClient 参数）
        /// </summary>
        private static bool IsForgeInstallerNewVersion(string mcVersion)
        {
            try
            {
                if (string.IsNullOrEmpty(mcVersion)) return false;

                var versionParts = mcVersion.Split('.');
                if (versionParts.Length < 2) return false;

                if (!int.TryParse(versionParts[0], out int major)) return false;
                if (!int.TryParse(versionParts[1], out int minor)) return false;

                if (major > 1) return true;
                if (major == 1 && minor >= 13) return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 判断是否是非常旧的 Forge 版本（1.9 之前）
        /// </summary>
        private static bool IsVeryOldForgeVersion(string mcVersion)
        {
            try
            {
                if (string.IsNullOrEmpty(mcVersion)) return false;

                var versionParts = mcVersion.Split('.');
                if (versionParts.Length < 2) return false;

                if (!int.TryParse(versionParts[0], out int major)) return false;
                if (!int.TryParse(versionParts[1], out int minor)) return false;

                if (major < 1) return true;
                if (major == 1 && minor < 9) return true;

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}

