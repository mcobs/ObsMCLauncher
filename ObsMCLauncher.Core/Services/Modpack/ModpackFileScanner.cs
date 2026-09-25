using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Services.Modpack;

/// <summary>
/// 枚举运行目录，产出「可导出文件全集」+「供 UI 展示的树」。
///
/// 树受深度上限与节点预算约束（否则 <c>config/</c> 下几千个文件会把 UI 拖死），
/// 但导出<b>必须</b>覆盖到树没有展开的文件。所以两者的关系是：
/// <list type="bullet">
/// <item>任何被截断的目录都会在 <see cref="CollectDescendants"/> 里把<b>全部后代文件</b>登记进
/// <see cref="ModpackScanResult.AllFiles"/>，并标记 <see cref="ModpackExportTreeNode.IsDepthLimited"/>；</item>
/// <item>勾选这种目录节点即代表包含其全部后代 —— 不会出现"树上看不到、导出也漏掉"的文件。</item>
/// </list>
/// </summary>
public static class ModpackFileScanner
{
    /// <summary>默认深度上限（0 = 运行目录的直接子项）。</summary>
    public const int DefaultMaxDepth = 3;

    /// <summary>
    /// 单次扫描展开的目录节点数上限。到达上限后剩下的目录不再展开（改为整棵登记文件），
    /// <b>只影响 UI 树的大小，不影响导出内容的完整性</b>。
    /// </summary>
    public const int DefaultMaxNodes = 4000;

    public static ModpackScanResult Scan(
        ModpackExportOptions options,
        int maxDepth = DefaultMaxDepth,
        int maxNodes = DefaultMaxNodes,
        CancellationToken cancellationToken = default)
    {
        var runDirectory = options.RunDirectory;
        if (string.IsNullOrWhiteSpace(runDirectory) || !Directory.Exists(runDirectory))
            throw new DirectoryNotFoundException($"运行目录不存在: {runDirectory}");

        var context = new ScanContext
        {
            RunDirectory = Path.GetFullPath(runDirectory),
            BlackList = ModpackFileFilter.BuildBlackList(options.VersionName),
            OutputFullPath = string.IsNullOrWhiteSpace(options.OutputPath)
                ? null
                : SafeGetFullPath(options.OutputPath),
            MaxDepth = maxDepth,
            NodeBudget = maxNodes,
            CancellationToken = cancellationToken
        };

        var result = new ModpackScanResult();

        var roots = new List<ModpackExportTreeNode>();
        foreach (var child in ListEntries(context.RunDirectory, result))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = BuildNode(child, context, 0, result);
            if (node != null)
                roots.Add(node);
        }

        SortNodes(roots);

