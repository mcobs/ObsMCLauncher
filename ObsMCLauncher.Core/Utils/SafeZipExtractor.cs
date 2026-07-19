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
