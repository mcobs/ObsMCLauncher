using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Accounts;

namespace ObsMCLauncher.Core.Services;

public class GameLauncher
{
    public static string LastError { get; private set; } = string.Empty;

    public static List<string> MissingLibraries { get; private set; } = new();

    public static List<string> MissingOptionalLibraries { get; private set; } = new();

    public static async Task<bool> CheckGameIntegrityAsync(
        string versionId,
        LauncherConfig config,
        Action<string>? onProgressUpdate = null,
        CancellationToken cancellationToken = default)
    {
        LastError = string.Empty;
        MissingLibraries.Clear();
        MissingOptionalLibraries.Clear();

        try
        {
            onProgressUpdate?.Invoke("正在读取版本信息...");
            cancellationToken.ThrowIfCancellationRequested();

            var versionJsonPath = Path.Combine(config.GameDirectory, "versions", versionId, $"{versionId}.json");
            if (!File.Exists(versionJsonPath))
            {
                LastError = $"版本配置文件不存在: {versionJsonPath}";
                throw new FileNotFoundException(LastError);
            }

            var versionJson = await File.ReadAllTextAsync(versionJsonPath, cancellationToken).ConfigureAwait(false);
            var versionInfo = JsonSerializer.Deserialize<VersionInfo>(versionJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (versionInfo == null)
            {
                LastError = "无法解析版本配置文件";
                throw new Exception(LastError);
            }

            string actualMcVersion = versionId;
            if (!string.IsNullOrEmpty(versionInfo.InheritsFrom))
            {
                actualMcVersion = versionInfo.InheritsFrom;
                versionInfo = MergeInheritedVersion(config.GameDirectory, versionId, versionInfo);
            }

            onProgressUpdate?.Invoke("正在验证Java环境...");
            cancellationToken.ThrowIfCancellationRequested();

            var actualJavaPath = config.GetActualJavaPath(actualMcVersion);
            if (!File.Exists(actualJavaPath))
            {
                LastError = $"Java路径不存在: {actualJavaPath}";
                return false;
            }

            onProgressUpdate?.Invoke("正在检查游戏主文件...");
            cancellationToken.ThrowIfCancellationRequested();

            bool isModLoader = versionInfo.MainClass?.Contains("forge", StringComparison.OrdinalIgnoreCase) == true ||
                               versionInfo.MainClass?.Contains("fabric", StringComparison.OrdinalIgnoreCase) == true ||
                               versionInfo.MainClass?.Contains("quilt", StringComparison.OrdinalIgnoreCase) == true;

            var clientJarPath = Path.Combine(config.GameDirectory, "versions", versionId, $"{versionId}.jar");
            if (!isModLoader && !File.Exists(clientJarPath))
            {
                LastError = $"游戏主文件不存在: {clientJarPath}\n请先下载游戏版本";
                throw new FileNotFoundException(LastError);
            }

            onProgressUpdate?.Invoke("正在检查游戏依赖库...");
            cancellationToken.ThrowIfCancellationRequested();

            var (missingRequired, missingOptional) = GetMissingLibraries(config.GameDirectory, versionId, versionInfo);
            MissingLibraries = missingRequired;
            MissingOptionalLibraries = missingOptional;

            if (missingRequired.Count > 0)
            {
                LastError = $"检测到 {missingRequired.Count} 个缺失或不完整的必需库文件";
                return true;
            }

            onProgressUpdate?.Invoke("游戏完整性检查完成");
            return false;
        }
        catch (OperationCanceledException)
        {
            LastError = "检查已取消";
            return true;
        }
        catch (Exception ex)
        {
            if (string.IsNullOrEmpty(LastError))
            {
                LastError = ex.Message;
            }
            return true;
        }
    }

    public static async Task<bool> LaunchGameAsync(
        string versionId,
        GameAccount account,
        LauncherConfig config,
        Action<string>? onProgressUpdate = null,
        Action<string>? onGameOutput = null,
        Action<int>? onGameExit = null,
        CancellationToken cancellationToken = default)
    {
        LastError = string.Empty;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (account.Type == AccountType.Yggdrasil)
            {
                onProgressUpdate?.Invoke("正在检查外置登录文件...");
                if (!AuthlibInjectorService.IsAuthlibInjectorExists())
                {
                    LastError = "外置登录需要 authlib-injector.jar 文件\n请在账号管理中重新登录以自动下载";
                    throw new Exception(LastError);
                }

                onProgressUpdate?.Invoke("正在刷新外置登录令牌...");
                _ = await AccountService.Instance.RefreshYggdrasilAccountAsync(account.Id, onProgressUpdate, cancellationToken).ConfigureAwait(false);
            }
            else if (account.Type == AccountType.Microsoft && account.IsTokenExpired())
            {
                onProgressUpdate?.Invoke("正在刷新微软账号令牌...");

                var refreshTask = Task.Run(async () =>
                    await AccountService.Instance.RefreshMicrosoftAccountAsync(account.Id, onProgressUpdate, cancellationToken));

                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                var completedTask = await Task.WhenAny(refreshTask, timeoutTask).ConfigureAwait(false);

                if (completedTask == timeoutTask)
                {
                    LastError = "微软账号令牌刷新超时（30秒）\n请检查网络连接或重新登录";
                    throw new Exception(LastError);
                }

                var refreshSuccess = await refreshTask.ConfigureAwait(false);
                if (!refreshSuccess)
                {
                    LastError = "微软账号令牌已过期且刷新失败\n请重新登录微软账号";
                    throw new Exception(LastError);
                }
            }

            EnsureOldVersionIconsExist(config.GameDirectory);

            onProgressUpdate?.Invoke("正在读取游戏版本信息...");
            cancellationToken.ThrowIfCancellationRequested();

            var versionJsonPath = Path.Combine(config.GameDirectory, "versions", versionId, $"{versionId}.json");
            if (!File.Exists(versionJsonPath))
            {
                LastError = $"版本JSON文件不存在\n路径: {versionJsonPath}";
                throw new FileNotFoundException(LastError);
            }

            var versionJson = File.ReadAllText(versionJsonPath);
            var versionInfo = JsonSerializer.Deserialize<VersionInfo>(versionJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (versionInfo == null)
            {
                LastError = "无法解析版本JSON文件，文件格式可能不正确";
                throw new Exception(LastError);
            }

            string actualMcVersion = versionId;
            if (!string.IsNullOrEmpty(versionInfo.InheritsFrom))
            {
                actualMcVersion = versionInfo.InheritsFrom;
                versionInfo = MergeInheritedVersion(config.GameDirectory, versionId, versionInfo);
            }

            onProgressUpdate?.Invoke("正在验证Java环境...");
            cancellationToken.ThrowIfCancellationRequested();

            var actualJavaPath = config.GetActualJavaPath(actualMcVersion);
            if (!File.Exists(actualJavaPath))
            {
                LastError = $"Java可执行文件不存在\n路径: {actualJavaPath}";
                throw new FileNotFoundException(LastError);
            }

            if (string.IsNullOrEmpty(versionInfo.MainClass))
            {
                LastError = "版本JSON中缺少MainClass字段";
                throw new Exception(LastError);
            }

            bool isModLoader = versionInfo.MainClass?.Contains("forge", StringComparison.OrdinalIgnoreCase) == true ||
                               versionInfo.MainClass?.Contains("fabric", StringComparison.OrdinalIgnoreCase) == true ||
                               versionInfo.MainClass?.Contains("quilt", StringComparison.OrdinalIgnoreCase) == true;

            var versionDir = Path.Combine(config.GameDirectory, "versions", versionId);
            var nativesDir = Path.Combine(versionDir, "natives");
            Directory.CreateDirectory(nativesDir);

            onProgressUpdate?.Invoke("正在解压本地库文件...");
            cancellationToken.ThrowIfCancellationRequested();
            ExtractNatives(config.GameDirectory, versionId, versionInfo, nativesDir);

            onProgressUpdate?.Invoke("正在验证游戏客户端文件...");
            cancellationToken.ThrowIfCancellationRequested();

            var clientJar = Path.Combine(versionDir, $"{versionId}.jar");
            if (!isModLoader && !File.Exists(clientJar))
            {
                LastError = $"客户端JAR文件不存在\n路径: {clientJar}";
                throw new FileNotFoundException(LastError);
            }

            onProgressUpdate?.Invoke("正在检查游戏依赖库...");
            cancellationToken.ThrowIfCancellationRequested();

            var (missingRequired, missingOptional) = GetMissingLibraries(config.GameDirectory, versionId, versionInfo);
            MissingLibraries = missingRequired;
            MissingOptionalLibraries = missingOptional;

            if (missingRequired.Count > 0)
            {
                onProgressUpdate?.Invoke($"正在下载 {missingRequired.Count} 个缺失的库文件...");

                var (successCount, failedCount) = await LibraryDownloader.DownloadMissingLibrariesAsync(
                    config.GameDirectory,
                    versionId,
                    missingRequired,
                    (progress, current, total) => { onProgressUpdate?.Invoke(progress); },
                    cancellationToken).ConfigureAwait(false);

                if (failedCount > 0)
                {
                    LastError = "❌ 必需依赖库下载失败！";
                    return false;
                }
            }

            onProgressUpdate?.Invoke("正在验证游戏资源...");
            cancellationToken.ThrowIfCancellationRequested();
            var assetsResult = await AssetsDownloadService.DownloadAndCheckAssetsAsync(
                config.GameDirectory,
                versionId,
                (p, total, msg, speed) => 
                {
                    // 移除速率显示
                    onProgressUpdate?.Invoke($"{msg} ({p}%)|{p}");
                },
                cancellationToken).ConfigureAwait(false);

            if (!assetsResult.Success)
            {
                LastError = "❌ 游戏资源检查或下载失败！";
                return false;
            }

            onProgressUpdate?.Invoke("正在准备启动参数...");
            cancellationToken.ThrowIfCancellationRequested();

            var arguments = BuildLaunchArguments(versionId, account, config, versionInfo);

            onProgressUpdate?.Invoke("正在启动游戏进程...");
            cancellationToken.ThrowIfCancellationRequested();

            var workingDirectory = config.GetRunDirectory(versionId);

            var processInfo = new ProcessStartInfo
            {
                FileName = actualJavaPath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var process = new Process { StartInfo = processInfo };

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    onGameOutput?.Invoke(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    onGameOutput?.Invoke(e.Data);
                }
            };

            if (!process.Start())
            {
                LastError = "无法启动Java进程，请检查Java路径是否正确";
                throw new Exception(LastError);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (onGameExit != null)
            {
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) =>
                {
                    var exitCode = process.ExitCode;
                    onGameExit.Invoke(exitCode);
                };
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);

            if (process.HasExited)
            {
                LastError = $"游戏进程启动后立即退出\n退出代码: {process.ExitCode}\n请检查Debug输出窗口查看详细错误日志";
                onGameExit?.Invoke(process.ExitCode);
                return false;
            }

            onProgressUpdate?.Invoke("启动完成");
            return true;
        }
        catch (OperationCanceledException)
        {
            LastError = "启动已取消";
            return false;
        }
        catch (Exception ex)
        {
            if (string.IsNullOrEmpty(LastError))
            {
                LastError = ex.Message;
            }
            return false;
        }
    }

    private static string BuildLaunchArguments(string versionId, GameAccount account, LauncherConfig config, VersionInfo versionInfo)
    {
        var args = new StringBuilder();

        args.Append($"-Xms{config.MinMemory}M ");
        args.Append($"-Xmx{config.MaxMemory}M ");

        if (IsVeryOldForgeVersion(versionId))
        {
            args.Append("-Dfml.ignoreInvalidMinecraftCertificates=true ");
            args.Append("-Dfml.ignorePatchDiscrepancies=true ");
        }

        if (account.Type == AccountType.Yggdrasil)
        {
            var server = YggdrasilServerService.Instance.GetServerById(account.YggdrasilServerId ?? "");
            if (server != null)
            {
                var authlibPath = AuthlibInjectorService.GetAuthlibInjectorPath();
                var apiUrl = server.GetFullApiUrl();
                args.Append($"-javaagent:\"{authlibPath}\"={apiUrl} ");
            }
        }

        if (!string.IsNullOrWhiteSpace(config.JvmArguments))
        {
            args.Append($"{config.JvmArguments} ");
        }

        bool isModularNeoForge = versionInfo.MainClass?.Contains("bootstraplauncher", StringComparison.OrdinalIgnoreCase) == true;

        var gameDir = config.GetRunDirectory(versionId);
        var baseGameDir = config.GameDirectory;
        var versionDir = Path.Combine(baseGameDir, "versions", versionId);
        var nativesDir = Path.Combine(versionDir, "natives");
        var librariesDir = Path.Combine(baseGameDir, "libraries");
        var assetsDir = Path.Combine(baseGameDir, "assets");

        var modulePathJars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var classpathItems = new List<string>();

        if (versionInfo.Libraries != null)
        {
            foreach (var lib in versionInfo.Libraries)
            {
                if (IsLibraryAllowed(lib))
                {
                    var libPath = GetLibraryPath(librariesDir, lib);
                    if (!string.IsNullOrEmpty(libPath) && File.Exists(libPath))
                    {
                        classpathItems.Add(libPath);
                    }
                }
            }
        }

        var versionJarPath = Path.Combine(versionDir, $"{versionId}.jar");
        if (File.Exists(versionJarPath))
        {
            classpathItems.Add(versionJarPath);
        }

        var classpathString = string.Join(Path.PathSeparator, classpathItems);

        bool hasModulePathInJson = false;
        bool hasPluginLayerLibrariesInJson = false;
        bool hasGameLayerLibrariesInJson = false;

        if (versionInfo.Arguments?.Jvm != null)
        {
            for (int i = 0; i < versionInfo.Arguments.Jvm.Count; i++)
            {
                var arg = versionInfo.Arguments.Jvm[i];
                string? argStr = null;

                if (arg is string str)
                    argStr = str;
                else if (arg is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
                    argStr = jsonElement.GetString();

                if (string.IsNullOrEmpty(argStr))
                    continue;

                var replacedArg = ReplaceArgVariables(argStr, versionId, gameDir, librariesDir, nativesDir, assetsDir, classpathString);

                if (replacedArg.Contains("-Dfml.pluginLayerLibraries"))
                {
                    hasPluginLayerLibrariesInJson = true;
                }
                if (replacedArg.Contains("-Dfml.gameLayerLibraries"))
                {
                    hasGameLayerLibrariesInJson = true;
                }

                if (replacedArg == "-cp" || replacedArg == "--class-path")
                {
                    if (i + 1 < versionInfo.Arguments.Jvm.Count)
                        i++;
                    continue;
                }

                if (replacedArg == "-p" || replacedArg == "--module-path")
                {
                    hasModulePathInJson = true;
                    if (i + 1 < versionInfo.Arguments.Jvm.Count)
                    {
                        var nextArg = versionInfo.Arguments.Jvm[i + 1];
                        string? nextArgStr = null;

                        if (nextArg is string nextStr)
                            nextArgStr = nextStr;
                        else if (nextArg is JsonElement nextJsonElement && nextJsonElement.ValueKind == JsonValueKind.String)
                            nextArgStr = nextJsonElement.GetString();

                        if (!string.IsNullOrEmpty(nextArgStr))
                        {
                            var replacedModulePath = ReplaceArgVariables(nextArgStr, versionId, gameDir, librariesDir, nativesDir, assetsDir, classpathString);

                            var modulePathList = new List<string>();

                            var moduleJars = replacedModulePath.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var jar in moduleJars)
                            {
                                var jarPath = jar.Trim().Trim('"');
                                if (!string.IsNullOrEmpty(jarPath) && File.Exists(jarPath))
                                {
                                    var fileName = Path.GetFileName(jarPath);

                                    if (fileName.Contains("earlydisplay", StringComparison.OrdinalIgnoreCase) ||
                                        fileName.Contains("loader", StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }

                                    var normalizedPath = Path.GetFullPath(jarPath);

                                    if (!modulePathJars.Contains(normalizedPath))
                                    {
                                        modulePathJars.Add(normalizedPath);
                                        modulePathList.Add(normalizedPath);
                                    }
                                }
                            }

                            if (versionInfo.Libraries != null)
                            {
                                var criticalModulePatterns = new[]
                                {
                                    "cpw.mods:bootstraplauncher:",
                                    "cpw.mods:securejarhandler:",
                                    "org.ow2.asm:asm:",
                                    "org.ow2.asm:asm-tree:",
                                    "org.ow2.asm:asm-commons:",
                                    "org.ow2.asm:asm-util:",
                                    "org.ow2.asm:asm-analysis:",
                                    "net.neoforged:JarJarFileSystems:"
                                };

                                foreach (var lib in versionInfo.Libraries)
                                {
                                    if (lib.Name != null && IsLibraryAllowed(lib))
                                    {
                                        foreach (var pattern in criticalModulePatterns)
                                        {
                                            if (lib.Name.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                                            {
                                                var libPath = GetLibraryPath(librariesDir, lib);
                                                if (!string.IsNullOrEmpty(libPath) && File.Exists(libPath))
                                                {
                                                    var normalizedPath = Path.GetFullPath(libPath);

                                                    if (!modulePathJars.Contains(normalizedPath))
                                                    {
                                                        modulePathList.Add(normalizedPath);
                                                        modulePathJars.Add(normalizedPath);
                                                    }
                                                }
                                                break;
                                            }
                                        }
                                    }
                                }
                            }

                            var shortPathList = new List<string>();
                            foreach (var longPath in modulePathList)
                            {
                                try
                                {
                                    var shortPath = GetShortPath(longPath);
                                    shortPathList.Add(shortPath);
                                }
                                catch
                                {
                                    shortPathList.Add(longPath);
                                }
                            }

                            var finalModulePath = string.Join(Path.PathSeparator, shortPathList);
                            args.Append($"--module-path \"{finalModulePath}\" ");

                            i++;
                            continue;
                        }
                    }
                }

                if (ShouldSkipJvmArg(replacedArg))
                    continue;

                var fixedArg = FixModuleArgument(replacedArg, isModularNeoForge);
                args.Append($"{fixedArg} ");
            }
        }

        if (versionInfo.MainClass?.Contains("bootstraplauncher", StringComparison.OrdinalIgnoreCase) == true)
        {
            args.Append("--add-exports cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED ");
        }

        args.Append($"-Djava.library.path=\"{nativesDir}\" ");

        bool isNeoForge = versionInfo.MainClass?.Contains("neoforge", StringComparison.OrdinalIgnoreCase) == true ||
                          versionInfo.MainClass?.Contains("forge", StringComparison.OrdinalIgnoreCase) == true ||
                          versionInfo.MainClass?.Contains("bootstraplauncher", StringComparison.OrdinalIgnoreCase) == true;

        if (isNeoForge)
        {
            args.Append($"-DlibraryDirectory=\"{librariesDir}\" ");

            if (versionInfo.MainClass?.Contains("bootstraplauncher", StringComparison.OrdinalIgnoreCase) == true)
            {
                var clientJarPath = Path.Combine(versionDir, $"{versionId}.jar");
                if (File.Exists(clientJarPath))
                {
                    args.Append($"-Dminecraft.client.jar=\"{clientJarPath}\" ");
                }

                args.Append("-DmergeModules=jna-5.15.0.jar,jna-platform-5.15.0.jar ");

                if (!hasPluginLayerLibrariesInJson)
                {
                    args.Append("-Dfml.pluginLayerLibraries= ");
                }

                if (!hasGameLayerLibrariesInJson)
                {
                    args.Append("-Dfml.gameLayerLibraries= ");
                }
            }
        }

        if (versionInfo.MainClass?.Contains("bootstraplauncher", StringComparison.OrdinalIgnoreCase) == true && !hasModulePathInJson)
        {
            var modulePaths = new List<string>();

            var modularLibraryPatterns = new[]
            {
                "cpw.mods:bootstraplauncher",
                "cpw.mods:securejarhandler",
                "net.neoforged:JarJarFileSystems",
                "org.ow2.asm:asm",
                "org.ow2.asm:asm-tree",
                "org.ow2.asm:asm-commons",
                "org.ow2.asm:asm-util",
                "org.ow2.asm:asm-analysis"
            };

            if (versionInfo.Libraries != null)
            {
                foreach (var lib in versionInfo.Libraries)
                {
                    if (lib.Name != null && IsLibraryAllowed(lib))
                    {
                        foreach (var pattern in modularLibraryPatterns)
                        {
                            if (lib.Name.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                            {
                                var libPath = GetLibraryPath(librariesDir, lib);
                                if (!string.IsNullOrEmpty(libPath) && File.Exists(libPath) && !modulePathJars.Contains(libPath))
                                {
                                    modulePaths.Add(libPath);
                                    modulePathJars.Add(libPath);
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            if (modulePaths.Count > 0)
            {
                args.Append("--module-path \"");
                args.Append(string.Join(Path.PathSeparator, modulePaths));
                args.Append("\" ");
            }
        }

        args.Append("-cp ");
        args.Append($"\"{classpathString}\" ");

        var mainClass = versionInfo.MainClass ?? "";
        if (mainClass.Contains(" "))
        {
            args.Append($"\"{mainClass}\" ");
        }
        else
        {
            args.Append($"{mainClass} ");
        }

        var gameArgs = BuildGameArguments(versionId, account, config, versionInfo, gameDir, assetsDir);
        args.Append(gameArgs);

        return args.ToString().Trim();
    }

    private static string BuildGameArguments(string versionId, GameAccount account, LauncherConfig config, VersionInfo versionInfo, string gameDir, string assetsDir)
    {
        var args = new StringBuilder();

        var assetIndex = versionInfo.AssetIndex?.Id ?? versionInfo.Assets ?? "legacy";

        if (!string.IsNullOrEmpty(versionInfo.MinecraftArguments))
        {
            var sessionToken = account.Type == AccountType.Microsoft ? (account.MinecraftAccessToken ?? "0") : "0";
            bool isVeryOldVersion = assetIndex == "legacy" || assetIndex == "pre-1.6";

            if (isVeryOldVersion)
            {
                sessionToken = "legacy";
            }

            var gameAssetsPath = isVeryOldVersion ? gameDir : assetsDir;

            var minecraftArgs = versionInfo.MinecraftArguments
                .Replace("${auth_player_name}", $"\"{account.Username}\"")
                .Replace("${version_name}", $"\"{versionId}\"")
                .Replace("${game_directory}", $"\"{gameDir}\"")
                .Replace("${assets_root}", $"\"{assetsDir}\"")
                .Replace("${assets_index_name}", $"\"{assetIndex}\"")
                .Replace("${auth_uuid}", isVeryOldVersion ? "00000000-0000-0000-0000-000000000000" : (account.Type == AccountType.Microsoft ? (account.MinecraftUUID ?? account.UUID) : account.UUID))
                .Replace("${auth_access_token}", sessionToken)
                .Replace("${auth_session}", sessionToken)
                .Replace("${user_properties}", "{}")
                .Replace("${user_type}", isVeryOldVersion ? "legacy" : (account.Type == AccountType.Microsoft ? "msa" : "legacy"))
                .Replace("${version_type}", "ObsMCLauncher")
                .Replace("${game_assets}", $"\"{gameAssetsPath}\"");

            args.Append(minecraftArgs);
            return args.ToString();
        }

        if (versionInfo.Arguments?.Game != null)
        {
            foreach (var arg in versionInfo.Arguments.Game)
            {
                string? argStr = null;

                if (arg is string str)
                    argStr = str;
                else if (arg is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
                    argStr = jsonElement.GetString();

                if (string.IsNullOrEmpty(argStr))
                    continue;

                if (ShouldSkipGameArg(argStr))
                    continue;

                args.Append($"{argStr} ");
            }
        }

        args.Append($"--username \"{account.Username}\" ");
        args.Append($"--version \"{versionId}\" ");
        args.Append($"--gameDir \"{gameDir}\" ");
        args.Append($"--assetsDir \"{assetsDir}\" ");
        args.Append($"--assetIndex \"{assetIndex}\" ");

        if (account.Type == AccountType.Microsoft)
        {
            var uuid = account.MinecraftUUID ?? account.UUID;
            var accessToken = account.MinecraftAccessToken ?? "0";
            args.Append($"--uuid {uuid} ");
            args.Append($"--accessToken {accessToken} ");
            args.Append("--userType msa ");
        }
        else if (account.Type == AccountType.Yggdrasil)
        {
            var uuid = account.UUID;
            var accessToken = account.YggdrasilAccessToken ?? "0";
            args.Append($"--uuid {uuid} ");
            args.Append($"--accessToken {accessToken} ");
            args.Append("--userType mojang ");
        }
        else
        {
            args.Append($"--uuid {account.UUID} ");
            args.Append("--accessToken 0 ");
            args.Append("--userType legacy ");
        }

        args.Append("--versionType \"ObsMCLauncher\" ");

        return args.ToString();
    }

    private static bool IsVeryOldForgeVersion(string versionId)
    {
        if (versionId.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!versionId.Contains("forge", StringComparison.OrdinalIgnoreCase))
            return false;

        if (versionId.Contains("1.6.") || versionId.Contains("1.7.") ||
            versionId.Contains("1.8.") || versionId.Contains("1.9.") ||
            versionId.Contains("1.10.") || versionId.Contains("1.11.") ||
            versionId.Contains("1.12."))
            return true;

        return false;
    }

    private static string ReplaceArgVariables(string arg, string versionId, string gameDir, string librariesDir, string nativesDir, string assetsDir, string classpath)
    {
        if (string.IsNullOrEmpty(arg))
            return arg;

        var versionDir = Path.Combine(gameDir, "versions", versionId);
        var clientJar = Path.Combine(versionDir, $"{versionId}.jar");

        var safeVersionId = NeedsQuoting(versionId) ? $"\"{versionId}\"" : versionId;
        var safeGameDir = NeedsQuoting(gameDir) ? $"\"{gameDir}\"" : gameDir;
        var safeAssetsDir = NeedsQuoting(assetsDir) ? $"\"{assetsDir}\"" : assetsDir;
        var safeNativesDir = NeedsQuoting(nativesDir) ? $"\"{nativesDir}\"" : nativesDir;
        var safeLibrariesDir = NeedsQuoting(librariesDir) ? $"\"{librariesDir}\"" : librariesDir;
        var safeClientJar = NeedsQuoting(clientJar) ? $"\"{clientJar}\"" : clientJar;

        return arg
            .Replace("${version_name}", safeVersionId)
            .Replace("${game_directory}", safeGameDir)
            .Replace("${assets_root}", safeAssetsDir)
            .Replace("${assets_index_name}", "26")
            .Replace("${auth_player_name}", "Player")
            .Replace("${version_type}", "release")
            .Replace("${auth_uuid}", Guid.Empty.ToString())
            .Replace("${auth_access_token}", "")
            .Replace("${user_type}", "msa")
            .Replace("${user_properties}", "{}")
            .Replace("${library_directory}", safeLibrariesDir)
            .Replace("${classpath_separator}", Path.PathSeparator.ToString())
            .Replace("${natives_directory}", safeNativesDir)
            .Replace("${launcher_name}", "ObsMCLauncher")
            .Replace("${launcher_version}", "1.0")
            .Replace("${clientid}", Guid.Empty.ToString())
            .Replace("${auth_xuid}", "")
            .Replace("${clientJar}", safeClientJar)
            .Replace("${primary_jar}", safeClientJar)
            .Replace("${classpath}", classpath);
    }

    private static bool NeedsQuoting(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        return value.Contains(' ') || value.Contains('[') || value.Contains(']') ||
               value.Contains('(') || value.Contains(')') || value.Contains('&') ||
               value.Contains('|') || value.Contains(';');
    }

    private static bool ShouldSkipJvmArg(string arg)
    {
        if (string.IsNullOrEmpty(arg))
            return true;

        if (arg.StartsWith("-Djava.library.path"))
            return true;
        if (arg.StartsWith("-Dminecraft.launcher.brand"))
            return true;
        if (arg.StartsWith("-Dminecraft.launcher.version"))
            return true;
        if (arg.Equals("-cp") || arg.Equals("--class-path"))
            return true;

        if (arg.Equals("-p") || arg.Equals("--module-path"))
            return true;

        return false;
    }

    private static string FixModuleArgument(string arg, bool isModularNeoForge)
    {
        if (string.IsNullOrEmpty(arg))
            return arg;

        if ((arg.StartsWith("--add-opens") || arg.StartsWith("--add-exports")) && arg.Contains("="))
        {
            var parts = arg.Split('=');
            if (parts.Length == 2)
            {
                var targetModule = parts[1];
                if (!targetModule.Equals("ALL-UNNAMED", StringComparison.OrdinalIgnoreCase) &&
                    !targetModule.StartsWith("java.", StringComparison.OrdinalIgnoreCase) &&
                    !targetModule.StartsWith("jdk.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!isModularNeoForge)
                    {
                        return $"{parts[0]}=ALL-UNNAMED";
                    }
                }
            }
        }

        if (arg.Contains("=") && arg.Contains("/"))
        {
            var parts = arg.Split('=');
            if (parts.Length == 2 && parts[0].Contains("/"))
            {
                var targetModule = parts[1];
                if (!targetModule.Equals("ALL-UNNAMED", StringComparison.OrdinalIgnoreCase) &&
                    !targetModule.StartsWith("java.", StringComparison.OrdinalIgnoreCase) &&
                    !targetModule.StartsWith("jdk.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!isModularNeoForge)
                    {
                        return $"{parts[0]}=ALL-UNNAMED";
                    }
                }
            }
        }

        return arg;
    }

    private static bool ShouldSkipGameArg(string arg)
    {
        if (string.IsNullOrEmpty(arg))
            return true;

        if (arg.Contains("${"))
            return true;

        var standardArgs = new[]
        {
            "--username", "--version", "--gameDir", "--assetsDir",
            "--assetIndex", "--uuid", "--accessToken", "--userType",
            "--versionType", "--width", "--height"
        };

        if (standardArgs.Contains(arg))
            return true;

        return false;
    }

    private static bool IsLibraryAllowed(Library lib)
    {
        if (lib.Rules == null || lib.Rules.Length == 0)
            return true;

        bool allowed = false;
        foreach (var rule in lib.Rules)
        {
            bool matches = true;

            if (rule.Os != null)
            {
                var osName = GetOSName();
                matches = rule.Os.Name == null || rule.Os.Name.Equals(osName, StringComparison.OrdinalIgnoreCase);
            }

            if (matches)
            {
                allowed = rule.Action == "allow";
            }
        }

        return allowed;
    }

    private static (List<string> missingRequired, List<string> missingOptional) GetMissingLibraries(string gameDir, string versionId, VersionInfo versionInfo)
    {
        var missingRequired = new List<string>();
        var missingOptional = new List<string>();
        var librariesDir = Path.Combine(gameDir, "libraries");

        if (versionInfo.Libraries == null) return (missingRequired, missingOptional);

        var osName = GetOSName();

        foreach (var lib in versionInfo.Libraries)
        {
            if (IsLibraryAllowed(lib))
            {
                bool isForgeSpecialLib = lib.Name != null && lib.Name.StartsWith("net.minecraftforge") &&
                                         (lib.Name.Contains(":client") || lib.Name.Contains(":server"));

                if (isForgeSpecialLib)
                {
                    continue;
                }

                bool isOptional = lib.Downloads?.Artifact == null;
                var libPath = GetLibraryPath(librariesDir, lib);

                if (!string.IsNullOrEmpty(libPath))
                {
                    bool isMissing = false;

                    if (!File.Exists(libPath))
                    {
                        isMissing = true;
                    }
                    else if (!isOptional && lib.Downloads?.Artifact?.Size > 0)
                    {
                        var fileInfo = new FileInfo(libPath);
                        if (fileInfo.Length != lib.Downloads.Artifact.Size)
                        {
                            isMissing = true;
                        }
                    }

                    if (isMissing)
                    {
                        if (isOptional)
                            missingOptional.Add(lib.Name ?? "Unknown");
                        else
                            missingRequired.Add(lib.Name ?? "Unknown");
                    }
                }

                if (lib.Natives != null && lib.Downloads?.Classifiers != null)
                {
                    if (lib.Natives.TryGetValue(osName, out var nativesKey) && !string.IsNullOrEmpty(nativesKey))
                    {
                        if (lib.Downloads.Classifiers.TryGetValue(nativesKey, out var nativeArtifact) &&
                            !string.IsNullOrEmpty(nativeArtifact.Path))
                        {
                            var nativesPath = Path.Combine(librariesDir, nativeArtifact.Path.Replace("/", "\\"));

                            if (!File.Exists(nativesPath))
                            {
                                if (!missingOptional.Contains(lib.Name ?? "Unknown"))
                                {
                                    missingOptional.Add(lib.Name ?? "Unknown");
                                }
                            }
                        }
                    }
                }
            }
        }

        return (missingRequired, missingOptional);
    }

    private static string GetLibraryPath(string librariesDir, Library lib)
    {
        if (lib.Downloads?.Artifact?.Path != null)
        {
            return Path.Combine(librariesDir, lib.Downloads.Artifact.Path.Replace('/', Path.DirectorySeparatorChar));
        }

        if (lib.Natives != null && lib.Natives.TryGetValue("windows", out var nativeKey))
        {
            if (lib.Downloads?.Classifiers != null && lib.Downloads.Classifiers.TryGetValue(nativeKey, out var classifierArtifact))
            {
                if (!string.IsNullOrEmpty(classifierArtifact.Path))
                {
                    return Path.Combine(librariesDir, classifierArtifact.Path.Replace('/', Path.DirectorySeparatorChar));
                }
            }
        }

        if (!string.IsNullOrEmpty(lib.Name))
        {
            try
            {
                var parts = lib.Name.Split(':');
                if (parts.Length >= 3)
                {
                    string group = parts[0].Replace('.', Path.DirectorySeparatorChar);
                    string artifact = parts[1];
                    string version = parts[2];
                    string? classifier = parts.Length > 3 ? parts[3] : null;

                    string fileName = !string.IsNullOrEmpty(classifier)
                        ? $"{artifact}-{version}-{classifier}.jar"
                        : $"{artifact}-{version}.jar";

                    string path = Path.Combine(librariesDir, group, artifact, version, fileName);
                    return path;
                }
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    private static string GetOSName()
    {
        if (OperatingSystem.IsWindows())
            return "windows";
        if (OperatingSystem.IsLinux())
            return "linux";
        if (OperatingSystem.IsMacOS())
            return "osx";
        return "unknown";
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return "0 B/s";
        string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
        int unitIndex = 0;
        while (bytesPerSecond >= 1024 && unitIndex < units.Length - 1)
        {
            bytesPerSecond /= 1024;
            unitIndex++;
        }
        return $"{bytesPerSecond:F2} {units[unitIndex]}";
    }

    private class VersionInfo
    {
        public string? MainClass { get; set; }
        public string? Assets { get; set; }
        public AssetIndexInfo? AssetIndex { get; set; }
        public Library[]? Libraries { get; set; }
        public GameArguments? Arguments { get; set; }
        public string? MinecraftArguments { get; set; }
        public string? InheritsFrom { get; set; }
        public string? VersionName { get; set; }
    }

    private class GameArguments
    {
        public List<object>? Game { get; set; }
        public List<object>? Jvm { get; set; }
    }

    private class AssetIndexInfo
    {
        public string? Id { get; set; }
    }

    private class Library
    {
        public string? Name { get; set; }
        public LibraryDownloads? Downloads { get; set; }
        public Rule[]? Rules { get; set; }
        public Dictionary<string, string>? Natives { get; set; }
    }

    private class LibraryDownloads
    {
        public Artifact? Artifact { get; set; }
        public Dictionary<string, Artifact>? Classifiers { get; set; }
    }

    private class Artifact
    {
        public string? Path { get; set; }
        public string? Url { get; set; }
        public long Size { get; set; }
    }

    private class Rule
    {
        public string? Action { get; set; }
        public OsInfo? Os { get; set; }
    }

    private class OsInfo
    {
        public string? Name { get; set; }
    }

    private static void ExtractNatives(string gameDir, string versionId, VersionInfo versionInfo, string nativesDir)
    {
        try
        {
            if (Directory.Exists(nativesDir))
            {
                Directory.Delete(nativesDir, true);
            }
            Directory.CreateDirectory(nativesDir);

            if (versionInfo.Libraries == null)
            {
                return;
            }

            var librariesDir = Path.Combine(gameDir, "libraries");
            var osName = GetOSName();

            foreach (var lib in versionInfo.Libraries)
            {
                if (!IsLibraryAllowed(lib))
                    continue;

                string? nativesJarPath = null;

                if (lib.Natives != null && lib.Downloads?.Classifiers != null)
                {
                    if (lib.Natives.TryGetValue(osName, out var nativesKey) && !string.IsNullOrEmpty(nativesKey))
                    {
                        if (lib.Downloads.Classifiers.TryGetValue(nativesKey, out var nativeArtifact) &&
                            !string.IsNullOrEmpty(nativeArtifact.Path))
                        {
                            nativesJarPath = Path.Combine(librariesDir, nativeArtifact.Path.Replace("/", "\\"));
                        }
                    }
                }
                else if (lib.Name != null && lib.Name.Contains("natives-windows"))
                {
                    var libPath = GetLibraryPath(librariesDir, lib);
                    if (!string.IsNullOrEmpty(libPath))
                    {
                        nativesJarPath = libPath;
                    }
                }

                if (string.IsNullOrEmpty(nativesJarPath))
                    continue;

                if (!File.Exists(nativesJarPath))
                {
                    continue;
                }

                try
                {
                    using var archive = System.IO.Compression.ZipFile.OpenRead(nativesJarPath);

                    foreach (var entry in archive.Entries)
                    {
                        var ext = Path.GetExtension(entry.Name).ToLower();
                        if (ext == ".dll" || ext == ".so" || ext == ".dylib")
                        {
                            var destPath = Path.Combine(nativesDir, entry.Name);
                            entry.ExtractToFile(destPath, overwrite: true);
                        }
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static string GetLibraryKey(string? libraryName)
    {
        if (string.IsNullOrEmpty(libraryName))
            return string.Empty;

        var parts = libraryName.Split(':');

        if (parts.Length >= 4)
        {
            return $"{parts[0]}:{parts[1]}:{parts[3]}";
        }

        if (parts.Length >= 2)
        {
            return $"{parts[0]}:{parts[1]}";
        }

        return libraryName;
    }

    private static VersionInfo MergeInheritedVersion(string gameDirectory, string childVersionId, VersionInfo childVersion)
    {
        try
        {
            var parentVersionId = childVersion.InheritsFrom;
            if (string.IsNullOrEmpty(parentVersionId))
                return childVersion;

            var parentJsonPath = Path.Combine(gameDirectory, "versions", parentVersionId, $"{parentVersionId}.json");

            if (!File.Exists(parentJsonPath))
            {
                var childVersionDir = Path.Combine(gameDirectory, "versions", childVersionId);
                var parentJsonInChildDir = Path.Combine(childVersionDir, $"{parentVersionId}.json");

                if (File.Exists(parentJsonInChildDir))
                {
                    parentJsonPath = parentJsonInChildDir;
                }
                else
                {
                    return childVersion;
                }
            }

            var parentJson = File.ReadAllText(parentJsonPath);
            var parentVersion = JsonSerializer.Deserialize<VersionInfo>(parentJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (parentVersion == null)
            {
                return childVersion;
            }

            if (!string.IsNullOrEmpty(parentVersion.InheritsFrom))
            {
                parentVersion = MergeInheritedVersion(gameDirectory, parentVersionId, parentVersion);
            }

            var mergedLibraries = new List<Library>();
            var libraryKeys = new HashSet<string>();

            if (childVersion.Libraries != null)
            {
                foreach (var library in childVersion.Libraries)
                {
                    mergedLibraries.Add(library);
                    var libKey = GetLibraryKey(library.Name);
                    libraryKeys.Add(libKey);
                }
            }

            if (parentVersion.Libraries != null)
            {
                foreach (var library in parentVersion.Libraries)
                {
                    var libKey = GetLibraryKey(library.Name);
                    if (!libraryKeys.Contains(libKey))
                    {
                        mergedLibraries.Add(library);
                        libraryKeys.Add(libKey);
                    }
                }
            }

            childVersion.Libraries = mergedLibraries.ToArray();

            if (childVersion.AssetIndex == null && parentVersion.AssetIndex != null)
                childVersion.AssetIndex = parentVersion.AssetIndex;
            if (string.IsNullOrEmpty(childVersion.Assets) && !string.IsNullOrEmpty(parentVersion.Assets))
                childVersion.Assets = parentVersion.Assets;
            if (childVersion.Arguments == null && parentVersion.Arguments != null)
                childVersion.Arguments = parentVersion.Arguments;
            if (string.IsNullOrEmpty(childVersion.MinecraftArguments) && !string.IsNullOrEmpty(parentVersion.MinecraftArguments))
                childVersion.MinecraftArguments = parentVersion.MinecraftArguments;

            return childVersion;
        }
        catch
        {
            return childVersion;
        }
    }

    private static void EnsureOldVersionIconsExist(string gameDirectory)
    {
        try
        {
            var iconsDir = Path.Combine(gameDirectory, "icons");

            var icon16Path = Path.Combine(iconsDir, "icon_16x16.png");
            var icon32Path = Path.Combine(iconsDir, "icon_32x32.png");

            if (File.Exists(icon16Path) && File.Exists(icon32Path))
            {
                return;
            }

            Directory.CreateDirectory(iconsDir);

            if (!File.Exists(icon16Path))
            {
                CreateMinimalTransparentPng(icon16Path, 16, 16);
            }

            if (!File.Exists(icon32Path))
            {
                CreateMinimalTransparentPng(icon32Path, 32, 32);
            }
        }
        catch
        {
        }
    }

    // D2: 写最小 PNG 字节（透明）
    private static void CreateMinimalTransparentPng(string filePath, int width, int height)
    {
        // 1x1 透明 PNG
        // iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+X7iQAAAAASUVORK5CYII=
        var pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+X7iQAAAAASUVORK5CYII=");

        File.WriteAllBytes(filePath, pngBytes);
    }

    private static string GetShortPath(string longPath)
    {
        if (string.IsNullOrEmpty(longPath))
            return longPath;

        if (!OperatingSystem.IsWindows() || !File.Exists(longPath))
            return longPath;

        try
        {
            var shortPath = new StringBuilder(1024);
            uint result = GetShortPathName(longPath, shortPath, shortPath.Capacity);

            if (result == 0 || result > shortPath.Capacity)
            {
                return longPath;
            }

            return shortPath.ToString();
        }
        catch
        {
            return longPath;
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern uint GetShortPathName(
        string lpszLongPath,
        StringBuilder lpszShortPath,
        int cchBuffer);
}
