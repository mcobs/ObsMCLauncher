using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// OptiFine 安装服务
    /// 负责获取 OptiFine 版本列表、下载和安装 OptiFine
    /// </summary>
    public class OptiFineService
    {
        private readonly HttpClient _httpClient;
        private readonly DownloadSourceManager _downloadSourceManager;

        public OptiFineService(DownloadSourceManager downloadSourceManager)
        {
            _downloadSourceManager = downloadSourceManager;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
        }

        /// <summary>
        /// 获取指定 Minecraft 版本的所有可用 OptiFine 版本
        /// </summary>
        /// <param name="mcVersion">Minecraft 版本（例如：1.20.1）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>OptiFine 版本列表</returns>
        public async Task<List<OptifineVersionModel>> GetOptifineVersionsAsync(
            string mcVersion, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                // 使用 BMCLAPI 获取 OptiFine 版本列表
                var bmclApiUrl = _downloadSourceManager.CurrentService.GetBMCLApiUrl();
                var url = $"{bmclApiUrl}/optifine/{mcVersion}";
                
                Debug.WriteLine($"[OptiFineService] 获取 OptiFine 版本列表: {url}");

                var response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var versions = JsonSerializer.Deserialize<List<OptifineVersionModel>>(json);

                if (versions == null || versions.Count == 0)
                {
                    Debug.WriteLine($"[OptiFineService] 未找到 Minecraft {mcVersion} 的 OptiFine 版本");
                    return new List<OptifineVersionModel>();
                }

                Debug.WriteLine($"[OptiFineService] 找到 {versions.Count} 个 OptiFine 版本");
                return versions;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OptiFineService] 获取 OptiFine 版本列表失败: {ex.Message}");
                throw new Exception($"获取 OptiFine 版本列表失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 下载 OptiFine 安装包
        /// </summary>
        /// <param name="version">OptiFine 版本信息</param>
        /// <param name="savePath">保存路径</param>
        /// <param name="progressCallback">进度回调 (status, currentProgress, totalProgress, bytes, totalBytes)</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task DownloadOptifineInstallerAsync(
            OptifineVersionModel version,
            string savePath,
            Action<string, double, double, long, long>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // 确保保存目录存在
                var saveDir = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(saveDir) && !Directory.Exists(saveDir))
                {
                    Directory.CreateDirectory(saveDir);
                }

                // 构建下载 URL
                // BMCLAPI URL 格式: https://bmclapi2.bangbang93.com/optifine/{mcversion}/{type}/{patch}
                var bmclApiUrl = _downloadSourceManager.CurrentService.GetBMCLApiUrl();
                var downloadUrl = $"{bmclApiUrl}/optifine/{version.McVersion}/{version.Type}/{version.Patch}";

                Debug.WriteLine($"[OptiFineService] 开始下载 OptiFine: {downloadUrl}");
                progressCallback?.Invoke($"正在下载 {version.Filename}...", 0, 100, 0, 0);

                using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var receivedBytes = 0L;
                var lastReportTime = DateTime.Now;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    receivedBytes += bytesRead;

                    // 限制进度报告频率（每 100ms 报告一次）
                    if ((DateTime.Now - lastReportTime).TotalMilliseconds >= 100)
                    {
                        var progress = totalBytes > 0 ? (receivedBytes * 100.0 / totalBytes) : 0;
                        var statusText = $"正在下载 {version.Filename} ({receivedBytes / 1024.0 / 1024.0:F2} MB / {totalBytes / 1024.0 / 1024.0:F2} MB)";
                        progressCallback?.Invoke(statusText, progress, 100, receivedBytes, totalBytes);
                        lastReportTime = DateTime.Now;
                    }
                }

                progressCallback?.Invoke($"{version.Filename} 下载完成", 100, 100, totalBytes, totalBytes);
                Debug.WriteLine($"[OptiFineService] OptiFine 下载完成: {savePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OptiFineService] OptiFine 下载失败: {ex.Message}");
                throw new Exception($"OptiFine 下载失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 安装 OptiFine
        /// </summary>
        /// <param name="version">OptiFine 版本信息</param>
        /// <param name="installerPath">OptiFine 安装包路径</param>
        /// <param name="gameDirectory">游戏根目录</param>
        /// <param name="baseVersionId">基础 Minecraft 版本ID</param>
        /// <param name="javaPath">Java 运行时路径</param>
        /// <param name="customVersionName">自定义版本名称（可选）</param>
        /// <param name="baseVersionDir">基础版本所在目录（可选，用于指定临时下载的基础版本路径）</param>
        /// <param name="progressCallback">进度回调 (status, currentProgress, totalProgress, bytes, totalBytes)</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>安装是否成功</returns>
        public async Task<bool> InstallOptifineAsync(
            OptifineVersionModel version,
            string installerPath,
            string gameDirectory,
            string baseVersionId,
            string javaPath,
            string? customVersionName = null,
            string? baseVersionDir = null,
            Action<string, double, double, long, long>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Debug.WriteLine($"========== 开始安装 OptiFine==========");
                Debug.WriteLine($"[OptiFineService] OptiFine 版本: {version.DisplayName}");
                Debug.WriteLine($"[OptiFineService] 安装包路径: {installerPath}");
                Debug.WriteLine($"[OptiFineService] 游戏目录: {gameDirectory}");
                Debug.WriteLine($"[OptiFineService] 基础版本: {baseVersionId}");

                // 验证文件存在
                if (!File.Exists(installerPath))
                {
                    throw new FileNotFoundException($"OptiFine 安装包不存在: {installerPath}");
                }

                Action<string, double> UpdateProgress = (status, progressPercent) =>
                {
                    progressCallback?.Invoke(status, progressPercent, 100, 0, 0);
                };

                // ============================================================
                // 阶段1: 验证基础版本 (0-10%)
                // ============================================================
                UpdateProgress("正在验证基础 Minecraft 版本...", 0);

                var versionId = customVersionName ?? $"{baseVersionId}-OptiFine_{version.FullVersion}";
                var versionDir = Path.Combine(gameDirectory, "versions", versionId);
                
                // 如果指定了自定义基础版本路径，使用它；否则使用默认路径
                var actualBaseVersionDir = baseVersionDir ?? Path.Combine(gameDirectory, "versions", baseVersionId);
                var baseVersionJar = Path.Combine(actualBaseVersionDir, $"{baseVersionId}.jar");
                var baseVersionJson = Path.Combine(actualBaseVersionDir, $"{baseVersionId}.json");
                
                Debug.WriteLine($"[OptiFineService] 基础版本验证信息:");
                Debug.WriteLine($"[OptiFineService] - 传入的 baseVersionDir: {baseVersionDir ?? "null (使用默认)"}");
                Debug.WriteLine($"[OptiFineService] - 实际使用的 actualBaseVersionDir: {actualBaseVersionDir}");
                Debug.WriteLine($"[OptiFineService] - 期望的 JAR 路径: {baseVersionJar}");
                Debug.WriteLine($"[OptiFineService] - 期望的 JSON 路径: {baseVersionJson}");

                if (!File.Exists(baseVersionJar))
                {
                    throw new Exception($"基础 Minecraft 版本不存在：{baseVersionId}\n请先下载并安装 Minecraft {baseVersionId}");
                }

                if (!File.Exists(baseVersionJson))
                {
                    throw new Exception($"基础版本配置文件不存在：{baseVersionJson}");
                }

                Debug.WriteLine($"[OptiFineService] ✅ 基础版本验证通过");

                // 创建版本目录
                if (!Directory.Exists(versionDir))
                {
                    Directory.CreateDirectory(versionDir);
                }

                UpdateProgress("正在解析 OptiFine 安装包...", 10);

                // ============================================================
                // 阶段2: 解析和提取 OptiFine 文件 (10-40%)
                // ============================================================
                var librariesDir = Path.Combine(gameDirectory, "libraries");
                var optiFineLibDir = Path.Combine(librariesDir, "optifine", "OptiFine", $"{version.McVersion}_{version.FullVersion}");
                
                if (!Directory.Exists(optiFineLibDir))
                {
                    Directory.CreateDirectory(optiFineLibDir);
                }

                var optiFineLibPath = Path.Combine(optiFineLibDir, $"OptiFine-{version.McVersion}_{version.FullVersion}.jar");
                
                UpdateProgress("正在处理 OptiFine 库文件...", 20);

                // 检查是否需要 Patch
                bool hasPatcher = false;
                string? launchWrapperVersion = null;
                
                using (var zipArchive = System.IO.Compression.ZipFile.OpenRead(installerPath))
                {
                    // 检查是否有 Patcher
                    var patcherEntry = zipArchive.Entries.FirstOrDefault(e => 
                        e.FullName == "optifine/Patcher.class" ||
                        e.FullName == "Patcher.class");
                    
                    hasPatcher = patcherEntry != null;
                    Debug.WriteLine($"[OptiFineService] Patcher 存在: {hasPatcher}");

                    if (hasPatcher)
                    {
                        // 如果有 Patcher，需要运行它
                        UpdateProgress("正在运行 OptiFine Patcher...", 25);
                        
                        var patchCommand = $"-cp \"{installerPath}\" optifine.Patcher \"{baseVersionJar}\" \"{installerPath}\" \"{optiFineLibPath}\"";
                        Debug.WriteLine($"[OptiFineService] Patch 命令: java {patchCommand}");

                        var patchProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = javaPath,
                                Arguments = patchCommand,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            }
                        };

                        patchProcess.Start();
                        var patchOutput = await patchProcess.StandardOutput.ReadToEndAsync();
                        var patchError = await patchProcess.StandardError.ReadToEndAsync();
                        await patchProcess.WaitForExitAsync(cancellationToken);

                        if (patchProcess.ExitCode != 0)
                        {
                            Debug.WriteLine($"[OptiFineService] Patcher 输出: {patchOutput}");
                            Debug.WriteLine($"[OptiFineService] Patcher 错误: {patchError}");
                            throw new Exception($"OptiFine Patcher 失败 (退出代码: {patchProcess.ExitCode})");
                        }

                        Debug.WriteLine($"[OptiFineService] ✅ Patch 完成");
                    }
                    else
                    {
                        // 没有 Patcher，直接复制
                        UpdateProgress("正在复制 OptiFine 文件...", 25);
                        File.Copy(installerPath, optiFineLibPath, true);
                        Debug.WriteLine($"[OptiFineService] ✅ OptiFine 文件已复制");
                    }

                    // 删除 META-INF/mods.toml（如果存在）
                    try
                    {
                        using (var optiFineZip = System.IO.Compression.ZipFile.Open(optiFineLibPath, System.IO.Compression.ZipArchiveMode.Update))
                        {
                            var modsToml = optiFineZip.GetEntry("META-INF/mods.toml");
                            modsToml?.Delete();
                        }
                    }
                    catch { }

                    UpdateProgress("正在提取 LaunchWrapper...", 30);

                    // 检查 launchwrapper
                    var launchWrapperEntry = zipArchive.Entries.FirstOrDefault(e => e.Name == "launchwrapper-2.0.jar");
                    var launchWrapperTxt = zipArchive.GetEntry("launchwrapper-of.txt");
                    
                    if (launchWrapperEntry != null)
                    {
                        // 提取 launchwrapper-2.0.jar
                        launchWrapperVersion = "2.0";
                        var lwLibDir = Path.Combine(librariesDir, "optifine", "launchwrapper", launchWrapperVersion);
                        if (!Directory.Exists(lwLibDir))
                        {
                            Directory.CreateDirectory(lwLibDir);
                        }

                        var lwLibPath = Path.Combine(lwLibDir, $"launchwrapper-{launchWrapperVersion}.jar");
                        launchWrapperEntry.ExtractToFile(lwLibPath, true);
                        Debug.WriteLine($"[OptiFineService] ✅ 已提取 launchwrapper-{launchWrapperVersion}.jar");
                    }
                    else if (launchWrapperTxt != null)
                    {
                        // 读取 launchwrapper 版本
                        using (var reader = new StreamReader(launchWrapperTxt.Open()))
                        {
                            launchWrapperVersion = (await reader.ReadToEndAsync()).Trim();
                        }

                        var lwJarName = $"launchwrapper-of-{launchWrapperVersion}.jar";
                        var lwJarEntry = zipArchive.GetEntry(lwJarName);

                        if (lwJarEntry != null)
                        {
                            var lwLibDir = Path.Combine(librariesDir, "optifine", "launchwrapper-of", launchWrapperVersion);
                            if (!Directory.Exists(lwLibDir))
                            {
                                Directory.CreateDirectory(lwLibDir);
                            }

                            var lwLibPath = Path.Combine(lwLibDir, lwJarName);
                            lwJarEntry.ExtractToFile(lwLibPath, true);
                            Debug.WriteLine($"[OptiFineService] ✅ 已提取 {lwJarName}");
                        }
                    }
                }

                UpdateProgress("正在生成版本配置文件...", 40);

                // ============================================================
                // 阶段3: 创建 version.json (40-100%)
                // ============================================================
                
                await CreateOptiFineVersionJson(
                    versionDir,
                    versionId,
                    baseVersionId,
                    version,
                    launchWrapperVersion,
                    cancellationToken);

                UpdateProgress("OptiFine 安装完成", 100);
                Debug.WriteLine($"[OptiFineService] ✅ OptiFine 安装成功: {versionId}");
                Debug.WriteLine($"========== OptiFine 安装完成 ==========");

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OptiFineService] OptiFine 安装失败: {ex.Message}");
                Debug.WriteLine($"[OptiFineService] 堆栈跟踪: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// 创建 OptiFine 的 version.json 文件
        /// </summary>
        private async Task CreateOptiFineVersionJson(
            string versionDir,
            string versionId,
            string inheritsFrom,
            OptifineVersionModel optifineVersion,
            string? launchWrapperVersion,
            CancellationToken cancellationToken)
        {
            var versionJson = new
            {
                id = versionId,
                inheritsFrom = inheritsFrom,
                time = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                releaseTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                type = "release",
                mainClass = "net.minecraft.launchwrapper.Launch",
                arguments = new
                {
                    game = new object[]
                    {
                        "--tweakClass",
                        "optifine.OptiFineTweaker"
                    }
                },
                libraries = new List<object>()
            };

            // 添加 OptiFine 库
            ((List<object>)versionJson.libraries).Add(new
            {
                name = $"optifine:OptiFine:{optifineVersion.McVersion}_{optifineVersion.FullVersion}"
            });

            // 添加 LaunchWrapper
            if (!string.IsNullOrEmpty(launchWrapperVersion))
            {
                if (launchWrapperVersion == "2.0")
                {
                    ((List<object>)versionJson.libraries).Add(new
                    {
                        name = $"optifine:launchwrapper:{launchWrapperVersion}"
                    });
                }
                else
                {
                    ((List<object>)versionJson.libraries).Add(new
                    {
                        name = $"optifine:launchwrapper-of:{launchWrapperVersion}"
                    });
                }
            }
            else
            {
                // 使用标准 Minecraft LaunchWrapper
                ((List<object>)versionJson.libraries).Add(new
                {
                    name = "net.minecraft:launchwrapper:1.12"
                });
            }

            // 保存 version.json
            var versionJsonPath = Path.Combine(versionDir, $"{versionId}.json");
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var jsonString = JsonSerializer.Serialize(versionJson, jsonOptions);
            await File.WriteAllTextAsync(versionJsonPath, jsonString, cancellationToken);

            Debug.WriteLine($"[OptiFineService] ✅ 已创建 version.json: {versionJsonPath}");
        }

        /// <summary>
        /// 获取所有支持 OptiFine 的 Minecraft 版本列表
        /// </summary>
        public async Task<List<string>> GetSupportedMcVersionsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // 使用 BMCLAPI 获取所有 OptiFine 版本列表
                var bmclApiUrl = _downloadSourceManager.CurrentService.GetBMCLApiUrl();
                var url = $"{bmclApiUrl}/optifine/versionList";

                Debug.WriteLine($"[OptiFineService] 获取 OptiFine 支持的 MC 版本列表: {url}");

                var response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var versions = JsonSerializer.Deserialize<List<string>>(json);

                if (versions == null || versions.Count == 0)
                {
                    Debug.WriteLine($"[OptiFineService] 未找到支持的 Minecraft 版本");
                    return new List<string>();
                }

                Debug.WriteLine($"[OptiFineService] 找到 {versions.Count} 个支持的 Minecraft 版本");
                return versions;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OptiFineService] 获取支持的 MC 版本列表失败: {ex.Message}");
                // 返回一些常见版本作为备用
                return new List<string> 
                { 
                    "1.20.1", "1.19.4", "1.19.2", "1.18.2", "1.17.1", 
                    "1.16.5", "1.12.2", "1.8.9", "1.7.10" 
                };
            }
        }

        /// <summary>
        /// 将 OptiFine 下载为 mod 文件到指定的 mods 文件夹
        /// 用于与 Forge/NeoForge 等模组加载器一起使用
        /// </summary>
        /// <param name="version">OptiFine 版本信息</param>
        /// <param name="modsDirectory">mods 文件夹路径</param>
        /// <param name="progressCallback">进度回调 (status, currentProgress, totalProgress, bytes, totalBytes)</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否下载成功</returns>
        public async Task<bool> DownloadOptiFineAsModAsync(
            OptifineVersionModel version,
            string modsDirectory,
            Action<string, double, double, long, long>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Debug.WriteLine($"[OptiFineService] 开始将 OptiFine 下载为 mod: {version.Filename}");
                
                // 确保 mods 目录存在
                if (!Directory.Exists(modsDirectory))
                {
                    Directory.CreateDirectory(modsDirectory);
                    Debug.WriteLine($"[OptiFineService] 创建 mods 目录: {modsDirectory}");
                }

                var modFilePath = Path.Combine(modsDirectory, version.Filename);

                // 如果文件已存在，检查是否完整
                if (File.Exists(modFilePath))
                {
                    var fileInfo = new FileInfo(modFilePath);
                    if (fileInfo.Length > 1024 * 100) // 大于 100KB 认为是完整文件
                    {
                        Debug.WriteLine($"[OptiFineService] OptiFine mod 已存在且完整: {modFilePath}");
                        progressCallback?.Invoke("OptiFine 已存在", 100, 100, fileInfo.Length, fileInfo.Length);
                        return true;
                    }
                    else
                    {
                        // 文件不完整，删除后重新下载
                        Debug.WriteLine($"[OptiFineService] OptiFine mod 文件不完整，重新下载");
                        File.Delete(modFilePath);
                    }
                }

                // 下载 OptiFine 到 mods 文件夹
                await DownloadOptifineInstallerAsync(
                    version,
                    modFilePath,
                    progressCallback,
                    cancellationToken
                );

                Debug.WriteLine($"[OptiFineService] OptiFine mod 下载完成: {modFilePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OptiFineService] 下载 OptiFine mod 失败: {ex.Message}");
                throw new Exception($"下载 OptiFine mod 失败: {ex.Message}", ex);
            }
        }
    }
}

