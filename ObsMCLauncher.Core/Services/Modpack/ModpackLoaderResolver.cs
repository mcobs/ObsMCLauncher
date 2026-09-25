using System;
using System.IO;
using System.Text.Json;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Services.Modpack;

/// <summary>
/// 从版本目录的 <c>{name}.json</c> 里解析出 Minecraft 版本与 Mod 加载器版本。
///
/// <see cref="Minecraft.LocalVersionService.DetectLoaderType"/> 只能给出加载器<b>名字</b>（用于判断"是不是 mod 加载器版本"），
/// 而整合包清单需要<b>版本号</b>（<c>dependencies.forge = 47.2.0</c> / <c>modLoaders[].id = forge-47.2.0</c>），
/// 所以这里单独解析 <c>libraries[].name</c> 的 Maven 坐标。
/// </summary>
public static class ModpackLoaderResolver
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>解析版本目录下的 <c>{versionName}.json</c>；找不到文件时退化为"只有目录名当 MC 版本"。</summary>
    public static ModpackLoaderInfo Resolve(string versionDirectory, string versionName)
    {
        var jsonPath = Path.Combine(versionDirectory, $"{versionName}.json");
        if (File.Exists(jsonPath))
        {
            try
            {
                return ResolveFromJson(File.ReadAllText(jsonPath), versionName);
            }
            catch (Exception ex)
            {
                Utils.DebugLogger.Warn("ModpackExport", $"解析版本 JSON 失败（{jsonPath}）: {ex.Message}");
            }
        }

        // 版本目录里可能只有别的名字的 json（整合包手工解压常见），退一步找一个
        try
        {
            var candidates = Directory.GetFiles(versionDirectory, "*.json", SearchOption.TopDirectoryOnly);
            foreach (var candidate in candidates)
            {
                try
                {
                    var info = ResolveFromJson(File.ReadAllText(candidate), versionName);
                    if (!string.IsNullOrWhiteSpace(info.MinecraftVersion))
                        return info;
                }
                catch
                {
                    // 换下一个候选
                }
            }
        }
        catch (Exception ex)
        {
            Utils.DebugLogger.Warn("ModpackExport", $"扫描版本目录 JSON 失败: {ex.Message}");
        }

        return new ModpackLoaderInfo { MinecraftVersion = versionName };
    }

    /// <summary>从版本 JSON 文本解析（可单测）。</summary>
    public static ModpackLoaderInfo ResolveFromJson(string jsonContent, string fallbackVersionName = "")
    {
        var info = new ModpackLoaderInfo { MinecraftVersion = fallbackVersionName };

        using var document = JsonDocument.Parse(jsonContent, DocumentOptions);
        var root = document.RootElement;

        // 1) Minecraft 版本：inheritsFrom 最可靠；原版包用它自己的 id
        foreach (var key in new[] { "inheritsFrom", "clientVersion", "id" })
        {
            if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    info.MinecraftVersion = text!;
                    break;
                }
            }
        }

        // 2) 加载器版本：扫 libraries[].name 的 Maven 坐标
        if (!root.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Array)
            return info;

        foreach (var library in libraries.EnumerateArray())
        {
            if (!library.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
                continue;

            var coordinate = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(coordinate) || !TryParseCoordinate(coordinate!, out var group, out var artifact, out var version))
                continue;

            var loaderType = ClassifyLoader(group, artifact);
            if (loaderType is null)
                continue;

            // Forge 的版本号形如 1.20.1-47.2.0，CurseForge 只认后段
            var normalized = NormalizeLoaderVersion(loaderType, version, info.MinecraftVersion);

            // 更具体的坐标（forge 本体 > neoforge 的 fancymodloader）优先，先到先得即可
            if (string.IsNullOrWhiteSpace(info.LoaderType))
            {
                info.LoaderType = loaderType;
                info.LoaderVersion = normalized;
            }
        }

        return info;
    }

    /// <summary>解析 Maven 坐标 <c>group:artifact:version[:classifier][@ext]</c>。</summary>
    public static bool TryParseCoordinate(string coordinate, out string group, out string artifact, out string version)
    {
        group = artifact = version = "";
        if (string.IsNullOrWhiteSpace(coordinate))
            return false;

        var at = coordinate.IndexOf('@');
        if (at >= 0)
            coordinate = coordinate.Substring(0, at);

        var parts = coordinate.Split(':');
        if (parts.Length < 3)
            return false;

        group = parts[0];
        artifact = parts[1];
        version = parts[2];
        return !string.IsNullOrWhiteSpace(group) && !string.IsNullOrWhiteSpace(artifact) && !string.IsNullOrWhiteSpace(version);
    }

    private static string? ClassifyLoader(string group, string artifact)
    {
        if (artifact.Equals("forge", StringComparison.OrdinalIgnoreCase))
        {
            if (group.Equals("net.neoforged", StringComparison.OrdinalIgnoreCase))
                return "NeoForge";
            if (group.Equals("net.minecraftforge", StringComparison.OrdinalIgnoreCase))
                return "Forge";
            return null;
        }

        if (artifact.Equals("neoforge", StringComparison.OrdinalIgnoreCase)
            && group.Equals("net.neoforged", StringComparison.OrdinalIgnoreCase))
        {
            return "NeoForge";
        }

        if (artifact.Equals("fabric-loader", StringComparison.OrdinalIgnoreCase)
            && group.Equals("net.fabricmc", StringComparison.OrdinalIgnoreCase))
        {
            return "Fabric";
        }

        if (artifact.Equals("quilt-loader", StringComparison.OrdinalIgnoreCase)
            && group.Equals("org.quiltmc", StringComparison.OrdinalIgnoreCase))
        {
            return "Quilt";
        }

        if (artifact.Equals("OptiFine", StringComparison.OrdinalIgnoreCase)
            && group.Equals("optifine", StringComparison.OrdinalIgnoreCase))
        {
            return "OptiFine";
        }

        return null;
    }

    /// <summary>把 <c>1.20.1-47.2.0</c> 这类带 MC 版本前缀的加载器版本号裁成 CurseForge 认可的 <c>47.2.0</c>。</summary>
    public static string NormalizeLoaderVersion(string? loaderType, string version, string minecraftVersion)
    {
        if (string.IsNullOrWhiteSpace(version))
            return version;

        if (loaderType is not ("Forge" or "NeoForge"))
            return version;

        if (string.IsNullOrWhiteSpace(minecraftVersion))
            return version;

        var prefix = minecraftVersion + "-";
        return version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? version.Substring(prefix.Length)
            : version;
    }
}
