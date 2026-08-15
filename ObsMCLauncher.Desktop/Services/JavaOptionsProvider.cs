#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ObsMCLauncher.Desktop.Services;

/// <summary>Java 选项类型：自动选择 / 已探测 / 自定义路径。</summary>
public enum JavaOptionType
{
    Auto,
    Detected,
    Custom
}

/// <summary>Java 下拉列表中的单个选项。</summary>
/// <remarks>
/// 相等性只比较 Type（Detected 额外比较路径，忽略大小写），
/// 保证 ComboBox 的 SelectedItem 能稳定匹配到列表中的「自定义路径...」等条目。
/// </remarks>
public sealed class JavaOption
{
    public JavaOptionType Type { get; init; }
    public string Path { get; init; } = "";
    public string Display { get; init; } = "";
    public string Version { get; init; } = "";
    public int MajorVersion { get; init; }
    public string Architecture { get; init; } = "";
    public string Source { get; init; } = "";
    public bool IsPathVisible => Type == JavaOptionType.Detected && !string.IsNullOrWhiteSpace(Path);

    public override string ToString() => string.IsNullOrWhiteSpace(Display) ? Path : Display;

    public override bool Equals(object? obj) =>
        obj is JavaOption o
        && Type == o.Type
        && (Type != JavaOptionType.Detected || string.Equals(Path, o.Path, StringComparison.OrdinalIgnoreCase));

    public override int GetHashCode() =>
        Type != JavaOptionType.Detected
            ? Type.GetHashCode()
            : StringComparer.OrdinalIgnoreCase.GetHashCode(Path);

    public static JavaOption Auto() => new()
    {
        Type = JavaOptionType.Auto,
        Display = "自动选择（根据游戏版本自动匹配）"
    };

    public static JavaOption Custom() => new()
    {
        Type = JavaOptionType.Custom,
        Display = "自定义路径..."
    };
}

/// <summary>
/// 系统 Java 运行时扫描服务：供「设置页」与「版本实例页」共用，
/// 保证两处 Java 列表完全一致。结果带 5 分钟缓存，避免反复启动 java -version。
/// </summary>
public static class JavaOptionsProvider
{
    private static readonly object CacheLock = new();
    private static List<JavaOption>? _cached;
    private static DateTime _cachedAt;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>扫描系统已安装的 Java（带缓存），按版本降序返回。</summary>
    public static async Task<List<JavaOption>> ScanAsync()
    {
        lock (CacheLock)
        {
            if (_cached != null && (DateTime.UtcNow - _cachedAt) < CacheTtl)
                return new List<JavaOption>(_cached);
        }

        var found = await Task.Run(DetectAllJavaOptions).ConfigureAwait(false);

        lock (CacheLock)
        {
            // 并发扫描时以先写入者为准
            if (_cached != null && (DateTime.UtcNow - _cachedAt) < CacheTtl)
                return new List<JavaOption>(_cached);

            _cached = found;
            _cachedAt = DateTime.UtcNow;
        }

        return new List<JavaOption>(found);
    }

    /// <summary>组装完整下拉选项：自动选择 + 已探测列表 + 自定义路径。</summary>
    public static List<JavaOption> BuildOptionList(List<JavaOption> found)
    {
        var list = new List<JavaOption>(found.Count + 2) { JavaOption.Auto() };
        list.AddRange(found);
        list.Add(JavaOption.Custom());
        return list;
    }

    /// <summary>读取指定 java 可执行文件的版本信息（如自定义路径浏览后展示），失败返回 null。</summary>
    public static JavaOption? Inspect(string javaExePath) => TryGetJavaVersion(javaExePath);

    // ==================== 探测实现 ====================

