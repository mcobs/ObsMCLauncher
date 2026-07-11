using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ObsMCLauncher.Core.Services;

public class ModMetadata
{
    public string ModId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string Loader { get; set; } = "";
    public string McVersion { get; set; } = "";
    public List<ModDependency> Dependencies { get; set; } = new();
    public string? IconPath { get; set; }
}

public class ModDependency
{
    public string ModId { get; set; } = "";
    public string VersionRange { get; set; } = "";
    public bool IsRequired { get; set; } = true;
    public string? Reason { get; set; }
}

public static class ModMetadataParser
{
    public static ModMetadata? ParseFromJar(string jarPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);

            // 优先 Fabric
            var fabricEntry = archive.GetEntry("fabric.mod.json");
            if (fabricEntry != null)
            {
                using var stream = fabricEntry.Open();
                return ParseFabricMod(stream);
            }

            // Quilt
            var quiltEntry = archive.GetEntry("quilt.mod.json");
            if (quiltEntry != null)
            {
                using var stream = quiltEntry.Open();
                return ParseQuiltMod(stream);
            }

            // Forge / NeoForge (mods.toml)
            var forgeEntry = archive.GetEntry("META-INF/mods.toml");
            if (forgeEntry != null)
            {
                using var stream = forgeEntry.Open();
                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();
                return ParseForgeMod(content, jarPath);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static ModMetadata? ParseFabricMod(Stream stream)
    {
        try
        {
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            var meta = new ModMetadata { Loader = "Fabric" };

            if (root.TryGetProperty("id", out var idProp))
                meta.ModId = idProp.GetString() ?? "";

            if (root.TryGetProperty("version", out var verProp))
                meta.Version = verProp.GetString() ?? "";

            if (root.TryGetProperty("name", out var nameProp))
                meta.Name = nameProp.GetString() ?? "";

            if (root.TryGetProperty("description", out var descProp))
                meta.Description = descProp.GetString() ?? "";

            if (root.TryGetProperty("depends", out var dependsProp) && dependsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var dep in dependsProp.EnumerateObject())
                {
                    var depInfo = new ModDependency { ModId = dep.Name };
                    if (dep.Value.ValueKind == JsonValueKind.String)
                    {
                        depInfo.VersionRange = dep.Value.GetString() ?? "";
                    }
                    else if (dep.Value.ValueKind == JsonValueKind.Object)
                    {
                        if (dep.Value.TryGetProperty("version", out var depVer))
                            depInfo.VersionRange = depVer.GetString() ?? "";
                    }
                    meta.Dependencies.Add(depInfo);
                }
            }

            if (root.TryGetProperty("icon", out var iconProp))
            {
                meta.IconPath = iconProp.ValueKind == JsonValueKind.String
                    ? iconProp.GetString()
                    : null;
            }

            return meta;
        }
        catch { return null; }
    }

    private static ModMetadata? ParseQuiltMod(Stream stream)
    {
        try
        {
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            var meta = new ModMetadata { Loader = "Quilt" };

            var loader = root.GetProperty("quilt_loader");
            if (loader.TryGetProperty("id", out var idProp))
                meta.ModId = idProp.GetString() ?? "";

            if (loader.TryGetProperty("version", out var verProp))
                meta.Version = verProp.GetString() ?? "";

            if (root.TryGetProperty("metadata", out var metadata))
            {
                if (metadata.TryGetProperty("name", out var nameProp))
                    meta.Name = nameProp.GetString() ?? "";

                if (metadata.TryGetProperty("description", out var descProp))
                    meta.Description = descProp.GetString() ?? "";

                if (metadata.TryGetProperty("icon", out var iconProp))
                    meta.IconPath = iconProp.ValueKind == JsonValueKind.String ? iconProp.GetString() : null;
            }

            if (loader.TryGetProperty("depends", out var dependsProp))
            {
                foreach (var dep in dependsProp.EnumerateArray())
                {
                    var depInfo = new ModDependency();
                    if (dep.TryGetProperty("id", out var depId))
                        depInfo.ModId = depId.GetString() ?? "";
                    if (dep.TryGetProperty("version", out var depVer))
                        depInfo.VersionRange = depVer.GetString() ?? "";
                    if (dep.TryGetProperty("optional", out var opt))
                        depInfo.IsRequired = !opt.GetBoolean();
                    meta.Dependencies.Add(depInfo);
                }
            }

            return meta;
        }
        catch { return null; }
    }

    private static ModMetadata? ParseForgeMod(string tomlContent, string jarPath)
    {
        try
        {
            var meta = new ModMetadata { Loader = "Forge" };

            var idMatch = Regex.Match(tomlContent, @"modId\s*=\s*""([^""]+)""");
            if (idMatch.Success) meta.ModId = idMatch.Groups[1].Value;

            var verMatch = Regex.Match(tomlContent, @"version\s*=\s*""([^""]+)""");
            if (verMatch.Success) meta.Version = verMatch.Groups[1].Value;

            var nameMatch = Regex.Match(tomlContent, @"displayName\s*=\s*""([^""]+)""");
            if (nameMatch.Success) meta.Name = nameMatch.Groups[1].Value;

            var descMatch = Regex.Match(tomlContent, @"description\s*=\s*""([^""]+)""");
            if (descMatch.Success) meta.Description = descMatch.Groups[1].Value;

            // 依赖解析
            var depSection = Regex.Match(tomlContent, @"\[\[dependencies\.\w+\]\]([\s\S]*?)(?=\[\[|\z)");
            foreach (Match depMatch in Regex.Matches(tomlContent, @"\[\[dependencies\.(\w+)\]\]([\s\S]*?)(?=\[\[|\z)"))
            {
                var depBlock = depMatch.Groups[2].Value;
                var depInfo = new ModDependency { ModId = depMatch.Groups[1].Value };

                var depModId = Regex.Match(depBlock, @"modId\s*=\s*""([^""]+)""");
                if (depModId.Success) depInfo.ModId = depModId.Groups[1].Value;

                var reqMatch = Regex.Match(depBlock, @"mandatory\s*=\s*(true|false)");
                if (reqMatch.Success) depInfo.IsRequired = reqMatch.Groups[1].Value == "true";

                var depVerMatch = Regex.Match(depBlock, @"versionRange\s*=\s*""([^""]+)""");
                if (depVerMatch.Success) depInfo.VersionRange = depVerMatch.Groups[1].Value;

                var reasonMatch = Regex.Match(depBlock, @"reason\s*=\s*""([^""]+)""");
                if (reasonMatch.Success) depInfo.Reason = reasonMatch.Groups[1].Value;

                meta.Dependencies.Add(depInfo);
            }

            // Forge 的 logoFile
            var logoMatch = Regex.Match(tomlContent, @"logoFile\s*=\s*""([^""]+)""");
            if (logoMatch.Success) meta.IconPath = logoMatch.Groups[1].Value;

            return meta;
        }
        catch { return null; }
    }
}
