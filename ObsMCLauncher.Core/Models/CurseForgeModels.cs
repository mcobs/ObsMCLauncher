using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ObsMCLauncher.Core.Models;

public class CurseForgeResponse<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("pagination")]
    public CurseForgePagination? Pagination { get; set; }
}

public class CurseForgePagination
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("resultCount")]
    public int ResultCount { get; set; }

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }
}

public class CurseForgeMod
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("gameId")]
    public int GameId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("links")]
    public CurseForgeLinks? Links { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("downloadCount")]
    public int DownloadCount { get; set; }

    [JsonPropertyName("isFeatured")]
    public bool IsFeatured { get; set; }

    [JsonPropertyName("primaryCategoryId")]
    public int PrimaryCategoryId { get; set; }

    [JsonPropertyName("categories")]
    public List<CurseForgeCategory> Categories { get; set; } = new();

    [JsonPropertyName("classId")]
    public int ClassId { get; set; }

    [JsonPropertyName("authors")]
    public List<CurseForgeAuthor> Authors { get; set; } = new();

    [JsonPropertyName("logo")]
    public CurseForgeLogo? Logo { get; set; }

    [JsonPropertyName("screenshots")]
    public List<CurseForgeScreenshot> Screenshots { get; set; } = new();

    [JsonPropertyName("mainFileId")]
    public int MainFileId { get; set; }

    [JsonPropertyName("latestFiles")]
    public List<CurseForgeFile> LatestFiles { get; set; } = new();

    [JsonPropertyName("latestFilesIndexes")]
    public List<CurseForgeFileIndex> LatestFilesIndexes { get; set; } = new();

    [JsonPropertyName("dateCreated")]
    public DateTime DateCreated { get; set; }

    [JsonPropertyName("dateModified")]
    public DateTime DateModified { get; set; }

    [JsonPropertyName("dateReleased")]
    public DateTime DateReleased { get; set; }

    [JsonPropertyName("allowModDistribution")]
    public bool AllowModDistribution { get; set; }

    [JsonPropertyName("gamePopularityRank")]
    public int GamePopularityRank { get; set; }

    [JsonPropertyName("isAvailable")]
    public bool IsAvailable { get; set; }

    [JsonPropertyName("thumbsUpCount")]
    public int ThumbsUpCount { get; set; }
}

public class CurseForgeLinks
{
    [JsonPropertyName("websiteUrl")]
    public string WebsiteUrl { get; set; } = "";

    [JsonPropertyName("wikiUrl")]
    public string? WikiUrl { get; set; }

    [JsonPropertyName("issuesUrl")]
    public string? IssuesUrl { get; set; }

    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; set; }
}

public class CurseForgeCategory
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("gameId")]
    public int GameId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("iconUrl")]
    public string IconUrl { get; set; } = "";

    [JsonPropertyName("dateModified")]
    public DateTime DateModified { get; set; }

    [JsonPropertyName("isClass")]
    public bool IsClass { get; set; }

    [JsonPropertyName("classId")]
    public int ClassId { get; set; }

    [JsonPropertyName("parentCategoryId")]
    public int ParentCategoryId { get; set; }
}

public class CurseForgeAuthor
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

public class CurseForgeLogo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("modId")]
    public int ModId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("thumbnailUrl")]
    public string ThumbnailUrl { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

public class CurseForgeScreenshot
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("modId")]
    public int ModId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("thumbnailUrl")]
    public string ThumbnailUrl { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

public class CurseForgeFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("gameId")]
    public int GameId { get; set; }

    [JsonPropertyName("modId")]
    public int ModId { get; set; }

    [JsonPropertyName("isAvailable")]
    public bool IsAvailable { get; set; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("releaseType")]
    public int ReleaseType { get; set; }

    [JsonPropertyName("fileStatus")]
    public int FileStatus { get; set; }

    [JsonPropertyName("hashes")]
    public List<CurseForgeFileHash> Hashes { get; set; } = new();

    [JsonPropertyName("fileDate")]
    public DateTime FileDate { get; set; }

    [JsonPropertyName("fileLength")]
    public long FileLength { get; set; }

    [JsonPropertyName("downloadCount")]
    public int DownloadCount { get; set; }

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("gameVersions")]
    public List<string> GameVersions { get; set; } = new();

    [JsonPropertyName("sortableGameVersions")]
    public List<CurseForgeSortableGameVersion> SortableGameVersions { get; set; } = new();

    [JsonPropertyName("dependencies")]
    public List<CurseForgeDependency> Dependencies { get; set; } = new();

    [JsonPropertyName("alternateFileId")]
    public int AlternateFileId { get; set; }

    [JsonPropertyName("isServerPack")]
    public bool IsServerPack { get; set; }

    [JsonPropertyName("fileFingerprint")]
    public long FileFingerprint { get; set; }

    [JsonPropertyName("modules")]
    public List<CurseForgeModule> Modules { get; set; } = new();

    public string GetDownloadUrl()
    {
        if (!string.IsNullOrEmpty(DownloadUrl))
            return DownloadUrl;

        return $"https://edge.forgecdn.net/files/{Id / 1000}/{Id % 1000}/{FileName}";
    }
}

public class CurseForgeFileHash
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("algo")]
    public int Algo { get; set; }
}

public class CurseForgeSortableGameVersion
{
    [JsonPropertyName("gameVersionName")]
    public string GameVersionName { get; set; } = "";

    [JsonPropertyName("gameVersionPadded")]
    public string GameVersionPadded { get; set; } = "";

    [JsonPropertyName("gameVersion")]
    public string GameVersion { get; set; } = "";

    [JsonPropertyName("gameVersionReleaseDate")]
    public DateTime GameVersionReleaseDate { get; set; }

    [JsonPropertyName("gameVersionTypeId")]
    public int? GameVersionTypeId { get; set; }
}

public class CurseForgeDependency
{
    [JsonPropertyName("modId")]
    public int ModId { get; set; }

    [JsonPropertyName("relationType")]
    public int RelationType { get; set; }
}

public class CurseForgeModule
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("fingerprint")]
    public long Fingerprint { get; set; }
}

public class CurseForgeFileIndex
{
    [JsonPropertyName("gameVersion")]
    public string GameVersion { get; set; } = "";

    [JsonPropertyName("fileId")]
    public int FileId { get; set; }

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = "";

    [JsonPropertyName("releaseType")]
    public int ReleaseType { get; set; }

    [JsonPropertyName("gameVersionTypeId")]
    public int? GameVersionTypeId { get; set; }

    [JsonPropertyName("modLoader")]
    public int? ModLoader { get; set; }
}

public enum ResourceSource
{
    CurseForge,
    Modrinth
}
