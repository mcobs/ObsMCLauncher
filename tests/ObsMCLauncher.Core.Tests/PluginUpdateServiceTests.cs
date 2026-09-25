using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using ObsMCLauncher.Core.Plugins;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 插件在线更新的单元测试：目录约定、版本比对、包体校验、待更新应用、数据保留、失败回滚。
///
/// 用一个临时目录模拟 &lt;启动器基础目录&gt;，其中 <c>OMCL/plugins</c> 是插件安装目录。
/// 所有用例都不联网——「已下载待应用」这个状态直接手写成磁盘结构，
/// 因为那正是 <see cref="PluginUpdateService.ApplyPendingUpdates"/> 唯一消费的输入。
/// </summary>
public class PluginUpdateServiceTests : IDisposable
{
    private const string PluginId = "test-plugin";

    /// <summary>模拟的启动器基础目录</summary>
    private readonly string _appDir;

    /// <summary>模拟的插件安装目录（OMCL/plugins）</summary>
    private readonly string _pluginsDir;

    public PluginUpdateServiceTests()
    {
        _appDir = Path.Combine(Path.GetTempPath(), "omcl-plugin-update-tests", Guid.NewGuid().ToString("N"));
        _pluginsDir = Path.Combine(_appDir, "OMCL", "plugins");
        Directory.CreateDirectory(_pluginsDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_appDir)) Directory.Delete(_appDir, true);
        }
        catch
        {
            // 清理失败不影响测试结论
        }
    }

    #region 辅助

    private static string ManifestJson(string id, string version)
        => "{\"id\":\"" + id + "\",\"name\":\"" + id + "\",\"version\":\"" + version +
           "\",\"author\":\"tester\",\"description\":\"test plugin\"}";

    /// <summary>铺一个"已安装插件"目录（含模拟 dll、README）</summary>
    private string CreateInstalledPlugin(string id, string version, bool disabled = false)
    {
        var dir = Path.Combine(_pluginsDir, id);
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "plugin.json"), ManifestJson(id, version));
        File.WriteAllText(Path.Combine(dir, "README.md"), "# " + id);
        File.WriteAllBytes(Path.Combine(dir, id + ".dll"), new byte[] { 0x4D, 0x5A, 0x00 });

        if (disabled) File.WriteAllText(Path.Combine(dir, ".disabled"), "2026-01-01");

        return dir;
    }

    /// <summary>铺一个"已下载、等待应用"的更新（staged 目录 + pending.json）</summary>
    private string StageUpdate(string id, string fromVersion, string toVersion, params (string Path, string Content)[] extraStagedFiles)
    {
        var staged = PluginUpdateService.GetStagedDirectory(_pluginsDir, id);
        Directory.CreateDirectory(staged);

        File.WriteAllText(Path.Combine(staged, "plugin.json"), ManifestJson(id, toVersion));
        File.WriteAllText(Path.Combine(staged, "README.md"), "# " + id);
        File.WriteAllBytes(Path.Combine(staged, id + ".dll"), new byte[] { 0x4D, 0x5A, 0x01 });

        foreach (var (relativePath, content) in extraStagedFiles)
        {
            var full = Path.Combine(staged, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        File.WriteAllText(PluginUpdateService.GetPendingFilePath(_pluginsDir, id),
            JsonSerializer.Serialize(new PendingPluginUpdate
            {
                PluginId = id,
                FromVersion = fromVersion,
                ToVersion = toVersion,
                StagedAt = DateTime.Now
            }));

        return staged;
    }

    private string ReadInstalledVersion(string id)
    {
        var json = File.ReadAllText(Path.Combine(_pluginsDir, id, "plugin.json"));
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("version").GetString() ?? string.Empty;
    }

    private static MarketPlugin Market(string id, string version, string? releaseUrl = null) => new()
    {
        Id = id,
        Name = id,
        Version = version,
        ReleaseUrl = releaseUrl
    };

    private LoadedPlugin Installed(string id, string version) => new()
    {
        Id = id,
        Name = id,
        Version = version,
        DirectoryPath = Path.Combine(_pluginsDir, id),
        IsLoaded = true
    };

    /// <summary>在系统临时目录打一个合法的插件包 zip</summary>
    private static string CreatePackage(string id, string version, bool includeManifest = true)
    {
        var path = Path.Combine(Path.GetTempPath(), $"omcl-pkg-{Guid.NewGuid():N}.zip");

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        if (includeManifest)
        {
            var entry = archive.CreateEntry("plugin.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(ManifestJson(id, version));
        }

        var readme = archive.CreateEntry("README.md");
        using var readmeWriter = new StreamWriter(readme.Open());
        readmeWriter.Write("# " + id);

        return path;
    }

    #endregion

    #region 目录约定

    [Fact]
    public void UpdateDirectories_AreSiblingsOfPluginsDirectory_NotInsideIt()
    {
        // 回归保护：暂存与备份一旦落进 plugins/ 里，就会被 PluginLoader 当成插件目录扫描加载
        Assert.Equal(
            Path.Combine(_appDir, "OMCL", "plugin-updates"),
            PluginUpdateService.GetUpdatesRoot(_pluginsDir));

        Assert.Equal(
            Path.Combine(_appDir, "OMCL", "cache", "plugin-updates"),
            PluginUpdateService.GetCacheRoot(_pluginsDir));

        Assert.DoesNotContain(
            Path.Combine("OMCL", "plugins") + Path.DirectorySeparatorChar,
            PluginUpdateService.GetUpdatesRoot(_pluginsDir) + Path.DirectorySeparatorChar);
    }

    [Fact]
    public void PendingState_IsReportedConsistently()
    {
        Assert.False(PluginUpdateService.HasPendingUpdates(_pluginsDir));
        Assert.Empty(PluginUpdateService.GetPendingUpdates(_pluginsDir));
        Assert.Null(PluginUpdateService.GetPendingUpdate(_pluginsDir, PluginId));

        CreateInstalledPlugin(PluginId, "1.0.0");
        StageUpdate(PluginId, "1.0.0", "1.1.0");

        Assert.True(PluginUpdateService.HasPendingUpdates(_pluginsDir));

        var pending = PluginUpdateService.GetPendingUpdate(_pluginsDir, PluginId);
        Assert.NotNull(pending);
        Assert.Equal("1.1.0", pending!.ToVersion);

        // 非法 id 不去猜路径
        Assert.Null(PluginUpdateService.GetPendingUpdate(_pluginsDir, "../../etc"));
    }

    [Fact]
    public void CancelPendingUpdate_RemovesStagedAndMarker_ButKeepsBackup()
    {
        CreateInstalledPlugin(PluginId, "1.0.0");
        StageUpdate(PluginId, "1.0.0", "1.1.0");

        // 造一个历史备份，取消更新不应该动它
        var backup = PluginUpdateService.GetBackupDirectory(_pluginsDir, PluginId);
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup, "keep.txt"), "old");

        Assert.True(PluginUpdateService.CancelPendingUpdate(_pluginsDir, PluginId));
        Assert.False(PluginUpdateService.HasPendingUpdates(_pluginsDir));
        Assert.False(Directory.Exists(PluginUpdateService.GetStagedDirectory(_pluginsDir, PluginId)));
        Assert.True(Directory.Exists(backup));

        // 插件本体不受影响
        Assert.Equal("1.0.0", ReadInstalledVersion(PluginId));
    }

    #endregion

    #region 版本比对

    [Fact]
    public void BuildUpdateInfo_FlagsNewerMarketVersion()
    {
        CreateInstalledPlugin(PluginId, "1.2.0");
        var installed = new List<LoadedPlugin> { Installed(PluginId, "1.2.0") };
        var market = new List<MarketPlugin> { Market(PluginId, "1.3.0") };

        var info = Assert.Single(PluginUpdateService.BuildUpdateInfo(_pluginsDir, installed, market));

        Assert.True(info.HasUpdate);
        Assert.Equal("1.2.0", info.InstalledVersion);
        Assert.Equal("1.3.0", info.AvailableVersion);
        Assert.False(info.IsStaged);
        Assert.True(info.IsEnabled);
        Assert.True(market[0].HasUpdate);
    }

    [Fact]
    public void BuildUpdateInfo_SameOrOlderMarketVersion_IsNotAnUpdate()
    {
        CreateInstalledPlugin(PluginId, "1.3.0");
        var installed = new List<LoadedPlugin> { Installed(PluginId, "1.3.0") };

        var same = Assert.Single(PluginUpdateService.BuildUpdateInfo(
            _pluginsDir, installed, new List<MarketPlugin> { Market(PluginId, "1.3.0") }));
        Assert.False(same.HasUpdate);

        var older = Assert.Single(PluginUpdateService.BuildUpdateInfo(
            _pluginsDir, installed, new List<MarketPlugin> { Market(PluginId, "1.2.9") }));
        Assert.False(older.HasUpdate);

        // 两位版本号不能按字符串比较出错误结论
        var tenFold = Assert.Single(PluginUpdateService.BuildUpdateInfo(
            _pluginsDir, installed, new List<MarketPlugin> { Market(PluginId, "1.10.0") }));
        Assert.True(tenFold.HasUpdate);
    }

    [Fact]
    public void BuildUpdateInfo_PluginMissingFromMarket_HasNoUpdate()
    {
        CreateInstalledPlugin(PluginId, "1.0.0");
        var info = Assert.Single(PluginUpdateService.BuildUpdateInfo(
            _pluginsDir,
            new List<LoadedPlugin> { Installed(PluginId, "1.0.0") },
            new List<MarketPlugin> { Market("other-plugin", "9.9.9") }));

        Assert.False(info.HasUpdate);
        Assert.Null(info.Market);
    }

    [Fact]
    public void BuildUpdateInfo_ReportsStagedAndDisabledState()
    {
        CreateInstalledPlugin(PluginId, "1.0.0", disabled: true);
        StageUpdate(PluginId, "1.0.0", "2.0.0");

        var info = Assert.Single(PluginUpdateService.BuildUpdateInfo(
            _pluginsDir,
            new List<LoadedPlugin> { Installed(PluginId, "1.0.0") },
            new List<MarketPlugin> { Market(PluginId, "2.0.0") }));

        Assert.True(info.IsStaged);
        Assert.False(info.IsEnabled);
    }

    [Fact]
    public void BuildUpdateInfo_PrefersReleaseCheckedVersionOverIndexVersion()
    {
        CreateInstalledPlugin(PluginId, "1.0.0");
        var entry = Market(PluginId, "1.1.0", "https://api.github.com/repos/x/y/releases/latest");
        entry.LatestVersion = "1.5.0"; // 深度检查拿到的更新版本

        var info = Assert.Single(PluginUpdateService.BuildUpdateInfo(
            _pluginsDir, new List<LoadedPlugin> { Installed(PluginId, "1.0.0") }, new List<MarketPlugin> { entry }));

        Assert.Equal("1.5.0", info.AvailableVersion);
        Assert.True(info.IsFromReleaseCheck);
    }

    #endregion

    #region 包体校验

    [Fact]
    public void ValidatePluginPackage_AcceptsWellFormedPackage()
    {
        var zip = CreatePackage(PluginId, "1.0.0");
        try
        {
            Assert.Null(PluginMarketService.ValidatePluginPackage(zip, PluginId));
        }
        finally
        {
            File.Delete(zip);
        }
    }

    [Fact]
    public void ValidatePluginPackage_RejectsMissingManifest()
    {
        var zip = CreatePackage(PluginId, "1.0.0", includeManifest: false);
        try
        {
            var error = PluginMarketService.ValidatePluginPackage(zip, PluginId);
            Assert.NotNull(error);
            Assert.Contains("plugin.json", error);
        }
        finally
        {
            File.Delete(zip);
        }
    }

    [Fact]
    public void ValidatePluginPackage_RejectsIdMismatch()
    {
        var zip = CreatePackage("another-plugin", "1.0.0");
        try
        {
            var error = PluginMarketService.ValidatePluginPackage(zip, PluginId);
            Assert.NotNull(error);
            Assert.Contains("不一致", error);
        }
        finally
        {
            File.Delete(zip);
        }
    }

    [Fact]
    public void ValidatePluginPackage_RejectsGarbageFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"omcl-garbage-{Guid.NewGuid():N}.zip");
        File.WriteAllText(path, "this is not a zip");
        try
        {
            Assert.NotNull(PluginMarketService.ValidatePluginPackage(path, PluginId));
        }
        finally
        {
            File.Delete(path);
        }
    }

    #endregion

    #region 应用待更新

    [Fact]
    public void ApplyPendingUpdates_ReplacesPluginAndClearsStagedState()
    {
        CreateInstalledPlugin(PluginId, "1.0.0");
        var staged = StageUpdate(PluginId, "1.0.0", "1.1.0");

        var result = Assert.Single(PluginUpdateService.ApplyPendingUpdates(_pluginsDir));

        Assert.True(result.Success);
        Assert.Equal("1.0.0", result.FromVersion);
        Assert.Equal("1.1.0", result.ToVersion);
        Assert.Equal("1.1.0", ReadInstalledVersion(PluginId));

        // 暂存与标记清掉，备份保留一代
        Assert.False(Directory.Exists(staged));
        Assert.False(File.Exists(PluginUpdateService.GetPendingFilePath(_pluginsDir, PluginId)));
        Assert.True(Directory.Exists(PluginUpdateService.GetBackupDirectory(_pluginsDir, PluginId)));
        Assert.False(PluginUpdateService.HasPendingUpdates(_pluginsDir));
    }

    [Fact]
    public void ApplyPendingUpdates_PreservesPluginDataFiles()
    {
        var pluginDir = CreateInstalledPlugin(PluginId, "1.0.0");
        File.WriteAllText(Path.Combine(pluginDir, "config.json"), "{\"theme\":\"dark\"}");
        Directory.CreateDirectory(Path.Combine(pluginDir, "userdata"));
        File.WriteAllText(Path.Combine(pluginDir, "userdata", "notes.txt"), "keep-me");

        // 新版包里没有这两个文件——它们是插件自己写下的数据
        StageUpdate(PluginId, "1.0.0", "1.1.0");

        Assert.True(Assert.Single(PluginUpdateService.ApplyPendingUpdates(_pluginsDir)).Success);

        Assert.Equal("1.1.0", ReadInstalledVersion(PluginId));
        Assert.Equal("{\"theme\":\"dark\"}", File.ReadAllText(Path.Combine(pluginDir, "config.json")));
        Assert.Equal("keep-me", File.ReadAllText(Path.Combine(pluginDir, "userdata", "notes.txt")));
    }

    [Fact]
    public void ApplyPendingUpdates_KeepsDisabledState()
    {
        CreateInstalledPlugin(PluginId, "1.0.0", disabled: true);
        StageUpdate(PluginId, "1.0.0", "1.1.0");

        Assert.True(Assert.Single(PluginUpdateService.ApplyPendingUpdates(_pluginsDir)).Success);

        // 更新不该把用户主动禁用的插件悄悄打开
        Assert.True(File.Exists(Path.Combine(_pluginsDir, PluginId, ".disabled")));
    }

    [Fact]
    public void ApplyPendingUpdates_DoesNotCarryOverDeleteOnRestartMarker()
    {
        var pluginDir = CreateInstalledPlugin(PluginId, "1.0.0");
        File.WriteAllText(Path.Combine(pluginDir, ".delete_on_restart"), "2026-01-01");

        StageUpdate(PluginId, "1.0.0", "1.1.0");
        Assert.True(Assert.Single(PluginUpdateService.ApplyPendingUpdates(_pluginsDir)).Success);

        Assert.False(File.Exists(Path.Combine(_pluginsDir, PluginId, ".delete_on_restart")));
    }

    [Fact]
    public void ApplyPendingUpdates_OverwritesChangedPayloadFiles()
    {
        var pluginDir = CreateInstalledPlugin(PluginId, "1.0.0");
        File.WriteAllText(Path.Combine(pluginDir, "README.md"), "# 旧版说明");

        StageUpdate(PluginId, "1.0.0", "1.1.0");
        Assert.True(Assert.Single(PluginUpdateService.ApplyPendingUpdates(_pluginsDir)).Success);

        // README 在新版包里也有一份 → 必须被新版覆盖，而不是保留旧版
        Assert.Equal("# " + PluginId, File.ReadAllText(Path.Combine(pluginDir, "README.md")));
    }

    [Fact]
    public void ApplyPendingUpdates_MismatchedId_IsDiscardedAndOldVersionSurvives()
    {
        var pluginDir = CreateInstalledPlugin(PluginId, "1.0.0");
        File.WriteAllText(Path.Combine(pluginDir, "config.json"), "{\"a\":1}");

        // 暂存包里的 id 是别的插件（打包错了 / 被替换过）
        StageUpdate(PluginId, "1.0.0", "1.1.0");
        File.WriteAllText(Path.Combine(PluginUpdateService.GetStagedDirectory(_pluginsDir, PluginId), "plugin.json"),
            ManifestJson("another-plugin", "1.1.0"));

        var result = Assert.Single(PluginUpdateService.ApplyPendingUpdates(_pluginsDir));

        Assert.False(result.Success);
        Assert.Contains("不一致", result.Error);
        Assert.Equal("1.0.0", ReadInstalledVersion(PluginId));
        Assert.Equal("{\"a\":1}", File.ReadAllText(Path.Combine(pluginDir, "config.json")));

        // 这种包重试也没有意义，直接丢弃
        Assert.False(PluginUpdateService.HasPendingUpdates(_pluginsDir));
    }

    [Fact]
    public void ApplyPendingUpdates_MissingStagedDirectory_IsDiscarded()
    {
        CreateInstalledPlugin(PluginId, "1.0.0");

        // 只写 pending.json，不铺暂存目录（模拟暂存被清理软件删掉）
        Directory.CreateDirectory(PluginUpdateService.GetPluginUpdateDirectory(_pluginsDir, PluginId));
        File.WriteAllText(PluginUpdateService.GetPendingFilePath(_pluginsDir, PluginId),
            JsonSerializer.Serialize(new PendingPluginUpdate { PluginId = PluginId, ToVersion = "1.1.0" }));

        var result = Assert.Single(PluginUpdateService.ApplyPendingUpdates(_pluginsDir));

        Assert.False(result.Success);
        Assert.Equal("1.0.0", ReadInstalledVersion(PluginId));
        Assert.False(PluginUpdateService.HasPendingUpdates(_pluginsDir));
    }

    [Fact]
    public void ApplyPendingUpdates_RollsBackWhenReplaceFails()
    {
        var pluginDir = CreateInstalledPlugin(PluginId, "1.0.0");
        File.WriteAllText(Path.Combine(pluginDir, "config.json"), "{\"keep\":true}");

        var staged = StageUpdate(PluginId, "1.0.0", "1.1.0");

        // 用独占句柄锁住暂存里的 dll：替换阶段的 File.Copy 必然失败，从而走回滚分支
        using (new FileStream(Path.Combine(staged, PluginId + ".dll"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = Assert.Single(PluginUpdateService.ApplyPendingUpdates(_pluginsDir));

            Assert.False(result.Success);
            Assert.True(result.RolledBack);
            Assert.Contains("替换插件目录失败", result.Error);
        }

        // 旧版本与插件数据都完好无损
        Assert.Equal("1.0.0", ReadInstalledVersion(PluginId));
        Assert.Equal("{\"keep\":true}", File.ReadAllText(Path.Combine(pluginDir, "config.json")));
        Assert.True(File.Exists(Path.Combine(pluginDir, PluginId + ".dll")));
    }

    [Fact]
    public void ApplyPendingUpdates_GivesUpAfterMaxAttempts()
    {
        CreateInstalledPlugin(PluginId, "1.0.0");
        var staged = StageUpdate(PluginId, "1.0.0", "1.1.0");

        using (new FileStream(Path.Combine(staged, PluginId + ".dll"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // 第 1 次失败：保留暂存，下次启动还能重试
            Assert.False(Assert.Single(PluginUpdateService.ApplyPendingUpdates(_pluginsDir)).Success);
            Assert.True(PluginUpdateService.HasPendingUpdates(_pluginsDir));
            Assert.Equal(1, PluginUpdateService.GetPendingUpdate(_pluginsDir, PluginId)!.ApplyAttempts);

            // 第 2 次失败：到上限，标记被清掉，之后不再重试
            Assert.False(Assert.Single(PluginUpdateService.ApplyPendingUpdates(_pluginsDir)).Success);
            Assert.False(PluginUpdateService.HasPendingUpdates(_pluginsDir));
            Assert.False(File.Exists(PluginUpdateService.GetPendingFilePath(_pluginsDir, PluginId)));

            // 标记没了就不可能再被应用——暂存目录里被独占锁住的 dll 删不掉是正常的，
            // 清理是 best-effort；残留目录不会再被任何流程读取，下次重新下载时会被覆盖
            Assert.Empty(PluginUpdateService.ApplyPendingUpdates(_pluginsDir));
        }

        // 无论怎么失败，插件本体始终还在
        Assert.Equal("1.0.0", ReadInstalledVersion(PluginId));
    }

    [Fact]
    public void ApplyPendingUpdates_InstallsWhenPluginNotPresentYet()
    {
        // 暂存了一个插件，但插件目录还不存在（先下载后安装的场景）
        StageUpdate(PluginId, "", "1.0.0");

        var result = Assert.Single(PluginUpdateService.ApplyPendingUpdates(_pluginsDir));

        Assert.True(result.Success);
        Assert.Equal("1.0.0", ReadInstalledVersion(PluginId));
    }

    [Fact]
    public void ApplyPendingUpdates_NoPending_ReturnsEmpty()
    {
        CreateInstalledPlugin(PluginId, "1.0.0");
        Assert.Empty(PluginUpdateService.ApplyPendingUpdates(_pluginsDir));
    }

    [Fact]
    public void ApplyPendingUpdates_MultiplePlugins_AreIndependent()
    {
        CreateInstalledPlugin("plugin-a", "1.0.0");
        CreateInstalledPlugin("plugin-b", "2.0.0");
        StageUpdate("plugin-a", "1.0.0", "1.1.0");
        StageUpdate("plugin-b", "2.0.0", "2.1.0");

        var results = PluginUpdateService.ApplyPendingUpdates(_pluginsDir);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal("1.1.0", ReadInstalledVersion("plugin-a"));
        Assert.Equal("2.1.0", ReadInstalledVersion("plugin-b"));
    }

    #endregion

    #region 后台检查节流

    [Fact]
    public void BackgroundCheck_IsThrottledUntilIntervalElapses()
    {
        Assert.True(PluginUpdateService.ShouldCheckInBackground(_pluginsDir));

        PluginUpdateService.MarkChecked(_pluginsDir);

        Assert.False(PluginUpdateService.ShouldCheckInBackground(_pluginsDir));

        // 时间戳落在 cache 目录里（可随时删除，不影响更新事务）
        var stamp = Path.Combine(PluginUpdateService.GetCacheRoot(_pluginsDir), "last-check.json");
        Assert.True(File.Exists(stamp));
    }

    [Fact]
    public void BackgroundCheck_CorruptedTimestamp_FallsBackToChecking()
    {
        Directory.CreateDirectory(PluginUpdateService.GetCacheRoot(_pluginsDir));
        File.WriteAllText(Path.Combine(PluginUpdateService.GetCacheRoot(_pluginsDir), "last-check.json"), "{ broken");

        Assert.True(PluginUpdateService.ShouldCheckInBackground(_pluginsDir));
    }

    #endregion

    #region 插件 id 规则

    [Theory]
    [InlineData("test-plugin", true)]
    [InlineData("abc", true)]
    [InlineData("Ab", false)]              // 太短且含大写
    [InlineData("Test-Plugin", false)]     // 大写
    [InlineData("-leading", false)]        // 不能以连字符开头
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("../../evil", false)]      // 路径穿越
    public void IsValidPluginId_MatchesDocumentedRule(string? id, bool expected)
    {
        Assert.Equal(expected, PluginLoader.IsValidPluginId(id));
    }

    #endregion
}
