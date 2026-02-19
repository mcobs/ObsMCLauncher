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

            Directory.CreateDirectory(configDir);
            Directory.CreateDirectory(pluginsDir);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("Bootstrap", $"创建OMCL目录失败: {ex.Message}");
        }
    }
}
