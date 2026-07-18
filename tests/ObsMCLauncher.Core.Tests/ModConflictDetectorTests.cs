using System.IO;
using System.IO.Compression;
using System.Text;
using ObsMCLauncher.Core.Services;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// ModConflictDetector 的单元测试，覆盖重复ID、缺失依赖、加载器混用、版本不兼容等场景
/// </summary>
public class ModConflictDetectorTests
{
    private static string? _tempRoot;
    private static int _counter;

    private static string CreateModsDir()
    {
        if (_tempRoot == null || !Directory.Exists(_tempRoot))
            _tempRoot = Path.Combine(Path.GetTempPath(), "omcl_modconflict_tests_" + System.Guid.NewGuid());
        var dir = Path.Combine(_tempRoot, System.Threading.Interlocked.Increment(ref _counter).ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 创建一个包含 fabric.mod.json 的 JAR (ZIP) 文件
    /// </summary>
    private static string CreateFabricModJar(string dir, string fileName, string modId, string version,
        string name = "", string? depends = null, bool disabled = false)
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
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(json);
        stream.Write(bytes, 0, bytes.Length);
        return path;
    }

    private static string CreateForgeModJar(string dir, string fileName, string modId, string version,
        string name = "", bool disabled = false)
    {
        // 简化的 mods.toml
        var toml = $$"""
modLoader="javafml"
loaderVersion="[40,)"
[[mods]]
modId="{{modId}}"
version="{{version}}"
displayName="{{name}}"
""";
        var path = Path.Combine(dir, fileName + (disabled ? ".jar.disabled" : ".jar"));
        using var fs = File.Create(path);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("META-INF/mods.toml");
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(toml);
        stream.Write(bytes, 0, bytes.Length);
        return path;
    }

    [Fact]
    public void DetectConflicts_EmptyDir_ReturnsEmpty()
    {
        var dir = CreateModsDir();
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        Assert.Empty(conflicts);
    }

    [Fact]
    public void DetectConflicts_NonExistentDir_ReturnsEmpty()
    {
        var conflicts = ModConflictDetector.DetectConflicts(Path.Combine(Path.GetTempPath(), "nonexistent_" + System.Guid.NewGuid()));
        Assert.Empty(conflicts);
    }

    [Fact]
    public void DetectConflicts_SingleMod_NoConflicts()
    {
        var dir = CreateModsDir();
        CreateFabricModJar(dir, "mod1", "examplemod", "1.0.0", "Example Mod");
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        Assert.Empty(conflicts);
    }

    // ===== 重复 ID 检测 =====

    [Fact]
    public void DetectConflicts_DuplicateIds_BothEnabled_ReportsConflict()
    {
        var dir = CreateModsDir();
        CreateFabricModJar(dir, "mod1", "dup", "1.0.0", "Mod A");
        CreateFabricModJar(dir, "mod2", "dup", "1.0.0", "Mod B");
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        var dup = conflicts.FindAll(c => c.Type == ConflictType.DuplicateId);
        Assert.Single(dup);
        Assert.Equal(ConflictSeverity.Error, dup[0].Severity);
        Assert.Contains("dup", dup[0].Description);
    }

    [Fact]
    public void DetectConflicts_DuplicateIds_OneDisabled_NoConflict()
    {
        var dir = CreateModsDir();
        CreateFabricModJar(dir, "mod1", "dup", "1.0.0");
        CreateFabricModJar(dir, "mod2", "dup", "1.0.0", disabled: true);
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        Assert.DoesNotContain(conflicts, c => c.Type == ConflictType.DuplicateId);
    }

    [Fact]
    public void DetectConflicts_DuplicateIds_CaseInsensitive()
    {
        var dir = CreateModsDir();
        CreateFabricModJar(dir, "mod1", "FooBar", "1.0.0");
        CreateFabricModJar(dir, "mod2", "foobar", "1.0.0");
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        Assert.Contains(conflicts, c => c.Type == ConflictType.DuplicateId);
    }

    [Fact]
    public void DetectConflicts_ThreeDuplicateIds_GeneratesThreePairs()
    {
        var dir = CreateModsDir();
        CreateFabricModJar(dir, "mod1", "dup", "1.0.0");
        CreateFabricModJar(dir, "mod2", "dup", "1.0.0");
        CreateFabricModJar(dir, "mod3", "dup", "1.0.0");
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        // 3 个重复模组两两组合 = 3 对
        var dupCount = conflicts.FindAll(c => c.Type == ConflictType.DuplicateId).Count;
        Assert.Equal(3, dupCount);
    }

    // ===== 缺失依赖检测 =====

    [Fact]
    public void DetectConflicts_MissingRequiredDependency_ReportsError()
    {
        var dir = CreateModsDir();
        CreateFabricModJar(dir, "mod1", "mainmod", "1.0.0", "Main Mod",
            depends: """{"requiredlib": "1.0.0"}""");
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        var missing = conflicts.FindAll(c => c.Type == ConflictType.MissingDependency);
        Assert.Single(missing);
        Assert.Equal(ConflictSeverity.Error, missing[0].Severity);
        Assert.Contains("requiredlib", missing[0].Description);
    }

    [Fact]
    public void DetectConflicts_DependencyPresent_NoConflict()
    {
        var dir = CreateModsDir();
        CreateFabricModJar(dir, "mod1", "mainmod", "1.0.0", "Main Mod",
            depends: """{"lib": "1.0.0"}""");
        CreateFabricModJar(dir, "mod2", "lib", "1.0.0", "Library");
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        Assert.DoesNotContain(conflicts, c => c.Type == ConflictType.MissingDependency);
    }

    [Fact]
    public void DetectConflicts_SystemDependency_NotReported()
    {
        var dir = CreateModsDir();
        CreateFabricModJar(dir, "mod1", "mainmod", "1.0.0", "Main Mod",
            depends: """{"minecraft": "1.20", "fabricloader": ">=0.14", "java": ">=17"}""");
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        Assert.DoesNotContain(conflicts, c => c.Type == ConflictType.MissingDependency);
    }

    // ===== 加载器混用检测 =====

    [Fact]
    public void DetectConflicts_FabricAndForgeMixed_ReportsError()
    {
        var dir = CreateModsDir();
        CreateFabricModJar(dir, "fabricmod", "fabricmod", "1.0.0", "Fabric Mod");
        CreateForgeModJar(dir, "forgemod", "forgemod", "1.0.0", "Forge Mod");
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        var mismatch = conflicts.FindAll(c => c.Type == ConflictType.LoaderMismatch);
        Assert.Single(mismatch);
        Assert.Equal(ConflictSeverity.Error, mismatch[0].Severity);
        Assert.Contains("Fabric", mismatch[0].Description);
        Assert.Contains("Forge", mismatch[0].Description);
    }

    [Fact]
    public void DetectConflicts_OnlyFabric_NoLoaderConflict()
    {
        var dir = CreateModsDir();
        CreateFabricModJar(dir, "mod1", "mod1", "1.0.0");
        CreateFabricModJar(dir, "mod2", "mod2", "1.0.0");
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        Assert.DoesNotContain(conflicts, c => c.Type == ConflictType.LoaderMismatch);
    }

    [Fact]
    public void DetectConflicts_QuiltAndNeoForge_ReportsError()
    {
        var dir = CreateModsDir();
        // Quilt 模组需要 quilt_loader 字段
        var quiltJson = """{"quilt_loader":{"id":"quiltmod","version":"1.0.0"},"metadata":{"name":"Quilt Mod"}}""";
        CreateModJarWithContent(dir, "quiltmod.jar", "quilt.mod.json", quiltJson);

        CreateForgeModJar(dir, "neoforgemod", "neoforgemod", "1.0.0", "NeoForge Mod");
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        Assert.Contains(conflicts, c => c.Type == ConflictType.LoaderMismatch);
    }

    private static void CreateModJarWithContent(string dir, string fileName, string entryName, string content)
    {
        var path = Path.Combine(dir, fileName);
        using var fs = File.Create(path);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    // ===== 版本兼容性检测 =====

    [Fact]
    public void DetectConflicts_VersionIncompatible_MajorGap_ReportsError()
    {
        var dir = CreateModsDir();
        // lib 1.0.0，要求 [2.0.0,3.0.0)，主版本号差距 2
        CreateFabricModJar(dir, "mod1", "mainmod", "1.0.0", "Main",
            depends: """{"lib": "[2.0.0,3.0.0)"}""");
        CreateFabricModJar(dir, "mod2", "lib", "1.0.0", "Lib");
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        var verInc = conflicts.FindAll(c => c.Type == ConflictType.VersionIncompatible);
        Assert.True(verInc.Count >= 1);
        Assert.Equal(ConflictSeverity.Error, verInc[0].Severity);
    }

    [Fact]
    public void DetectConflicts_VersionInRange_NoConflict()
    {
        var dir = CreateModsDir();
        CreateFabricModJar(dir, "mod1", "mainmod", "1.0.0", "Main",
            depends: """{"lib": "[1.0.0,2.0.0)"}""");
        CreateFabricModJar(dir, "mod2", "lib", "1.5.0", "Lib");
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        Assert.DoesNotContain(conflicts, c => c.Type == ConflictType.VersionIncompatible);
    }

    [Fact]
    public void DetectConflicts_VersionMinorMismatch_ReportsWarning()
    {
        var dir = CreateModsDir();
        // lib 2.5.0，要求 [2.0.0,2.1.0)，同主版本差距 -> Warning
        CreateFabricModJar(dir, "mod1", "mainmod", "1.0.0", "Main",
            depends: """{"lib": "[2.0.0,2.1.0)"}""");
        CreateFabricModJar(dir, "mod2", "lib", "2.5.0", "Lib");
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        var verInc = conflicts.FindAll(c => c.Type == ConflictType.VersionIncompatible);
        Assert.True(verInc.Count >= 1);
        Assert.Equal(ConflictSeverity.Warning, verInc[0].Severity);
    }

    [Fact]
    public void DetectConflicts_SoftRequirement_NoConflict()
    {
        var dir = CreateModsDir();
        // 软要求：仅推荐版本，不报告冲突
        CreateFabricModJar(dir, "mod1", "mainmod", "1.0.0", "Main",
            depends: """{"lib": "1.0.0"}""");
        CreateFabricModJar(dir, "mod2", "lib", "99.0.0", "Lib");
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        Assert.DoesNotContain(conflicts, c => c.Type == ConflictType.VersionIncompatible);
    }

    [Fact]
    public void DetectConflicts_MissingDependencyWithVersion_NotVersionConflict()
    {
        // 依赖缺失时由 MissingDependency 处理，不应触发 VersionIncompatible
        var dir = CreateModsDir();
        CreateFabricModJar(dir, "mod1", "mainmod", "1.0.0", "Main",
            depends: """{"missinglib": "[1.0.0,2.0.0)"}""");
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        Assert.Contains(conflicts, c => c.Type == ConflictType.MissingDependency);
        Assert.DoesNotContain(conflicts, c => c.Type == ConflictType.VersionIncompatible);
    }

    [Fact]
    public void DetectConflicts_VersionConflict_IncludesSuggestion()
    {
        var dir = CreateModsDir();
        CreateFabricModJar(dir, "mod1", "mainmod", "1.0.0", "Main",
            depends: """{"lib": "[2.0.0,3.0.0)"}""");
        CreateFabricModJar(dir, "mod2", "lib", "1.0.0", "Lib");
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        var verInc = conflicts.Find(c => c.Type == ConflictType.VersionIncompatible);
        Assert.NotNull(verInc);
        Assert.False(string.IsNullOrEmpty(verInc.Suggestion));
    }

    [Fact]
    public void DetectConflicts_DisabledMod_NotCheckedForConflicts()
    {
        // 禁用的模组不参与冲突检测
        var dir = CreateModsDir();
        CreateFabricModJar(dir, "mod1", "dup", "1.0.0", "Mod A", disabled: true);
        CreateFabricModJar(dir, "mod2", "dup", "1.0.0", "Mod B", disabled: true);
        var conflicts = ModConflictDetector.DetectConflicts(dir);
        Assert.DoesNotContain(conflicts, c => c.Type == ConflictType.DuplicateId);
    }
}
