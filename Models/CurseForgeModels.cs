using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ObsMCLauncher.Models
{
    /// <summary>
    /// CurseForge API 响应包装
    /// </summary>
    public class CurseForgeResponse<T>
    {
        [JsonPropertyName("data")]
        public T? Data { get; set; }

        [JsonPropertyName("pagination")]
        public CurseForgePagination? Pagination { get; set; }
    }

    /// <summary>
    /// CurseForge 分页信息
    /// </summary>
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

    /// <summary>
    /// CurseForge Mod/资源包/整合包等
    /// </summary>
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

    /// <summary>
    /// CurseForge 链接
    /// </summary>
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

    /// <summary>
    /// CurseForge 分类
    /// </summary>
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

    /// <summary>
    /// CurseForge 作者
    /// </summary>
    public class CurseForgeAuthor
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }

    /// <summary>
    /// CurseForge Logo
    /// </summary>
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

    /// <summary>
    /// CurseForge 截图
    /// </summary>
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

    /// <summary>
    /// CurseForge 文件
    /// </summary>
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
        public int ReleaseType { get; set; } // 1=Release, 2=Beta, 3=Alpha

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

        /// <summary>
        /// 获取下载链接（处理null的情况）
        /// </summary>
        public string GetDownloadUrl()
        {
            if (!string.IsNullOrEmpty(DownloadUrl))
                return DownloadUrl;

            // 如果 downloadUrl 为 null，使用备用 CDN 链接
            return $"https://edge.forgecdn.net/files/{Id / 1000}/{Id % 1000}/{FileName}";
        }
    }

    /// <summary>
    /// CurseForge 文件哈希
    /// </summary>
    public class CurseForgeFileHash
    {
        [JsonPropertyName("value")]
        public string Value { get; set; } = "";

        [JsonPropertyName("algo")]
        public int Algo { get; set; } // 1=Sha1, 2=Md5
    }

    /// <summary>
    /// CurseForge 可排序游戏版本
    /// </summary>
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

    /// <summary>
    /// CurseForge 依赖
    /// </summary>
    public class CurseForgeDependency
    {
        [JsonPropertyName("modId")]
        public int ModId { get; set; }

        [JsonPropertyName("relationType")]
        public int RelationType { get; set; } // 1=EmbeddedLibrary, 2=OptionalDependency, 3=RequiredDependency, 4=Tool, 5=Incompatible, 6=Include
    }

    /// <summary>
    /// CurseForge 模块
    /// </summary>
    public class CurseForgeModule
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("fingerprint")]
        public long Fingerprint { get; set; }
    }

    /// <summary>
    /// CurseForge 文件索引
    /// </summary>
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
        public int? ModLoader { get; set; } // 0=Any, 1=Forge, 2=Cauldron, 3=LiteLoader, 4=Fabric, 5=Quilt, 6=NeoForge
    }

    /// <summary>
    /// 资源下载源枚举
    /// </summary>
    public enum ResourceSource
    {
        CurseForge,
        Modrinth
    }
}

