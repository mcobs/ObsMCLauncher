using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ObsMCLauncher.Core.Services;

public class VersionConfig
{
    public bool? UseVersionIsolation { get; set; } = null;
}

public static class VersionConfigService
{
    private const string ConfigFileName = "version_config.json";

    private static string GetConfigPath(string versionPath) => Path.Combine(versionPath, ConfigFileName);

    public static VersionConfig LoadVersionConfig(string versionPath)
    {
        var configPath = GetConfigPath(versionPath);
        if (!File.Exists(configPath)) return new VersionConfig();

        try
        {
            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<VersionConfig>(json);
            return config ?? new VersionConfig();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VersionConfig] 加载配置失败: {ex.Message}");
            return new VersionConfig();
        }
    }

    public static bool SaveVersionConfig(string versionPath, VersionConfig config)
    {
        try
        {
            var configPath = GetConfigPath(versionPath);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configPath, JsonSerializer.Serialize(config, options));
            Debug.WriteLine($"[VersionConfig] 配置已保存: {versionPath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VersionConfig] 保存配置失败: {ex.Message}");
            return false;
        }
    }

    public static bool SetVersionIsolation(string versionPath, bool? useIsolation)
    {
        var config = LoadVersionConfig(versionPath);
        config.UseVersionIsolation = useIsolation;
        return SaveVersionConfig(versionPath, config);
    }

    public static bool? GetVersionIsolation(string versionPath)
    {
        var config = LoadVersionConfig(versionPath);
        return config.UseVersionIsolation;
    }
}