    private static List<JavaOption> DetectAllJavaOptions()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    candidates.Add(Path.GetFullPath(path));
                }
            }
            catch
            {
            }
        }

        // PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var d = dir.Trim();
            if (OperatingSystem.IsWindows())
            {
                AddIfExists(Path.Combine(d, "javaw.exe"));
            }
            else
            {
                AddIfExists(Path.Combine(d, "java"));
            }
        }

        // JAVA_HOME
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            if (OperatingSystem.IsWindows())
            {
                AddIfExists(Path.Combine(javaHome, "bin", "javaw.exe"));
            }
            else
            {
                AddIfExists(Path.Combine(javaHome, "bin", "java"));
            }
        }

        // 常见目录
        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            foreach (var root in new[] { programFiles, programFilesX86 })
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

                foreach (var baseDir in new[] { "Java", "Eclipse Adoptium", "Eclipse Foundation", "Microsoft", "Zulu", "BellSoft", "Amazon Corretto", "Alibaba", "GraalVM", "SapMachine" })
                {
                    var dir = Path.Combine(root, baseDir);
                    if (!Directory.Exists(dir)) continue;

                    foreach (var sub in Directory.GetDirectories(dir))
                    {
                        AddIfExists(Path.Combine(sub, "bin", "javaw.exe"));
                    }
                }
            }

            // 用户级 JDK 目录
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var userJdksDir = Path.Combine(homeDir, ".jdks");
            if (Directory.Exists(userJdksDir))
            {
                foreach (var sub in Directory.GetDirectories(userJdksDir))
                {
                    AddIfExists(Path.Combine(sub, "bin", "javaw.exe"));
                }
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            var macDirs = new[]
            {
                "/Library/Java/JavaVirtualMachines",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Java", "JavaVirtualMachines"),
            };
            foreach (var baseDir in macDirs)
            {
                if (!Directory.Exists(baseDir)) continue;
                foreach (var sub in Directory.GetDirectories(baseDir))
                {
                    AddIfExists(Path.Combine(sub, "Contents", "Home", "bin", "java"));
                }
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            var linuxDirs = new[] { "/usr/lib/jvm", "/usr/java", "/opt/jdk", "/opt/jre", "/opt/java" };
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var userDirs = new[]
            {
                Path.Combine(homeDir, ".sdkman", "candidates", "java"),
                Path.Combine(homeDir, ".jdks"),
            };
            foreach (var baseDir in linuxDirs.Concat(userDirs))
            {
                if (!Directory.Exists(baseDir)) continue;
                foreach (var sub in Directory.GetDirectories(baseDir))
                {
                    AddIfExists(Path.Combine(sub, "bin", "java"));
                }
            }
            AddIfExists("/usr/bin/java");
        }

        var result = new List<JavaOption>();
        foreach (var exe in candidates)
        {
            var info = TryGetJavaVersion(exe);
            if (info != null)
                result.Add(info);
        }

        // 优先高版本
        return result
            .OrderByDescending(x => x.MajorVersion)
            .ThenByDescending(x => x.Version)
            .ToList();
    }

    private static JavaOption? TryGetJavaVersion(string javaExePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = javaExePath,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null) return null;

            var stderr = p.StandardError.ReadToEnd();
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);

            var text = (stderr + "\n" + stdout).Trim();

            var m = Regex.Match(text, "version\\s+\"(?<ver>[^\"]+)\"");
            if (!m.Success) return null;

            var ver = m.Groups["ver"].Value;
            var major = ParseMajor(ver);

            var arch = text.Contains("64-Bit", StringComparison.OrdinalIgnoreCase) ? "x64" : "x86";
            var vendor = DetectVendor(text);

            return new JavaOption
            {
                Type = JavaOptionType.Detected,
                Path = javaExePath,
                Version = ver,
                MajorVersion = major,
                Architecture = arch,
                Source = vendor,
                Display = $"Java {major} ({arch}) - {vendor}"
            };
        }
        catch
        {
            return null;
        }
    }

    private static string DetectVendor(string output)
    {
        if (output.Contains("Dragonwell", StringComparison.OrdinalIgnoreCase))
            return "Alibaba Dragonwell";
        if (output.Contains("Zulu", StringComparison.OrdinalIgnoreCase))
            return "Azul Zulu";
        if (output.Contains("BellSoft", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Liberica", StringComparison.OrdinalIgnoreCase))
            return "Liberica";
        if (output.Contains("Temurin", StringComparison.OrdinalIgnoreCase))
            return "Eclipse Temurin";
        if (output.Contains("Adoptium", StringComparison.OrdinalIgnoreCase))
            return "Eclipse Adoptium";
        if (output.Contains("Corretto", StringComparison.OrdinalIgnoreCase))
            return "Amazon Corretto";
        if (output.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            return "Microsoft";
        if (output.Contains("GraalVM", StringComparison.OrdinalIgnoreCase))
            return "GraalVM";
        if (output.Contains("SapMachine", StringComparison.OrdinalIgnoreCase))
            return "SapMachine";
        if (output.Contains("Red Hat", StringComparison.OrdinalIgnoreCase))
            return "Red Hat";
        if (output.Contains("IBM", StringComparison.OrdinalIgnoreCase))
            return "IBM";
        if (output.Contains("Java(TM) SE", StringComparison.OrdinalIgnoreCase))
            return "Oracle";
        if (output.Contains("OpenJDK", StringComparison.OrdinalIgnoreCase))
            return "OpenJDK";
        return "Unknown";
    }

    private static int ParseMajor(string version)
    {
        // 1.8.x => 8；17.0.10 => 17
        try
        {
            var parts = version.Split('.');
            if (parts.Length >= 2 && parts[0] == "1" && int.TryParse(parts[1], out var legacy))
                return legacy;

            if (int.TryParse(parts[0], out var major))
                return major;

            return 0;
        }
        catch
        {
            return 0;
        }
    }
}
