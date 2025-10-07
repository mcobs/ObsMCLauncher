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
                    versionInfo = MergeInheritedVersion(config.GameDirectory, versionInfo);
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
                    versionInfo = MergeInheritedVersion(config.GameDirectory, versionInfo);
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
                        // 跳过需要动态替换的参数
                        if (!str.StartsWith("${"))
                        {
                            args.Append($"{str} ");
                        }
                    }
                    else if (arg is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var argStr = jsonElement.GetString();
                        if (!string.IsNullOrEmpty(argStr) && !argStr.StartsWith("${"))
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

            // 1. 首先添加version.json中定义的额外游戏参数（如Forge的--launchTarget参数）
            if (versionInfo.Arguments?.Game != null)
            {
                foreach (var arg in versionInfo.Arguments.Game)
                {
                    if (arg is string str)
                    {
                        // 跳过需要动态替换的参数（这些参数我们会在后面手动添加）
                        if (!str.StartsWith("${"))
                        {
                            args.Append($"{str} ");
                        }
                    }
                    else if (arg is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var argStr = jsonElement.GetString();
                        if (!string.IsNullOrEmpty(argStr) && !argStr.StartsWith("${"))
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
                Debug.WriteLine($"开始检查natives库文件: {nativesDir}");
                
                if (versionInfo.Libraries == null)
                {
                    Debug.WriteLine("没有库文件");
                    return;
                }
                
                var librariesDir = Path.Combine(gameDir, "libraries");
                var osName = GetOSName();
                int extractedCount = 0;
#pragma warning disable CS0219
                int skippedCount = 0;
#pragma warning restore CS0219
                
                foreach (var lib in versionInfo.Libraries)
                {
                    // 检查库是否适用于当前操作系统
                    if (!IsLibraryAllowed(lib))
                        continue;
                    
                    // 检查是否有natives字段（1.19+版本没有natives字段，直接跳过）
                    if (lib.Natives == null || lib.Downloads?.Classifiers == null)
                    {
                        skippedCount++;
                        continue;
                    }
                    
                    // 获取当前操作系统对应的natives键
                    if (!lib.Natives.TryGetValue(osName, out var nativesKey) || string.IsNullOrEmpty(nativesKey))
                        continue;
                    
                    // 获取natives文件路径
                    if (!lib.Downloads.Classifiers.TryGetValue(nativesKey, out var nativeArtifact) || 
                        string.IsNullOrEmpty(nativeArtifact.Path))
                        continue;
                    
                    var nativesJarPath = Path.Combine(librariesDir, nativeArtifact.Path.Replace("/", "\\"));
                    
                    if (!File.Exists(nativesJarPath))
                    {
                        Debug.WriteLine($"   ⚠️ Natives文件不存在: {lib.Name} -> {nativesJarPath}");
                        continue;
                    }
                    
                    try
                    {
                        // 解压jar文件
                        using var archive = System.IO.Compression.ZipFile.OpenRead(nativesJarPath);
                        foreach (var entry in archive.Entries)
                        {
                            // 只解压.dll、.so、.dylib等本地库文件
                            var ext = System.IO.Path.GetExtension(entry.Name).ToLower();
                            if (ext == ".dll" || ext == ".so" || ext == ".dylib")
                            {
                                var destPath = Path.Combine(nativesDir, entry.Name);
                                
                                // 如果文件已存在且大小相同，跳过
                                if (File.Exists(destPath))
                                {
                                    var existingFile = new FileInfo(destPath);
                                    if (existingFile.Length == entry.Length)
                                        continue;
                                }
                                
                                entry.ExtractToFile(destPath, overwrite: true);
                                extractedCount++;
                            }
                        }
                        
                        Debug.WriteLine($"   ✅ 已解压natives: {lib.Name}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"   ❌ 解压natives失败: {lib.Name} - {ex.Message}");
                    }
                }
                
                // 输出解压结果
                if (extractedCount > 0)
                {
                    Debug.WriteLine($"✅ Natives解压完成，共解压 {extractedCount} 个文件");
                }
                else if (skippedCount > 0)
                {
                    Debug.WriteLine($"ℹ️ 当前版本无需解压natives（可能是1.19+版本），已跳过 {skippedCount} 个库");
                }
                else
                {
                    Debug.WriteLine("ℹ️ 没有需要处理的natives库");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 解压natives过程出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 合并继承的版本信息（处理inheritsFrom字段）
        /// </summary>
        private static VersionInfo MergeInheritedVersion(string gameDirectory, VersionInfo childVersion)
        {
            try
            {
                var parentVersionId = childVersion.InheritsFrom;
                if (string.IsNullOrEmpty(parentVersionId))
                    return childVersion;

                // 读取父版本JSON
                var parentJsonPath = Path.Combine(gameDirectory, "versions", parentVersionId, $"{parentVersionId}.json");
                if (!File.Exists(parentJsonPath))
                {
                    Debug.WriteLine($"⚠️ 父版本JSON不存在: {parentJsonPath}，跳过合并");
                    return childVersion;
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
                    parentVersion = MergeInheritedVersion(gameDirectory, parentVersion);
                }

                // 合并libraries（子版本的libraries优先，然后添加父版本的）
                var mergedLibraries = new System.Collections.Generic.List<Library>();
                if (childVersion.Libraries != null)
                    mergedLibraries.AddRange(childVersion.Libraries);
                if (parentVersion.Libraries != null)
                    mergedLibraries.AddRange(parentVersion.Libraries);
                childVersion.Libraries = mergedLibraries.ToArray();

                // 合并其他缺失的字段
                if (childVersion.AssetIndex == null && parentVersion.AssetIndex != null)
                    childVersion.AssetIndex = parentVersion.AssetIndex;
                if (string.IsNullOrEmpty(childVersion.Assets) && !string.IsNullOrEmpty(parentVersion.Assets))
                    childVersion.Assets = parentVersion.Assets;
                if (childVersion.Arguments == null && parentVersion.Arguments != null)
                    childVersion.Arguments = parentVersion.Arguments;

                Debug.WriteLine($"✅ 已合并父版本 {parentVersionId}，总libraries: {childVersion.Libraries?.Length ?? 0}");
                return childVersion;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 合并继承版本失败: {ex.Message}");
                return childVersion;
            }
        }
    }
}

