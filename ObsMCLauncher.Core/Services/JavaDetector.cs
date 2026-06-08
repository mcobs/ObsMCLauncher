using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace ObsMCLauncher.Core.Services;

public class JavaInfo
{
    public string Path { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int MajorVersion { get; set; }
    public string Architecture { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
}

public static class JavaDetector
{
    private static readonly object _cacheLock = new();
    private static List<JavaInfo>? _cachedJavaList;
    private static DateTime _cachedAt;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static bool IsWindows => OperatingSystem.IsWindows();
    private static bool IsMacOS => OperatingSystem.IsMacOS();
    private static bool IsLinux => OperatingSystem.IsLinux();

    public static List<JavaInfo> DetectAllJava()
    {
        lock (_cacheLock)
        {
            if (_cachedJavaList != null && (DateTime.Now - _cachedAt) < CacheTtl)
            {
                return _cachedJavaList;
            }
        }

        var javaList = new List<JavaInfo>();
        var foundPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        DetectFromPath(javaList, foundPaths);
        DetectFromJavaHome(javaList, foundPaths);

        if (IsWindows)
        {
#pragma warning disable CA1416
            DetectFromRegistry(javaList, foundPaths);
#pragma warning restore CA1416
        }

        DetectFromCommonLocations(javaList, foundPaths);

        var sorted = javaList.OrderByDescending(j => j.MajorVersion).ToList();

        lock (_cacheLock)
        {
            _cachedJavaList = sorted;
            _cachedAt = DateTime.Now;
        }

        return sorted;
    }

    public static JavaInfo? SelectBestJava()
    {
        var javaList = DetectAllJava();
        var java17Plus = javaList.Where(j => j.MajorVersion >= 17).ToList();
        if (java17Plus.Count > 0) return java17Plus.First();

        var java8Plus = javaList.Where(j => j.MajorVersion >= 8).ToList();
        if (java8Plus.Count > 0) return java8Plus.First();

        return javaList.FirstOrDefault();
    }

    public static string? SelectJavaForMinecraftVersion(string minecraftVersion)
    {
        var javaList = DetectAllJava();
        if (javaList.Count == 0) return null;

        var versionParts = minecraftVersion.Split('.');
        if (versionParts.Length < 2) return javaList.First().Path;

        if (!int.TryParse(versionParts[0], out int major) || 
            !int.TryParse(versionParts[1], out int minor))
        {
            return javaList.First().Path;
        }

        if (major >= 1 && minor >= 17)
        {
            var java17 = javaList.FirstOrDefault(j => j.MajorVersion >= 17);
            if (java17 != null) return java17.Path;
        }

        if (major >= 1 && minor >= 13 && minor < 17)
        {
            var java8to16 = javaList.FirstOrDefault(j => j.MajorVersion >= 8 && j.MajorVersion < 17);
            if (java8to16 != null) return java8to16.Path;
            
            var java17 = javaList.FirstOrDefault(j => j.MajorVersion >= 17);
            if (java17 != null) return java17.Path;
        }

        if (major >= 1 && minor <= 12)
        {
            var java8 = javaList.FirstOrDefault(j => j.MajorVersion == 8);
            if (java8 != null) return java8.Path;
            
            var javaPlus = javaList.FirstOrDefault(j => j.MajorVersion >= 8);
            if (javaPlus != null) return javaPlus.Path;
        }

        return javaList.First().Path;
    }

    private static void DetectFromPath(List<JavaInfo> javaList, HashSet<string> foundPaths)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return;

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (IsWindows)
            {
                var javawPath = Path.Combine(dir.Trim(), "javaw.exe");
                if (File.Exists(javawPath)) AddJavaIfValid(javaList, foundPaths, javawPath, "PATH");
            }
            else
            {
                var javaPath = Path.Combine(dir.Trim(), "java");
                if (File.Exists(javaPath)) AddJavaIfValid(javaList, foundPaths, javaPath, "PATH");
            }
        }
    }

    private static void DetectFromJavaHome(List<JavaInfo> javaList, HashSet<string> foundPaths)
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (string.IsNullOrEmpty(javaHome)) return;

