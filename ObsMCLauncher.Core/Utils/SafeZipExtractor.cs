using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ObsMCLauncher.Core.Utils;

public static class SafeZipExtractor
{
    public static void ExtractToDirectory(string sourceArchiveFilePath, string destinationDirectoryName, bool overwrite = true, bool flattenSingleRoot = false)
    {
        using var archive = ZipFile.OpenRead(sourceArchiveFilePath);
        ExtractArchive(archive, destinationDirectoryName, overwrite, flattenSingleRoot);
    }

    public static void ExtractArchive(ZipArchive archive, string destinationDirectoryName, bool overwrite = true, bool flattenSingleRoot = false)
    {
        var fullDestDir = Path.GetFullPath(destinationDirectoryName);
        Directory.CreateDirectory(fullDestDir);

        // 发布包经常把内容套在一个顶层文件夹里；flatten 时削掉这层前缀，
        // 否则解出来的结构会多一级，调用方按约定路径就找不到文件了。
        var rootPrefix = flattenSingleRoot ? GetSingleRootDirectory(archive.Entries) : null;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;

            var relativePath = NormalizeEntryPath(entry.FullName);

            if (rootPrefix != null)
            {
                if (!relativePath.StartsWith(rootPrefix + "/", StringComparison.OrdinalIgnoreCase)) continue;
                relativePath = relativePath.Substring(rootPrefix.Length + 1);
            }

            if (relativePath.Length == 0) continue;

            var destPath = Path.GetFullPath(Path.Combine(fullDestDir, relativePath));

            if (!destPath.StartsWith(fullDestDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"ZIP条目路径遍历攻击被阻止: {entry.FullName}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite);
        }
    }

    /// <summary>
    /// 把条目路径统一成 "/" 分隔、不带前导斜杠的形式，方便跨平台比较。
    /// </summary>
    public static string NormalizeEntryPath(string entryPath)
        => entryPath.Replace('\\', '/').TrimStart('/');

    /// <summary>
    /// 判断压缩包是否把所有文件都装在一个顶层文件夹里。
    /// 是则返回该文件夹名（不带斜杠）；根目录直接有文件或有多个顶层目录时返回 null。
    /// </summary>
    public static string? GetSingleRootDirectory(IEnumerable<ZipArchiveEntry> entries)
    {
        string? root = null;

        foreach (var entry in entries)
        {
            // 目录项没有 Name，跳过
            if (string.IsNullOrEmpty(entry.Name)) continue;

            var relativePath = NormalizeEntryPath(entry.FullName);
            var slash = relativePath.IndexOf('/');

            if (slash <= 0)
            {
                // macOS 打包时混进来的松散文件，不参与"顶层文件夹"判断
                if (relativePath.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase)) continue;
                return null;
            }

            var first = relativePath.Substring(0, slash);
            if (first.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)) continue;

            if (root == null) root = first;
            else if (!string.Equals(root, first, StringComparison.OrdinalIgnoreCase)) return null;
        }

        return root;
    }

    public static void ExtractEntryToFile(ZipArchiveEntry entry, string destinationPath, string allowedBaseDir)
    {
        var fullDestPath = Path.GetFullPath(destinationPath);
        var fullBaseDir = Path.GetFullPath(allowedBaseDir);

        if (!fullDestPath.StartsWith(fullBaseDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"ZIP条目路径遍历攻击被阻止: {entry.FullName}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullDestPath)!);
        entry.ExtractToFile(fullDestPath, overwrite: true);
    }

    public static void ExtractEntryWithNameOnly(ZipArchiveEntry entry, string destinationDir)
    {
        // ZIP 条目本身的 FullName 可能包含路径（例如 NeoForge 安装器中的 "data/client.lzma"）
        // 这是正常的，我们只使用 entry.Name（文件名部分）作为输出文件名，
        // 保证输出路径不会逃逸出 destinationDir。
        // 真正的安全检查是验证最终的输出路径是否在 destinationDir 内。

        var fileName = entry.Name;
        if (string.IsNullOrEmpty(fileName))
        {
            // entry.Name 为空说明条目本身是目录或异常条目，跳过
            throw new IOException($"ZIP条目没有有效的文件名: {entry.FullName}");
        }

        var destPath = Path.GetFullPath(Path.Combine(destinationDir, fileName));
        var fullBaseDir = Path.GetFullPath(destinationDir);

        if (!destPath.StartsWith(fullBaseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !destPath.Equals(fullBaseDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"ZIP条目路径遍历攻击被阻止: {entry.FullName}");
        }

        Directory.CreateDirectory(fullBaseDir);
        entry.ExtractToFile(destPath, overwrite: true);
    }
}
