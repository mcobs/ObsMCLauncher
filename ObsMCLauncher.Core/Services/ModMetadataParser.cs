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

            var idMatch = ForgeModIdRegex.Match(tomlContent);
            if (idMatch.Success) meta.ModId = idMatch.Groups[1].Value;

            var verMatch = ForgeVersionRegex.Match(tomlContent);
            if (verMatch.Success) meta.Version = verMatch.Groups[1].Value;

            var nameMatch = ForgeDisplayNameRegex.Match(tomlContent);
            if (nameMatch.Success) meta.Name = nameMatch.Groups[1].Value;

            var descMatch = ForgeDescriptionRegex.Match(tomlContent);
            if (descMatch.Success) meta.Description = descMatch.Groups[1].Value;

            // 依赖解析
            foreach (Match depMatch in ForgeDependenciesRegex.Matches(tomlContent))
            {
                var depBlock = depMatch.Groups[2].Value;
                var depInfo = new ModDependency { ModId = depMatch.Groups[1].Value };

                var depModId = ForgeModIdRegex.Match(depBlock);
                if (depModId.Success) depInfo.ModId = depModId.Groups[1].Value;

                var reqMatch = ForgeMandatoryRegex.Match(depBlock);
                if (reqMatch.Success) depInfo.IsRequired = reqMatch.Groups[1].Value == "true";

                var depVerMatch = ForgeVersionRangeRegex.Match(depBlock);
                if (depVerMatch.Success) depInfo.VersionRange = depVerMatch.Groups[1].Value;

                var reasonMatch = ForgeReasonRegex.Match(depBlock);
                if (reasonMatch.Success) depInfo.Reason = reasonMatch.Groups[1].Value;

                meta.Dependencies.Add(depInfo);
            }

            // Forge 的 logoFile
            var logoMatch = ForgeLogoFileRegex.Match(tomlContent);
            if (logoMatch.Success) meta.IconPath = logoMatch.Groups[1].Value;

            return meta;
        }
        catch { return null; }
    }

    private static readonly Regex ForgeModIdRegex = new(@"modId\s*=\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ForgeVersionRegex = new(@"version\s*=\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ForgeDisplayNameRegex = new(@"displayName\s*=\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ForgeDescriptionRegex = new(@"description\s*=\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ForgeDependenciesRegex = new(@"\[\[dependencies\.(\w+)\]\]([\s\S]*?)(?=\[\[|\z)", RegexOptions.Compiled);
    private static readonly Regex ForgeMandatoryRegex = new(@"mandatory\s*=\s*(true|false)", RegexOptions.Compiled);
    private static readonly Regex ForgeVersionRangeRegex = new(@"versionRange\s*=\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ForgeReasonRegex = new(@"reason\s*=\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ForgeLogoFileRegex = new(@"logoFile\s*=\s*""([^""]+)""", RegexOptions.Compiled);
}
