using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ObsMCLauncher.Core.Services;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// LocalModScanner 的单元测试：元数据/图标解析、持久缓存命中与失效、以及冲突检测的复用重载。
/// </summary>
public class LocalModScannerTests
{
    private static string CreateWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "omcl_modscan_tests_" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// 创建一个含 fabric.mod.json 的 JAR；可选写入一张 icon.png 作为图标。
    /// </summary>
    private static string CreateFabricModJar(
        string dir, string fileName, string modId, string version,
        string name = "", string? depends = null, bool disabled = false, bool withIcon = false)
    {
        var json = $$"""
{
  "schemaVersion": 1,
  "id": "{{modId}}",
  "version": "{{version}}",
  "name": "{{name}}",
  "depends": {{depends ?? "{}"}}
}
""";
        var path = Path.Combine(dir, fileName + (disabled ? ".jar.disabled" : ".jar"));
        using var fs = File.Create(path);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

        var entry = archive.CreateEntry("fabric.mod.json");
        using (var stream = entry.Open())
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            stream.Write(bytes, 0, bytes.Length);
        }

        if (withIcon)
        {
            var icon = archive.CreateEntry("icon.png");
            using var iconStream = icon.Open();
            // 1x1 PNG，够用来验证「提取出来了」
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
            iconStream.Write(png, 0, png.Length);
        }

