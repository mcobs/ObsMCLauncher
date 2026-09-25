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
            var baseDir = VersionInfo.GetAppBaseDirectory();
            var configDir = Path.Combine(baseDir, "OMCL", "config");
            var configPluginsDir = Path.Combine(configDir, "plugins");
            var pluginsDir = Path.Combine(baseDir, "OMCL", "plugins");
            var pluginUpdatesDir = Path.Combine(baseDir, "OMCL", "plugin-updates");
            var cacheDir = Path.Combine(baseDir, "OMCL", "cache");
            var cacheCurseForgeDir = Path.Combine(cacheDir, "curseforge");
            var cacheModrinthDir = Path.Combine(cacheDir, "modrinth");
            var cacheIconsDir = Path.Combine(cacheDir, "icons");
            var cachePluginUpdatesDir = Path.Combine(cacheDir, "plugin-updates");

            Directory.CreateDirectory(configDir);
            Directory.CreateDirectory(configPluginsDir);
            Directory.CreateDirectory(pluginsDir);
            Directory.CreateDirectory(pluginUpdatesDir);
            Directory.CreateDirectory(cacheCurseForgeDir);
            Directory.CreateDirectory(cacheModrinthDir);
            Directory.CreateDirectory(cacheIconsDir);
            Directory.CreateDirectory(cachePluginUpdatesDir);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("Bootstrap", $"创建OMCL目录失败: {ex.Message}");
        }
    }
}
