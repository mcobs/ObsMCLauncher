using System.Collections.Generic;

namespace ObsMCLauncher.Core.Models;

/// <summary>导出整合包的目标格式。</summary>
public enum ModpackExportFormat
{
    /// <summary>Modrinth 整合包（.mrpack，清单为 modrinth.index.json）。</summary>
    Modrinth = 0,

    /// <summary>CurseForge 整合包（.zip，清单为 manifest.json）。</summary>
    CurseForge = 1
}

/// <summary>
/// 文件在导出树里的建议状态。语义与 HMCL 的 ModAdviser.ModSuggestion 一致。
/// </summary>
public enum ModpackFileSuggestion
{
    /// <summary>默认勾选（正常的整合包内容）。</summary>
    Suggested = 0,

    /// <summary>可见但默认不勾（存档、选项文件等个人数据）。</summary>
    Normal = 1,

    /// <summary>不出现在导出树里（日志、缓存、其它启动器的文件等）。</summary>
    Hidden = 2
}

/// <summary>导出选项。</summary>
public class ModpackExportOptions
{
    public ModpackExportFormat Format { get; set; } = ModpackExportFormat.Modrinth;

    /// <summary>整合包名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>整合包版本号。</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>作者。CurseForge 格式下必填。</summary>
    public string Author { get; set; } = "";

    /// <summary>简介。仅 Modrinth 清单会写入。</summary>
    public string Description { get; set; } = "";

    /// <summary>整合包主页链接。仅 Modrinth 清单会写入。</summary>
    public string Url { get; set; } = "";

    /// <summary>
    /// 是否联网匹配 Modrinth / CurseForge 上已托管的资源。
    /// true = 命中远端只写下载地址、本地副本不进包；false = 全部文件直接进包（离线可分享）。
    /// </summary>
    public bool ResolveRemoteFiles { get; set; } = true;

    /// <summary>
    /// 要导出的文件集合：相对运行目录的路径（以 '/' 分隔，大小写不敏感）。
    /// 该集合即"白名单"，不在其中的文件一律不导出。
    /// </summary>
    public List<string> IncludePaths { get; set; } = new();

    /// <summary>运行目录（版本隔离时为版本目录，否则为游戏根目录）。</summary>
    public string RunDirectory { get; set; } = "";

    /// <summary>版本目录（versions/{name}），用于解析版本 JSON 与加载器。</summary>
    public string VersionDirectory { get; set; } = "";

    /// <summary>版本号（用于排除 {name}.jar / {name}.json / {name}-natives）。</summary>
    public string VersionName { get; set; } = "";

    /// <summary>输出文件完整路径。</summary>
    public string OutputPath { get; set; } = "";

    /// <summary>打包时的压缩级别（0 = 不压缩，仅存储）。</summary>
    public bool StoreOnlyCompression { get; set; }
}

/// <summary>扫描出的候选文件。</summary>
public class ModpackFileEntry
{
    public string RelativePath { get; set; } = "";

    public string FullPath { get; set; } = "";

    public long Length { get; set; }
}

/// <summary>版本 JSON 里解析出的 Minecraft / 加载器信息。</summary>
public class ModpackLoaderInfo
{
    public string MinecraftVersion { get; set; } = "";

    /// <summary>null / "Forge" / "NeoForge" / "Fabric" / "Quilt" / "OptiFine"。</summary>
    public string? LoaderType { get; set; }

    public string? LoaderVersion { get; set; }

    /// <summary>CurseForge manifest 的 modLoaders[].id，形如 forge-47.2.0。</summary>
    public string? CurseForgeLoaderId =>
        string.IsNullOrWhiteSpace(LoaderType) || string.IsNullOrWhiteSpace(LoaderVersion)
            ? null
            : LoaderType switch
            {
                "Forge" => $"forge-{LoaderVersion}",
                "NeoForge" => $"neoforge-{LoaderVersion}",
                "Fabric" => $"fabric-{LoaderVersion}",
                "Quilt" => $"quilt-{LoaderVersion}",
                _ => null
            };

