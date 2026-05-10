using System;
using System.IO;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Bootstrap;

public static class LauncherBootstrap
{
    public static void Initialize()
    {
        EnsureOmclDirectories();
    }

    private static void EnsureOmclDirectories()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var configDir = Path.Combine(baseDir, "OMCL", "config");
            var pluginsDir = Path.Combine(baseDir, "OMCL", "plugins");
            var cacheDir = Path.Combine(baseDir, "OMCL", "cache");
            var cacheCurseForgeDir = Path.Combine(cacheDir, "curseforge");
            var cacheModrinthDir = Path.Combine(cacheDir, "modrinth");
            var cacheIconsDir = Path.Combine(cacheDir, "icons");

            Directory.CreateDirectory(configDir);
            Directory.CreateDirectory(pluginsDir);
            Directory.CreateDirectory(cacheCurseForgeDir);
            Directory.CreateDirectory(cacheModrinthDir);
            Directory.CreateDirectory(cacheIconsDir);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("Bootstrap", $"创建OMCL目录失败: {ex.Message}");
        }
    }
}
