using System;
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
        /// <summary>
        /// 启动游戏
        /// </summary>
        /// <param name="versionId">版本ID（文件夹名称）</param>
        /// <param name="account">游戏账号</param>
        /// <param name="config">启动器配置</param>
        /// <returns>是否启动成功</returns>
        public static bool LaunchGame(string versionId, GameAccount account, LauncherConfig config)
        {
            try
            {
                Debug.WriteLine($"========== 开始启动游戏 ==========");
                Debug.WriteLine($"版本: {versionId}");
                Debug.WriteLine($"账号: {account.Username} ({account.Type})");
                Debug.WriteLine($"游戏目录: {config.GameDirectory}");

                // 1. 验证Java路径
                if (!File.Exists(config.JavaPath))
                {
                    throw new FileNotFoundException($"Java路径不存在: {config.JavaPath}");
                }

                // 2. 读取版本JSON
                var versionJsonPath = Path.Combine(config.GameDirectory, "versions", versionId, $"{versionId}.json");
                if (!File.Exists(versionJsonPath))
                {
                    throw new FileNotFoundException($"版本JSON不存在: {versionJsonPath}");
                }

                var versionJson = File.ReadAllText(versionJsonPath);
                var versionInfo = JsonSerializer.Deserialize<VersionInfo>(versionJson, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                if (versionInfo == null)
                {
                    throw new Exception("无法解析版本JSON");
                }

                // 3. 构建启动参数
                var arguments = BuildLaunchArguments(versionId, account, config, versionInfo);
                Debug.WriteLine($"启动参数: {arguments}");

                // 4. 启动游戏进程
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = config.JavaPath,
                        Arguments = arguments,
                        WorkingDirectory = config.GameDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = false
                    }
                };

                process.Start();
                Debug.WriteLine($"游戏进程已启动 (PID: {process.Id})");
                Debug.WriteLine($"========== 启动完成 ==========");

                return true;
            }
            catch (Exception ex)
            {
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
                            args.Append($"{libPath};");
                        }
                    }
                }
            }

            // 添加客户端JAR
            var clientJar = Path.Combine(versionDir, $"{versionId}.jar");
            args.Append($"{clientJar}\" ");

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
            args.Append($"--uuid {account.UUID} ");
            args.Append($"--accessToken 0 ");
            args.Append($"--userType {(account.Type == AccountType.Offline ? "legacy" : "msa")} ");
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

