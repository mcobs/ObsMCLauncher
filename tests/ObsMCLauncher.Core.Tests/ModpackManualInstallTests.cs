using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Minecraft;
using ObsMCLauncher.Core.Services.Modpack;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 手工整合包（无清单，靠目录结构表达）的解压前缀剥离，
/// 以及导出页用的目录用途标注。
/// </summary>
public class ModpackManualInstallTests : IDisposable
{
    private readonly string _root;

    public ModpackManualInstallTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "omcl-manual-modpack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch
        {
            // 清理失败不影响断言
        }
    }

    /// <summary>造一个 zip 并返回条目（用于探测前缀 / 计算落点）。</summary>
    private List<ZipArchiveEntry> EntriesOf(params string[] relativePaths)
    {
        var zipPath = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using (var stream = File.Create(zipPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var relative in relativePaths)
            {
                var entry = archive.CreateEntry(relative);
                using var writer = new StreamWriter(entry.Open());
                writer.Write("x");
            }
        }

        // 读出来交给待测方法（ZipArchiveEntry 始终绑定在打开的 archive 上）
        var readArchive = ZipFile.OpenRead(zipPath);
        return readArchive.Entries.ToList();
    }

    [Fact]
    public void ManualPack_MinecraftPrefixIsStripped()
    {
        var entries = EntriesOf(
            ".minecraft/mods/sodium.jar",
            ".minecraft/config/sodium-options.json",
            ".minecraft/options.txt");

        var prefix = ModpackInstallService.DetectManualRootPrefix(entries);
        Assert.Equal(".minecraft/", prefix);

        Assert.Equal("mods/sodium.jar",
            ModpackInstallService.ResolveManualEntryPath(entries[0], prefix, "mypack"));
        Assert.Equal("config/sodium-options.json",
            ModpackInstallService.ResolveManualEntryPath(entries[1], prefix, "mypack"));
        Assert.Equal("options.txt",
            ModpackInstallService.ResolveManualEntryPath(entries[2], prefix, "mypack"));
    }

    [Fact]
    public void ManualPack_VersionsPrefixIsStrippedForOwnVersionOnly()
    {
        var entries = EntriesOf(
            "versions/mypack/mypack.json",
            "versions/mypack/mods/sodium.jar",
            "versions/other-version/other-version.json");

        // versions/ 不在"根前缀"的探测范围内（每条的版本名要分别判断）
        Assert.Equal(string.Empty, ModpackInstallService.DetectManualRootPrefix(entries));

        Assert.Equal("mypack.json",
            ModpackInstallService.ResolveManualEntryPath(entries[0], string.Empty, "mypack"));
        Assert.Equal("mods/sodium.jar",
            ModpackInstallService.ResolveManualEntryPath(entries[1], string.Empty, "mypack"));
        Assert.Null(ModpackInstallService.ResolveManualEntryPath(entries[2], string.Empty, "mypack"));
    }

    [Fact]
    public void ManualPack_PlainStructureIsLeftUntouched()
    {
        var entries = EntriesOf("mods/sodium.jar", "config/a.toml");

        Assert.Equal(string.Empty, ModpackInstallService.DetectManualRootPrefix(entries));
        Assert.Equal("mods/sodium.jar",
            ModpackInstallService.ResolveManualEntryPath(entries[0], string.Empty, "mypack"));
        Assert.Equal("config/a.toml",
            ModpackInstallService.ResolveManualEntryPath(entries[1], string.Empty, "mypack"));
    }

    [Fact]
    public void ManualPack_MixedPrefixDoesNotStripMinecraftFolder()
    {
        // 只有"所有条目都带同一前缀"时才剥离，避免误伤正常的 mods/ 结构
        var entries = EntriesOf(".minecraft/mods/sodium.jar", "config/a.toml");
        Assert.Equal(string.Empty, ModpackInstallService.DetectManualRootPrefix(entries));
    }

    [Fact]
    public void ManualPack_VersionsBareEntryIsDropped()
    {
        var entries = EntriesOf("versions/mypack");
        Assert.Null(ModpackInstallService.ResolveManualEntryPath(entries[0], string.Empty, "mypack"));
    }

    [Fact]
    public void ManualPack_BackslashPathsAreNormalized()
    {
        var entries = EntriesOf(@"mods\sodium.jar");
        Assert.Equal("mods/sodium.jar",
            ModpackInstallService.ResolveManualEntryPath(entries[0], string.Empty, "mypack"));
    }

    // ===== 目录用途标注 =====

    [Theory]
    [InlineData("mods", "模组")]
    [InlineData("config", "配置")]
    [InlineData("defaultconfigs", "默认配置")]
    [InlineData("resourcepacks", "资源包")]
    [InlineData("shaderpacks", "光影包")]
    [InlineData("saves", "存档")]
    [InlineData("datapacks", "数据包")]
    [InlineData("kubejs", "KubeJS 脚本")]
    [InlineData("mods/kubejs", "KubeJS 脚本")]
    [InlineData("journeymap", "JourneyMap 地图")]
    [InlineData("MODS", "模组")]
    [InlineData("mods/unknown-thing", "")]
    [InlineData("totally-unknown", "")]
    public void FolderPurpose_DescribesKnownFolders(string path, string expected)
    {
        Assert.Equal(expected, ModpackContentPurpose.DescribeFolder(path));
    }

    [Fact]
    public void FolderPurpose_FullPathRuleWinsOverNameRule()
    {
        // config/sodium 是"Sodium 设置"，裸的 sodium 是"Sodium 渲染"
        Assert.Equal("Sodium 设置", ModpackContentPurpose.DescribeFolder("config/sodium"));
        Assert.Equal("Sodium 渲染", ModpackContentPurpose.DescribeFolder("sodium"));
        Assert.Equal("Sodium 设置", ModpackContentPurpose.DescribeFolder("config/sodium/presets"));
    }

    [Fact]
    public void FolderPurpose_EmptyOrSlashOnlyReturnsEmpty()
    {
        Assert.Equal(string.Empty, ModpackContentPurpose.DescribeFolder(""));
        Assert.Equal(string.Empty, ModpackContentPurpose.DescribeFolder("   "));
        Assert.Equal(string.Empty, ModpackContentPurpose.DescribeFolder("/"));
    }

    [Theory]
    [InlineData("PCL", "PCL 启动器数据")]
    [InlineData("OMCL", "启动器版本配置")]
    [InlineData("local", "本地数据")]
    public void FolderPurpose_LabelsLauncherOwnedFolders(string path, string expected)
        => Assert.Equal(expected, ModpackContentPurpose.DescribeFolder(path));

    // ===== 文件用途标注 =====

    [Theory]
    [InlineData("options.txt", "游戏设置")]
    [InlineData("optionsof.txt", "OptiFine 设置")]
    [InlineData("optionsshaders.txt", "光影设置")]
    [InlineData("servers.dat", "服务器列表")]
    [InlineData("version_config.json", "启动器版本配置")]
    [InlineData("OMCL/init.json", "版本配置")]
    [InlineData("PCL/Setup.ini", "PCL 设置")]
    [InlineData("mods/sodium.jar", "")]
    [InlineData("config/sodium-options.json", "")]
    [InlineData("saves/world/level.dat", "存档数据")]
    public void FilePurpose_LabelsWellKnownFiles(string path, string expected)
        => Assert.Equal(expected, ModpackContentPurpose.DescribeFile(path));

    [Fact]
    public void FilePurpose_RecognisesVersionJsonAndJarAtRunRoot()
    {
        Assert.Equal("版本信息", ModpackContentPurpose.DescribeFile("1.18.2.json"));
        Assert.Equal("版本信息", ModpackContentPurpose.DescribeFile("1.7.10.json"));
        Assert.Equal("游戏本体", ModpackContentPurpose.DescribeFile("1.18.2.jar"));
        // 只有运行目录根下的才算"版本信息"，嵌套的同名文件不猜
        Assert.Equal(string.Empty, ModpackContentPurpose.DescribeFile("config/1.18.2.json"));
    }

    // ===== 必选内容 =====

    [Theory]
    [InlineData("mods", false)]
    [InlineData("mods/sodium.jar", false)]
    [InlineData("kubejs/server_scripts/main.js", true)]
    [InlineData("defaultconfigs", true)]
    [InlineData("datapacks/foo.zip", true)]
    [InlineData("scripts", true)]
    [InlineData("openloader", true)]
    [InlineData("config", false)]
    [InlineData("config/sodium-options.json", false)]
    [InlineData("resourcepacks", false)]
    [InlineData("options.txt", false)]
    [InlineData("PCL", false)]
    [InlineData("OMCL", false)]
    [InlineData("mods-backup/whatever.jar", false)]
    public void RequiredList_OnlyCoversPackCore(string path, bool expected)
        => Assert.Equal(expected, ModpackFileFilter.IsRequired(path));

    // ===== 启动器自己的数据：可见但不勾，账号文件挡死 =====

    [Theory]
    [InlineData("PCL", true)]
    [InlineData("OMCL", true)]
    [InlineData("PCL/Setup.ini", true)]
    [InlineData("OMCL/init.json", true)]
    [InlineData("version_config.json", true)]
    [InlineData("mods/sodium.jar", false)]
    [InlineData("resourcepacks/faithful.zip", false)]
    [InlineData("options.txt", true)]
    public void SuggestedBlackList_UnchecksLauncherOwnedData(string path, bool expectedNormal)
    {
        var suggestion = ModpackFileFilter.GetSuggestion(path, isDirectory: false);
        Assert.Equal(expectedNormal ? ModpackFileSuggestion.Normal : ModpackFileSuggestion.Suggested, suggestion);
    }

    [Fact]
    public void LauncherAccountFilesAreHiddenNoMatterWhat()
    {
        // OMCL 整目录是"可见但不勾"，但 config 子目录里有账号文件 → 必须隐藏
        Assert.Equal(ModpackFileSuggestion.Hidden,
            ModpackFileFilter.GetSuggestion("OMCL/config", isDirectory: true));
        Assert.Equal(ModpackFileSuggestion.Hidden,
            ModpackFileFilter.GetSuggestion("OMCL/config/accounts.json", isDirectory: false));
        Assert.Equal(ModpackFileSuggestion.Hidden,
            ModpackFileFilter.GetSuggestion("OMCL/config/config.json", isDirectory: false));
    }
}
