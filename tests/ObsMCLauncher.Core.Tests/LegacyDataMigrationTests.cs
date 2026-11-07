using ObsMCLauncher.Core.Services;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// macOS 数据根从 .app 包内换到用户目录后，老用户升级时要靠这里把数据搬出来。
/// 迁移只做 Directory.Move（同盘秒完）+ 冲突不覆盖的递归合并；
/// 跨卷/权限不足时宁可放弃也不能卡启动。
/// </summary>
public class LegacyDataMigrationTests
{
    private static string NewTempRoot()
        => Path.Combine(Path.GetTempPath(), "omcl-migrate-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void MigrateTo_MovesOmclAndGameDirectory()
    {
        var root = NewTempRoot();
        var oldRoot = Path.Combine(root, "old");
        var newRoot = Path.Combine(root, "new");
        try
        {
            Directory.CreateDirectory(Path.Combine(oldRoot, "OMCL", "config"));
            File.WriteAllText(Path.Combine(oldRoot, "OMCL", "config", "launcher.json"), "{\"a\":1}");
            Directory.CreateDirectory(Path.Combine(oldRoot, ".minecraft", "versions", "1.21.4"));

            LegacyDataMigration.MigrateTo(oldRoot, newRoot);

            Assert.True(File.Exists(Path.Combine(newRoot, "OMCL", "config", "launcher.json")));
            Assert.False(Directory.Exists(Path.Combine(oldRoot, "OMCL")));
            Assert.True(Directory.Exists(Path.Combine(newRoot, ".minecraft", "versions", "1.21.4")));
            Assert.False(Directory.Exists(Path.Combine(oldRoot, ".minecraft")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MigrateTo_MergesIntoExistingTarget_AndNeverOverwrites()
    {
        // 目标已存在（上次迁到一半、或配置层先建了目录）时：
        // 把不冲突的搬进去，同名的一律保留目标侧，旧文件留在原地不删
        var root = NewTempRoot();
        var oldRoot = Path.Combine(root, "old");
        var newRoot = Path.Combine(root, "new");
        try
        {
            Directory.CreateDirectory(Path.Combine(oldRoot, "OMCL"));
            File.WriteAllText(Path.Combine(oldRoot, "OMCL", "conflict.txt"), "old-content");
            File.WriteAllText(Path.Combine(oldRoot, "OMCL", "only-old.txt"), "old-only");

            Directory.CreateDirectory(Path.Combine(newRoot, "OMCL"));
            File.WriteAllText(Path.Combine(newRoot, "OMCL", "conflict.txt"), "new-content");

            LegacyDataMigration.MigrateTo(oldRoot, newRoot);

            Assert.Equal("new-content", File.ReadAllText(Path.Combine(newRoot, "OMCL", "conflict.txt")));
            Assert.Equal("old-only", File.ReadAllText(Path.Combine(newRoot, "OMCL", "only-old.txt")));

            // 冲突项不会被删，继续留在旧位置
            Assert.True(File.Exists(Path.Combine(oldRoot, "OMCL", "conflict.txt")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MigrateTo_FillsDirectoriesCreatedByConfigLoad()
    {
        // 复现真实顺序 bug：LauncherConfig.Load() 先 CreateDirectory(新根/OMCL/config)，
        // 迁移随后撞上"目标已存在"。此时必须把旧配置搬进这个空壳目录，而不是整个跳过。
        var root = NewTempRoot();
        var oldRoot = Path.Combine(root, "old");
        var newRoot = Path.Combine(root, "new");
        try
        {
            Directory.CreateDirectory(Path.Combine(oldRoot, "OMCL", "config"));
            File.WriteAllText(Path.Combine(oldRoot, "OMCL", "config", "launcher.json"), "{\"version\":\"old\"}");
            Directory.CreateDirectory(Path.Combine(oldRoot, "OMCL", "plugins", "demo"));

            Directory.CreateDirectory(Path.Combine(newRoot, "OMCL", "config"));

            LegacyDataMigration.MigrateTo(oldRoot, newRoot);

            Assert.True(File.Exists(Path.Combine(newRoot, "OMCL", "config", "launcher.json")));
            Assert.True(Directory.Exists(Path.Combine(newRoot, "OMCL", "plugins", "demo")));
            Assert.False(Directory.Exists(Path.Combine(oldRoot, "OMCL", "config")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MigrateTo_WithoutLegacyData_CreatesNothing()
    {
        var root = NewTempRoot();
        var oldRoot = Path.Combine(root, "old");
        var newRoot = Path.Combine(root, "new");
        try
        {
            Directory.CreateDirectory(oldRoot);

            LegacyDataMigration.MigrateTo(oldRoot, newRoot);

            Assert.False(Directory.Exists(newRoot));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
