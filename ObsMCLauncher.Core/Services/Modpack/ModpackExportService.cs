using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services.Modpack;

/// <summary>
/// 整合包导出：扫描结果的白名单 + 版本信息 → 一个可被第三方启动器导入的压缩包。
///
/// 零临时目录的直写流程（顺序是刻意设计的）：
/// <code>
/// 校验 → 解析加载器 → 收集文件 → 算哈希 → 联网匹配 → 在内存里定稿清单 → 开 zip：先写清单，再写 overrides
/// </code>
/// 清单内容只依赖"哈希 + 网络结果"，在打开 zip 之前就已经完全确定，所以 <see cref="ZipArchive"/> 的单向写入不是问题，
/// 也就不需要 PCL 那种"先打一遍再套一层"的二次压缩。
/// </summary>
public static class ModpackExportService
{
    private const string LogCategory = "ModpackExport";

    /// <summary>各阶段在总进度里的占比。</summary>
    private const double HashStageStart = 5;
    private const double HashStageEnd = 45;
    private const double RemoteStageStart = 45;
    private const double RemoteStageEnd = 70;
    private const double WriteStageStart = 70;
    private const double WriteStageEnd = 99;

    public static async Task<ModpackExportResult> ExportAsync(
        ModpackExportOptions options,
        IProgress<ModpackExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var result = new ModpackExportResult { OutputPath = options.OutputPath };
        Report(progress, 0, "正在准备导出...");

        Validate(options);

        var runDirectory = Path.GetFullPath(options.RunDirectory);
        var outputPath = Path.GetFullPath(options.OutputPath);

        // 加载器版本
        result.Loader = ModpackLoaderResolver.Resolve(
            string.IsNullOrWhiteSpace(options.VersionDirectory) ? runDirectory : options.VersionDirectory,
            options.VersionName);

        if (string.IsNullOrWhiteSpace(result.Loader.LoaderType))
        {
            result.Warnings.Add("未能识别出 Mod 加载器，导出的整合包导入后只会安装原版；如确实不是加载器版本可忽略。");
        }
        else if (result.Loader.LoaderType == "OptiFine")
        {
            result.Warnings.Add("OptiFine 无法写入整合包清单，导入后需要自行安装。");
        }

        Report(progress, 2, $"已识别 Minecraft {result.Loader.MinecraftVersion}");

        // 收集文件
        var entries = CollectEntries(options, runDirectory, outputPath, result);
        if (entries.Count == 0)
            throw new InvalidOperationException("没有选中任何可导出的文件");

        result.TotalFiles = entries.Count;
        result.TotalBytes = entries.Sum(e => e.Length);

        Report(progress, HashStageStart, $"已选中 {entries.Count} 个文件");

        // 算哈希（关掉联网匹配就完全不需要哈希）
        Dictionary<string, ModpackFileHashes> hashesByPath = new(StringComparer.OrdinalIgnoreCase);
        if (options.ResolveRemoteFiles)
        {
            var hashTargets = entries
                .Where(e => ModpackFileFilter.IsPotentiallyRemoteResource(e.RelativePath))
                .ToList();

            if (hashTargets.Count > 0)
            {
                for (var i = 0; i < hashTargets.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = hashTargets[i];
                    try
                    {
                        hashesByPath[entry.RelativePath] = ModpackFileHasher.ComputeForLookup(entry.FullPath, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        result.SkippedFiles++;
                        result.Warnings.Add($"无法读取文件（已按原样打包）：{entry.RelativePath}（{ex.Message}）");
                        DebugLogger.Warn(LogCategory, $"读文件失败: {entry.RelativePath} - {ex.Message}");
                    }

                    var done = HashStageStart + (i + 1) * (HashStageEnd - HashStageStart) / hashTargets.Count;
                    Report(progress, done, $"正在计算文件指纹 ({i + 1}/{hashTargets.Count})");
                }
            }
        }

        // 联网匹配
        Dictionary<string, ModpackRemoteFile> remoteFiles = new(StringComparer.OrdinalIgnoreCase);
        if (options.ResolveRemoteFiles && hashesByPath.Count > 0)
        {
            Report(progress, RemoteStageStart, "正在匹配 Modrinth / CurseForge 上已托管的资源...");
            remoteFiles = await RemoteFileResolver.ResolveAsync(
                hashesByPath,
                options.Format,
                (done, total) =>
                {
                    if (total <= 0)
                        return;
                    var p = RemoteStageStart + done * (RemoteStageEnd - RemoteStageStart) / total;
                    Report(progress, p, $"正在匹配已托管资源 ({done}/{total})");
                },
                cancellationToken).ConfigureAwait(false);
        }

        // 组装清单条目
        var manifestEntries = new List<ModpackManifestRemoteFile>();
        var packedEntries = new List<ModpackFileEntry>();
        var notDownloadable = 0;

        foreach (var entry in entries)
        {
            var remote = TryBuildManifestEntry(entry, remoteFiles, hashesByPath, options.Format);
            if (remote != null)
            {
                manifestEntries.Add(remote);
                result.RemoteReferencedFiles++;
            }
            else
            {
                if (remoteFiles.ContainsKey(entry.RelativePath))
                    notDownloadable++;
                packedEntries.Add(entry);
                result.PackedFiles++;
            }
        }

        if (notDownloadable > 0)
        {
            result.Warnings.Add($"有 {notDownloadable} 个文件在托管平台上禁止第三方下载，已直接打包。");
        }

        // 写包
        Report(progress, WriteStageStart, "正在写入整合包...");
        try
        {
            await WriteArchiveAsync(options, result, manifestEntries, packedEntries, outputPath, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            TryDelete(outputPath);
            throw;
        }

        try
        {
            result.OutputBytes = new FileInfo(outputPath).Length;
        }
        catch
        {
            result.OutputBytes = 0;
        }

        Report(progress, 100, "导出完成");
        DebugLogger.Info(LogCategory,
            $"导出完成：{options.Format} {options.Name} {options.Version}，" +
            $"共 {result.TotalFiles} 个文件（引用远端 {result.RemoteReferencedFiles}、打包 {result.PackedFiles}）");

        return result;
    }

    private static void Validate(ModpackExportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RunDirectory))
            throw new InvalidOperationException("未指定运行目录");
        if (!Directory.Exists(options.RunDirectory))
            throw new DirectoryNotFoundException($"运行目录不存在：{options.RunDirectory}");
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new InvalidOperationException("未指定导出位置");
        if (string.IsNullOrWhiteSpace(options.Name))
            throw new InvalidOperationException("整合包名称不能为空");
        if (string.IsNullOrWhiteSpace(options.Version))
            throw new InvalidOperationException("整合包版本号不能为空");
        if (options.Format == ModpackExportFormat.CurseForge && string.IsNullOrWhiteSpace(options.Author))
            throw new InvalidOperationException("CurseForge 格式必须填写作者");
        if (options.IncludePaths.Count == 0)
            throw new InvalidOperationException("没有选中任何要导出的内容");

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
        if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);
    }

    /// <summary>把白名单展开成实际文件列表（去重、挡住越界路径、排除导出目标自身）。</summary>
    private static List<ModpackFileEntry> CollectEntries(
        ModpackExportOptions options,
        string runDirectory,
        string outputPath,
        ModpackExportResult result)
    {
        var entries = new List<ModpackFileEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var runPrefix = EnsureTrailingSeparator(runDirectory);
        // 显式传入的**单个文件**不过滤（用户可能就是要在 options.txt 上打勾），
        // 但"按目录展开"必须过滤 —— 否则勾一个 mods/ 就会把 mods/debug.log、*.old 之类一起带走
        var blackList = ModpackFileFilter.BuildBlackList(options.VersionName);

        foreach (var rawPath in options.IncludePaths)
        {
            var relative = ModpackFileFilter.NormalizeRelativePath(rawPath);
            if (string.IsNullOrEmpty(relative))
                continue;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Path.Combine(runDirectory, relative));
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"路径非法，已跳过：{rawPath}（{ex.Message}）");
                continue;
            }

            // 挡住 ../ 越界与目录联接导致的越界
            if (!fullPath.StartsWith(runPrefix, StringComparison.OrdinalIgnoreCase))
            {
                result.Warnings.Add($"跳过运行目录之外的文件：{rawPath}");
                continue;
            }

            if (string.Equals(fullPath, outputPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (Directory.Exists(fullPath))
            {
                foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
                {
                    var relativeToRun = ModpackFileFilter.MakeRelativePath(runDirectory, file);
                    if (ModpackFileFilter.Matches(blackList, relativeToRun))
                        continue;

                    AddFile(file, runDirectory, outputPath, result, entries, seen);
                }
                continue;
            }

            AddFile(fullPath, runDirectory, outputPath, result, entries, seen);
        }

        // 必选内容兜底：就算调用方漏把必选路径放进白名单，也一定要导出
        // （UI 侧已经"强制勾选 + 禁用复选框"，这里是给其它调用方 / 将来改动上的第二道保险）
        foreach (var required in ModpackFileFilter.RequiredList)
        {
            var requiredPath = Path.GetFullPath(Path.Combine(runDirectory, required));
            if (!requiredPath.StartsWith(runPrefix, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(requiredPath))
                continue;

            foreach (var file in Directory.EnumerateFiles(requiredPath, "*", SearchOption.AllDirectories))
            {
                var relative = ModpackFileFilter.MakeRelativePath(runDirectory, file);
                if (ModpackFileFilter.Matches(blackList, relative))
                    continue;

                AddFile(file, runDirectory, outputPath, result, entries, seen);
            }
        }

        entries.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    private static void AddFile(
        string fullPath,
        string runDirectory,
        string outputPath,
        ModpackExportResult result,
        List<ModpackFileEntry> entries,
        HashSet<string> seen)
    {
        try
        {
            if (!File.Exists(fullPath))
            {
                result.Warnings.Add($"文件不存在，已跳过：{ModpackFileFilter.MakeRelativePath(runDirectory, fullPath)}");
                return;
            }

            var absolute = Path.GetFullPath(fullPath);
            if (string.Equals(absolute, outputPath, StringComparison.OrdinalIgnoreCase))
                return;

            var relative = ModpackFileFilter.MakeRelativePath(runDirectory, absolute);
            if (string.IsNullOrEmpty(relative) || !seen.Add(relative))
                return;

            entries.Add(new ModpackFileEntry
            {
                RelativePath = relative,
                FullPath = absolute,
                Length = new FileInfo(absolute).Length
            });
        }
        catch (Exception ex)
        {
            result.SkippedFiles++;
            result.Warnings.Add($"无法读取文件信息：{fullPath}（{ex.Message}）");
        }
    }

    /// <summary>命中远端且该格式真的用得上时，产出一条清单条目；否则返回 null（该文件进包）。</summary>
    private static ModpackManifestRemoteFile? TryBuildManifestEntry(
        ModpackFileEntry entry,
        IReadOnlyDictionary<string, ModpackRemoteFile> remoteFiles,
        IReadOnlyDictionary<string, ModpackFileHashes> hashesByPath,
        ModpackExportFormat format)
    {
        if (!remoteFiles.TryGetValue(entry.RelativePath, out var remote))
            return null;

        if (format == ModpackExportFormat.Modrinth)
        {
            // mrpack 的 files[] 需要 sha1 + sha512 + 可下载地址，缺一不可
            if (string.IsNullOrWhiteSpace(remote.DownloadUrl))
                return null;
            if (!hashesByPath.TryGetValue(entry.RelativePath, out var hashes))
                return null;

            return new ModpackManifestRemoteFile
            {
                RelativePath = entry.RelativePath,
                FileSize = entry.Length,
                ModrinthDownloadUrl = remote.DownloadUrl,
                Sha1 = hashes.Sha1,
                Sha512 = hashes.Sha512
            };
        }

        if (remote.CurseForgeProjectId is not { } projectId || remote.CurseForgeFileId is not { } fileId)
            return null;

        return new ModpackManifestRemoteFile
        {
            RelativePath = entry.RelativePath,
            FileSize = entry.Length,
            CurseForgeProjectId = projectId,
            CurseForgeFileId = fileId
        };
    }

    private static async Task WriteArchiveAsync(
        ModpackExportOptions options,
        ModpackExportResult result,
        IReadOnlyList<ModpackManifestRemoteFile> manifestEntries,
        IReadOnlyList<ModpackFileEntry> packedEntries,
        string outputPath,
        IProgress<ModpackExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var compression = options.StoreOnlyCompression ? CompressionLevel.NoCompression : CompressionLevel.Optimal;

        await using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        // 1) 清单必须先写（ZipArchive 只能顺序追加，写完就不能回头改）
        var (manifestName, manifestText) = options.Format == ModpackExportFormat.Modrinth
            ? (ModpackManifestWriter.ModrinthIndexFileName,
                ModpackManifestWriter.BuildModrinthIndex(options, result.Loader!, manifestEntries))
            : (ModpackManifestWriter.CurseForgeManifestFileName,
                ModpackManifestWriter.BuildCurseForgeManifest(options, result.Loader!, manifestEntries));

        var manifestEntry = archive.CreateEntry(manifestName, CompressionLevel.Optimal);
        await using (var entryStream = manifestEntry.Open())
        {
            var bytes = ModpackManifestWriter.ToUtf8Bytes(manifestText);
            await entryStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        // 2) 其余文件进 overrides/
        for (var i = 0; i < packedEntries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = packedEntries[i];
            var entryName = ModpackManifestWriter.OverridesDirectoryName + "/" + entry.RelativePath;

            try
            {
                var zipEntry = archive.CreateEntry(entryName, compression);
                var lastWrite = File.GetLastWriteTime(entry.FullPath);
                if (lastWrite.Year is >= 1980 and <= 2107)
                    zipEntry.LastWriteTime = lastWrite;

                await using var entryStream = zipEntry.Open();
                await using var source = new FileStream(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    128 * 1024, useAsync: true);
                await source.CopyToAsync(entryStream, 128 * 1024, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 游戏运行中文件被占用是常见情况：跳过并记账，不中断整次导出
                result.SkippedFiles++;
                result.Warnings.Add($"打包失败（已跳过）：{entry.RelativePath}（{ex.Message}）");
                DebugLogger.Warn(LogCategory, $"打包失败: {entry.RelativePath} - {ex.Message}");
            }

            var done = WriteStageStart + (i + 1) * (WriteStageEnd - WriteStageStart) / Math.Max(1, packedEntries.Count);
            Report(progress, done, $"正在打包 ({i + 1}/{packedEntries.Count}) {Path.GetFileName(entry.RelativePath)}");
        }

        archive.Dispose();
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string EnsureTrailingSeparator(string directory)
        => directory.EndsWith(Path.DirectorySeparatorChar) || directory.EndsWith(Path.AltDirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            DebugLogger.Warn(LogCategory, $"清理未完成的导出文件失败: {ex.Message}");
        }
    }

    private static void Report(IProgress<ModpackExportProgress>? progress, double percentage, string message)
        => progress?.Report(new ModpackExportProgress { Percentage = percentage, Message = message });
}
