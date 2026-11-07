using System;
using System.IO;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

/// <summary>
/// 数据根目录变化后的一次性迁移。
///
/// 背景：macOS 上程序在 ObsMCLauncher.app/Contents/MacOS 里运行，没有 Velopack 的 current
/// 标记，旧版本会把 OMCL 与「运行目录」模式的 .minecraft 写进 .app 包内部——
/// 删除应用时数据一并被删，接了签名/公证后写包内还会破坏签名；更要命的是自动更新是整包替换，
/// 只要更新一次包内数据就全没了。现在这类环境的数据根改到用户目录
/// （见 VersionInfo.ResolveDataRoot），老用户升级后必须把包内的旧数据搬出来。
///
/// 两条硬约束：
/// 1. 必须在任何代码碰 OMCL 之前调用（见 App.OnFrameworkInitializationCompleted）。
///    只要有人先 CreateDirectory 了新根（LauncherConfig.Load 就会），迁移就会撞上"目标已存在"。
/// 2. 只做同盘快捷的 Directory.Move（含递归合并）；跨卷或权限不足时放弃并留日志，
///    下一次启动重试。绝不在这里递归复制整份数据——存档几十 GB，启动时复制会把启动卡死。
/// </summary>
public static class LegacyDataMigration
{
    public static void MigrateIfDataRootChanged()
    {
        try
        {
            var newRoot = VersionInfo.GetAppBaseDirectory();
            var oldRoot = VersionInfo.GetLegacyBaseDirectory();

            if (string.Equals(newRoot, oldRoot, StringComparison.OrdinalIgnoreCase))
                return;

            DebugLogger.Info("Migration", $"数据根已变更，检查是否需迁移: {oldRoot} -> {newRoot}");
            MigrateTo(oldRoot, newRoot);
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("Migration", $"数据迁移检查失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 把旧数据根下的 OMCL 与「运行目录」模式存档搬到新根（纯文件操作，便于测试）。
    /// </summary>
    internal static void MigrateTo(string oldRoot, string newRoot)
    {
        MergeDirectory(Path.Combine(oldRoot, "OMCL"), Path.Combine(newRoot, "OMCL"));
        MergeDirectory(Path.Combine(oldRoot, ".minecraft"), Path.Combine(newRoot, ".minecraft"));
    }

    /// <summary>
    /// 目标不存在就整目录搬（同盘只是一次 rename）；已存在则逐项递归合并，
    /// 同名冲突一律保留目标侧内容——**绝不覆盖用户数据**。
    /// </summary>
    private static void MergeDirectory(string source, string target)
    {
        try
        {
            if (!Directory.Exists(source)) return;

            if (!Directory.Exists(target))
            {
                var parent = Path.GetDirectoryName(Path.GetFullPath(target));
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                Directory.Move(source, target);
                DebugLogger.Info("Migration", $"已迁移: {source} -> {target}");
                return;
            }

            // 目标可能只是别处顺手 CreateDirectory 出来的空壳（LauncherConfig.Load 会先建
            // OMCL/config），也可能是上次迁移到一半留下的。逐项搬，搬不动的留在原地。
            foreach (var entry in Directory.EnumerateFileSystemEntries(source))
            {
                var dest = Path.Combine(target, Path.GetFileName(entry));

                if (Directory.Exists(entry))
                {
                    MergeDirectory(entry, dest);
                }
                else if (!File.Exists(dest))
                {
                    File.Move(entry, dest);
                    DebugLogger.Info("Migration", $"已迁移: {entry} -> {dest}");
                }
            }

            // 源目录搬空后顺手删掉，免得在应用包里留一堆空文件夹
            if (Directory.GetFileSystemEntries(source).Length == 0)
                Directory.Delete(source, recursive: false);
        }
        catch (Exception ex)
        {
            // 留在原地，下次启动再试；数据不会被破坏，只是暂时还在应用包内
            DebugLogger.Warn("Migration", $"迁移失败（数据仍留在原位置）: {source} -> {target}: {ex.Message}");
        }
    }
}