        result.Roots = roots;
        result.TotalBytes = result.AllFiles.Sum(f => f.Length);
        return result;
    }

    private sealed class ScanContext
    {
        public string RunDirectory { get; init; } = "";
        public IReadOnlyList<string> BlackList { get; init; } = Array.Empty<string>();
        public string? OutputFullPath { get; init; }
        public int MaxDepth { get; init; }
        public int NodeBudget { get; set; }
        public required CancellationToken CancellationToken { get; init; }
    }

    private static ModpackExportTreeNode? BuildNode(string fullPath, ScanContext context, int depth, ModpackScanResult result)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        string relativePath;
        try
        {
            relativePath = ModpackFileFilter.MakeRelativePath(context.RunDirectory, fullPath);
        }
        catch (Exception ex)
        {
            result.SkippedCount++;
            result.Warnings.Add($"路径无法相对化，已跳过：{fullPath}（{ex.Message}）");
            return null;
        }

        if (string.IsNullOrEmpty(relativePath))
            return null;

        var isDirectory = Directory.Exists(fullPath);

        // 导出目标自身绝不能进包（重复导出时上一次的产物就躺在旁边）
        if (!isDirectory && IsOutputFile(fullPath, context.OutputFullPath))
            return null;

        var suggestion = ModpackFileFilter.GetSuggestion(relativePath, isDirectory, context.BlackList);
        if (suggestion == ModpackFileSuggestion.Hidden)
        {
            result.HiddenCount++;
            return null;
        }

        if (!isDirectory)
        {
            long length;
            try
            {
                length = new FileInfo(fullPath).Length;
            }
            catch
            {
                result.SkippedCount++;
                return null;
            }

            result.AllFiles.Add(new ModpackFileEntry
            {
                RelativePath = relativePath,
                FullPath = fullPath,
                Length = length
            });

            return new ModpackExportTreeNode
            {
                Name = Path.GetFileName(fullPath),
                RelativePath = relativePath,
                IsDirectory = false,
                Suggestion = suggestion,
                IsRequired = ModpackFileFilter.IsRequired(relativePath),
                Depth = depth,
                Purpose = ModpackContentPurpose.DescribeFile(relativePath),
                Length = length,
                FileCount = 1,
                TotalBytes = length
            };
        }

        // 目录联接 / 符号链接：不跟进，避免枚举爆炸或成环
        if (IsReparsePoint(fullPath))
        {
            result.SkippedCount++;
            result.Warnings.Add($"跳过了指向其它位置的目录联接：{relativePath}");
            return null;
        }

        var node = new ModpackExportTreeNode
        {
            Name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            RelativePath = relativePath,
            IsDirectory = true,
            Suggestion = suggestion,
            IsRequired = ModpackFileFilter.IsRequired(relativePath),
            Depth = depth,
            Purpose = ModpackContentPurpose.DescribeFolder(relativePath)
        };

        // 到达深度上限或节点预算耗尽：不展开子节点，但把整棵子树的文件全部登记
        if (depth + 1 >= context.MaxDepth || context.NodeBudget <= 0)
        {
            CollectDescendants(fullPath, relativePath, context, result, node);
            node.IsDepthLimited = true;
            return node.FileCount > 0 ? node : null;
        }

        context.NodeBudget--;

        var children = new List<ModpackExportTreeNode>();
        foreach (var child in ListEntries(fullPath, result))
        {
            var childNode = BuildNode(child, context, depth + 1, result);
            if (childNode != null)
                children.Add(childNode);
        }

        SortNodes(children);
        node.Children = children;
        node.FileCount = children.Sum(c => c.FileCount);
        node.TotalBytes = children.Sum(c => c.TotalBytes);

        // 空目录不进树（整合包里的空目录没有意义）
        return node.FileCount > 0 ? node : null;
    }

    /// <summary>把目录的全部后代文件登记进 AllFiles，并累加到节点的计数上。</summary>
    private static void CollectDescendants(
        string directory,
        string relativePath,
        ScanContext context,
        ModpackScanResult result,
        ModpackExportTreeNode node)
    {
        foreach (var file in EnumerateFilesRecursive(directory, context, result))
        {
            var relative = ModpackFileFilter.MakeRelativePath(directory, file);
            if (string.IsNullOrEmpty(relative))
                continue;

            if (IsOutputFile(file, context.OutputFullPath))
                continue;

            long length;
            try
            {
                length = new FileInfo(file).Length;
            }
            catch
            {
                result.SkippedCount++;
                continue;
            }

            result.AllFiles.Add(new ModpackFileEntry
            {
                RelativePath = relativePath + "/" + relative,
                FullPath = file,
                Length = length
            });

            node.FileCount++;
            node.TotalBytes += length;
        }
    }

    /// <summary>递归枚举文件，跳过 reparse point 目录（不跟进联接，避免成环或枚举爆炸）。</summary>
    private static IEnumerable<string> EnumerateFilesRecursive(
        string directory,
        ScanContext context,
        ModpackScanResult result)
    {
        foreach (var file in ListFiles(directory, result))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            yield return file;
        }

        foreach (var child in ListDirectories(directory, result))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (IsReparsePoint(child))
            {
                result.SkippedCount++;
                continue;
            }

            foreach (var file in EnumerateFilesRecursive(child, context, result))
                yield return file;
        }
    }

    private static List<string> ListEntries(string directory, ModpackScanResult result)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(directory).ToList();
        }
        catch (Exception ex)
        {
            result.SkippedCount++;
            result.Warnings.Add($"无法读取目录：{directory}（{ex.Message}）");
            return new List<string>();
        }
    }

    private static List<string> ListFiles(string directory, ModpackScanResult result)
    {
        try
        {
            return Directory.EnumerateFiles(directory).ToList();
        }
        catch (Exception ex)
        {
            result.SkippedCount++;
            result.Warnings.Add($"无法读取目录：{directory}（{ex.Message}）");
            return new List<string>();
        }
    }

    private static List<string> ListDirectories(string directory, ModpackScanResult result)
    {
        try
        {
            return Directory.EnumerateDirectories(directory).ToList();
        }
        catch (Exception ex)
        {
            result.SkippedCount++;
            result.Warnings.Add($"无法读取目录：{directory}（{ex.Message}）");
            return new List<string>();
        }
    }

    private static bool IsOutputFile(string path, string? outputFullPath)
    {
        if (outputFullPath == null)
            return false;

        var full = SafeGetFullPath(path);
        return full != null && string.Equals(full, outputFullPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string? SafeGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true; // 取不到属性就当作不可用
        }
    }

    /// <summary>目录在前，再按名称不区分大小写排序。</summary>
    private static void SortNodes(List<ModpackExportTreeNode> nodes)
    {
        nodes.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory)
                return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
    }
}
