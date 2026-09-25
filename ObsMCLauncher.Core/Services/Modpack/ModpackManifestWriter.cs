using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Services.Modpack;

/// <summary>要写进清单 files[] 的远端引用。</summary>
public class ModpackManifestRemoteFile
{
    public string RelativePath { get; set; } = "";

    public long FileSize { get; set; }

    public string? ModrinthDownloadUrl { get; set; }

    public string? Sha1 { get; set; }

    public string? Sha512 { get; set; }

    public int? CurseForgeProjectId { get; set; }

    public int? CurseForgeFileId { get; set; }
}

/// <summary>
/// 生成整合包清单（<c>modrinth.index.json</c> / CurseForge <c>manifest.json</c>）。
/// 字段名必须与 <c>ModpackInstallService</c> 的读取模型严格对应，否则"导出能成功、导入装不上"。
/// </summary>
public static class ModpackManifestWriter
{
    public const string ModrinthIndexFileName = "modrinth.index.json";
    public const string CurseForgeManifestFileName = "manifest.json";

    /// <summary>两份清单都用 <c>overrides</c>：本工程的安装器就是按这个目录名解压的。</summary>
    public const string OverridesDirectoryName = "overrides";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // 让中文保持原样而不是 \uXXXX（清单是给人看的）
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static string BuildModrinthIndex(
        ModpackExportOptions options,
        ModpackLoaderInfo loader,
        IReadOnlyList<ModpackManifestRemoteFile> remoteFiles)
    {
        var dto = new ModrinthIndexDto
        {
            Name = options.Name,
            VersionId = options.Version,
            Summary = string.IsNullOrWhiteSpace(options.Description) ? null : options.Description
        };

        foreach (var file in remoteFiles)
        {
            if (file.ModrinthDownloadUrl == null || file.Sha1 == null || file.Sha512 == null)
                continue;

            dto.Files.Add(new ModrinthFileDto
            {
                Path = file.RelativePath,
                FileSize = file.FileSize,
                Hashes = new Dictionary<string, string>
                {
                    ["sha1"] = file.Sha1,
                    ["sha512"] = file.Sha512
                },
                Downloads = new List<string> { file.ModrinthDownloadUrl }
            });
        }

        dto.Dependencies["minecraft"] = loader.MinecraftVersion;
        if (loader.ModrinthDependencyKey is { } key && !string.IsNullOrWhiteSpace(loader.LoaderVersion))
            dto.Dependencies[key] = loader.LoaderVersion!;

        return JsonSerializer.Serialize(dto, SerializerOptions);
    }

    public static string BuildCurseForgeManifest(
        ModpackExportOptions options,
        ModpackLoaderInfo loader,
        IReadOnlyList<ModpackManifestRemoteFile> remoteFiles)
    {
        var dto = new CurseForgeManifestDto
        {
            Name = options.Name,
            Version = options.Version,
            Author = options.Author,
            Overrides = OverridesDirectoryName,
            Minecraft = new CurseForgeMinecraftDto { Version = loader.MinecraftVersion }
        };

        if (loader.CurseForgeLoaderId is { } loaderId)
            dto.Minecraft.ModLoaders.Add(new CurseForgeModLoaderDto { Id = loaderId, Primary = true });

        foreach (var file in remoteFiles)
        {
            if (file.CurseForgeProjectId is not { } projectId || file.CurseForgeFileId is not { } fileId)
                continue;

            dto.Files.Add(new CurseForgeFileRefDto
            {
                ProjectId = projectId,
                FileId = fileId,
                Required = true
            });
        }

        return JsonSerializer.Serialize(dto, SerializerOptions);
    }

    /// <summary>清单作为 zip 条目写盘时的字节（UTF-8，无 BOM）。</summary>
    public static byte[] ToUtf8Bytes(string text) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);

    // ===== 序列化用 DTO（字段名与安装器读取模型对齐）=====

    private sealed class ModrinthIndexDto
    {
        [JsonPropertyName("game")]
        public string Game { get; set; } = "minecraft";

        [JsonPropertyName("formatVersion")]
        public int FormatVersion { get; set; } = 1;

        [JsonPropertyName("versionId")]
        public string VersionId { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("files")]
        public List<ModrinthFileDto> Files { get; set; } = new();

        [JsonPropertyName("dependencies")]
        public Dictionary<string, string> Dependencies { get; set; } = new();
    }

    private sealed class ModrinthFileDto
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        /// <summary>形如 { "sha1": "...", "sha512": "..." }。</summary>
        [JsonPropertyName("hashes")]
        public Dictionary<string, string> Hashes { get; set; } = new();

        [JsonPropertyName("downloads")]
        public List<string> Downloads { get; set; } = new();

        [JsonPropertyName("fileSize")]
        public long FileSize { get; set; }
    }

    private sealed class CurseForgeManifestDto
    {
        [JsonPropertyName("minecraft")]
        public CurseForgeMinecraftDto Minecraft { get; set; } = new();

        [JsonPropertyName("manifestType")]
        public string ManifestType { get; set; } = "minecraftModpack";

        [JsonPropertyName("manifestVersion")]
        public int ManifestVersion { get; set; } = 1;

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("author")]
        public string Author { get; set; } = "";

        [JsonPropertyName("files")]
        public List<CurseForgeFileRefDto> Files { get; set; } = new();

        [JsonPropertyName("overrides")]
        public string Overrides { get; set; } = OverridesDirectoryName;
    }

    private sealed class CurseForgeMinecraftDto
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("modLoaders")]
        public List<CurseForgeModLoaderDto> ModLoaders { get; set; } = new();
    }

    private sealed class CurseForgeModLoaderDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("primary")]
        public bool Primary { get; set; }
    }

    private sealed class CurseForgeFileRefDto
    {
        [JsonPropertyName("projectID")]
        public int ProjectId { get; set; }

        [JsonPropertyName("fileID")]
        public int FileId { get; set; }

        [JsonPropertyName("required")]
        public bool Required { get; set; } = true;
    }
}
