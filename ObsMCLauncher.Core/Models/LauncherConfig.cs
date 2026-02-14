using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ObsMCLauncher.Core.Services;

namespace ObsMCLauncher.Core.Models;

public class LauncherConfig
{
    public string GetActualJavaPath(string? minecraftVersion = null)
    {
        switch (JavaSelectionMode)
        {
            case 0:
                if (!string.IsNullOrEmpty(minecraftVersion))
                {
                    var autoPath = JavaDetector.SelectJavaForMinecraftVersion(minecraftVersion);
                    if (!string.IsNullOrEmpty(autoPath))
                    {
                        return autoPath;
                    }
                }

                var bestJava = JavaDetector.SelectBestJava();
                return bestJava?.Path ?? "javaw.exe";

            case 1:
                return string.IsNullOrEmpty(JavaPath) ? "javaw.exe" : JavaPath;

            case 2:
                return string.IsNullOrEmpty(CustomJavaPath) ? JavaPath : CustomJavaPath;

            default:
                return JavaPath;
        }
    }

    public string GetRunDirectory(string versionName)
    {
        var versionPath = Path.Combine(GameDirectory, "versions", versionName);
        var versionIsolation = VersionConfigService.GetVersionIsolation(versionPath);

        bool useIsolation;
        if (versionIsolation.HasValue)
        {
            useIsolation = versionIsolation.Value;
        }
        else
        {
            useIsolation = GameDirectoryType == GameDirectoryType.VersionFolder;
        }

        return useIsolation
            ? Path.Combine(GameDirectory, "versions", versionName)
            : GameDirectory;
    }

    public string GetModsDirectory(string versionName) => Path.Combine(GetRunDirectory(versionName), "mods");

    public string GetResourcePacksDirectory(string versionName) => Path.Combine(GetRunDirectory(versionName), "resourcepacks");

    public string GetShaderPacksDirectory(string versionName) => Path.Combine(GetRunDirectory(versionName), "shaderpacks");

    public string GetSavesDirectory(string versionName) => Path.Combine(GetRunDirectory(versionName), "saves");

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DownloadSource DownloadSource { get; set; } = DownloadSource.BMCLAPI;

    public int MaxMemory { get; set; } = 4096;

    public int MinMemory { get; set; } = 1024;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DirectoryLocation GameDirectoryLocation { get; set; } = DirectoryLocation.AppData;

    public string CustomGameDirectory { get; set; } = "";

    [JsonIgnore]
    public string GameDirectory
        => GameDirectoryLocation switch
        {
            DirectoryLocation.AppData => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft"),
            DirectoryLocation.RunningDirectory => Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                ".minecraft"),
            DirectoryLocation.Custom => string.IsNullOrEmpty(CustomGameDirectory)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft")
                : CustomGameDirectory,
            _ => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft")
        };

    public int JavaSelectionMode { get; set; } = 0;

    public string JavaPath { get; set; } = "javaw.exe";

    public string CustomJavaPath { get; set; } = "";

    public string JvmArguments { get; set; } = "-XX:+UseG1GC -XX:+UnlockExperimentalVMOptions";

    public int MaxDownloadThreads { get; set; } = 8;

    public bool DownloadAssetsWithGame { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GameDirectoryType GameDirectoryType { get; set; } = GameDirectoryType.RootFolder;

    public bool CloseAfterLaunch { get; set; } = false;

    public bool ShowGameLogOnLaunch { get; set; } = false;

    public bool AutoCheckUpdate { get; set; } = true;

    public int ThemeMode { get; set; } = 0;

    public string? SelectedVersion { get; set; }

    public string? SelectedAccountId { get; set; }

    public bool IsNavCollapsed { get; set; } = false;

    public List<ServerInfo> Servers { get; set; } = [];

    public List<HomeCardConfig> HomeCards { get; set; } = [];

    public List<VersionIsolationSetting> VersionIsolationSettings { get; set; } = [];

    public static string GetConfigFilePath()
    {
        return Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "OMCL",
            "config",
            "config.json");
    }

    private static string _currentConfigPath = GetConfigFilePath();

    public void Save()
    {
        var configPath = GetConfigFilePath();

        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var json = JsonSerializer.Serialize(this, options);
        File.WriteAllText(configPath, json);

        _currentConfigPath = configPath;
    }

    public static LauncherConfig Load()
    {
        var configPath = GetConfigFilePath();

        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<LauncherConfig>(json);
                if (config != null)
                {
                    _currentConfigPath = configPath;
                    return config;
                }
            }
            catch
            {
            }
        }

        var defaultConfig = new LauncherConfig();
        _currentConfigPath = configPath;
        return defaultConfig;
    }

    public string GetAccountFilePath()
    {
        return Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "OMCL",
            "config",
            "accounts.json");
    }

    public string GetPluginDirectory()
    {
        return Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "OMCL",
            "plugins");
    }

    public string GetDataDirectory()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OMCL");
    }
}
