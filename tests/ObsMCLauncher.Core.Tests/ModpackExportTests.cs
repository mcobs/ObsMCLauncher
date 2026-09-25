using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Modpack;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 导出整合包：文件过滤、CurseForge 指纹、加载器解析、清单生成、扫描与端到端导出。
/// </summary>
public class ModpackExportTests : IDisposable
{
    private readonly string _root;

    public ModpackExportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "omcl-modpack-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 清理失败不影响断言
        }
    }

    // ===== ModpackFileFilter =====

    [Theory]
    [InlineData("logs", true)]
    [InlineData("logs/latest.log", false)]
    [InlineData("crash-reports", true)]
    [InlineData("versions", true)]
    [InlineData("assets", true)]
    [InlineData("libraries", true)]
    [InlineData("usercache.json", false)]
    [InlineData("launcher_profiles.json", false)]
    [InlineData("modrinth.index.json", false)]
    [InlineData("manifest.json", false)]
    [InlineData(".temp", true)]
    [InlineData(".temp/versions/foo/foo.json", false)]
    [InlineData("1.20.1-natives", true)]
    [InlineData("debug.log", false)]
    [InlineData("world/level.dat_old", false)]
    public void Filter_HidesBlacklistedEntries(string path, bool isDirectory)
    {
        Assert.Equal(ModpackFileSuggestion.Hidden, ModpackFileFilter.GetSuggestion(path, isDirectory));
    }

    [Theory]
    [InlineData("saves", true)]
    [InlineData("options.txt", false)]
    [InlineData("servers.dat", false)]
    [InlineData("journeymap", true)]
    [InlineData("optionsof.txt", false)]
    public void Filter_MarksPersonalDataAsVisibleButUnchecked(string path, bool isDirectory)
    {
        Assert.Equal(ModpackFileSuggestion.Normal, ModpackFileFilter.GetSuggestion(path, isDirectory));
    }

    [Theory]
    [InlineData("mods", true)]
    [InlineData("config", true)]
    [InlineData("mods/sodium.jar", false)]
    [InlineData("config/sodium-options.json", false)]
    [InlineData("resourcepacks/faithful.zip", false)]
    public void Filter_SuggestsNormalContent(string path, bool isDirectory)
    {
        Assert.Equal(ModpackFileSuggestion.Suggested, ModpackFileFilter.GetSuggestion(path, isDirectory));
    }

    [Fact]
    public void Filter_RegexRuleIsAnchoredLikeJavaMatches()
    {
        // Java 的 String.matches 是整串匹配：a.log.bak 不应被 .*\.log 命中
        Assert.Equal(ModpackFileSuggestion.Suggested, ModpackFileFilter.GetSuggestion("a.log.bak", false));
        Assert.Equal(ModpackFileSuggestion.Hidden, ModpackFileFilter.GetSuggestion("a.log", false));
        Assert.Equal(ModpackFileSuggestion.Hidden, ModpackFileFilter.GetSuggestion("latest.log", false));
    }

    [Fact]
    public void Filter_BlackListIncludesVersionOwnFiles()
    {
        var blackList = ModpackFileFilter.BuildBlackList("my-pack");
        Assert.True(ModpackFileFilter.Matches(blackList, "my-pack.jar"));
        Assert.True(ModpackFileFilter.Matches(blackList, "my-pack.json"));
        Assert.True(ModpackFileFilter.Matches(blackList, "my-pack-natives"));
        Assert.False(ModpackFileFilter.Matches(blackList, "mods/sodium.jar"));
    }

    [Fact]
    public void Filter_BlacklistedDirectoryHidesItsContents()
    {
        Assert.Equal(ModpackFileSuggestion.Hidden, ModpackFileFilter.GetSuggestion("logs/2026-01-01.log.gz", false));
        Assert.Equal(ModpackFileSuggestion.Hidden, ModpackFileFilter.GetSuggestion(".temp/x/y.json", false));
        Assert.Equal(ModpackFileSuggestion.Hidden, ModpackFileFilter.GetSuggestion("mods/.connector/a.jar", false));
    }

    [Theory]
    [InlineData("mods/sodium.jar", true)]
    [InlineData("mods/sodium.jar.disabled", true)]
    [InlineData("resourcepacks/faithful.zip", true)]
    [InlineData("shaderpacks/bsl.zip", true)]
    [InlineData("datapacks/x.zip", true)]
    [InlineData("config/sodium-options.json", false)]
    [InlineData("options.txt", false)]
    [InlineData("saves/world/level.dat", false)]
    public void Filter_OnlyTreatsResourceArchivesAsRemoteCandidates(string path, bool expected)
    {
        Assert.Equal(expected, ModpackFileFilter.IsPotentiallyRemoteResource(path));
    }

    [Fact]
    public void Filter_NormalizesRelativePaths()
    {
        Assert.Equal("mods/a.jar", ModpackFileFilter.NormalizeRelativePath(@"\mods\a.jar\"));
        Assert.Equal("mods/a.jar", ModpackFileFilter.NormalizeRelativePath("mods/a.jar"));
    }

    // ===== CurseForgeFingerprint =====

    [Fact]
    public void Fingerprint_IgnoresWhitespaceBytes()
    {
        var clean = Encoding.ASCII.GetBytes("hello-fingerprint");
        var noisy = Encoding.ASCII.GetBytes("hel\tlo\n-fing\rerprint ");
        Assert.Equal(CurseForgeFingerprintAccumulator.Compute(clean), CurseForgeFingerprintAccumulator.Compute(noisy));
    }

    [Fact]
    public void Fingerprint_EmptyInputMatchesSeedXorLength()
    {
        // h = 1 ^ 0 = 1 → h ^= h>>13 → 1；h *= M；h ^= h>>15
        const uint m = 0x5BD1E995;
        var expected = (1u * m) ^ ((1u * m) >> 15);
        Assert.Equal(expected, CurseForgeFingerprintAccumulator.Compute(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Fingerprint_StreamingChunksMatchSinglePass()
    {
        var data = new byte[1000];
        new Random(1234).NextBytes(data);
        for (var i = 0; i < data.Length; i += 7)
            data[i] = 0x20; // 掺入空白字节，验证过滤在分块边界上也成立

        var single = CurseForgeFingerprintAccumulator.Compute(data);

        var filteredLength = data.Count(b => b is not (0x09 or 0x0A or 0x0D or 0x20));
        var accumulator = new CurseForgeFingerprintAccumulator(filteredLength);
        // 故意用不规则分块喂入，覆盖"块被切开"的路径
        var offset = 0;
        foreach (var size in new[] { 1, 2, 3, 5, 8, 13, 21, 34, 55, 89, 144, 233, 377 })
        {
            var take = Math.Min(size, data.Length - offset);
            if (take <= 0)
                break;
            accumulator.Append(data.AsSpan(offset, take));
            offset += take;
        }
        if (offset < data.Length)
            accumulator.Append(data.AsSpan(offset));

        Assert.Equal(single, accumulator.Finish());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    public void Fingerprint_FileComputationMatchesInMemory(int length)
    {
        var data = new byte[length];
        new Random(99 + length).NextBytes(data);
        var file = Path.Combine(_root, $"fp-{length}.bin");
        File.WriteAllBytes(file, data);

        Assert.Equal(CurseForgeFingerprintAccumulator.Compute(data),
            CurseForgeFingerprintAccumulator.ComputeFromFile(file));
        Assert.Equal(data.Count(b => b is not (0x09 or 0x0A or 0x0D or 0x20)),
            CurseForgeFingerprintAccumulator.CountFilteredBytes(file));
    }

    [Fact]
    public void FileHasher_ProducesExpectedSha1AndSha512()
    {
        var file = Path.Combine(_root, "hash.bin");
        File.WriteAllText(file, "abc", new UTF8Encoding(false));

        var hashes = ModpackFileHasher.ComputeForLookup(file);

        // 已知向量：SHA1("abc") / SHA512("abc")
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", hashes.Sha1);
        Assert.StartsWith("ddaf35a193617abacc417349ae204131", hashes.Sha512);
        Assert.Equal(
            CurseForgeFingerprintAccumulator.Compute(Encoding.ASCII.GetBytes("abc")),
            hashes.CurseForgeFingerprint);
    }

    // ===== ModpackLoaderResolver =====

    [Theory]
    [InlineData("net.minecraftforge:forge:1.20.1-47.2.0", "1.20.1", "Forge", "47.2.0")]
    [InlineData("net.neoforged:neoforge:20.4.237", "1.20.4", "NeoForge", "20.4.237")]
    [InlineData("net.neoforged:forge:1.20.1-47.1.106", "1.20.1", "NeoForge", "47.1.106")]
    [InlineData("net.fabricmc:fabric-loader:0.15.11", "1.20.1", "Fabric", "0.15.11")]
    [InlineData("org.quiltmc:quilt-loader:0.23.1", "1.20.1", "Quilt", "0.23.1")]
    [InlineData("optifine:OptiFine:1.20.1_HD_U_I6", "1.20.1", "OptiFine", "1.20.1_HD_U_I6")]
    public void LoaderResolver_ExtractsLoaderVersionFromLibraries(
        string libraryName, string minecraftVersion, string expectedType, string expectedVersion)
    {
        var json = $$"""
        {
          "id": "test",
          "inheritsFrom": "{{minecraftVersion}}",
          "libraries": [
            { "name": "net.minecraft:client:{{minecraftVersion}}" },
            { "name": "{{libraryName}}" }
          ]
        }
        """;

        var info = ModpackLoaderResolver.ResolveFromJson(json, "test");

        Assert.Equal(minecraftVersion, info.MinecraftVersion);
        Assert.Equal(expectedType, info.LoaderType);
        Assert.Equal(expectedVersion, info.LoaderVersion);
    }

    [Fact]
    public void LoaderResolver_VanillaHasNoLoaderAndUsesItsOwnId()
    {
        var info = ModpackLoaderResolver.ResolveFromJson("""{ "id": "1.20.1", "libraries": [] }""");
        Assert.Equal("1.20.1", info.MinecraftVersion);
        Assert.Null(info.LoaderType);
        Assert.Null(info.CurseForgeLoaderId);
        Assert.Null(info.ModrinthDependencyKey);
    }

    [Fact]
    public void LoaderResolver_BuildsPlatformSpecificIdentifiers()
    {
        var json = """
        {
          "inheritsFrom": "1.20.1",
          "libraries": [ { "name": "net.minecraftforge:forge:1.20.1-47.2.0" } ]
        }
        """;
        var info = ModpackLoaderResolver.ResolveFromJson(json);

        Assert.Equal("forge-47.2.0", info.CurseForgeLoaderId);
        Assert.Equal("forge", info.ModrinthDependencyKey);
    }

    [Theory]
    [InlineData("net.fabricmc:fabric-loader:0.15.11", true)]
    [InlineData("net.fabricmc:fabric-loader", false)]
    [InlineData("", false)]
    [InlineData("net.minecraftforge:forge:1.20.1-47.2.0", true)]
    public void LoaderResolver_ParsesMavenCoordinates(string coordinate, bool expected)
    {
        Assert.Equal(expected, ModpackLoaderResolver.TryParseCoordinate(coordinate, out _, out _, out _));
    }

    // ===== ModpackManifestWriter =====

    [Fact]
    public void ManifestWriter_ModrinthIndexUsesInstallerCompatibleFieldNames()
    {
        var options = new ModpackExportOptions
        {
            Name = "测试整合包",
            Version = "1.0.0",
            Description = "简介"
        };
        var loader = new ModpackLoaderInfo
        {
            MinecraftVersion = "1.20.1",
            LoaderType = "Fabric",
            LoaderVersion = "0.15.11"
        };
        var remote = new List<ModpackManifestRemoteFile>
        {
            new()
            {
                RelativePath = "mods/sodium.jar",
                FileSize = 1024,
                ModrinthDownloadUrl = "https://cdn.modrinth.com/data/abc/sodium.jar",
                Sha1 = "aa",
                Sha512 = "bb"
            }
        };

        var json = ModpackManifestWriter.BuildModrinthIndex(options, loader, remote);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("minecraft", root.GetProperty("game").GetString());
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("1.0.0", root.GetProperty("versionId").GetString());
        Assert.Equal("测试整合包", root.GetProperty("name").GetString());
        Assert.Equal("简介", root.GetProperty("summary").GetString());

        var file = root.GetProperty("files")[0];
        Assert.Equal("mods/sodium.jar", file.GetProperty("path").GetString());
        Assert.Equal("aa", file.GetProperty("hashes").GetProperty("sha1").GetString());
        Assert.Equal("bb", file.GetProperty("hashes").GetProperty("sha512").GetString());
        Assert.Equal("https://cdn.modrinth.com/data/abc/sodium.jar", file.GetProperty("downloads")[0].GetString());
        Assert.Equal(1024, file.GetProperty("fileSize").GetInt64());

        var deps = root.GetProperty("dependencies");
        Assert.Equal("1.20.1", deps.GetProperty("minecraft").GetString());
        Assert.Equal("0.15.11", deps.GetProperty("fabric-loader").GetString());
    }

    [Fact]
    public void ManifestWriter_CurseForgeManifestUsesInstallerCompatibleFieldNames()
    {
        var options = new ModpackExportOptions
        {
            Name = "CF 包",
            Version = "2.0",
            Author = "作者"
        };
        var loader = new ModpackLoaderInfo
        {
            MinecraftVersion = "1.20.1",
            LoaderType = "Forge",
            LoaderVersion = "47.2.0"
        };
        var remote = new List<ModpackManifestRemoteFile>
        {
            new() { RelativePath = "mods/jei.jar", FileSize = 1, CurseForgeProjectId = 238222, CurseForgeFileId = 4593547 }
        };

        var json = ModpackManifestWriter.BuildCurseForgeManifest(options, loader, remote);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("minecraftModpack", root.GetProperty("manifestType").GetString());
        Assert.Equal(1, root.GetProperty("manifestVersion").GetInt32());
        Assert.Equal("作者", root.GetProperty("author").GetString());
        Assert.Equal("overrides", root.GetProperty("overrides").GetString());
        Assert.Equal("1.20.1", root.GetProperty("minecraft").GetProperty("version").GetString());

        var modLoader = root.GetProperty("minecraft").GetProperty("modLoaders")[0];
        Assert.Equal("forge-47.2.0", modLoader.GetProperty("id").GetString());
        Assert.True(modLoader.GetProperty("primary").GetBoolean());

        var file = root.GetProperty("files")[0];
        Assert.Equal(238222, file.GetProperty("projectID").GetInt32());
        Assert.Equal(4593547, file.GetProperty("fileID").GetInt32());
        Assert.True(file.GetProperty("required").GetBoolean());
    }

    [Fact]
    public void ManifestWriter_OmitsEmptySummaryAndKeepsEmptyFileList()
    {
        var options = new ModpackExportOptions { Name = "n", Version = "1" };
        var loader = new ModpackLoaderInfo { MinecraftVersion = "1.20.1" };

        var json = ModpackManifestWriter.BuildModrinthIndex(options, loader, Array.Empty<ModpackManifestRemoteFile>());
        using var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.TryGetProperty("summary", out _));
        Assert.Equal(0, doc.RootElement.GetProperty("files").GetArrayLength());
    }

    // ===== ModpackFileScanner =====

    [Fact]
    public void Scanner_CollectsFilesAndHidesBlacklisted()
    {
        var gameDir = CreateFakeInstance();
        var scan = ModpackFileScanner.Scan(new ModpackExportOptions
        {
            RunDirectory = gameDir,
            VersionName = "test-1.20.1"
        });

        var paths = scan.AllFiles.Select(f => f.RelativePath).ToList();
        Assert.Contains("mods/sodium.jar", paths);
        Assert.Contains("config/sodium-options.json", paths);
        Assert.Contains("options.txt", paths);
        Assert.Contains("saves/world/level.dat", paths);

        Assert.DoesNotContain(paths, p => p.StartsWith("logs/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(paths, p => p.StartsWith(".temp/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("test-1.20.1.jar", paths);
        Assert.DoesNotContain("test-1.20.1.json", paths);
        Assert.DoesNotContain(paths, p => p.EndsWith(".log", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scanner_DepthLimitedDirectoryStillRegistersAllDescendants()
    {
        var gameDir = Path.Combine(_root, "deep");
        var nested = Path.Combine(gameDir, "config", "a", "b", "c", "d");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "deep.toml"), "x");

        // maxDepth = 1 → config 的直接子目录不再展开
        var scan = ModpackFileScanner.Scan(new ModpackExportOptions { RunDirectory = gameDir }, maxDepth: 1);

        Assert.Contains("config/a/b/c/d/deep.toml", scan.AllFiles.Select(f => f.RelativePath));

        var configNode = scan.Roots.Single(n => n.RelativePath == "config");
        Assert.True(configNode.IsDepthLimited);
        Assert.Empty(configNode.Children);
        Assert.Equal(1, configNode.FileCount);
    }

    [Fact]
    public void Scanner_ExcludesOutputArchiveInsideRunDirectory()
    {
        var gameDir = Path.Combine(_root, "out-guard");
        Directory.CreateDirectory(Path.Combine(gameDir, "mods"));
        File.WriteAllText(Path.Combine(gameDir, "mods", "a.jar"), "a");
        var outputPath = Path.Combine(gameDir, "exported.mrpack");
        File.WriteAllText(outputPath, "old");

        var scan = ModpackFileScanner.Scan(new ModpackExportOptions
        {
            RunDirectory = gameDir,
            OutputPath = outputPath
        });

        Assert.DoesNotContain(scan.AllFiles, f => f.RelativePath == "exported.mrpack");
    }

    [Fact]
    public void Scanner_SortsDirectoriesBeforeFiles()
    {
        var gameDir = Path.Combine(_root, "sorting");
        Directory.CreateDirectory(Path.Combine(gameDir, "mods"));
        Directory.CreateDirectory(Path.Combine(gameDir, "config"));
        File.WriteAllText(Path.Combine(gameDir, "aaa.txt"), "x");
        File.WriteAllText(Path.Combine(gameDir, "mods", "a.jar"), "x");
        File.WriteAllText(Path.Combine(gameDir, "config", "a.toml"), "x");

        var scan = ModpackFileScanner.Scan(new ModpackExportOptions { RunDirectory = gameDir });

        Assert.Equal("config", scan.Roots[0].RelativePath);
        Assert.Equal("mods", scan.Roots[1].RelativePath);
        Assert.Equal("aaa.txt", scan.Roots[^1].RelativePath);
    }

    // ===== 端到端 =====

    [Fact]
    public async Task Export_AlwaysIncludesRequiredContentEvenIfWhitelistOmitsIt()
    {
        var gameDir = CreateFakeInstance();
        var output = Path.Combine(_root, "required-fallback.mrpack");

        var options = BuildOptions(gameDir, output, ModpackExportFormat.Modrinth);
        // 白名单只给一个无关文件：defaultconfigs 属于必选内容，必须被兜底补上；
        // mods 刻意**不是**必选（有人就想导出只带配置/脚本的包），不该被自动塞进来
        options.IncludePaths = new List<string> { "options.txt" };

        await ModpackExportService.ExportAsync(options);
        var entries = ReadZipEntries(output);

        Assert.Contains("overrides/options.txt", entries);
        Assert.Contains("overrides/defaultconfigs/example.toml", entries);
        Assert.DoesNotContain("overrides/mods/sodium.jar", entries);
    }

    [Fact]
    public async Task Export_RequiredFallbackStillRespectsBlackList()
    {
        var gameDir = CreateFakeInstance();
        // mods 下塞一个黑名单命中的备份文件（*.old）与一份日志，必选兜底不能把它们带进来
        WriteFile(Path.Combine(gameDir, "mods", "sodium.jar.old"), "backup");
        WriteFile(Path.Combine(gameDir, "mods", "debug.log"), "log");
        var output = Path.Combine(_root, "required-blacklist.mrpack");

        var options = BuildOptions(gameDir, output, ModpackExportFormat.Modrinth);
        options.IncludePaths = new List<string> { "options.txt" };

        // 显式勾上 mods（它不再是必选），兜底与白名单都要过黑名单
        options.IncludePaths = new List<string> { "options.txt", "mods" };

        await ModpackExportService.ExportAsync(options);
        var entries = ReadZipEntries(output);

        Assert.Contains("overrides/mods/sodium.jar", entries);
        Assert.DoesNotContain(entries, e => e.EndsWith(".old", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, e => e.EndsWith(".log", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Export_OfflineMode_PacksEverythingIntoOverrides()
    {
        var gameDir = CreateFakeInstance();
        var output = Path.Combine(_root, "offline.mrpack");

        var result = await ModpackExportService.ExportAsync(BuildOptions(gameDir, output, ModpackExportFormat.Modrinth));
        var entries = ReadZipEntries(output);

        // 清单必须在包根
        Assert.Contains("modrinth.index.json", entries);
        Assert.Contains("overrides/mods/sodium.jar", entries);
        Assert.Contains("overrides/config/sodium-options.json", entries);
        Assert.Contains("overrides/options.txt", entries);

        // 黑名单不出现在包里
        Assert.DoesNotContain(entries, e => e.Contains("/logs/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, e => e.EndsWith(".log", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, e => e.Contains(".temp/", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(0, result.RemoteReferencedFiles);
        Assert.Equal(result.TotalFiles, result.PackedFiles);
        Assert.True(result.OutputBytes > 0);

        using var doc = JsonDocument.Parse(ReadZipText(output, "modrinth.index.json"));
        Assert.Equal(0, doc.RootElement.GetProperty("files").GetArrayLength());
        Assert.Equal("1.20.1", doc.RootElement.GetProperty("dependencies").GetProperty("minecraft").GetString());
        Assert.Equal("0.15.11", doc.RootElement.GetProperty("dependencies").GetProperty("fabric-loader").GetString());
    }

    [Fact]
    public async Task Export_CurseForgeFormat_WritesManifestWithLoaderAndOverrides()
    {
        var gameDir = CreateFakeInstance();
        var output = Path.Combine(_root, "cf.zip");

        var options = BuildOptions(gameDir, output, ModpackExportFormat.CurseForge);
        options.Author = "测试作者";
        await ModpackExportService.ExportAsync(options);

        var entries = ReadZipEntries(output);
        Assert.Contains("manifest.json", entries);
        Assert.Contains("overrides/mods/sodium.jar", entries);

        using var doc = JsonDocument.Parse(ReadZipText(output, "manifest.json"));
        var root = doc.RootElement;
        Assert.Equal("minecraftModpack", root.GetProperty("manifestType").GetString());
        Assert.Equal("测试作者", root.GetProperty("author").GetString());
        Assert.Equal("overrides", root.GetProperty("overrides").GetString());
        Assert.Equal("fabric-0.15.11", root.GetProperty("minecraft").GetProperty("modLoaders")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Export_HonoursWhitelist()
    {
        var gameDir = CreateFakeInstance();
        var output = Path.Combine(_root, "subset.mrpack");

        var options = BuildOptions(gameDir, output, ModpackExportFormat.Modrinth);
        options.IncludePaths = new List<string> { "mods/sodium.jar" };

        await ModpackExportService.ExportAsync(options);

        var entries = ReadZipEntries(output);
        Assert.Contains("overrides/mods/sodium.jar", entries);
        Assert.DoesNotContain("overrides/options.txt", entries);
        Assert.DoesNotContain("overrides/config/sodium-options.json", entries);
    }

    [Fact]
    public async Task Export_RejectsPathEscaping()
    {
        var gameDir = CreateFakeInstance();
        var output = Path.Combine(_root, "escape.mrpack");
        File.WriteAllText(Path.Combine(_root, "outside.txt"), "secret");

        var options = BuildOptions(gameDir, output, ModpackExportFormat.Modrinth);
        options.IncludePaths = new List<string> { "../outside.txt", "mods/sodium.jar" };

        var result = await ModpackExportService.ExportAsync(options);
        var entries = ReadZipEntries(output);

        Assert.Contains("overrides/mods/sodium.jar", entries);
        Assert.DoesNotContain(entries, e => e.EndsWith("outside.txt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, w => w.Contains("运行目录之外"));
    }

    [Fact]
    public async Task Export_RequiresAuthorForCurseForge()
    {
        var gameDir = CreateFakeInstance();
        var options = BuildOptions(gameDir, Path.Combine(_root, "no-author.zip"), ModpackExportFormat.CurseForge);
        options.Author = "";

        await Assert.ThrowsAsync<InvalidOperationException>(() => ModpackExportService.ExportAsync(options));
    }

    [Fact]
    public async Task Export_RequiresSelection()
    {
        var gameDir = CreateFakeInstance();
        var options = BuildOptions(gameDir, Path.Combine(_root, "empty.mrpack"), ModpackExportFormat.Modrinth);
        options.IncludePaths = new List<string>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => ModpackExportService.ExportAsync(options));
    }

    [Fact]
    public async Task Export_DeletesPartialArchiveOnCancellation()
    {
        var gameDir = CreateFakeInstance();
        // 造足够多的文件，让取消发生在写包过程中
        for (var i = 0; i < 60; i++)
            File.WriteAllBytes(Path.Combine(gameDir, "config", $"bulk-{i}.bin"), new byte[256 * 1024]);

        var output = Path.Combine(_root, "cancelled.mrpack");
        var options = BuildOptions(gameDir, output, ModpackExportFormat.Modrinth);
        options.ResolveRemoteFiles = false;

        using var cts = new CancellationTokenSource();
        // Progress<T> 在无同步上下文的测试里会走线程池，取消时机不确定；这里同步回调
        var progress = new SyncProgress<ModpackExportProgress>(p =>
        {
            if (p.Percentage >= ModpackExportServiceTestHooks.WriteStagePercentage)
                cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ModpackExportService.ExportAsync(options, progress, cts.Token));

        Assert.False(File.Exists(output), "取消后不应留下半成品压缩包");
    }

    [Fact]
    public async Task Export_ResultCanBeDetectedByInstallerFormatSniffing()
    {
        var gameDir = CreateFakeInstance();

        var mrpack = Path.Combine(_root, "detect.mrpack");
        await ModpackExportService.ExportAsync(BuildOptions(gameDir, mrpack, ModpackExportFormat.Modrinth));
        Assert.Equal("Modrinth", SniffModpackType(mrpack));

        var cfZip = Path.Combine(_root, "detect.zip");
        var cfOptions = BuildOptions(gameDir, cfZip, ModpackExportFormat.CurseForge);
        cfOptions.Author = "作者";
        await ModpackExportService.ExportAsync(cfOptions);
        Assert.Equal("CurseForge", SniffModpackType(cfZip));
    }

    // ===== 辅助 =====

    /// <summary>与 ModpackInstallService.DetectModpackType 同口径的嗅探（该方法为 private，这里复刻判定顺序）。</summary>
    private static string SniffModpackType(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        if (names.Contains("manifest.json"))
            return "CurseForge";
        if (names.Contains("modrinth.index.json"))
            return "Modrinth";
        if (names.Any(n => n.Contains(".minecraft/") || n.StartsWith("versions/")))
            return "Manual";
        return "Unknown";
    }

    /// <param name="runDirectory">
    /// 运行目录。测试用的假实例是<b>隔离模式</b>的，所以运行目录 == 版本目录 ==
    /// <c>{game}/versions/test-1.20.1</c>（<see cref="CreateFakeInstance"/> 的返回值）。
    /// </param>
    private static ModpackExportOptions BuildOptions(string runDirectory, string output, ModpackExportFormat format)
    {
        return new ModpackExportOptions
        {
            Format = format,
            Name = "测试整合包",
            Version = "1.0.0",
            Author = "作者",
            Description = "用于单测",
            // 端到端用例一律走离线模式，避免测试依赖网络
            ResolveRemoteFiles = false,
            RunDirectory = runDirectory,
            VersionDirectory = runDirectory,
            VersionName = "test-1.20.1",
            OutputPath = output,
            IncludePaths = new List<string>
            {
                "mods/sodium.jar",
                "config/sodium-options.json",
                "options.txt",
                "saves/world/level.dat"
            }
        };
    }

    /// <summary>造一个隔离模式的假版本目录：文件布局覆盖黑名单、建议排除与正常内容。</summary>
    private string CreateFakeInstance()
    {
        var gameDir = Path.Combine(_root, "game-" + Guid.NewGuid().ToString("N")[..8]);
        var versionDir = Path.Combine(gameDir, "versions", "test-1.20.1");
        Directory.CreateDirectory(versionDir);

        WriteFile(Path.Combine(versionDir, "mods", "sodium.jar"), "jar-bytes");
        WriteFile(Path.Combine(versionDir, "config", "sodium-options.json"), "{}");
        WriteFile(Path.Combine(versionDir, "options.txt"), "lang:zh_cn");
        WriteFile(Path.Combine(versionDir, "saves", "world", "level.dat"), "level");
        WriteFile(Path.Combine(versionDir, "logs", "latest.log"), "log-line");
        WriteFile(Path.Combine(versionDir, "debug.log"), "root-log");
        WriteFile(Path.Combine(versionDir, "launcher_profiles.json"), "{}");
        WriteFile(Path.Combine(versionDir, ".temp", "versions", "x", "x.json"), "{}");
        WriteFile(Path.Combine(versionDir, "defaultconfigs", "example.toml"), "x");
        WriteFile(Path.Combine(versionDir, "PCL", "Setup.ini"), "x");
        WriteFile(Path.Combine(versionDir, "OMCL", "config", "accounts.json"), "never-export-this");
        WriteFile(Path.Combine(versionDir, "test-1.20.1.json"), VersionJson());
        WriteFile(Path.Combine(versionDir, "test-1.20.1.jar"), "vanilla-jar");

        return versionDir;
    }

    private static string VersionJson() => """
    {
      "id": "test-1.20.1",
      "inheritsFrom": "1.20.1",
      "libraries": [
        { "name": "net.fabricmc:fabric-loader:0.15.11" }
      ]
    }
    """;

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string[] ReadZipEntries(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        return archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToArray();
    }

    private static string ReadZipText(string path, string entryName)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"压缩包内找不到 {entryName}");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

/// <summary>测试里需要与实现共享的阶段常量（避免把内部常量公开成产品 API）。</summary>
internal static class ModpackExportServiceTestHooks
{
    /// <summary>等于 ModpackExportService 的写包阶段起点（70）。</summary>
    public const double WriteStagePercentage = 70;
}

/// <summary>同步执行回调的 IProgress（测试里需要确定的取消时机）。</summary>
internal sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SyncProgress(Action<T> handler) => _handler = handler;

    public void Report(T value) => _handler(value);
}