        if (IsWindows)
        {
            var javawPath = Path.Combine(javaHome, "bin", "javaw.exe");
            if (File.Exists(javawPath)) AddJavaIfValid(javaList, foundPaths, javawPath, "JAVA_HOME");
        }
        else
        {
            var javaPath = Path.Combine(javaHome, "bin", "java");
            if (File.Exists(javaPath)) AddJavaIfValid(javaList, foundPaths, javaPath, "JAVA_HOME");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void DetectFromRegistry(List<JavaInfo> javaList, HashSet<string> foundPaths)
    {
        var registryPaths = new[]
        {
            // Oracle JDK / OpenJDK
            @"SOFTWARE\JavaSoft\Java Runtime Environment",
            @"SOFTWARE\JavaSoft\Java Development Kit",
            @"SOFTWARE\JavaSoft\JDK",
            @"SOFTWARE\JavaSoft\JRE",
            // Eclipse Adoptium / Temurin
            @"SOFTWARE\Eclipse Adoptium\JDK",
            @"SOFTWARE\Eclipse Foundation\JDK",
            // Azul Zulu
            @"SOFTWARE\Azul Systems\Zulu",
            @"SOFTWARE\Azul Systems\Zulu runtimes",
            // Microsoft
            @"SOFTWARE\Microsoft\JDK",
            // Amazon Corretto
            @"SOFTWARE\Amazon Services LLC\Corretto",
            // BellSoft Liberica
            @"SOFTWARE\BellSoft\Liberica",
            // WOW6432Node
            @"SOFTWARE\WOW6432Node\JavaSoft\Java Runtime Environment",
            @"SOFTWARE\WOW6432Node\JavaSoft\Java Development Kit",
            @"SOFTWARE\WOW6432Node\Eclipse Adoptium\JDK",
            @"SOFTWARE\WOW6432Node\Azul Systems\Zulu",
        };

        foreach (var regPath in registryPaths)
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regPath);
            if (key == null) continue;

            foreach (var versionKey in key.GetSubKeyNames())
            {
                using var versionSubKey = key.OpenSubKey(versionKey);
                if (versionSubKey == null) continue;

                var javaHome = versionSubKey.GetValue("JavaHome")?.ToString();
                if (string.IsNullOrEmpty(javaHome)) continue;

                var javawPath = Path.Combine(javaHome, "bin", "javaw.exe");
                if (File.Exists(javawPath)) AddJavaIfValid(javaList, foundPaths, javawPath, "Registry");
            }
        }
    }

    private static void DetectFromCommonLocations(List<JavaInfo> javaList, HashSet<string> foundPaths)
    {
        if (IsWindows)
        {
            DetectFromWindowsLocations(javaList, foundPaths);
        }
        else if (IsMacOS)
        {
            DetectFromMacOSLocations(javaList, foundPaths);
        }
        else if (IsLinux)
        {
            DetectFromLinuxLocations(javaList, foundPaths);
        }
    }

    private static void DetectFromWindowsLocations(List<JavaInfo> javaList, HashSet<string> foundPaths)
    {
        var commonLocations = new List<string>();
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        if (!string.IsNullOrEmpty(programFiles))
        {
            commonLocations.Add(Path.Combine(programFiles, "Java"));
            commonLocations.Add(Path.Combine(programFiles, "Eclipse Adoptium"));
            commonLocations.Add(Path.Combine(programFiles, "Eclipse Foundation"));
            commonLocations.Add(Path.Combine(programFiles, "Microsoft"));
            commonLocations.Add(Path.Combine(programFiles, "Zulu"));
            commonLocations.Add(Path.Combine(programFiles, "BellSoft"));
            commonLocations.Add(Path.Combine(programFiles, "Amazon Corretto"));
            commonLocations.Add(Path.Combine(programFiles, "Alibaba"));
            commonLocations.Add(Path.Combine(programFiles, "GraalVM"));
            commonLocations.Add(Path.Combine(programFiles, "SapMachine"));
        }
        if (!string.IsNullOrEmpty(programFilesX86))
        {
            commonLocations.Add(Path.Combine(programFilesX86, "Java"));
            commonLocations.Add(Path.Combine(programFilesX86, "Eclipse Adoptium"));
            commonLocations.Add(Path.Combine(programFilesX86, "Zulu"));
            commonLocations.Add(Path.Combine(programFilesX86, "BellSoft"));
        }

        // 用户级JDK目录
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        commonLocations.Add(Path.Combine(homeDir, ".jdks"));

        foreach (var location in commonLocations)
        {
            if (!Directory.Exists(location)) continue;
            foreach (var javaDir in Directory.GetDirectories(location))
            {
                var javawPath = Path.Combine(javaDir, "bin", "javaw.exe");
                if (File.Exists(javawPath)) AddJavaIfValid(javaList, foundPaths, javawPath, "Common Location");
            }
        }
    }

    private static void DetectFromMacOSLocations(List<JavaInfo> javaList, HashSet<string> foundPaths)
    {
        var macLocations = new[]
        {
            "/Library/Java/JavaVirtualMachines",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Java", "JavaVirtualMachines"),
            "/usr/local/opt",
        };

        foreach (var location in macLocations)
        {
            if (!Directory.Exists(location)) continue;
            foreach (var dir in Directory.GetDirectories(location))
            {
                var javaPath = Path.Combine(dir, "Contents", "Home", "bin", "java");
                if (File.Exists(javaPath))
                {
                    AddJavaIfValid(javaList, foundPaths, javaPath, "Common Location");
                    continue;
                }

                javaPath = Path.Combine(dir, "bin", "java");
                if (File.Exists(javaPath))
                {
                    AddJavaIfValid(javaList, foundPaths, javaPath, "Common Location");
                }
            }
        }

        var javaHome = "/usr/libexec/java_home";
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = javaHome,
                    Arguments = "-v",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            if (!string.IsNullOrWhiteSpace(output))
            {
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var path = Path.Combine(line.Trim(), "bin", "java");
                    if (File.Exists(path)) AddJavaIfValid(javaList, foundPaths, path, "java_home");
                }
            }
        }
        catch { }
    }

    private static void DetectFromLinuxLocations(List<JavaInfo> javaList, HashSet<string> foundPaths)
    {
        var linuxLocations = new[]
        {
            "/usr/lib/jvm",
            "/usr/java",
            "/usr/local/java",
            "/opt/java",
            "/opt/jdk",
            "/opt/jre",
            "/usr/local/opt",
        };

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var userLocations = new[]
        {
            Path.Combine(homeDir, ".sdkman", "candidates", "java"),
            Path.Combine(homeDir, ".jdks"),
        };

        foreach (var location in linuxLocations.Concat(userLocations))
        {
            if (!Directory.Exists(location)) continue;
            foreach (var dir in Directory.GetDirectories(location))
            {
                var javaPath = Path.Combine(dir, "bin", "java");
                if (File.Exists(javaPath))
                {
                    AddJavaIfValid(javaList, foundPaths, javaPath, "Common Location");
                }
            }
        }

        var directPaths = new[] { "/usr/bin/java", "/usr/local/bin/java" };
        foreach (var p in directPaths)
        {
            if (File.Exists(p)) AddJavaIfValid(javaList, foundPaths, p, "Common Location");
        }
    }

    private static void AddJavaIfValid(List<JavaInfo> javaList, HashSet<string> foundPaths, string javaPath, string source)
    {
        var normalizedPath = Path.GetFullPath(javaPath);
        if (foundPaths.Contains(normalizedPath)) return;

        var versionInfo = GetJavaVersion(javaPath);
        if (versionInfo == null) return;

        foundPaths.Add(normalizedPath);
        javaList.Add(new JavaInfo
        {
            Path = normalizedPath,
            Version = versionInfo.Value.Version,
            MajorVersion = versionInfo.Value.MajorVersion,
            Architecture = versionInfo.Value.Architecture,
            Source = source,
            Vendor = versionInfo.Value.Vendor
        });
    }

    private static (string Version, int MajorVersion, string Architecture, string Vendor)? GetJavaVersion(string javaPath)
    {
        try
        {
            using var process = new Process
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

            if (string.IsNullOrEmpty(output)) return null;

            var lines = output.Split('\n');
            if (lines.Length == 0) return null;

            var versionMatch = System.Text.RegularExpressions.Regex.Match(lines[0], @"version ""(.+?)""");
            if (!versionMatch.Success) return null;

            var version = versionMatch.Groups[1].Value;
            int majorVersion = 0;
            if (version.StartsWith("1."))
            {
                var parts = version.Split('.');
                if (parts.Length >= 2) int.TryParse(parts[1], out majorVersion);
            }
            else
            {
                var parts = version.Split('.');
                if (parts.Length >= 1) int.TryParse(parts[0], out majorVersion);
            }

            var architecture = output.Contains("64-Bit") || output.Contains("64-bit") ? "x64" : "x86";
            var vendor = DetectVendor(output);
            return (version, majorVersion, architecture, vendor);
        }
        catch { return null; }
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
}
