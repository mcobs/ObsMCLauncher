using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ObsMCLauncher.Core.Utils;

public static class SafeZipExtractor
{
    public static void ExtractToDirectory(string sourceArchiveFilePath, string destinationDirectoryName, bool overwrite = true)
    {
        using var archive = ZipFile.OpenRead(sourceArchiveFilePath);
        ExtractArchive(archive, destinationDirectoryName, overwrite);
    }

    public static void ExtractArchive(ZipArchive archive, string destinationDirectoryName, bool overwrite = true)
    {
        var fullDestDir = Path.GetFullPath(destinationDirectoryName);
        Directory.CreateDirectory(fullDestDir);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;

            var destPath = Path.GetFullPath(Path.Combine(destinationDirectoryName, entry.FullName));

            if (!destPath.StartsWith(fullDestDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"ZIP条目路径遍历攻击被阻止: {entry.FullName}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite);
        }
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
        // FullName 包含路径，Name 仅返回文件名部分，所以用 FullName 检测路径分隔符
        if (entry.FullName.Contains('/') || entry.FullName.Contains('\\'))
        {
            throw new IOException($"ZIP条目包含路径分隔符，预期仅为文件名: {entry.FullName}");
        }

        var destPath = Path.GetFullPath(Path.Combine(destinationDir, entry.Name));
        var fullBaseDir = Path.GetFullPath(destinationDir);

        if (!destPath.StartsWith(fullBaseDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"ZIP条目路径遍历攻击被阻止: {entry.Name}");
        }

        entry.ExtractToFile(destPath, overwrite: true);
    }
}
