using System;
using System.IO;

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
            // 记录错误日志
            System.Diagnostics.Debug.WriteLine($"创建OMCL目录失败: {ex.Message}");
        }
    }
}