        return path;
    }

    [Fact]
    public void Scan_ParsesMetadataAndExtractsIcon()
    {
        var root = CreateWorkspace();
        var modsDir = Path.Combine(root, "mods");
        var iconDir = Path.Combine(root, "icons");
        Directory.CreateDirectory(modsDir);
        CreateFabricModJar(modsDir, "examplemod", "examplemod", "1.2.3", "Example Mod", withIcon: true);

        var scanned = LocalModScanner.Scan(modsDir, iconDir);

        var mod = Assert.Single(scanned);
        Assert.Equal("examplemod.jar", mod.FileName);
        Assert.True(mod.IsEnabled);
        Assert.True(mod.Size > 0);
        Assert.NotNull(mod.Metadata);
        Assert.Equal("examplemod", mod.Metadata!.ModId);
        Assert.Equal("1.2.3", mod.Metadata.Version);
        Assert.Equal("Example Mod", mod.Metadata.Name);
        Assert.Equal("Fabric", mod.Metadata.Loader);
        Assert.NotNull(mod.IconCachePath);
        Assert.True(File.Exists(mod.IconCachePath!));
    }

    [Fact]
    public void Scan_DisabledJar_IsMarkedDisabled()
    {
        var root = CreateWorkspace();
        var modsDir = Path.Combine(root, "mods");
        Directory.CreateDirectory(modsDir);
        CreateFabricModJar(modsDir, "off", "offmod", "1.0.0", "Off Mod", disabled: true);

        var mod = Assert.Single(LocalModScanner.Scan(modsDir, Path.Combine(root, "icons")));

        Assert.False(mod.IsEnabled);
        Assert.Equal("off.jar.disabled", mod.FileName);
        Assert.Equal("Off Mod", mod.Metadata?.Name);
    }

    [Fact]
    public void Scan_ResultsAreSortedByFileName()
    {
        var root = CreateWorkspace();
        var modsDir = Path.Combine(root, "mods");
        Directory.CreateDirectory(modsDir);
        CreateFabricModJar(modsDir, "zeta", "zeta", "1.0.0", "Zeta");
        CreateFabricModJar(modsDir, "alpha", "alpha", "1.0.0", "Alpha");
        CreateFabricModJar(modsDir, "mid", "mid", "1.0.0", "Mid");

        var scanned = LocalModScanner.Scan(modsDir, Path.Combine(root, "icons"));

        Assert.Equal(new[] { "alpha.jar", "mid.jar", "zeta.jar" }, scanned.Select(m => m.FileName));
    }

    [Fact]
    public void Scan_MissingDirectory_ReturnsEmpty()
    {
        var root = CreateWorkspace();
        Assert.Empty(LocalModScanner.Scan(Path.Combine(root, "nope"), Path.Combine(root, "icons")));
    }

    /// <summary>
    /// 第二次扫描时把 jar 内容破坏成同样长度的垃圾数据（并还原修改时间），
    /// 如果元数据还能读出来，就说明确实走的是缓存、没有重新解压。
    /// </summary>
    [Fact]
    public void Scan_SecondRun_HitsCacheWithoutOpeningJar()
    {
        var root = CreateWorkspace();
        var modsDir = Path.Combine(root, "mods");
        var iconDir = Path.Combine(root, "icons");
        Directory.CreateDirectory(modsDir);
        var jarPath = CreateFabricModJar(modsDir, "cached", "cachedmod", "9.9.9", "Cached Mod", withIcon: true);

        var first = Assert.Single(LocalModScanner.Scan(modsDir, iconDir));
        Assert.Equal("Cached Mod", first.Metadata?.Name);

        // 内容破坏但长度不变、修改时间还原 —— 缓存键（路径 + 时间 + 大小）仍然命中
        var info = new FileInfo(jarPath);
        var length = info.Length;
        var lastWrite = info.LastWriteTimeUtc;
        File.WriteAllBytes(jarPath, new byte[length]);
        File.SetLastWriteTimeUtc(jarPath, lastWrite);

        var second = Assert.Single(LocalModScanner.Scan(modsDir, iconDir));

        Assert.Equal("Cached Mod", second.Metadata?.Name);
        Assert.Equal("cachedmod", second.Metadata?.ModId);
        Assert.Equal(first.IconCachePath, second.IconCachePath);
    }

    [Fact]
    public void Scan_FileChanged_ReparsesMetadata()
    {
        var root = CreateWorkspace();
        var modsDir = Path.Combine(root, "mods");
        var iconDir = Path.Combine(root, "icons");
        Directory.CreateDirectory(modsDir);
        CreateFabricModJar(modsDir, "updatable", "updatable", "1.0.0", "Old Name");

        var first = Assert.Single(LocalModScanner.Scan(modsDir, iconDir));
        Assert.Equal("Old Name", first.Metadata?.Name);

        // 换成同名的另一个版本，长度必然不同 —— 缓存必须失效
        File.Delete(Path.Combine(modsDir, "updatable.jar"));
        CreateFabricModJar(modsDir, "updatable", "updatable", "2.0.0", "New Name");

        var second = Assert.Single(LocalModScanner.Scan(modsDir, iconDir));

        Assert.Equal("New Name", second.Metadata?.Name);
        Assert.Equal("2.0.0", second.Metadata?.Version);
    }

    [Fact]
    public void Scan_JarWithoutIcon_LeavesIconPathNull()
    {
        var root = CreateWorkspace();
        var modsDir = Path.Combine(root, "mods");
        Directory.CreateDirectory(modsDir);
        CreateFabricModJar(modsDir, "noicon", "noicon", "1.0.0", "No Icon");

        var mod = Assert.Single(LocalModScanner.Scan(modsDir, Path.Combine(root, "icons")));

        Assert.Null(mod.IconCachePath);
        Assert.Equal("No Icon", mod.Metadata?.Name);
    }

    [Fact]
    public void Scan_NonZipFile_KeepsFileEntryWithoutMetadata()
    {
        var root = CreateWorkspace();
        var modsDir = Path.Combine(root, "mods");
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, "broken.jar"), "this is not a zip");

        var mod = Assert.Single(LocalModScanner.Scan(modsDir, Path.Combine(root, "icons")));

        Assert.Null(mod.Metadata);
        Assert.Equal("broken.jar", mod.FileName);
    }

    /// <summary>冲突检测复用已解析元数据的重载，结果必须与「给目录」的旧重载一致。</summary>
    [Fact]
    public void DetectConflicts_MetadataOverload_MatchesDirectoryOverload()
    {
        var root = CreateWorkspace();
        var modsDir = Path.Combine(root, "mods");
        Directory.CreateDirectory(modsDir);
        CreateFabricModJar(modsDir, "dup_a", "duplicated", "1.0.0", "Dup A");
        CreateFabricModJar(modsDir, "dup_b", "duplicated", "1.0.0", "Dup B");
        CreateFabricModJar(modsDir, "needy", "needy", "1.0.0", "Needy", depends: """{"missinglib":"*"}""");

        var fromDirectory = ModConflictDetector.DetectConflicts(modsDir);

        var scanned = LocalModScanner.Scan(modsDir, Path.Combine(root, "icons"));
        var metadataList = scanned
            .Where(s => s.Metadata != null)
            .Select(s => (s.FilePath, s.Metadata!, s.IsEnabled))
            .ToList();
        var fromMetadata = ModConflictDetector.DetectConflicts(metadataList);

        Assert.NotEmpty(fromDirectory);
        Assert.Equal(fromDirectory.Count, fromMetadata.Count);
        Assert.Equal(fromDirectory.Select(c => c.Type), fromMetadata.Select(c => c.Type));
        Assert.Equal(fromDirectory.Select(c => c.Description), fromMetadata.Select(c => c.Description));
    }

    [Fact]
    public void DetectConflicts_EmptyMetadataList_ReturnsEmpty()
    {
        Assert.Empty(ModConflictDetector.DetectConflicts(
            new System.Collections.Generic.List<(string FilePath, ModMetadata Meta, bool Enabled)>()));
    }
}