    /// <summary>Modrinth dependencies 的键，形如 fabric-loader。</summary>
    public string? ModrinthDependencyKey =>
        string.IsNullOrWhiteSpace(LoaderType)
            ? null
            : LoaderType switch
            {
                "Forge" => "forge",
                "NeoForge" => "neoforge",
                "Fabric" => "fabric-loader",
                "Quilt" => "quilt-loader",
                _ => null
            };
}

/// <summary>联网解析到的远端文件信息。</summary>
public class ModpackRemoteFile
{
    /// <summary>Modrinth 下载地址。</summary>
    public string? DownloadUrl { get; set; }

    /// <summary>CurseForge projectID。</summary>
    public int? CurseForgeProjectId { get; set; }

    /// <summary>CurseForge fileID。</summary>
    public int? CurseForgeFileId { get; set; }

    /// <summary>远端文件名（可能与本地不同，仅用于日志）。</summary>
    public string? RemoteFileName { get; set; }
}

/// <summary>导出统计结果。</summary>
public class ModpackExportResult
{
    public string OutputPath { get; set; } = "";

    public int TotalFiles { get; set; }

    /// <summary>写进清单 files[]、不进包的文件数。</summary>
    public int RemoteReferencedFiles { get; set; }

    /// <summary>直接打进 overrides / client-overrides 的文件数。</summary>
    public int PackedFiles { get; set; }

    /// <summary>读取失败被跳过的文件数。</summary>
    public int SkippedFiles { get; set; }

    public long TotalBytes { get; set; }

    public long OutputBytes { get; set; }

    /// <summary>非致命警告（加载器识别失败、文件读不到、远端查询失败等）。</summary>
    public List<string> Warnings { get; set; } = new();

    public ModpackLoaderInfo? Loader { get; set; }
}

/// <summary>导出进度。</summary>
public class ModpackExportProgress
{
    /// <summary>0~100。</summary>
    public double Percentage { get; set; }

    public string Message { get; set; } = "";
}

/// <summary>导出内容树节点。</summary>
public class ModpackExportTreeNode
{
    public string Name { get; set; } = "";

    /// <summary>相对运行目录的路径（<c>/</c> 分隔，无开头斜杠）。</summary>
    public string RelativePath { get; set; } = "";

    public bool IsDirectory { get; set; }

    /// <summary>是否达到深度上限（UI 不展示子节点，勾选即包含全部后代）。</summary>
    public bool IsDepthLimited { get; set; }

    /// <summary>层级（0 = 运行目录的直接子项）。UI 用它决定默认展开到第几层。</summary>
    public int Depth { get; set; }

    /// <summary>目录用途的中文标注（认不出来为空串）。</summary>
    public string Purpose { get; set; } = "";

    /// <summary>必选内容：UI 强制勾选并禁用复选框，导出时也会兜底补上。</summary>
    public bool IsRequired { get; set; }

    public ModpackFileSuggestion Suggestion { get; set; } = ModpackFileSuggestion.Suggested;

    /// <summary>文件大小（仅文件）。</summary>
    public long Length { get; set; }

    /// <summary>含后代在内的文件总数（目录）。</summary>
    public int FileCount { get; set; }

    /// <summary>含后代在内的总字节数（目录）。</summary>
    public long TotalBytes { get; set; }

    public List<ModpackExportTreeNode> Children { get; set; } = new();
}

/// <summary>导出内容扫描结果。</summary>
public class ModpackScanResult
{
    /// <summary>供 UI 展示的树（受深度上限约束）。</summary>
    public List<ModpackExportTreeNode> Roots { get; set; } = new();

    /// <summary>
    /// 全部可导出文件（已剔除黑名单）。即使树因深度上限没有展开到该文件，它也会出现在这里，
    /// 保证"勾选某个深度受限目录"能覆盖到其全部后代。
    /// </summary>
    public List<ModpackFileEntry> AllFiles { get; set; } = new();

    /// <summary>运行目录下被判为黑名单而隐藏的条目数。</summary>
    public int HiddenCount { get; set; }

    /// <summary>读取失败或跳过（reparse point 等）的条目数。</summary>
    public int SkippedCount { get; set; }

    /// <summary>可导出文件总字节数。</summary>
    public long TotalBytes { get; set; }

    public List<string> Warnings { get; set; } = new();
}
