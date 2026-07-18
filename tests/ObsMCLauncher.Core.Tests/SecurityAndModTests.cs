using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Tests;

public class SafeZipExtractorTests
{
    private static string CreateTestZip(params (string entryPath, string content)[] entries)
    {
        var zipPath = Path.GetTempFileName();
        File.Delete(zipPath);
        zipPath += ".zip";

        using (var fs = File.Create(zipPath))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var (entryPath, content) in entries)
            {
                var entry = archive.CreateEntry(entryPath);
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream);
                writer.Write(content);
            }
        }

        return zipPath;
    }

    [Fact]
    public void ExtractToDirectory_NormalFiles_ExtractsSuccessfully()
    {
        var zipPath = CreateTestZip(
            ("file1.txt", "hello"),
            ("subdir/file2.txt", "world"));

        var destDir = Path.Combine(Path.GetTempPath(), "safzeziptest_" + Guid.NewGuid());
        try
        {
            SafeZipExtractor.ExtractToDirectory(zipPath, destDir);

            Assert.True(File.Exists(Path.Combine(destDir, "file1.txt")));
            Assert.True(File.Exists(Path.Combine(destDir, "subdir", "file2.txt")));
            Assert.Equal("hello", File.ReadAllText(Path.Combine(destDir, "file1.txt")));
        }
        finally
        {
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void ExtractToDirectory_PathTraversal_ThrowsException()
    {
        var zipPath = CreateTestZip(
            ("../../../etc/passwd", "malicious"));

        var destDir = Path.Combine(Path.GetTempPath(), "safzeziptest_" + Guid.NewGuid());
        try
        {
            Assert.Throws<IOException>(() =>
                SafeZipExtractor.ExtractToDirectory(zipPath, destDir));
        }
        finally
        {
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void ExtractEntryToFile_PathTraversal_ThrowsException()
    {
        var zipPath = CreateTestZip(
            ("../../secret.txt", "malicious"));

        var destDir = Path.Combine(Path.GetTempPath(), "safzeziptest_" + Guid.NewGuid());
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.Entries[0];
            Assert.Throws<IOException>(() =>
                SafeZipExtractor.ExtractEntryToFile(entry, Path.Combine(destDir, "../../secret.txt"), destDir));
        }
        finally
        {
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void ExtractEntryWithNameOnly_NameWithSlash_ThrowsException()
    {
        var zipPath = CreateTestZip(("sub/file.txt", "content"));
        var destDir = Path.Combine(Path.GetTempPath(), "safzeziptest_" + Guid.NewGuid());

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            Assert.Throws<IOException>(() =>
                SafeZipExtractor.ExtractEntryWithNameOnly(archive.Entries[0], destDir));
        }
        finally
        {
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
            File.Delete(zipPath);
        }
    }
}

public class FileHashVerifierTests
{
    [Fact]
    public void ComputeSha1_ReturnsCorrectHash()
    {
        var testFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(testFile, "test content");
            var hash = FileHashVerifier.ComputeSha1(testFile);
            Assert.False(string.IsNullOrEmpty(hash));
            Assert.Equal(40, hash.Length);
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public void ComputeSha256_ReturnsCorrectHash()
    {
        var testFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(testFile, "test content");
            var hash = FileHashVerifier.ComputeSha256(testFile);
            Assert.False(string.IsNullOrEmpty(hash));
            Assert.Equal(64, hash.Length);
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public void VerifyFileHash_SameContent_ReturnsTrue()
    {
        var testFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(testFile, "hello world");
            var expectedHash = FileHashVerifier.ComputeSha1(testFile);
            Assert.True(FileHashVerifier.VerifyFileHash(testFile, expectedHash, HashType.Sha1));
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public void VerifyFileHash_DifferentContent_ReturnsFalse()
    {
        var testFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(testFile, "hello world");
            Assert.False(FileHashVerifier.VerifyFileHash(testFile, "0000000000000000000000000000000000000000", HashType.Sha1));
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public void VerifyFileHash_NonExistentFile_ReturnsFalse()
    {
        Assert.False(FileHashVerifier.VerifyFileHash("nonexistent.txt", "anyhash", HashType.Sha1));
    }

    [Fact]
    public void VerifyFileHash_Sha256_MatchesExpected()
    {
        var testFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(testFile, "sha256 test");
            var expectedHash = FileHashVerifier.ComputeSha256(testFile);
            Assert.True(FileHashVerifier.VerifyFileHash(testFile, expectedHash, HashType.Sha256));
        }
        finally
        {
            File.Delete(testFile);
        }
    }
}

public class ModMetadataParserTests
{
    private static string CreateFabricModJar(string modId, string name, string version, string? iconPath = null)
    {
        var zipPath = Path.GetTempFileName();
        File.Delete(zipPath);
        zipPath += ".jar";

        object fabricMod = new
        {
            id = modId,
            name = name,
            version = version,
            icon = iconPath
        };

        using (var fs = File.Create(zipPath))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("fabric.mod.json");
            using var stream = entry.Open();
            JsonSerializer.Serialize(stream, fabricMod);
        }

        return zipPath;
    }

    [Fact]
    public void ParseFromJar_FabricMod_ParsesCorrectly()
    {
        var jarPath = CreateFabricModJar("testmod", "Test Mod", "1.0.0");
        try
        {
            var meta = ModMetadataParser.ParseFromJar(jarPath);
            Assert.NotNull(meta);
            Assert.Equal("testmod", meta!.ModId);
            Assert.Equal("Test Mod", meta.Name);
            Assert.Equal("1.0.0", meta.Version);
            Assert.Equal("Fabric", meta.Loader);
        }
        finally
        {
            File.Delete(jarPath);
        }
    }

    [Fact]
    public void ParseFromJar_NonModJar_ReturnsNull()
    {
        var zipPath = Path.GetTempFileName();
        File.Delete(zipPath);
        zipPath += ".jar";

        using (var fs = File.Create(zipPath))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("somefile.txt");
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream);
            writer.Write("not a mod");
        }

        try
        {
            var meta = ModMetadataParser.ParseFromJar(zipPath);
            Assert.Null(meta);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void ParseFromJar_IconPath_ParsedCorrectly()
    {
        var jarPath = CreateFabricModJar("iconmod", "Icon Mod", "2.0.0", "assets/iconmod/icon.png");
        try
        {
            var meta = ModMetadataParser.ParseFromJar(jarPath);
            Assert.NotNull(meta);
            Assert.Equal("assets/iconmod/icon.png", meta!.IconPath);
        }
        finally
        {
            File.Delete(jarPath);
        }
    }
}
