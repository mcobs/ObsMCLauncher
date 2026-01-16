using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
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
        public static async Task InstallForgeAsync(
            string mcVersion,
            string forgeVersion,
            string gameDirectory,
            string customVersionName,
            Action<string, double>? progressCallback = null)
        {
            // 复用 VersionDetailPage 中成熟的 Forge 安装流程（无 UI 依赖版）
            var config = LauncherConfig.Load();
            var forgeFullVersion = $"{mcVersion}-{forgeVersion}";
            var installerPath = Path.Combine(Path.GetTempPath(), $"forge-installer-{forgeFullVersion}.jar");

            System.Threading.Timer? progressSimulator = null;

            // 将 Action<string,double> 适配成 DownloadProgress
            IProgress<DownloadProgress> progress = new Progress<DownloadProgress>(p =>
            {
                // p 里可能没有百分比字段，这里用 CurrentFileBytes/Total 兜底或直接透传状态
                // 由于整合包安装页只需要粗粒度进度，所以这里用 Status + 估算百分比
                double percent = 0;
                if (p.CurrentFileTotalBytes > 0)
                    percent = p.CurrentFileBytes * 100.0 / p.CurrentFileTotalBytes;

                progressCallback?.Invoke(p.Status ?? "正在安装Forge...", percent);
            });

            try
            {
                // 1. 下载 Forge 安装器（使用你现有 ForgeService.DownloadForgeInstallerWithDetailsAsync）
                progressCallback?.Invoke("正在下载Forge安装器...", 0);

                if (!await DownloadForgeInstallerWithDetailsAsync(
                        forgeFullVersion,
                        installerPath,
                        (currentBytes, speed, totalBytes) =>
                        {
                            double percentage = totalBytes > 0 ? (double)currentBytes / totalBytes * 100 : 0;
                            progressCallback?.Invoke("正在下载Forge安装器...", Math.Min(40, percentage * 0.4));
                        },
                        default))
                {
                    throw new Exception("Forge安装器下载失败");
                }

                // 2. 下载原版文件到标准位置（Forge安装器要求）
                string standardVanillaDir = Path.Combine(gameDirectory, "versions", mcVersion);
                await DownloadVanillaForForge(gameDirectory, mcVersion, standardVanillaDir, progress, default);

                // 2.5 （移除）不执行 install_profile 的 processors / libraries 下载，完全交给 installer 处理（复用 VersionDetailPage 流程）

                // 3. 运行官方安装器（完整复刻 VersionDetailPage 流程）
                progressCallback?.Invoke("执行Forge安装...", 50);
                progressSimulator = SimulateForgeInstallerProgress(progress);

                bool installSuccess = await RunForgeInstallerAsync(installerPath, gameDirectory, mcVersion, forgeVersion, config, default);

                progressSimulator.Dispose();
                progressSimulator = null;

                if (!installSuccess)
                    throw new Exception("Forge安装器执行失败，请查看日志");

                // 4. 重命名官方生成的版本到自定义名称
                progressCallback?.Invoke("配置版本信息...", 85);
                await RenameForgeVersionAsync(gameDirectory, mcVersion, forgeVersion, customVersionName, default);

                // 5. 清理原版文件夹（与 VersionDetailPage 相同的兜底策略）
                string vanillaDir = Path.Combine(gameDirectory, "versions", mcVersion);
                if (Directory.Exists(vanillaDir))
                {
                    try { Directory.Delete(vanillaDir, true); } catch { }
                }

                progressCallback?.Invoke("Forge安装完成", 100);
            }
            finally
            {
                try { progressSimulator?.Dispose(); } catch { }
                try { if (File.Exists(installerPath)) File.Delete(installerPath); } catch { }
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
                if (!string.IsNullOrWhiteSpace(outText)) Debug.WriteLine($"[Forge Processor] {outText}");
                if (!string.IsNullOrWhiteSpace(errText)) Debug.WriteLine($"[Forge Processor ERROR] {errText}");

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
                        Debug.WriteLine($"[Forge] 已为 processor 定位到 slim jar: {Path.GetFileName(best)}");
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[Forge] 查找 slim jar 失败: {ex.Message}"); }

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

            // 去重
            var list = mavenCoords.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();

            using var semaphore = new SemaphoreSlim(8);
            int done = 0;

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
                    Debug.WriteLine($"[Forge] processor 依赖下载失败: {maven} - {ex.Message}");
                }
                finally
                {
                    var c = Interlocked.Increment(ref done);
                    progressCallback?.Invoke($"正在准备 Forge 处理器依赖... ({c}/{list.Count})", 52);
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);
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

            using var semaphore = new SemaphoreSlim(8);
            var tasks = libraries.Select(async lib =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    string? downloadUrl = null;
                    string? savePath = null;

                    // 1) 优先使用 install_profile 给出的 artifact.path
                    if (lib.Downloads?.Artifact != null)
                    {
                        var artifact = lib.Downloads.Artifact;
                        if (!string.IsNullOrWhiteSpace(artifact.Path))
                        {
                            downloadUrl = downloadService.GetLibraryUrl(artifact.Path);
                            savePath = Path.Combine(librariesDir, artifact.Path.Replace("/", "\\"));
                        }
                        else if (!string.IsNullOrWhiteSpace(artifact.Url) && !string.IsNullOrWhiteSpace(artifact.Path))
                        {
                            downloadUrl = artifact.Url;
                            savePath = Path.Combine(librariesDir, artifact.Path.Replace("/", "\\"));
                        }
                    }

                    // 2) fallback：从 name 推导 maven 路径
                    if ((string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(savePath)) && !string.IsNullOrWhiteSpace(lib.Name))
                    {
                        var mavenPath = MavenToPath(lib.Name);
                        if (!string.IsNullOrWhiteSpace(mavenPath))
                        {
                            downloadUrl = downloadService.GetLibraryUrl(mavenPath);
                            savePath = Path.Combine(librariesDir, mavenPath.Replace("/", "\\"));
                        }
                    }

                    if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(savePath))
                    {
                        Debug.WriteLine($"[Forge] 跳过 install_profile 库（无法构建URL）: {lib.Name}");
                        return;
                    }

                    // 已存在则跳过
                    if (File.Exists(savePath))
                    {
                        return;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

                    using var resp = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    resp.EnsureSuccessStatusCode();
                    var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
                    await File.WriteAllBytesAsync(savePath, bytes, cancellationToken);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Forge] install_profile 库下载失败: {lib.Name} - {ex.Message}");
                }
                finally
                {
                    var done = Interlocked.Increment(ref completed);
                    progressCallback?.Invoke($"正在下载 Forge 依赖库... ({done}/{total})", 45 + (done / (double)Math.Max(1, total) * 10)); // 45-55
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);
        }

        private static System.Threading.Timer SimulateForgeInstallerProgress(IProgress<DownloadProgress>? progress)
        {
            double currentProgress = 50;
            var random = new Random();

            var timer = new System.Threading.Timer(_ =>
            {
                try
                {
                    if (currentProgress < 69)
                    {
                        currentProgress += random.NextDouble() * 0.5;

                        string statusText;
                        if (currentProgress < 55)
                            statusText = "正在下载依赖库...";
                        else if (currentProgress < 60)
                            statusText = "正在处理混淆映射...";
                        else if (currentProgress < 65)
                            statusText = "正在应用访问转换器...";
                        else
                            statusText = "正在生成Forge客户端...";

                        progress?.Report(new DownloadProgress
                        {
                            Status = statusText,
                            CurrentFile = statusText,
                            CurrentFileBytes = (long)currentProgress,
                            CurrentFileTotalBytes = 100,
                            CompletedFiles = 2,
                            TotalFiles = 3
                        });
                    }
                }
                catch { }
            }, null, 500, 500);

            return timer;
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
            CancellationToken cancellationToken = default)
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

            // 参数组合（按 VersionDetailPage 的逻辑）
            var argsList = new List<string>();

            if (isNewVersion)
            {
                argsList.Add($"--installClient \"{gameDirectory}\"");
            }
            else if (isVeryOldVersion)
            {
                argsList.Add($"--installClient \"{gameDirectory}\"");
                argsList.Add($"--install-client \"{gameDirectory}\"");
                argsList.Add($"-installClient \"{gameDirectory}\"");
            }
            else
            {
                argsList.Add($"--installClient \"{gameDirectory}\"");
                argsList.Add($"--install-client \"{gameDirectory}\"");
            }

            foreach (var args in argsList)
            {
                if (await TryRunForgeInstallerWithArgs(installerPath, gameDirectory, mcVersion, args, cancellationToken))
                    return true;
            }

            // 对于 1.12.2 及更早的版本，安装器参数可能无效，需手动安装（完整迁移 VersionDetailPage 逻辑）
            if (isVeryOldVersion || !isNewVersion)
            {
                Debug.WriteLine($"[Forge] 安装器参数不适用，尝试手动安装客户端... {mcVersion}");
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
            CancellationToken cancellationToken = default)
        {
            // 根据Minecraft版本选择Java，这对于旧版本Forge兼容性至关重要
            var config = LauncherConfig.Load();
            string javaPath;
            
            // 根据Minecraft版本选择Java
            if (!string.IsNullOrEmpty(mcVersion))
            {
                javaPath = config.GetActualJavaPath(mcVersion);
                Debug.WriteLine($"[Forge] 根据Minecraft版本 {mcVersion} 选择Java: {javaPath}");
            }
            else
            {
                javaPath = config.JavaPath ?? "java";
            }

            if (string.IsNullOrEmpty(javaPath) || !File.Exists(javaPath))
            {
                javaPath = "java"; // 回退到系统 PATH
                Debug.WriteLine("[Forge] 未配置有效Java路径，将使用系统 PATH 中的 'java'。这可能导致安装失败！");
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
                    Debug.WriteLine($"[Forge Installer] {e.Data}");
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    stderr.AppendLine(e.Data);
                    Debug.WriteLine($"[Forge Installer ERROR] {e.Data}");
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // 给 Forge 安装器足够时间
            var waitTask = process.WaitForExitAsync(cancellationToken);
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
            var completed = await Task.WhenAny(waitTask, timeoutTask);

            if (completed == timeoutTask)
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                Debug.WriteLine("[Forge] 安装器超时（10分钟），已终止进程");
                return false;
            }

            Debug.WriteLine($"[Forge] 安装器退出码: {process.ExitCode}");

            if (process.ExitCode != 0)
            {
                // 汇总输出，便于定位原因
                var outText = stdout.ToString().Trim();
                var errText = stderr.ToString().Trim();
                if (!string.IsNullOrEmpty(outText))
                    Debug.WriteLine($"[Forge] 安装器输出汇总:\n{outText}");
                if (!string.IsNullOrEmpty(errText))
                    Debug.WriteLine($"[Forge] 安装器错误汇总:\n{errText}");
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
                var forgeJsonContent = await File.ReadAllTextAsync(forgeJsonPath, cancellationToken);
                var forgeJson = JsonNode.Parse(forgeJsonContent)!.AsObject();

                bool isOldVersion = IsForgeInstallerNewVersion(vanillaVersion) == false;

                forgeJson["id"] = customVersionName;

                string vanillaJsonPath = Path.Combine(gameDirectory, "versions", vanillaVersion, $"{vanillaVersion}.json");
                if (!File.Exists(vanillaJsonPath))
                {
                    Debug.WriteLine($"[Forge] 原版JSON不存在，保留inheritsFrom: {vanillaVersion}");
                    forgeJson["inheritsFrom"] = vanillaVersion;
                    await File.WriteAllTextAsync(forgeJsonPath, forgeJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
                    return;
                }

                var vanillaJsonContent = await File.ReadAllTextAsync(vanillaJsonPath, cancellationToken);
                var vanillaJson = JsonNode.Parse(vanillaJsonContent)!.AsObject();

                var forgeLibraries = forgeJson["libraries"]?.AsArray() ?? new JsonArray();
                var vanillaLibraries = vanillaJson["libraries"]?.AsArray() ?? new JsonArray();

                // 收集已存在的库（忽略版本号，保留 classifier 区分 natives）
                var existingLibs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var lib in forgeLibraries)
                {
                    if (lib? ["name"] != null)
                    {
                        existingLibs.Add(GetLibraryKeyFromName(lib["name"]!.ToString()));
                    }
                }

                int addedCount = 0;
                foreach (var vlib in vanillaLibraries)
                {
                    bool isNativesLib = vlib? ["natives"] != null;
                    if (isNativesLib)
                    {
                        forgeLibraries.Add(vlib?.DeepClone());
                        addedCount++;
                        continue;
                    }

                    var name = vlib? ["name"]?.ToString();
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

                forgeJson["libraries"] = forgeLibraries;

                if (!forgeJson.ContainsKey("assetIndex") && vanillaJson.ContainsKey("assetIndex"))
                    forgeJson["assetIndex"] = vanillaJson["assetIndex"]!.DeepClone();
                if (!forgeJson.ContainsKey("assets") && vanillaJson.ContainsKey("assets"))
                    forgeJson["assets"] = vanillaJson["assets"]!.DeepClone();
                if (!forgeJson.ContainsKey("arguments") && vanillaJson.ContainsKey("arguments"))
                    forgeJson["arguments"] = vanillaJson["arguments"]!.DeepClone();

                // 新旧版本都移除 inheritsFrom，避免依赖父版本目录
                forgeJson.Remove("inheritsFrom");

                await File.WriteAllTextAsync(forgeJsonPath, forgeJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

                Debug.WriteLine($"[Forge] MergeVanillaIntoForgeJson 完成，新增库 {addedCount} 个");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Forge] MergeVanillaIntoForgeJson 失败: {ex.Message}");
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
                Debug.WriteLine($"[Forge] 开始手动安装旧版Forge: {mcVersion} -> {targetVersionName}");

                using var zip = ZipFile.OpenRead(installerPath);
                var profileEntry = zip.GetEntry("install_profile.json");
                if (profileEntry == null)
                {
                    Debug.WriteLine("[Forge] 安装器中找不到 install_profile.json");
                    return false;
                }

                string profileJson;
                using (var stream = profileEntry.Open())
                using (var reader = new StreamReader(stream))
                    profileJson = await reader.ReadToEndAsync();

                var profile = JsonDocument.Parse(profileJson);
                var versionInfo = profile.RootElement.GetProperty("versionInfo");

                Directory.CreateDirectory(targetVersionDir);

                var versionJsonPath = Path.Combine(targetVersionDir, $"{targetVersionName}.json");
                var versionJson = JsonNode.Parse(versionInfo.GetRawText())!.AsObject();
                versionJson["id"] = targetVersionName;

                await File.WriteAllTextAsync(versionJsonPath, versionJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

                // 尝试合并原版信息
                var vanillaJsonPath = Path.Combine(gameDirectory, "versions", mcVersion, $"{mcVersion}.json");
                if (File.Exists(vanillaJsonPath))
                {
                    await MergeVanillaIntoForgeJson(versionJsonPath, targetVersionName, gameDirectory, mcVersion, cancellationToken);
                }

                // 旧版需要复制原版 jar 到版本目录
                var vanillaJar = Path.Combine(gameDirectory, "versions", mcVersion, $"{mcVersion}.jar");
                var jarInTarget = Path.Combine(targetVersionDir, $"{mcVersion}.jar");
                if (File.Exists(vanillaJar) && !File.Exists(jarInTarget))
                    File.Copy(vanillaJar, jarInTarget, true);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Forge] 手动安装旧版Forge失败: {ex.Message}");
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
            // 极简重命名：把官方目录移动到自定义名称
            // 注：VersionDetailPage 里有更完整的兼容与 JSON 修正，后续可继续迁移。
            string officialForgeId = $"{gameVersion}-forge-{forgeVersion}";
            string officialDir = Path.Combine(gameDirectory, "versions", officialForgeId);
            string customDir = Path.Combine(gameDirectory, "versions", customVersionName);

            if (!Directory.Exists(officialDir))
                throw new Exception($"找不到Forge安装目录: {officialForgeId}");

            if (Directory.Exists(customDir))
                Directory.Delete(customDir, true);

            Directory.Move(officialDir, customDir);

            // 重命名 json/jar 文件（如果存在）
            var oldJson = Path.Combine(customDir, $"{officialForgeId}.json");
            var newJson = Path.Combine(customDir, $"{customVersionName}.json");
            if (File.Exists(oldJson)) File.Move(oldJson, newJson, true);

            var oldJar = Path.Combine(customDir, $"{officialForgeId}.jar");
            var newJar = Path.Combine(customDir, $"{customVersionName}.jar");
            if (File.Exists(oldJar)) File.Move(oldJar, newJar, true);

            // 修正 json 内的 id 字段（尽量）
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
                catch { }
            }

            // 兜底：确保自定义版本主 JAR 存在（很多新版 Forge 并不会生成同名 JAR，会导致完整性检查失败）
            var customJar = Path.Combine(customDir, $"{customVersionName}.jar");
            if (!File.Exists(customJar))
            {
                try
                {
                    // 优先尝试 Forge 目录内可能存在的父版本 JAR（例如 {mc}.jar）
                    var jarInForgeDir = Path.Combine(customDir, $"{gameVersion}.jar");
                    var jarInVanillaDir = Path.Combine(gameDirectory, "versions", gameVersion, $"{gameVersion}.jar");

                    if (File.Exists(jarInForgeDir))
                    {
                        File.Copy(jarInForgeDir, customJar, true);
                        Debug.WriteLine($"[Forge] 已从Forge目录内复制主JAR: {gameVersion}.jar -> {customVersionName}.jar");
                    }
                    else if (File.Exists(jarInVanillaDir))
                    {
                        File.Copy(jarInVanillaDir, customJar, true);
                        Debug.WriteLine($"[Forge] 已从原版目录复制主JAR: {gameVersion}.jar -> {customVersionName}.jar");
                    }
                    else
                    {
                        Debug.WriteLine($"[Forge] 未找到可复制的原版JAR（{gameVersion}.jar），新版Forge可能不需要独立JAR");
                    }
                }
                catch (Exception jarEx)
                {
                    Debug.WriteLine($"[Forge] 复制主JAR失败: {jarEx.Message}");
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
                Debug.WriteLine($"[ForgeService] 获取Forge支持的MC版本列表... (源: {config.DownloadSource})");
                
                if (config.DownloadSource == DownloadSource.BMCLAPI)
                {
                    // 使用镜像源
                    var response = await _httpClient.GetAsync(BMCL_FORGE_SUPPORT);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    var versions = JsonSerializer.Deserialize<List<string>>(json);

                    Debug.WriteLine($"[ForgeService] 从镜像源获取到 {versions?.Count ?? 0} 个支持的MC版本");
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
                        
                        Debug.WriteLine($"[ForgeService] 从官方源获取到 {versions.Count} 个支持的MC版本");
                        return versions;
                    }
                    
                    return new List<string>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeService] 获取Forge支持版本失败: {ex.Message}");
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
                Debug.WriteLine($"[ForgeService] 获取 MC {mcVersion} 的Forge版本列表... (源: {config.DownloadSource})");
                
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
                        Debug.WriteLine($"[ForgeService] 从镜像源获取到 {forgeList.Count} 个Forge版本");
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
                    
                    Debug.WriteLine($"[ForgeService] 从官方源获取到 {forgeList.Count} 个Forge版本");
                    return forgeList;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeService] 获取Forge版本列表失败: {ex.Message}");
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
                Debug.WriteLine($"[ForgeService] 解析Maven元数据失败: {ex.Message}");
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
                Debug.WriteLine($"[ForgeService] 开始下载Forge安装器: {forgeVersion} (源: {config.DownloadSource})");
                
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
                        Debug.WriteLine($"[ForgeService] 尝试下载URL: {url}");
                        response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        response.EnsureSuccessStatusCode();
                        Debug.WriteLine($"[ForgeService] 成功找到Forge安装器: {url}");
                        break; // 成功，跳出循环
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ForgeService] URL失败: {url} - {ex.Message}");
                        lastException = ex;
                        response?.Dispose();
                        response = null;
                    }
                }
                
                // 如果所有URL都失败了
                if (response == null || !response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[ForgeService] 所有URL都无法下载Forge安装器");
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

                Debug.WriteLine($"[ForgeService] Forge安装器下载完成: {savePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeService] 下载Forge安装器失败: {ex.Message}");
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
                Debug.WriteLine($"[ForgeService] 开始下载Forge安装器: {forgeVersion} (源: {config.DownloadSource})");
                
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
                        Debug.WriteLine($"[ForgeService] 尝试下载URL: {url}");
                        response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        response.EnsureSuccessStatusCode();
                        successUrl = url;
                        Debug.WriteLine($"[ForgeService] 成功找到Forge安装器: {url}");
                        break; // 成功，跳出循环
                    }
                    catch (HttpRequestException ex)
                    {
                        Debug.WriteLine($"[ForgeService] URL失败 ({ex.StatusCode}): {url}");
                        lastException = ex;
                        response?.Dispose();
                        response = null;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ForgeService] URL错误: {url} - {ex.Message}");
                        lastException = ex;
                        response?.Dispose();
                        response = null;
                    }
                }
                
                // 如果所有URL都失败了
                if (response == null || !response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[ForgeService] 所有URL都无法下载Forge安装器");
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

                Debug.WriteLine($"[ForgeService] Forge安装器下载完成: {savePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeService] 下载Forge安装器失败: {ex.Message}");
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
                Debug.WriteLine($"[ForgeService] 解析Forge安装器: {installerPath}");

                using var zip = ZipFile.OpenRead(installerPath);
                var profileEntry = zip.GetEntry("install_profile.json");

                if (profileEntry == null)
                {
                    Debug.WriteLine($"[ForgeService] 未找到install_profile.json");
                    return null;
                }

                using var stream = profileEntry.Open();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();

                var profile = JsonSerializer.Deserialize<ForgeInstallProfile>(json);
                Debug.WriteLine($"[ForgeService] 成功解析install_profile.json");

                return profile;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeService] 解析install_profile失败: {ex.Message}");
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
                Debug.WriteLine($"[ForgeService] 从安装器提取version.json: {versionId}");

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
                        Debug.WriteLine($"[ForgeService] 找到version.json: {path}");
                        break;
                    }
                }

                if (versionEntry == null)
                {
                    Debug.WriteLine($"[ForgeService] 未找到version.json");
                    return null;
                }

                using var stream = versionEntry.Open();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();

                Debug.WriteLine($"[ForgeService] 成功提取version.json");
                return json;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeService] 提取version.json失败: {ex.Message}");
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

