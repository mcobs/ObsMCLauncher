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
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var configDir = Path.Combine(baseDir, "OMCL", "config");
        var pluginsDir = Path.Combine(baseDir, "OMCL", "plugins");

        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(pluginsDir);
    }
}
