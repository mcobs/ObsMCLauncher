using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        /// 缺失的库文件列表（用于UI显示）
        /// </summary>
        public static List<string> MissingLibraries { get; private set; } = new List<string>();

        /// <summary>
        /// 检查游戏完整性（不启动游戏）
        /// </summary>
        /// <param name="versionId">版本ID</param>
        /// <param name="config">启动器配置</param>
        /// <param name="onProgressUpdate">进度更新回调</param>
        /// <returns>是否存在完整性问题（true表示有缺失文件）</returns>
        public static async System.Threading.Tasks.Task<bool> CheckGameIntegrityAsync(string versionId, LauncherConfig config, Action<string>? onProgressUpdate = null)
        {
            LastError = string.Empty;
            MissingLibraries.Clear();
            
            try
            {
                Debug.WriteLine($"========== 检查游戏完整性 ==========");
                Debug.WriteLine($"版本: {versionId}");
                Debug.WriteLine($"游戏目录: {config.GameDirectory}");

                // 1. 验证Java路径
                onProgressUpdate?.Invoke("正在验证Java环境...");
                if (!File.Exists(config.JavaPath))
                {
                    LastError = $"Java路径不存在: {config.JavaPath}";
                    Debug.WriteLine($"❌ {LastError}");
                    return false;
                }

                // 2. 读取版本JSON
                onProgressUpdate?.Invoke("正在读取版本信息...");
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

                Debug.WriteLine($"版本JSON路径: {versionJsonPath}");
                Debug.WriteLine($"MainClass: {versionInfo.MainClass}");
                Debug.WriteLine($"Libraries: {versionInfo.Libraries?.Length ?? 0} 个");

                // 3. 检查客户端JAR文件
                onProgressUpdate?.Invoke("正在检查游戏主文件...");
                var clientJarPath = Path.Combine(config.GameDirectory, "versions", versionId, $"{versionId}.jar");
                if (!File.Exists(clientJarPath))
                {
                    LastError = $"游戏主文件不存在: {clientJarPath}\n请先下载游戏版本";
                    Debug.WriteLine($"❌ {LastError}");
                    throw new FileNotFoundException(LastError);
                }
                Debug.WriteLine($"客户端JAR: {clientJarPath}");

                // 4. 检查库文件完整性（包括文件大小验证）
                onProgressUpdate?.Invoke("正在检查游戏依赖库...");
                Debug.WriteLine($"检查库文件完整性...");
                var missingLibs = GetMissingLibraries(config.GameDirectory, versionId, versionInfo);
                
                if (missingLibs.Count > 0)
                {
                    MissingLibraries = missingLibs;
                    LastError = $"检测到 {missingLibs.Count} 个缺失或不完整的库文件";
                    Debug.WriteLine($"❌ 缺失 {missingLibs.Count} 个库文件");
                    return true; // 有完整性问题
                }
                
                Debug.WriteLine($"✅ 所有库文件完整");
                onProgressUpdate?.Invoke("游戏完整性检查完成");
                return false; // 没有完整性问题
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
        /// 启动游戏（异步版本）
        /// </summary>
        /// <param name="versionId">版本ID（文件夹名称）</param>
        /// <param name="account">游戏账号</param>
        /// <param name="config">启动器配置</param>
        /// <param name="onProgressUpdate">进度更新回调</param>
        /// <returns>是否启动成功</returns>
        public static async System.Threading.Tasks.Task<bool> LaunchGameAsync(string versionId, GameAccount account, LauncherConfig config, Action<string>? onProgressUpdate = null)
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
                if (!File.Exists(config.JavaPath))
                {
                    LastError = $"Java可执行文件不存在\n路径: {config.JavaPath}";
                    throw new FileNotFoundException(LastError);
                }

                // 2. 读取版本JSON
                onProgressUpdate?.Invoke("正在读取游戏版本信息...");
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

                Debug.WriteLine($"MainClass: {versionInfo.MainClass}");
                Debug.WriteLine($"Libraries: {versionInfo.Libraries?.Length ?? 0} 个");

                if (string.IsNullOrEmpty(versionInfo.MainClass))
                {
                    LastError = "版本JSON中缺少MainClass字段";
                    throw new Exception(LastError);
                }

                // 3. 确保natives目录存在
                var versionDir = Path.Combine(config.GameDirectory, "versions", versionId);
                var nativesDir = Path.Combine(versionDir, "natives");
                if (!Directory.Exists(nativesDir))
                {
                    Debug.WriteLine($"创建natives目录: {nativesDir}");
                    Directory.CreateDirectory(nativesDir);
                }

                // 4. 验证客户端JAR存在
                onProgressUpdate?.Invoke("正在验证游戏客户端文件...");
                var clientJar = Path.Combine(versionDir, $"{versionId}.jar");
                Debug.WriteLine($"客户端JAR: {clientJar}");
                
                if (!File.Exists(clientJar))
                {
                    LastError = $"客户端JAR文件不存在\n路径: {clientJar}";
                    throw new FileNotFoundException(LastError);
                }

                // 5. 检查并下载缺失的库文件
                onProgressUpdate?.Invoke("正在检查游戏依赖库...");
                Debug.WriteLine($"检查库文件完整性...");
                var missingLibs = GetMissingLibraries(config.GameDirectory, versionId, versionInfo);
                
                if (missingLibs.Count > 0)
                {
                    MissingLibraries = missingLibs;
                    LastError = $"检测到 {missingLibs.Count} 个缺失的库文件\n请在主页点击启动按钮，系统将自动下载";
                    Debug.WriteLine($"❌ 缺失 {missingLibs.Count} 个库文件，需要下载");
                    return false;
                }
                
                Debug.WriteLine($"✅ 所有库文件完整");
                onProgressUpdate?.Invoke("游戏依赖检查完成");

                // 6. 构建启动参数
                onProgressUpdate?.Invoke("正在准备启动参数...");
                var arguments = BuildLaunchArguments(versionId, account, config, versionInfo);
                Debug.WriteLine($"完整启动命令: \"{config.JavaPath}\" {arguments}");

                // 7. 启动游戏进程
                onProgressUpdate?.Invoke("正在启动游戏进程...");
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
            
            // 添加所有库
            if (versionInfo.Libraries != null)
            {
                foreach (var lib in versionInfo.Libraries)
                {
                    if (IsLibraryAllowed(lib))
                    {
                        var libPath = GetLibraryPath(librariesDir, lib);
                        if (File.Exists(libPath))
                        {
                            classpathItems.Add(libPath);
                        }
                        else
                        {
                            Debug.WriteLine($"⚠️ 库文件不存在: {libPath}");
                        }
                    }
                }
            }

            // 添加客户端JAR
            var clientJar = Path.Combine(versionDir, $"{versionId}.jar");
            classpathItems.Add(clientJar);
            
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

            // 标准参数
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
        /// 获取缺失的库文件列表
        /// </summary>
        private static List<string> GetMissingLibraries(string gameDir, string versionId, VersionInfo versionInfo)
        {
            var missing = new List<string>();
            var librariesDir = Path.Combine(gameDir, "libraries");
            
            if (versionInfo.Libraries == null) return missing;
            
            foreach (var lib in versionInfo.Libraries)
            {
                if (IsLibraryAllowed(lib))
                {
                    var libPath = GetLibraryPath(librariesDir, lib);
                    if (!string.IsNullOrEmpty(libPath))
                    {
                        bool isMissing = false;
                        
                        // 检查文件是否存在
                        if (!File.Exists(libPath))
                        {
                            isMissing = true;
                            Debug.WriteLine($"   ❌ 文件不存在: {lib.Name}");
                            Console.WriteLine($"   ❌ 文件不存在: {lib.Name}");
                        }
                        // 如果文件存在，验证文件大小
                        else if (lib.Downloads?.Artifact?.Size > 0)
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
                            missing.Add(lib.Name ?? "Unknown");
                            Debug.WriteLine($"      期望路径: {libPath}");
                        }
                    }
                }
            }
            
            return missing;
        }

        /// <summary>
        /// 获取库文件路径
        /// </summary>
        private static string GetLibraryPath(string librariesDir, Library lib)
        {
            if (lib.Downloads?.Artifact?.Path != null)
            {
                return Path.Combine(librariesDir, lib.Downloads.Artifact.Path.Replace("/", "\\"));
            }

            // 备用方式：从name构建路径
            if (!string.IsNullOrEmpty(lib.Name))
            {
                var parts = lib.Name.Split(':');
                if (parts.Length >= 3)
                {
                    var package = parts[0].Replace('.', '\\');
                    var name = parts[1];
                    var version = parts[2];
                    return Path.Combine(librariesDir, package, name, version, $"{name}-{version}.jar");
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
        }

        private class LibraryDownloads
        {
            public Artifact? Artifact { get; set; }
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
    }
}

