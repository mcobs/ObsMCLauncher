using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// 游戏启动器服务
    /// </summary>
    public class GameLauncher
    {
        public static string LastError { get; private set; } = string.Empty;
        
        /// <summary>
        /// 缺失的必需库文件列表
        /// </summary>
        public static List<string> MissingLibraries { get; private set; } = new List<string>();
        
        /// <summary>
        /// 缺失的可选库列表（natives、Twitch、JInput等）
        /// </summary>
        public static List<string> MissingOptionalLibraries { get; private set; } = new List<string>();

        /// <summary>
        /// 检查游戏完整性（不启动游戏）
        /// </summary>
        /// <param name="versionId">版本ID</param>
        /// <param name="config">启动器配置</param>
        /// <param name="onProgressUpdate">进度更新回调</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否存在完整性问题（true表示有缺失文件）</returns>
        public static async System.Threading.Tasks.Task<bool> CheckGameIntegrityAsync(string versionId, LauncherConfig config, Action<string>? onProgressUpdate = null, System.Threading.CancellationToken cancellationToken = default)
        {
            LastError = string.Empty;
            MissingLibraries.Clear();
            MissingOptionalLibraries.Clear();
            
            try
            {
                Debug.WriteLine($"========== 检查游戏完整性 ==========");
                Debug.WriteLine($"版本: {versionId}");
                Debug.WriteLine($"游戏目录: {config.GameDirectory}");

                // 1. 验证Java路径
                onProgressUpdate?.Invoke("正在验证Java环境...");
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(config.JavaPath))
                {
                    LastError = $"Java路径不存在: {config.JavaPath}";
                    Debug.WriteLine($"❌ {LastError}");
                    return false;
                }

                // 2. 读取版本JSON
                onProgressUpdate?.Invoke("正在读取版本信息...");
                cancellationToken.ThrowIfCancellationRequested();
                var versionJsonPath = Path.Combine(config.GameDirectory, "versions", versionId, $"{versionId}.json");
                if (!File.Exists(versionJsonPath))
                {
                    LastError = $"版本配置文件不存在: {versionJsonPath}";
                    Debug.WriteLine($"❌ {LastError}");
                    throw new FileNotFoundException(LastError);
                }

                var versionJson = await File.ReadAllTextAsync(versionJsonPath);
                var versionInfo = JsonSerializer.Deserialize<VersionInfo>(versionJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (versionInfo == null)
                {
                    LastError = "无法解析版本配置文件";
                    Debug.WriteLine($"❌ {LastError}");
                    throw new Exception(LastError);
                }

                // 处理inheritsFrom（合并父版本的libraries和其他信息）
                if (!string.IsNullOrEmpty(versionInfo.InheritsFrom))
                {
                    Debug.WriteLine($"检测到inheritsFrom: {versionInfo.InheritsFrom}，开始合并父版本信息");
                    versionInfo = MergeInheritedVersion(config.GameDirectory, versionId, versionInfo);
                }

                Debug.WriteLine($"版本JSON路径: {versionJsonPath}");
                Debug.WriteLine($"MainClass: {versionInfo.MainClass}");
                Debug.WriteLine($"Libraries: {versionInfo.Libraries?.Length ?? 0} 个");

                // 判断是否为Mod加载器（Forge/Fabric等的JAR在libraries中）
                bool isModLoader = versionInfo.MainClass?.Contains("forge", StringComparison.OrdinalIgnoreCase) == true ||
                                   versionInfo.MainClass?.Contains("fabric", StringComparison.OrdinalIgnoreCase) == true ||
                                   versionInfo.MainClass?.Contains("quilt", StringComparison.OrdinalIgnoreCase) == true;

                // 3. 检查客户端JAR文件（原版需要，Mod加载器不需要）
                onProgressUpdate?.Invoke("正在检查游戏主文件...");
                cancellationToken.ThrowIfCancellationRequested();
                var clientJarPath = Path.Combine(config.GameDirectory, "versions", versionId, $"{versionId}.jar");
                
                if (!isModLoader)
                {
                    // 原版Minecraft需要版本文件夹中的JAR
                    if (!File.Exists(clientJarPath))
                    {
                        LastError = $"游戏主文件不存在: {clientJarPath}\n请先下载游戏版本";
                        Debug.WriteLine($"❌ {LastError}");
                        throw new FileNotFoundException(LastError);
                    }
                    Debug.WriteLine($"客户端JAR: {clientJarPath}");
                }
                else
                {
                    Debug.WriteLine($"检测到Mod加载器版本，跳过版本文件夹JAR检查（JAR在libraries中）");
                }

                // 4. 检查库文件完整性（包括文件大小验证）
                onProgressUpdate?.Invoke("正在检查游戏依赖库...");
                cancellationToken.ThrowIfCancellationRequested();
                Debug.WriteLine($"检查库文件完整性...");
                var (missingRequired, missingOptional) = GetMissingLibraries(config.GameDirectory, versionId, versionInfo);
                
                MissingLibraries = missingRequired;
                MissingOptionalLibraries = missingOptional;
                
                if (missingRequired.Count > 0)
                {
                    LastError = $"检测到 {missingRequired.Count} 个缺失或不完整的必需库文件";
                    Debug.WriteLine($"❌ 缺失 {missingRequired.Count} 个必需库文件");
                    return true; // 有完整性问题
                }
                
                if (missingOptional.Count > 0)
                {
                    Debug.WriteLine($"⚠️ 检测到 {missingOptional.Count} 个缺失的可选库（将尝试下载，失败不影响启动）");
                    // 可选库缺失不算完整性问题，但需要尝试下载
                }
                
                Debug.WriteLine($"✅ 所有必需库文件完整");
                onProgressUpdate?.Invoke("游戏完整性检查完成");
                return false; // 没有完整性问题
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("❌ 游戏完整性检查已取消");
                LastError = "检查已取消";
                return true; // 有问题（被取消）
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(LastError))
                {
                    LastError = ex.Message;
                }
                
                Debug.WriteLine($"❌ 检查游戏完整性失败: {ex.Message}");
                return true; // 有问题
            }
        }

        /// <summary>
        /// 启动游戏（异步）
        /// </summary>
        /// <param name="versionId">版本ID（文件夹名称）</param>
        /// <param name="account">游戏账号</param>
        /// <param name="config">启动器配置</param>
        /// <param name="onProgressUpdate">进度更新回调</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否启动成功</returns>
        public static async System.Threading.Tasks.Task<bool> LaunchGameAsync(string versionId, GameAccount account, LauncherConfig config, Action<string>? onProgressUpdate = null, System.Threading.CancellationToken cancellationToken = default)
        {
            LastError = string.Empty;
            
            try
            {
                Debug.WriteLine($"========== 开始启动游戏 ==========");
                Debug.WriteLine($"版本: {versionId}");
                Debug.WriteLine($"账号: {account.Username} ({account.Type})");
                Debug.WriteLine($"游戏目录: {config.GameDirectory}");
                Debug.WriteLine($"Java路径: {config.JavaPath}");

                // 0. 如果是微软账号且令牌过期，尝试刷新
                cancellationToken.ThrowIfCancellationRequested();
                if (account.Type == AccountType.Microsoft && account.IsTokenExpired())
                {
                    Debug.WriteLine("⚠️ 微软账号令牌已过期，尝试刷新...");
                    Console.WriteLine("⚠️ 微软账号令牌已过期，尝试刷新...");
                    onProgressUpdate?.Invoke("正在刷新微软账号令牌...");
                    
                    // 使用Task.Run在后台线程执行，并设置30秒超时
                    var refreshTask = System.Threading.Tasks.Task.Run(async () => 
                        await AccountService.Instance.RefreshMicrosoftAccountAsync(account.Id));
                    
                    var timeoutTask = System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(30));
                    var completedTask = await System.Threading.Tasks.Task.WhenAny(refreshTask, timeoutTask);
                    
                    if (completedTask == timeoutTask)
                    {
                        LastError = "微软账号令牌刷新超时（30秒）\n请检查网络连接或重新登录";
                        Console.WriteLine($"❌ {LastError}");
                        throw new Exception(LastError);
                    }
                    
                    var refreshSuccess = await refreshTask;
                    
                    if (!refreshSuccess)
                    {
                        LastError = "微软账号令牌已过期且刷新失败\n请重新登录微软账号";
                        Console.WriteLine($"❌ {LastError}");
                        throw new Exception(LastError);
                    }
                    
                    Debug.WriteLine("✅ 令牌刷新成功");
                    Console.WriteLine("✅ 令牌刷新成功");
                    onProgressUpdate?.Invoke("令牌刷新成功");
                }

                // 0.5. 确保旧版本所需的图标文件存在（1.5.x及更早版本）
                EnsureOldVersionIconsExist(config.GameDirectory);

                // 注意：1.5.2不需要现代资源系统（虚拟目录、resources目录等）
                // 它期望资源文件在JAR内部或游戏目录的根级别
                // 因此，对于1.5.2，跳过所有现代资源处理可以加快启动速度

                // 1. 验证Java路径
                onProgressUpdate?.Invoke("正在验证Java环境...");
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(config.JavaPath))
                {
                    LastError = $"Java可执行文件不存在\n路径: {config.JavaPath}";
                    throw new FileNotFoundException(LastError);
                }

                // 2. 读取版本JSON
                onProgressUpdate?.Invoke("正在读取游戏版本信息...");
                cancellationToken.ThrowIfCancellationRequested();
                var versionJsonPath = Path.Combine(config.GameDirectory, "versions", versionId, $"{versionId}.json");
                Debug.WriteLine($"版本JSON路径: {versionJsonPath}");
                
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

                // 处理inheritsFrom（合并父版本的libraries和其他信息）
                if (!string.IsNullOrEmpty(versionInfo.InheritsFrom))
                {
                    Debug.WriteLine($"检测到inheritsFrom: {versionInfo.InheritsFrom}，开始合并父版本信息");
                    versionInfo = MergeInheritedVersion(config.GameDirectory, versionId, versionInfo);
                }

                Debug.WriteLine($"MainClass: {versionInfo.MainClass}");
                Debug.WriteLine($"Libraries: {versionInfo.Libraries?.Length ?? 0} 个");

                if (string.IsNullOrEmpty(versionInfo.MainClass))
                {
                    LastError = "版本JSON中缺少MainClass字段";
                    throw new Exception(LastError);
                }

                // 判断是否为Mod加载器（Forge/Fabric等的JAR在libraries中）
                bool isModLoader = versionInfo.MainClass?.Contains("forge", StringComparison.OrdinalIgnoreCase) == true ||
                                   versionInfo.MainClass?.Contains("fabric", StringComparison.OrdinalIgnoreCase) == true ||
                                   versionInfo.MainClass?.Contains("quilt", StringComparison.OrdinalIgnoreCase) == true;

                // 3. 确保natives目录存在并解压natives库
                var versionDir = Path.Combine(config.GameDirectory, "versions", versionId);
                var nativesDir = Path.Combine(versionDir, "natives");
                if (!Directory.Exists(nativesDir))
                {
                    Debug.WriteLine($"创建natives目录: {nativesDir}");
                    Directory.CreateDirectory(nativesDir);
                }
                
                // 解压natives库文件（LWJGL等本地库）
                onProgressUpdate?.Invoke("正在解压本地库文件...");
                cancellationToken.ThrowIfCancellationRequested();
                ExtractNatives(config.GameDirectory, versionId, versionInfo, nativesDir);

                // 4. 验证客户端JAR存在（原版需要，Mod加载器不需要）
                onProgressUpdate?.Invoke("正在验证游戏客户端文件...");
                cancellationToken.ThrowIfCancellationRequested();
                var clientJar = Path.Combine(versionDir, $"{versionId}.jar");
                
                if (!isModLoader)
                {
                    // 原版Minecraft需要版本文件夹中的JAR
                    Debug.WriteLine($"客户端JAR: {clientJar}");
                    if (!File.Exists(clientJar))
                    {
                        LastError = $"客户端JAR文件不存在\n路径: {clientJar}";
                        throw new FileNotFoundException(LastError);
                    }
                }
                else
                {
                    Debug.WriteLine($"检测到Mod加载器版本，跳过版本文件夹JAR验证（JAR在libraries中）");
                }

                // 5. 检查并下载缺失的库文件
                onProgressUpdate?.Invoke("正在检查游戏依赖库...");
                cancellationToken.ThrowIfCancellationRequested();
                Debug.WriteLine($"检查库文件完整性...");
                var (missingRequired, missingOptional) = GetMissingLibraries(config.GameDirectory, versionId, versionInfo);
                
                MissingLibraries = missingRequired;
                MissingOptionalLibraries = missingOptional;
                
                if (missingRequired.Count > 0)
                {
                    LastError = $"检测到 {missingRequired.Count} 个缺失的必需库文件\n请在主页点击启动按钮，系统将自动下载";
                    Debug.WriteLine($"❌ 缺失 {missingRequired.Count} 个必需库文件，需要下载");
                    return false;
                }
                
                if (missingOptional.Count > 0)
                {
                    Debug.WriteLine($"⚠️ 检测到 {missingOptional.Count} 个缺失的可选库（不影响启动）");
                    // 可选库缺失不阻止游戏启动，只记录日志
                }
                
                Debug.WriteLine($"✅ 所有必需库文件完整");
                onProgressUpdate?.Invoke("游戏依赖检查完成");

                // 6. 构建启动参数
                onProgressUpdate?.Invoke("正在准备启动参数...");
                cancellationToken.ThrowIfCancellationRequested();
                var arguments = BuildLaunchArguments(versionId, account, config, versionInfo);
                Debug.WriteLine($"完整启动命令: \"{config.JavaPath}\" {arguments}");

                // 7. 启动游戏进程
                onProgressUpdate?.Invoke("正在启动游戏进程...");
                cancellationToken.ThrowIfCancellationRequested();
                var processInfo = new ProcessStartInfo
                {
                    FileName = config.JavaPath,
                    Arguments = arguments,
                    WorkingDirectory = config.GameDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                var process = new Process { StartInfo = processInfo };
                
                // 输出日志
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Debug.WriteLine($"[Minecraft] {e.Data}");
                };
                
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Debug.WriteLine($"[Minecraft Error] {e.Data}");
                };

                if (!process.Start())
                {
                    LastError = "无法启动Java进程，请检查Java路径是否正确";
                    throw new Exception(LastError);
                }
                
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                Debug.WriteLine($"✅ 游戏进程已启动 (PID: {process.Id})");
                onProgressUpdate?.Invoke("游戏进程已启动，检查运行状态...");
                
                // 等待一小段时间检查进程是否立即退出
                await System.Threading.Tasks.Task.Delay(500);
                
                if (process.HasExited)
                {
                    LastError = $"游戏进程启动后立即退出\n退出代码: {process.ExitCode}\n请检查Debug输出窗口查看详细错误日志";
                    Debug.WriteLine($"❌ 进程立即退出，退出代码: {process.ExitCode}");
                    return false;
                }
                
                Debug.WriteLine($"========== 启动完成 ==========");
                onProgressUpdate?.Invoke("启动完成");
                return true;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("❌ 游戏启动已取消");
                LastError = "启动已取消";
                return false;
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(LastError))
                {
                    LastError = ex.Message;
                }
                
                Debug.WriteLine($"❌ 启动游戏失败: {ex.Message}");
                Debug.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// 构建启动参数
        /// </summary>
        private static string BuildLaunchArguments(string versionId, GameAccount account, LauncherConfig config, VersionInfo versionInfo)
        {
            var args = new StringBuilder();

            // 1. 内存参数
            args.Append($"-Xms{config.MinMemory}M ");
            args.Append($"-Xmx{config.MaxMemory}M ");

            // 1.5. 对非常旧的Forge版本添加安全绕过参数（1.6.4, 1.7.10等）
            if (IsVeryOldForgeVersion(versionId))
            {
                args.Append("-Dfml.ignoreInvalidMinecraftCertificates=true ");
                args.Append("-Dfml.ignorePatchDiscrepancies=true ");
                Debug.WriteLine($"[GameLauncher] 检测到非常旧的Forge版本 ({versionId})，已添加安全绕过参数");
            }

            // 2. 自定义JVM参数
            if (!string.IsNullOrWhiteSpace(config.JvmArguments))
            {
                args.Append($"{config.JvmArguments} ");
            }

            // 2.5. version.json中定义的JVM参数（如Forge的额外JVM参数）
            if (versionInfo.Arguments?.Jvm != null)
            {
                foreach (var arg in versionInfo.Arguments.Jvm)
                {
                    if (arg is string str)
                    {
                        // 跳过标准JVM参数
                        if (ShouldSkipJvmArg(str))
                            continue;
                        
                            args.Append($"{str} ");
                    }
                    else if (arg is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var argStr = jsonElement.GetString();
                        if (!string.IsNullOrEmpty(argStr) && !ShouldSkipJvmArg(argStr))
                        {
                            args.Append($"{argStr} ");
                        }
                    }
                }
            }

            // 3. 游戏目录相关
            var gameDir = config.GameDirectory;
            var versionDir = Path.Combine(gameDir, "versions", versionId);
            var nativesDir = Path.Combine(versionDir, "natives");
            var librariesDir = Path.Combine(gameDir, "libraries");
            var assetsDir = Path.Combine(gameDir, "assets");

            // 4. 原生库路径
            args.Append($"-Djava.library.path=\"{nativesDir}\" ");

            // 5. 类路径
            args.Append("-cp \"");
            
            var classpathItems = new System.Collections.Generic.List<string>();
            
            // 添加客户端JAR到classpath（所有版本都需要）
                var versionJarPath = Path.Combine(versionDir, $"{versionId}.jar");
                if (File.Exists(versionJarPath))
                {
                    classpathItems.Add(versionJarPath);
                
                if (!string.IsNullOrEmpty(versionInfo.MinecraftArguments))
                {
                    Debug.WriteLine($"✅ 旧版本格式，已添加版本JAR到classpath: {versionId}.jar");
                }
            else if (!string.IsNullOrEmpty(versionInfo.InheritsFrom))
            {
                    Debug.WriteLine($"✅ Mod加载器版本，已添加Minecraft客户端到classpath: {versionId}.jar");
                }
                else
                {
                    Debug.WriteLine($"✅ 新版本格式，已添加客户端JAR到classpath: {versionId}.jar");
                }
            }
            else
            {
                Debug.WriteLine($"⚠️ 客户端JAR不存在: {versionJarPath}");
            }
            
            // 遍历所有库，构建classpath
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
                        else
                        {
                            // 仅当库不是可选的特殊库时才打印警告
                            bool isForgeSpecialLib = lib.Name != null && lib.Name.StartsWith("net.minecraftforge") && 
                                                     (lib.Name.Contains(":client") || lib.Name.Contains(":server"));
                            if (!isForgeSpecialLib)
                            {
                                Debug.WriteLine($"⚠️ 库文件不存在或路径无效: {libPath} (来自: {lib.Name})");
                            }
                        }
                    }
                }
            }
            
            // 使用系统路径分隔符连接
            args.Append(string.Join(Path.PathSeparator, classpathItems));
            args.Append("\" ");

            // 6. 主类
            args.Append($"{versionInfo.MainClass} ");

            // 7. 游戏参数
            var gameArgs = BuildGameArguments(versionId, account, config, versionInfo, gameDir, assetsDir);
            args.Append(gameArgs);

            return args.ToString();
        }

        /// <summary>
        /// 构建游戏参数
        /// </summary>
        private static string BuildGameArguments(string versionId, GameAccount account, LauncherConfig config, VersionInfo versionInfo, string gameDir, string assetsDir)
        {
            var args = new StringBuilder();

            // 资源索引
            var assetIndex = versionInfo.AssetIndex?.Id ?? versionInfo.Assets ?? "legacy";

            // 处理旧版本格式（1.12.2及之前使用minecraftArguments字符串）
            if (!string.IsNullOrEmpty(versionInfo.MinecraftArguments))
            {
                Debug.WriteLine($"使用旧版本参数格式: minecraftArguments");
                
                // 替换旧版本参数中的占位符
                // ⭐ 为极旧版本（1.6之前）使用简化的session token，避免认证问题
                var sessionToken = account.Type == AccountType.Microsoft ? (account.MinecraftAccessToken ?? "0") : "0";
                
                // 检测是否是极旧版本（使用legacy或pre-1.6资源索引）
                bool isVeryOldVersion = assetIndex == "legacy" || assetIndex == "pre-1.6";
                
                if (isVeryOldVersion)
                {
                    // 1.5.2等极旧版本使用简化的token，避免JWT token导致的问题
                    sessionToken = "legacy";
                    Debug.WriteLine($"[旧版本] 检测到极旧版本（{assetIndex}），使用简化认证模式");
                }
                
                // 折磨我！    1.5.2的${game_assets}应该指向gameDir本身，而不是gameDir/assets！
                var gameAssetsPath = isVeryOldVersion ? gameDir : assetsDir;
                
                var minecraftArgs = versionInfo.MinecraftArguments
                    .Replace("${auth_player_name}", account.Username)
                    .Replace("${version_name}", versionId)
                    .Replace("${game_directory}", $"\"{gameDir}\"")
                    .Replace("${assets_root}", $"\"{assetsDir}\"")
                    .Replace("${assets_index_name}", assetIndex)
                    .Replace("${auth_uuid}", isVeryOldVersion ? "00000000-0000-0000-0000-000000000000" : (account.Type == AccountType.Microsoft ? (account.MinecraftUUID ?? account.UUID) : account.UUID))
                    .Replace("${auth_access_token}", sessionToken)
                    .Replace("${auth_session}", sessionToken) // ⭐ 1.5.2等旧版本使用 auth_session
                    .Replace("${user_properties}", "{}") // 用户属性，离线模式使用空对象
                    .Replace("${user_type}", isVeryOldVersion ? "legacy" : (account.Type == AccountType.Microsoft ? "msa" : "legacy"))
                    .Replace("${version_type}", "ObsMCLauncher")
                    .Replace("${game_assets}", $"\"{gameAssetsPath}\""); // ⭐ 1.5.2使用gameDir，现代版本使用assetsDir
                
                args.Append(minecraftArgs);
                return args.ToString();
            }

            // 新版本格式（1.13+使用arguments.game数组）
            // 1. 首先添加version.json中定义的额外游戏参数（如Forge的--launchTarget参数）
            if (versionInfo.Arguments?.Game != null)
            {
                foreach (var arg in versionInfo.Arguments.Game)
                {
                    if (arg is string str)
                    {
                        // 跳过标准游戏参数（我们自己会添加）
                        if (ShouldSkipGameArg(str))
                            continue;
                        
                            args.Append($"{str} ");
                    }
                    else if (arg is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var argStr = jsonElement.GetString();
                        if (!string.IsNullOrEmpty(argStr) && !ShouldSkipGameArg(argStr))
                        {
                            args.Append($"{argStr} ");
                        }
                    }
                }
            }

            // 2. 标准参数
            args.Append($"--username {account.Username} ");
            args.Append($"--version {versionId} ");
            args.Append($"--gameDir \"{gameDir}\" ");
            args.Append($"--assetsDir \"{assetsDir}\" ");
            args.Append($"--assetIndex {assetIndex} ");
            
            // 根据账号类型使用不同的 UUID 和 AccessToken
            if (account.Type == AccountType.Microsoft)
            {
                // 微软账号使用真实的 Minecraft UUID 和 AccessToken
                var uuid = account.MinecraftUUID ?? account.UUID;
                var accessToken = account.MinecraftAccessToken ?? "0";
                args.Append($"--uuid {uuid} ");
                args.Append($"--accessToken {accessToken} ");
                args.Append($"--userType msa ");
            }
            else
            {
                // 离线账号使用随机 UUID 和虚拟 AccessToken
                args.Append($"--uuid {account.UUID} ");
                args.Append($"--accessToken 0 ");
                args.Append($"--userType legacy ");
            }
            
            args.Append($"--versionType \"ObsMCLauncher\" ");

            return args.ToString();
        }

        /// <summary>
        /// 检测是否为非常旧的Forge版本（需要安全绕过参数）
        /// </summary>
        private static bool IsVeryOldForgeVersion(string versionId)
        {
            // 检查是否包含forge标识
            if (!versionId.Contains("forge", StringComparison.OrdinalIgnoreCase))
                return false;

            // 检查是否为非常旧的版本（1.6.x, 1.7.x）
            // 这些版本的Forge有严格的JAR完整性检查
            if (versionId.Contains("1.6.") || versionId.Contains("1.7."))
                return true;

            return false;
        }

        /// <summary>
        /// 判断是否应该跳过JSON中的JVM参数（避免重复或冲突）
        /// </summary>
        private static bool ShouldSkipJvmArg(string arg)
        {
            if (string.IsNullOrEmpty(arg))
                return true;

            // 跳过包含变量占位符的参数（如${natives_directory}）
            if (arg.Contains("${"))
                return true;

            // 跳过标准JVM参数
            if (arg.StartsWith("-Djava.library.path"))
                return true;
            if (arg.StartsWith("-Dminecraft.launcher.brand"))
                return true;
            if (arg.StartsWith("-Dminecraft.launcher.version"))
                return true;
            if (arg.Equals("-cp") || arg.Equals("--class-path"))
                return true;

            return false;
        }

        /// <summary>
        /// 判断是否应该跳过JSON中的游戏参数（避免重复或冲突）
        /// </summary>
        private static bool ShouldSkipGameArg(string arg)
        {
            if (string.IsNullOrEmpty(arg))
                return true;

            // 跳过包含变量占位符的参数（如${auth_player_name}）
            if (arg.Contains("${"))
                return true;

            // 跳过标准游戏参数
            var standardArgs = new[] { 
                "--username", "--version", "--gameDir", "--assetsDir", 
                "--assetIndex", "--uuid", "--accessToken", "--userType", 
                "--versionType", "--width", "--height" 
            };
            
            if (standardArgs.Contains(arg))
                return true;

            return false;
        }

        /// <summary>
        /// 检查库是否应该被加载（根据操作系统规则）
        /// </summary>
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

        /// <summary>
        /// 获取缺失的库文件列表（区分必需库和可选库）
        /// </summary>
        /// <returns>(缺失的必需库列表, 缺失的可选库列表)</returns>
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
                        Debug.WriteLine($"   ⚠️ 跳过Forge特殊库检查: {lib.Name}");
                        continue;
                    }
                    
                    // 1. 检查普通库文件（artifact）
                    bool isOptional = lib.Downloads?.Artifact == null;
                    var libPath = GetLibraryPath(librariesDir, lib);
                    
                    if (!string.IsNullOrEmpty(libPath))
                    {
                        bool isMissing = false;
                        
                        // 检查文件是否存在
                        if (!File.Exists(libPath))
                        {
                            isMissing = true;
                            if (isOptional)
                            {
                                Debug.WriteLine($"   ⚠️ 可选库不存在: {lib.Name} (将尝试下载)");
                                Console.WriteLine($"   ⚠️ 可选库不存在: {lib.Name}");
                            }
                            else
                            {
                                Debug.WriteLine($"   ❌ 必需库不存在: {lib.Name}");
                                Console.WriteLine($"   ❌ 必需库不存在: {lib.Name}");
                            }
                        }
                        // 如果文件存在且是必需库，验证文件大小
                        else if (!isOptional && lib.Downloads?.Artifact?.Size > 0)
                        {
                            var fileInfo = new FileInfo(libPath);
                            if (fileInfo.Length != lib.Downloads.Artifact.Size)
                            {
                                isMissing = true;
                                Debug.WriteLine($"   ❌ 文件大小不匹配: {lib.Name}");
                                Debug.WriteLine($"      期望大小: {lib.Downloads.Artifact.Size} 字节");
                                Debug.WriteLine($"      实际大小: {fileInfo.Length} 字节");
                                Console.WriteLine($"   ❌ 文件大小不匹配: {lib.Name} (期望 {lib.Downloads.Artifact.Size}, 实际 {fileInfo.Length})");
                            }
                        }
                        
                        if (isMissing)
                        {
                            if (isOptional)
                            {
                                missingOptional.Add(lib.Name ?? "Unknown");
                            }
                            else
                            {
                                missingRequired.Add(lib.Name ?? "Unknown");
                            }
                            Debug.WriteLine($"      期望路径: {libPath}");
                        }
                    }
                    
                    // 2. 检查natives文件（classifiers）
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
                                    Debug.WriteLine($"   ⚠️ Natives库不存在: {lib.Name} (natives) - 将尝试下载");
                                    Console.WriteLine($"   ⚠️ Natives库不存在: {lib.Name} (natives)");
                                    
                                    // Natives始终作为可选库处理（因为它们没有artifact）
                                    if (!missingOptional.Contains(lib.Name ?? "Unknown"))
                                    {
                                        missingOptional.Add(lib.Name ?? "Unknown");
                                    }
                                    Debug.WriteLine($"      期望路径: {nativesPath}");
                                }
                            }
                        }
                    }
                }
            }
            
            return (missingRequired, missingOptional);
        }

        /// <summary>
        /// 获取库文件路径
        /// </summary>
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

            // 如果没有 downloads 字段，尝试使用 Maven 标准路径（适用于旧版 Forge）
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
                    // 解析失败，返回空字符串
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 获取操作系统名称
        /// </summary>
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

        // 版本信息模型（用于解析JSON）
        private class VersionInfo
        {
            public string? MainClass { get; set; }
            public string? Assets { get; set; }
            public AssetIndexInfo? AssetIndex { get; set; }
            public Library[]? Libraries { get; set; }
            public GameArguments? Arguments { get; set; }
            public string? MinecraftArguments { get; set; }  // 旧版本格式（1.12.2及之前）
            public string? InheritsFrom { get; set; }
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
        
        /// <summary>
        /// 解压natives库文件
        /// </summary>
        private static void ExtractNatives(string gameDir, string versionId, VersionInfo versionInfo, string nativesDir)
        {
            try
            {
                Debug.WriteLine($"========== 开始解压Natives库 ==========");
                Debug.WriteLine($"Natives目录: {nativesDir}");
                
                // 清理并重新创建natives目录
                if (Directory.Exists(nativesDir))
                {
                    Debug.WriteLine($"清理旧的natives目录...");
                    Directory.Delete(nativesDir, true);
                }
                Directory.CreateDirectory(nativesDir);
                Debug.WriteLine($"✅ Natives目录已创建");
                
                if (versionInfo.Libraries == null)
                {
                    Debug.WriteLine("⚠️ 没有库文件");
                    return;
                }
                
                var librariesDir = Path.Combine(gameDir, "libraries");
                var osName = GetOSName();
                int extractedFileCount = 0;
                int extractedJarCount = 0;
                
                Debug.WriteLine($"操作系统: {osName}");
                Debug.WriteLine($"开始扫描natives库...");
                
                foreach (var lib in versionInfo.Libraries)
                {
                    // 检查库是否适用于当前操作系统
                    if (!IsLibraryAllowed(lib))
                        continue;
                    
                    string? nativesJarPath = null;
                    
                    // 方式1：检查是否有natives字段（旧版本格式，1.18及之前）
                    if (lib.Natives != null && lib.Downloads?.Classifiers != null)
                    {
                        // 获取当前操作系统对应的natives键
                        if (lib.Natives.TryGetValue(osName, out var nativesKey) && !string.IsNullOrEmpty(nativesKey))
                        {
                            // 获取natives文件路径
                            if (lib.Downloads.Classifiers.TryGetValue(nativesKey, out var nativeArtifact) && 
                                !string.IsNullOrEmpty(nativeArtifact.Path))
                            {
                                nativesJarPath = Path.Combine(librariesDir, nativeArtifact.Path.Replace("/", "\\"));
                                Debug.WriteLine($"[方式1] 找到natives库: {lib.Name}");
                            }
                        }
                    }
                    // 方式2：检查库名中是否包含natives-windows（新版本格式，1.19+）
                    else if (lib.Name != null && lib.Name.Contains("natives-windows"))
                    {
                        var libPath = GetLibraryPath(librariesDir, lib);
                        if (!string.IsNullOrEmpty(libPath))
                        {
                            nativesJarPath = libPath;
                            Debug.WriteLine($"[方式2] 找到natives库: {lib.Name}");
                        }
                    }
                    
                    // 如果没有找到natives文件，跳过
                    if (string.IsNullOrEmpty(nativesJarPath))
                        continue;
                    
                    // 检查natives JAR文件是否存在
                    if (!File.Exists(nativesJarPath))
                    {
                        Debug.WriteLine($"   ❌ Natives JAR不存在: {nativesJarPath}");
                        continue;
                    }
                    
                    Debug.WriteLine($"   开始解压: {Path.GetFileName(nativesJarPath)}");
                    
                    try
                    {
                        // 解压jar文件
                        using var archive = System.IO.Compression.ZipFile.OpenRead(nativesJarPath);
                        int fileCountInJar = 0;
                        
                        foreach (var entry in archive.Entries)
                        {
                            // 只解压.dll、.so、.dylib等本地库文件
                            var ext = System.IO.Path.GetExtension(entry.Name).ToLower();
                            if (ext == ".dll" || ext == ".so" || ext == ".dylib")
                            {
                                var destPath = Path.Combine(nativesDir, entry.Name);
                                
                                entry.ExtractToFile(destPath, overwrite: true);
                                extractedFileCount++;
                                fileCountInJar++;
                                Debug.WriteLine($"      ✅ {entry.Name} ({entry.Length} bytes)");
                            }
                        }
                        
                        if (fileCountInJar > 0)
                        {
                            extractedJarCount++;
                            Debug.WriteLine($"   ✅ 完成，解压了 {fileCountInJar} 个文件");
                        }
                        else
                        {
                            Debug.WriteLine($"   ⚠️ JAR中没有找到natives文件");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"   ❌ 解压失败: {ex.Message}");
                    }
                }
                
                Debug.WriteLine($"========== Natives解压统计 ==========");
                Debug.WriteLine($"解压的JAR数量: {extractedJarCount}");
                Debug.WriteLine($"解压的文件数量: {extractedFileCount}");
                
                // 列出natives目录中的所有文件
                if (Directory.Exists(nativesDir))
                {
                    var files = Directory.GetFiles(nativesDir, "*.*", SearchOption.AllDirectories);
                    Debug.WriteLine($"========== Natives目录文件列表 ==========");
                    Debug.WriteLine($"总共 {files.Length} 个文件:");
                    foreach (var file in files)
                    {
                        var fileInfo = new FileInfo(file);
                        Debug.WriteLine($"  - {Path.GetFileName(file)} ({fileInfo.Length} bytes)");
                    }
                }
                
                if (extractedFileCount == 0)
                {
                    Debug.WriteLine("❌ 没有解压任何natives文件，游戏将无法启动！");
                }
                else
                {
                    Debug.WriteLine($"✅ Natives解压完成");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 解压natives过程出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取库文件的唯一标识（groupId:artifactId[:classifier]，忽略版本号）
        /// </summary>
        /// <param name="libraryName">库名称，格式如 "org.ow2.asm:asm:9.8" 或 "org.lwjgl:lwjgl:3.3.3:natives-windows"</param>
        /// <returns>库的唯一标识，如 "org.ow2.asm:asm" 或 "org.lwjgl:lwjgl:natives-windows"</returns>
        private static string GetLibraryKey(string? libraryName)
        {
            if (string.IsNullOrEmpty(libraryName))
                return string.Empty;
            
            // 库名格式：groupId:artifactId:version[:classifier][@extension]
            // 例如：org.ow2.asm:asm:9.8 或 org.lwjgl:lwjgl:3.3.3:natives-windows
            var parts = libraryName.Split(':');
            
            if (parts.Length >= 4)
            {
                // 有classifier（如natives-windows），返回 groupId:artifactId:classifier
                // 这样不同平台的natives库不会被误判为冲突
                return $"{parts[0]}:{parts[1]}:{parts[3]}";
            }
            else if (parts.Length >= 2)
            {
                // 没有classifier，返回 groupId:artifactId（忽略版本号）
                return $"{parts[0]}:{parts[1]}";
            }
            
            return libraryName;
        }

        /// <summary>
        /// 合并继承的版本信息（处理inheritsFrom字段）
        /// </summary>
        private static VersionInfo MergeInheritedVersion(string gameDirectory, string childVersionId, VersionInfo childVersion)
        {
            try
            {
                var parentVersionId = childVersion.InheritsFrom;
                if (string.IsNullOrEmpty(parentVersionId))
                    return childVersion;

                // 尝试读取父版本JSON
                // 1. 优先从标准位置读取：versions/1.21.10/1.21.10.json
                var parentJsonPath = Path.Combine(gameDirectory, "versions", parentVersionId, $"{parentVersionId}.json");
                
                // 2. 如果不存在，尝试从子版本目录读取：versions/Minecraft-1.21.10-fabric-xxx/1.21.10.json
                // （Fabric安装时会将原版JSON保存在这里）
                if (!File.Exists(parentJsonPath))
                {
                    var childVersionDir = Path.Combine(gameDirectory, "versions", childVersionId);
                    var parentJsonInChildDir = Path.Combine(childVersionDir, $"{parentVersionId}.json");
                    
                    if (File.Exists(parentJsonInChildDir))
                    {
                        parentJsonPath = parentJsonInChildDir;
                        Debug.WriteLine($"✅ 从子版本目录找到父版本JSON: {parentJsonInChildDir}");
                    }
                    else
                {
                    Debug.WriteLine($"⚠️ 父版本JSON不存在: {parentJsonPath}，跳过合并");
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
                    Debug.WriteLine($"⚠️ 无法解析父版本JSON: {parentVersionId}");
                    return childVersion;
                }

                // 递归处理父版本的inheritsFrom
                if (!string.IsNullOrEmpty(parentVersion.InheritsFrom))
                {
                    parentVersion = MergeInheritedVersion(gameDirectory, parentVersionId, parentVersion);
                }

                // 合并libraries（子版本的libraries优先，避免版本冲突）
                var mergedLibraries = new System.Collections.Generic.List<Library>();
                var libraryKeys = new System.Collections.Generic.HashSet<string>();
                
                // 先添加子版本（Fabric/Forge）的库，并记录库的标识
                if (childVersion.Libraries != null)
                {
                    foreach (var library in childVersion.Libraries)
                    {
                        mergedLibraries.Add(library);
                        var libKey = GetLibraryKey(library.Name);
                        libraryKeys.Add(libKey);
                    }
                }
                
                // 再添加父版本的库，跳过已存在的（避免冲突，如ASM 9.6和9.8！！！！！！！！！！！！！牛魔的）
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
                        else
                        {
                            Debug.WriteLine($"⚠️ 跳过冲突的父版本库: {library.Name} (已有子版本库)");
                        }
                    }
                }
                
                childVersion.Libraries = mergedLibraries.ToArray();

                // 合并其他缺失的字段
                if (childVersion.AssetIndex == null && parentVersion.AssetIndex != null)
                    childVersion.AssetIndex = parentVersion.AssetIndex;
                if (string.IsNullOrEmpty(childVersion.Assets) && !string.IsNullOrEmpty(parentVersion.Assets))
                    childVersion.Assets = parentVersion.Assets;
                if (childVersion.Arguments == null && parentVersion.Arguments != null)
                    childVersion.Arguments = parentVersion.Arguments;
                // 合并旧版本参数格式（如果子版本没有minecraftArguments，使用父版本的）
                if (string.IsNullOrEmpty(childVersion.MinecraftArguments) && !string.IsNullOrEmpty(parentVersion.MinecraftArguments))
                    childVersion.MinecraftArguments = parentVersion.MinecraftArguments;

                Debug.WriteLine($"✅ 已合并父版本 {parentVersionId}，总libraries: {childVersion.Libraries?.Length ?? 0}");
                return childVersion;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 合并继承版本失败: {ex.Message}");
                return childVersion;
            }
        }

        /// <summary>
        /// 确保旧版本Minecraft所需的图标文件存在（1.5.x及更早版本需要）
        /// </summary>
        private static void EnsureOldVersionIconsExist(string gameDirectory)
        {
            try
            {
                //1.5.2期望图标在 .minecraft/icons/ 而不是 .minecraft/assets/icons/
                var iconsDir = Path.Combine(gameDirectory, "icons");
                
                // 检查是否已存在图标
                var icon16Path = Path.Combine(iconsDir, "icon_16x16.png");
                var icon32Path = Path.Combine(iconsDir, "icon_32x32.png");
                
                if (File.Exists(icon16Path) && File.Exists(icon32Path))
                {
                    return; // 图标已存在，无需创建
                }
                
                Debug.WriteLine("[图标] 为旧版本创建默认窗口图标...");
                
                // 创建目录
                Directory.CreateDirectory(iconsDir);
                
                // 创建16x16透明PNG（最小有效PNG）
                if (!File.Exists(icon16Path))
                {
                    CreateMinimalPng(icon16Path, 16);
                    Debug.WriteLine($"[图标] ✅ 已创建 icon_16x16.png");
                }
                
                // 创建32x32透明PNG
                if (!File.Exists(icon32Path))
                {
                    CreateMinimalPng(icon32Path, 32);
                    Debug.WriteLine($"[图标] ✅ 已创建 icon_32x32.png");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[图标] ⚠️ 创建图标文件失败（不影响游戏启动）: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建一个最小的透明PNG文件
        /// </summary>
        private static void CreateMinimalPng(string filePath, int size)
        {
            // 创建一个透明的位图
            using (var bitmap = new System.Drawing.Bitmap(size, size))
            {
                // 将整个位图设置为透明
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.Clear(System.Drawing.Color.Transparent);
                    
                    // 可选：在中心绘制一个简单的Minecraft草方块颜色
                    var grassGreen = System.Drawing.Color.FromArgb(127, 204, 25);
                    var dirtBrown = System.Drawing.Color.FromArgb(150, 75, 0);
                    
                    var halfSize = size / 2;
                    graphics.FillRectangle(new System.Drawing.SolidBrush(grassGreen), 0, 0, size, halfSize);
                    graphics.FillRectangle(new System.Drawing.SolidBrush(dirtBrown), 0, halfSize, size, halfSize);
                }
                
                // 保存为PNG
                bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        /// <summary>
        /// 确保旧版本Minecraft的Legacy虚拟资源目录存在（1.5.x及更早版本需要）
        /// </summary>
        private static async System.Threading.Tasks.Task EnsureLegacyAssetsVirtualDirExist(
            string gameDirectory, 
            string versionId, 
            System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                // 读取版本JSON获取AssetIndex信息
                var versionJsonPath = Path.Combine(gameDirectory, "versions", versionId, $"{versionId}.json");
                if (!File.Exists(versionJsonPath))
                {
                    return; // 版本JSON不存在，跳过
                }

                var versionJson = await File.ReadAllTextAsync(versionJsonPath, cancellationToken);
                var versionInfo = System.Text.Json.JsonSerializer.Deserialize<VersionInfo>(versionJson);

                if (versionInfo?.AssetIndex == null)
                {
                    return; // 没有AssetIndex信息，跳过
                }

                var assetIndexId = versionInfo.AssetIndex.Id ?? versionInfo.Assets ?? "legacy";

                // 只处理legacy和pre-1.6版本
                if (assetIndexId != "legacy" && assetIndexId != "pre-1.6")
                {
                    return; // 不是旧版本，跳过
                }

                Debug.WriteLine($"[Legacy Assets] 检测到旧版本资源索引: {assetIndexId}");

                // 检查虚拟目录是否已存在且有内容
                var virtualDir = Path.Combine(gameDirectory, "assets", "virtual", "legacy");
                if (Directory.Exists(virtualDir) && Directory.GetFiles(virtualDir, "*", SearchOption.AllDirectories).Length > 0)
                {
                    Debug.WriteLine($"[Legacy Assets] 虚拟目录已存在，跳过创建");
                    return; // 虚拟目录已存在，跳过
                }

                Debug.WriteLine($"[Legacy Assets] 虚拟目录不存在或为空，调用AssetsDownloadService创建...");

                // 调用AssetsDownloadService创建虚拟目录
                await AssetsDownloadService.DownloadAndCheckAssetsAsync(
                    gameDirectory,
                    versionId,
                    (progress, total, message, speed) =>
                    {
                        if (progress % 10 == 0 || progress == 100)
                        {
                            Debug.WriteLine($"[Legacy Assets] 进度: {progress}% - {message}");
                        }
                    },
                    cancellationToken
                );

                Debug.WriteLine($"[Legacy Assets] ✅ 虚拟目录检查/创建完成");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[Legacy Assets] 虚拟目录创建被取消");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Legacy Assets] ⚠️ 创建虚拟目录失败（不影响现代版本）: {ex.Message}");
            }
        }

        /// <summary>
        /// 为极旧版本（1.5.2等）创建传统resources目录结构
        /// </summary>
        private static async System.Threading.Tasks.Task EnsureLegacyResourcesDirectory(
            string gameDirectory, 
            string versionId, 
            System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                // 读取版本JSON获取AssetIndex信息
                var versionJsonPath = Path.Combine(gameDirectory, "versions", versionId, $"{versionId}.json");
                if (!File.Exists(versionJsonPath))
                {
                    return;
                }

                var versionJson = await File.ReadAllTextAsync(versionJsonPath, cancellationToken);
                var versionInfo = System.Text.Json.JsonSerializer.Deserialize<VersionInfo>(versionJson);

                if (versionInfo?.AssetIndex == null)
                {
                    return;
                }

                var assetIndexId = versionInfo.AssetIndex.Id ?? versionInfo.Assets ?? "legacy";

                // 只为1.5.2及更早版本创建resources目录
                if (assetIndexId != "pre-1.6")
                {
                    return;
                }

                Debug.WriteLine($"[Legacy Resources] 检测到1.5.2或更早版本，创建传统resources目录...");

                var resourcesDir = Path.Combine(gameDirectory, "resources");
                var virtualDir = Path.Combine(gameDirectory, "assets", "virtual", "legacy");

                // 如果resources目录已存在且有内容，跳过
                if (Directory.Exists(resourcesDir) && Directory.GetFiles(resourcesDir, "*", SearchOption.AllDirectories).Length > 100)
                {
                    Debug.WriteLine($"[Legacy Resources] resources目录已存在，跳过创建");
                    return;
                }

                // 创建resources目录结构
                Directory.CreateDirectory(resourcesDir);
                Debug.WriteLine($"[Legacy Resources] 创建目录: {resourcesDir}");

                // 创建子目录结构（1.5.2期望的结构）
                var subDirs = new[] { "newsound", "music", "sound", "sound3", "streaming", "title", "mob", "random", "step" };
                foreach (var subDir in subDirs)
                {
                    Directory.CreateDirectory(Path.Combine(resourcesDir, subDir));
                }

                // 如果虚拟目录存在，从中复制关键文件
                if (Directory.Exists(virtualDir))
                {
                    await CopyLegacyResourcesFromVirtualDir(virtualDir, resourcesDir, cancellationToken);
                }

                Debug.WriteLine($"[Legacy Resources] ✅ 传统resources目录创建完成");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[Legacy Resources] 创建被取消");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Legacy Resources] ⚠️ 创建传统resources目录失败（尝试继续）: {ex.Message}");
            }
        }

        /// <summary>
        /// 从虚拟目录复制资源文件到传统resources目录
        /// </summary>
        private static async System.Threading.Tasks.Task CopyLegacyResourcesFromVirtualDir(
            string virtualDir, 
            string resourcesDir, 
            System.Threading.CancellationToken cancellationToken)
        {
            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    int copied = 0;
                    var allFiles = Directory.GetFiles(virtualDir, "*.*", SearchOption.AllDirectories);

                    Debug.WriteLine($"[Legacy Resources] 虚拟目录中有 {allFiles.Length} 个文件");

                    foreach (var sourceFile in allFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var relativePath = Path.GetRelativePath(virtualDir, sourceFile);
                        var targetFile = Path.Combine(resourcesDir, relativePath);

                        // 确保目标目录存在
                        var targetDir = Path.GetDirectoryName(targetFile);
                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }

                        // 复制文件
                        if (!File.Exists(targetFile))
                        {
                            File.Copy(sourceFile, targetFile, false);
                            copied++;

                            if (copied % 50 == 0)
                            {
                                Debug.WriteLine($"[Legacy Resources] 已复制 {copied} 个文件...");
                            }
                        }
                    }

                    Debug.WriteLine($"[Legacy Resources] ✅ 从虚拟目录复制了 {copied} 个文件");
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine($"[Legacy Resources] 复制被取消");
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Legacy Resources] ⚠️ 复制文件失败: {ex.Message}");
                }
            }, cancellationToken);
        }
    }
}

