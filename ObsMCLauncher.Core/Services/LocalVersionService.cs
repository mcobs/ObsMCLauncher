using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Services;

public class FlexibleDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dateString = reader.GetString();
        if (string.IsNullOrEmpty(dateString))
            return DateTime.MinValue;

        if (DateTime.TryParse(dateString, out var result))
            return result;

        try
        {
            if (dateString.Length >= 24 && dateString.Contains("+"))
            {
                var tzIndex = dateString.LastIndexOf('+');
                if (tzIndex < 0)
                    tzIndex = dateString.LastIndexOf('-');

                if (tzIndex > 0 && dateString.Length - tzIndex == 5)
                {
                    var fixedString = dateString.Substring(0, tzIndex + 3) + ":" + dateString.Substring(tzIndex + 3);
                    if (DateTime.TryParse(fixedString, out result))
                        return result;
                }
            }
        }
        catch
        {
        }

        return DateTime.MinValue;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("yyyy-MM-ddTHH:mm:ss+00:00"));
    }
}

public class InstalledVersion
{
    public string Id { get; set; } = "";
    public string ActualVersionId { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime ReleaseTime { get; set; }
    public DateTime LastPlayed { get; set; }
    public string Path { get; set; } = "";
    public bool IsSelected { get; set; }

    public string? LoaderType { get; set; }

    public bool? UseVersionIsolation { get; set; } = null;
}

public static class LocalVersionService
{
    public static List<InstalledVersion> GetInstalledVersions(string gameDirectory)
    {
        var installedVersions = new List<InstalledVersion>();

        try
        {
            var versionsPath = System.IO.Path.Combine(gameDirectory, "versions");

            if (!Directory.Exists(versionsPath))
                return installedVersions;

            var versionDirs = Directory.GetDirectories(versionsPath);

            foreach (var versionDir in versionDirs)
            {
                var versionId = System.IO.Path.GetFileName(versionDir);
                var jsonPath = System.IO.Path.Combine(versionDir, $"{versionId}.json");
                var jarPath = System.IO.Path.Combine(versionDir, $"{versionId}.jar");

                if (!File.Exists(jsonPath))
                    continue;

                string jsonContent;
                try
                {
                    jsonContent = File.ReadAllText(jsonPath);
                }
                catch
                {
                    continue;
                }

                var loaderType = DetectLoaderType(jsonContent);
                var isModLoader = !string.IsNullOrEmpty(loaderType);

                if (!isModLoader && !File.Exists(jarPath))
                    continue;

                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        Converters = { new FlexibleDateTimeConverter() }
                    };

                    var versionData = JsonSerializer.Deserialize<VersionJsonData>(jsonContent, options);

                    if (versionData != null)
                    {
                        var lastPlayed = Directory.GetLastAccessTime(versionDir);

                        installedVersions.Add(new InstalledVersion
                        {
                            Id = versionId,
                            ActualVersionId = versionData.Id ?? versionId,
                            Type = versionData.Type ?? "release",
                            ReleaseTime = versionData.ReleaseTime,
                            LastPlayed = lastPlayed,
                            Path = versionDir,
                            IsSelected = false,
                            LoaderType = loaderType,
                            UseVersionIsolation = null
                        });
                    }
                }
                catch
                {
                }
            }

            var selectedVersionId = GetSelectedVersion();
            installedVersions = installedVersions
                .OrderByDescending(v => v.Id == selectedVersionId)
                .ThenByDescending(v => v.LastPlayed)
                .ToList();
        }
        catch
        {
        }

        return installedVersions;
    }

    public static string? GetSelectedVersion()
    {
        var config = LauncherConfig.Load();
        return config.SelectedVersion;
    }

    public static void SetSelectedVersion(string versionId)
    {
        var config = LauncherConfig.Load();
        config.SelectedVersion = versionId;
        config.Save();
    }

    public static void OpenVersionFolder(string versionPath)
    {
        try
        {
            if (Directory.Exists(versionPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", versionPath);
            }
        }
        catch
        {
        }
    }

    public static bool DeleteVersion(string versionPath)
    {
        try
        {
            if (Directory.Exists(versionPath))
            {
                Directory.Delete(versionPath, true);
                return true;
            }
        }
        catch
        {
        }
        return false;
    }

    private static string? DetectLoaderType(string jsonContent)
    {
        try
        {
            var jsonLower = jsonContent.ToLowerInvariant();

            if (jsonLower.Contains("\"minecraftforge\"") ||
                jsonLower.Contains("\"net.minecraftforge\"") ||
                (jsonLower.Contains("forge") && jsonLower.Contains("\"mainclass\"")))
            {
                return "Forge";
            }

            if (jsonLower.Contains("\"fabricmc\"") ||
                jsonLower.Contains("\"net.fabricmc\"") ||
                (jsonLower.Contains("fabric") && jsonLower.Contains("\"mainclass\"")))
            {
                return "Fabric";
            }

            if (jsonLower.Contains("optifine"))
                return "OptiFine";

            if (jsonLower.Contains("quilt"))
                return "Quilt";

            return null;
        }
        catch
        {
            return null;
        }
    }

    private class VersionJsonData
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public DateTime ReleaseTime { get; set; }
    }
}
