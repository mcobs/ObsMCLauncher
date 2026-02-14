using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ObsMCLauncher.Core.Services;

public class JavaInfo
{
    public string Path { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int MajorVersion { get; set; }
    public string Architecture { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

public static class JavaDetector
{
    private static readonly object _cacheLock = new();
    private static List<JavaInfo>? _cachedJavaList;
    private static DateTime _cachedAt;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

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

        if (OperatingSystem.IsWindows())
        {
            DetectFromRegistry(javaList, foundPaths);
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
            var javawPath = Path.Combine(dir.Trim(), "javaw.exe");
            var javaPath = Path.Combine(dir.Trim(), "java.exe");

            if (File.Exists(javawPath)) AddJavaIfValid(javaList, foundPaths, javawPath, "PATH");
            else if (File.Exists(javaPath)) AddJavaIfValid(javaList, foundPaths, javaPath, "PATH");
        }
    }

    private static void DetectFromJavaHome(List<JavaInfo> javaList, HashSet<string> foundPaths)
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (string.IsNullOrEmpty(javaHome)) return;

        var javawPath = Path.Combine(javaHome, "bin", "javaw.exe");
        var javaPath = Path.Combine(javaHome, "bin", "java.exe");

        if (File.Exists(javawPath)) AddJavaIfValid(javaList, foundPaths, javawPath, "JAVA_HOME");
        else if (File.Exists(javaPath)) AddJavaIfValid(javaList, foundPaths, javaPath, "JAVA_HOME");
    }

    [SupportedOSPlatform("windows")]
    private static void DetectFromRegistry(List<JavaInfo> javaList, HashSet<string> foundPaths)
    {
        var registryPaths = new[]
        {
            @"SOFTWARE\JavaSoft\Java Runtime Environment",
            @"SOFTWARE\JavaSoft\Java Development Kit",
            @"SOFTWARE\JavaSoft\JDK",
            @"SOFTWARE\JavaSoft\JRE",
            @"SOFTWARE\WOW6432Node\JavaSoft\Java Runtime Environment",
            @"SOFTWARE\WOW6432Node\JavaSoft\Java Development Kit"
        };

        foreach (var regPath in registryPaths)
        {
            using var key = Registry.LocalMachine.OpenSubKey(regPath);
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
        var commonLocations = new List<string>();
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        if (!string.IsNullOrEmpty(programFiles))
        {
            commonLocations.Add(Path.Combine(programFiles, "Java"));
            commonLocations.Add(Path.Combine(programFiles, "Eclipse Adoptium"));
            commonLocations.Add(Path.Combine(programFiles, "Microsoft"));
        }
        if (!string.IsNullOrEmpty(programFilesX86)) commonLocations.Add(Path.Combine(programFilesX86, "Java"));

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
            Source = source
        });
    }

    private static (string Version, int MajorVersion, string Architecture)? GetJavaVersion(string javaPath)
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
            return (version, majorVersion, architecture);
        }
        catch { return null; }
    }
}
