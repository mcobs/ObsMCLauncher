using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ObsMCLauncher.Core.Services.Modrinth;

public class ModrinthSearchResponse
{
    [JsonPropertyName("hits")]
    public List<ModrinthSearchHit> Hits { get; set; } = new();

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("total_hits")]
    public int TotalHits { get; set; }
}

public class ModrinthSearchHit
{
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("downloads")]
    public int Downloads { get; set; }

    [JsonPropertyName("date_modified")]
    public DateTime DateModified { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }
}

public class ModrinthProject
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }

    /// <summary>mod / modpack / resourcepack / shader / datapack / plugin。老缓存里可能没有该字段，故可空</summary>
    [JsonPropertyName("project_type")]
    public string? ProjectType { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("downloads")]
    public int Downloads { get; set; }

    [JsonPropertyName("updated")]
    public DateTime DateModified { get; set; }
}

public class ModrinthVersion
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version_number")]
    public string VersionNumber { get; set; } = "";

    [JsonPropertyName("date_published")]
    public DateTime DatePublished { get; set; }

    [JsonPropertyName("game_versions")]
    public List<string> GameVersions { get; set; } = new();

    [JsonPropertyName("loaders")]
    public List<string> Loaders { get; set; } = new();

    [JsonPropertyName("files")]
    public List<ModrinthVersionFile> Files { get; set; } = new();

    [JsonPropertyName("dependencies")]
    public List<ModrinthDependency> Dependencies { get; set; } = new();
}

public class ModrinthDependency
{
    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }

    [JsonPropertyName("version_id")]
    public string? VersionId { get; set; }

    [JsonPropertyName("dependency_type")]
    public string DependencyType { get; set; } = "";

    public bool IsRequired => string.Equals(DependencyType, "required", StringComparison.OrdinalIgnoreCase);
    public bool IsOptional => string.Equals(DependencyType, "optional", StringComparison.OrdinalIgnoreCase);
    public bool IsIncompatible => string.Equals(DependencyType, "incompatible", StringComparison.OrdinalIgnoreCase);
}

public class ModrinthVersionFile
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }

    /// <summary>形如 { "sha1": "...", "sha512": "..." }。用于在校验本地文件是否真的对应这个版本。</summary>
    [JsonPropertyName("hashes")]
    public Dictionary<string, string>? Hashes { get; set; }
}

/// <summary>POST /v2/version_files 的请求体。</summary>
public class ModrinthHashLookupRequest
{
    [JsonPropertyName("hashes")]
    public List<string> Hashes { get; set; } = new();

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = "sha1";
}
