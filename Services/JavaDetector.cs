using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// Java检测信息
    /// </summary>
    public class JavaInfo
    {
        public string Path { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public int MajorVersion { get; set; }
        public string Architecture { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    /// <summary>
    /// Java自动检测服务
    /// </summary>
    public static class JavaDetector
    {
        /// <summary>
        /// 检测所有可用的Java安装
        /// </summary>
        public static List<JavaInfo> DetectAllJava()
        {
            var javaList = new List<JavaInfo>();
            var foundPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Debug.WriteLine("========== 开始检测Java ==========");

            // 1. 检查PATH环境变量
            DetectFromPath(javaList, foundPaths);

            // 2. 检查JAVA_HOME
            DetectFromJavaHome(javaList, foundPaths);

            // 3. 检查Windows注册表
            if (OperatingSystem.IsWindows())
            {
                DetectFromRegistry(javaList, foundPaths);
            }

            // 4. 检查常见安装目录
            DetectFromCommonLocations(javaList, foundPaths);

            Debug.WriteLine($"✅ 共检测到 {javaList.Count} 个Java安装");
            Debug.WriteLine("========== Java检测完成 ==========");

            // 按版本号降序排序
            return javaList.OrderByDescending(j => j.MajorVersion).ToList();
        }

        /// <summary>
        /// 自动选择最佳Java
        /// </summary>
        public static JavaInfo? SelectBestJava()
        {
            var javaList = DetectAllJava();
            
            // 优先选择Java 17或更高版本（推荐用于现代Minecraft）
            var java17Plus = javaList.Where(j => j.MajorVersion >= 17).ToList();
            if (java17Plus.Count > 0)
            {
                return java17Plus.First();
            }

            // 其次选择Java 8-16
            var java8Plus = javaList.Where(j => j.MajorVersion >= 8).ToList();
            if (java8Plus.Count > 0)
            {
                return java8Plus.First();
            }

            // 返回任何可用的Java
            return javaList.FirstOrDefault();
        }

        /// <summary>
        /// 根据Minecraft版本自动选择合适的Java
        /// </summary>
        /// <param name="minecraftVersion">Minecraft版本号，例如 "1.20.1", "1.12.2"</param>
        /// <returns>合适的Java路径，如果找不到则返回null</returns>
        public static string? SelectJavaForMinecraftVersion(string minecraftVersion)
        {
            var javaList = DetectAllJava();
            if (javaList.Count == 0)
            {
                Debug.WriteLine("❌ 未检测到任何Java安装");
                return null;
            }

            // 解析Minecraft版本号
            var versionParts = minecraftVersion.Split('.');
            if (versionParts.Length < 2)
            {
                Debug.WriteLine($"⚠️ 无法解析Minecraft版本号: {minecraftVersion}，使用最高版本Java");
                return javaList.First().Path;
            }

            int majorVersion = 0, minorVersion = 0;
            if (!int.TryParse(versionParts[0], out majorVersion) || 
                !int.TryParse(versionParts[1], out minorVersion))
            {
                Debug.WriteLine($"⚠️ 无法解析Minecraft版本号: {minecraftVersion}，使用最高版本Java");
                return javaList.First().Path;
            }

            JavaInfo? selectedJava = null;

            // Minecraft 1.17+ 需要 Java 17+
            if (majorVersion >= 1 && minorVersion >= 17)
            {
                selectedJava = javaList.FirstOrDefault(j => j.MajorVersion >= 17);
                if (selectedJava != null)
                {
                    Debug.WriteLine($"✅ Minecraft {minecraftVersion} (1.17+) -> 使用 Java {selectedJava.MajorVersion}");
                    return selectedJava.Path;
                }
            }

            // Minecraft 1.13-1.16 推荐 Java 8-16
            if (majorVersion >= 1 && minorVersion >= 13 && minorVersion < 17)
            {
                selectedJava = javaList.FirstOrDefault(j => j.MajorVersion >= 8 && j.MajorVersion < 17);
                if (selectedJava != null)
                {
                    Debug.WriteLine($"✅ Minecraft {minecraftVersion} (1.13-1.16) -> 使用 Java {selectedJava.MajorVersion}");
                    return selectedJava.Path;
                }
                
                // 如果没有Java 8-16，尝试使用Java 17+
                selectedJava = javaList.FirstOrDefault(j => j.MajorVersion >= 17);
                if (selectedJava != null)
                {
                    Debug.WriteLine($"⚠️ Minecraft {minecraftVersion} 推荐Java 8-16，但只找到Java {selectedJava.MajorVersion}");
                    return selectedJava.Path;
                }
            }

            // Minecraft 1.12及以下 推荐 Java 8
            if (majorVersion >= 1 && minorVersion <= 12)
            {
                selectedJava = javaList.FirstOrDefault(j => j.MajorVersion == 8);
                if (selectedJava != null)
                {
                    Debug.WriteLine($"✅ Minecraft {minecraftVersion} (≤1.12) -> 使用 Java {selectedJava.MajorVersion}");
                    return selectedJava.Path;
                }
                
                // 如果没有Java 8，尝试使用更高版本
                selectedJava = javaList.FirstOrDefault(j => j.MajorVersion >= 8);
                if (selectedJava != null)
                {
                    Debug.WriteLine($"⚠️ Minecraft {minecraftVersion} 推荐Java 8，但只找到Java {selectedJava.MajorVersion}");
                    return selectedJava.Path;
                }
            }

            // 如果以上都不匹配，使用最高版本的Java
            Debug.WriteLine($"⚠️ 无法为Minecraft {minecraftVersion} 找到最佳Java，使用最高版本");
            return javaList.First().Path;
        }

        /// <summary>
        /// 从PATH环境变量检测
        /// </summary>
        private static void DetectFromPath(List<JavaInfo> javaList, HashSet<string> foundPaths)
        {
            try
            {
                var pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrEmpty(pathEnv)) return;

                foreach (var dir in pathEnv.Split(';'))
                {
                    var javawPath = Path.Combine(dir.Trim(), "javaw.exe");
                    var javaPath = Path.Combine(dir.Trim(), "java.exe");

                    if (File.Exists(javawPath))
                    {
                        AddJavaIfValid(javaList, foundPaths, javawPath, "PATH环境变量");
                    }
                    else if (File.Exists(javaPath))
                    {
                        AddJavaIfValid(javaList, foundPaths, javaPath, "PATH环境变量");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"从PATH检测Java失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从JAVA_HOME检测
        /// </summary>
        private static void DetectFromJavaHome(List<JavaInfo> javaList, HashSet<string> foundPaths)
        {
            try
            {
                var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
                if (string.IsNullOrEmpty(javaHome)) return;

                var javawPath = Path.Combine(javaHome, "bin", "javaw.exe");
                var javaPath = Path.Combine(javaHome, "bin", "java.exe");

                if (File.Exists(javawPath))
                {
                    AddJavaIfValid(javaList, foundPaths, javawPath, "JAVA_HOME");
                }
                else if (File.Exists(javaPath))
                {
                    AddJavaIfValid(javaList, foundPaths, javaPath, "JAVA_HOME");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"从JAVA_HOME检测Java失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从Windows注册表检测
        /// </summary>
        private static void DetectFromRegistry(List<JavaInfo> javaList, HashSet<string> foundPaths)
        {
            try
            {
                var registryPaths = new[]
                {
                    @"SOFTWARE\JavaSoft\Java Runtime Environment",
                    @"SOFTWARE\JavaSoft\Java Development Kit",
                    @"SOFTWARE\JavaSoft\JDK",
                    @"SOFTWARE\JavaSoft\JRE",
                    @"SOFTWARE\WOW6432Node\JavaSoft\Java Runtime Environment",
                    @"SOFTWARE\WOW6432Node\JavaSoft\Java Development Kit",
                    @"SOFTWARE\WOW6432Node\JavaSoft\JDK",
                    @"SOFTWARE\WOW6432Node\JavaSoft\JRE"
                };

                foreach (var regPath in registryPaths)
                {
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(regPath);
                        if (key == null) continue;

                        foreach (var versionKey in key.GetSubKeyNames())
                        {
                            try
                            {
                                using var versionSubKey = key.OpenSubKey(versionKey);
                                if (versionSubKey == null) continue;

                                var javaHome = versionSubKey.GetValue("JavaHome")?.ToString();
                                if (string.IsNullOrEmpty(javaHome)) continue;

                                var javawPath = Path.Combine(javaHome, "bin", "javaw.exe");
                                var javaPath = Path.Combine(javaHome, "bin", "java.exe");

                                if (File.Exists(javawPath))
                                {
                                    AddJavaIfValid(javaList, foundPaths, javawPath, "注册表");
                                }
                                else if (File.Exists(javaPath))
                                {
                                    AddJavaIfValid(javaList, foundPaths, javaPath, "注册表");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"读取注册表子键失败: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"读取注册表 {regPath} 失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"从注册表检测Java失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从常见安装位置检测
        /// </summary>
        private static void DetectFromCommonLocations(List<JavaInfo> javaList, HashSet<string> foundPaths)
        {
            var commonLocations = new List<string>();

            // Program Files
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            if (!string.IsNullOrEmpty(programFiles))
            {
                commonLocations.Add(Path.Combine(programFiles, "Java"));
                commonLocations.Add(Path.Combine(programFiles, "Eclipse Adoptium"));
                commonLocations.Add(Path.Combine(programFiles, "BellSoft"));
                commonLocations.Add(Path.Combine(programFiles, "Zulu"));
                commonLocations.Add(Path.Combine(programFiles, "Microsoft"));
            }

            if (!string.IsNullOrEmpty(programFilesX86))
            {
                commonLocations.Add(Path.Combine(programFilesX86, "Java"));
            }

            // 遍历常见位置
            foreach (var location in commonLocations)
            {
                try
                {
                    if (!Directory.Exists(location)) continue;

                    foreach (var javaDir in Directory.GetDirectories(location))
                    {
                        var javawPath = Path.Combine(javaDir, "bin", "javaw.exe");
                        var javaPath = Path.Combine(javaDir, "bin", "java.exe");

                        if (File.Exists(javawPath))
                        {
                            AddJavaIfValid(javaList, foundPaths, javawPath, "常见安装目录");
                        }
                        else if (File.Exists(javaPath))
                        {
                            AddJavaIfValid(javaList, foundPaths, javaPath, "常见安装目录");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"搜索 {location} 失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 添加Java到列表（如果有效且未重复）
        /// </summary>
        private static void AddJavaIfValid(List<JavaInfo> javaList, HashSet<string> foundPaths, string javaPath, string source)
        {
            try
            {
                // 标准化路径
                var normalizedPath = Path.GetFullPath(javaPath);

                // 检查是否已添加
                if (foundPaths.Contains(normalizedPath))
                {
                    return;
                }

                // 获取Java版本信息
                var versionInfo = GetJavaVersion(javaPath);
                if (versionInfo == null)
                {
                    return;
                }

                foundPaths.Add(normalizedPath);
                var (version, majorVersion, architecture) = versionInfo.Value;
                javaList.Add(new JavaInfo
                {
                    Path = normalizedPath,
                    Version = version,
                    MajorVersion = majorVersion,
                    Architecture = architecture,
                    Source = source
                });

                Debug.WriteLine($"找到Java {majorVersion} ({architecture}) - {source}");
                Debug.WriteLine($"   路径: {normalizedPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"添加Java失败 ({javaPath}): {ex.Message}");
            }
        }

        /// <summary>
        /// 获取Java版本信息
        /// </summary>
        private static (string Version, int MajorVersion, string Architecture)? GetJavaVersion(string javaPath)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = javaPath,
                        Arguments = "-version",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardError.ReadToEnd();
                process.WaitForExit(5000);

                if (string.IsNullOrEmpty(output))
                {
                    return null;
                }

                // 解析版本号
                // 示例输出：java version "1.8.0_301" 或 openjdk version "17.0.1"
                var lines = output.Split('\n');
                if (lines.Length == 0) return null;

                var versionLine = lines[0];
                var versionMatch = System.Text.RegularExpressions.Regex.Match(versionLine, @"version ""(.+?)""");
                if (!versionMatch.Success) return null;

                var version = versionMatch.Groups[1].Value;
                
                // 解析主版本号
                int majorVersion = 0;
                if (version.StartsWith("1."))
                {
                    // Java 8及以下: "1.8.0_301" -> 8
                    var parts = version.Split('.');
                    if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
                    {
                        majorVersion = minor;
                    }
                }
                else
                {
                    // Java 9+: "17.0.1" -> 17
                    var parts = version.Split('.');
                    if (parts.Length >= 1 && int.TryParse(parts[0], out var major))
                    {
                        majorVersion = major;
                    }
                }

                // 检测架构
                var architecture = output.Contains("64-Bit") || output.Contains("64-bit") ? "x64" : "x86";

                return (version, majorVersion, architecture);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取Java版本失败 ({javaPath}): {ex.Message}");
                return null;
            }
        }
    }
}

