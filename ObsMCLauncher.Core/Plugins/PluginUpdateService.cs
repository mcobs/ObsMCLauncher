using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Plugins;

/// <summary>
/// 插件在线更新：检查更新 → 下载暂存 → 下次启动应用。
///
/// <para><b>为什么不就地替换？</b> <see cref="PluginLoader"/> 用 <c>Assembly.LoadFrom</c> 加载插件，
/// 走的是默认 ALC，<c>OnUnload()</c> 之后程序集并不会真正卸载，dll 句柄仍被进程占用。
/// 因此在运行期替换插件文件必然被文件锁挡住。本服务采用两阶段方案：</para>
/// <list type="number">
/// <item>运行期：下载并校验新版本，解压到 <c>plugin-updates/{id}/new</c>，写 <c>pending.json</c> 标记"待应用"；</item>
/// <item>下次启动、任何插件 dll 被加载之前（<see cref="PluginLoader.LoadAllPlugins"/> 开头）：
/// 备份旧目录 → 替换 → 保留插件数据与禁用状态 → 失败回滚。</item>
/// </list>
///
/// <para><b>目录约定</b>（暂存与备份必须放在 plugins 目录<b>之外</b>，
/// 否则会被 <see cref="PluginLoader"/> 当作插件目录扫描加载）：</para>
/// <code>
/// OMCL/plugins/{id}/                     已安装插件（不变）
/// OMCL/plugin-updates/{id}.zip           下载的原始包（解压后删除）
/// OMCL/plugin-updates/{id}/new/          校验通过、待应用的新版本
/// OMCL/plugin-updates/{id}/backup/       上一个大版本的备份（保留一代）
/// OMCL/plugin-updates/{id}/pending.json  待更新标记
/// OMCL/cache/plugin-updates/last-check.json   后台检查节流时间戳（可随时删）
/// OMCL/cache/plugin-updates/check-cache.json  GitHub Release 深度检查结果缓存（TTL）
/// </code>
/// </summary>
public static class PluginUpdateService
{
    /// <summary>待更新标记文件名</summary>
    public const string PendingFileName = "pending.json";

    /// <summary>暂存的新版本子目录名</summary>
    public const string StagedFolderName = "new";

    /// <summary>备份子目录名</summary>
    public const string BackupFolderName = "backup";

    /// <summary>后台静默检查的节流间隔</summary>
    public static readonly TimeSpan BackgroundCheckInterval = TimeSpan.FromHours(12);

    /// <summary>GitHub Release 深度检查结果的缓存时长</summary>
    public static readonly TimeSpan DeepCheckCacheDuration = TimeSpan.FromHours(6);

    /// <summary>深度检查并发上限</summary>
    public const int DeepCheckConcurrency = 4;

    /// <summary>同一更新连续应用失败多少次后放弃（丢弃暂存，避免每次启动都重试）</summary>
    public const int MaxApplyAttempts = 2;

    /// <summary>替换插件目录时排除的启动器标记文件（由替换流程单独处理）</summary>
    private static readonly string[] LauncherMarkerFiles = { ".disabled", ".delete_on_restart" };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    #region 路径

    /// <summary>插件目录的父目录（即 OMCL）</summary>
    private static string GetOmclDirectory(string pluginsDirectory)
    {
        var full = Path.GetFullPath(pluginsDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return Path.GetDirectoryName(full) ?? full;
    }

    /// <summary>事务性数据根：&lt;OMCL&gt;/plugin-updates</summary>
    public static string GetUpdatesRoot(string pluginsDirectory) =>
        Path.Combine(GetOmclDirectory(pluginsDirectory), "plugin-updates");

    /// <summary>可随时删除的缓存根：&lt;OMCL&gt;/cache/plugin-updates</summary>
    public static string GetCacheRoot(string pluginsDirectory) =>
        Path.Combine(GetOmclDirectory(pluginsDirectory), "cache", "plugin-updates");

    /// <summary>某插件的更新工作目录：&lt;OMCL&gt;/plugin-updates/{id}</summary>
    public static string GetPluginUpdateDirectory(string pluginsDirectory, string pluginId) =>
        Path.Combine(GetUpdatesRoot(pluginsDirectory), pluginId);

    /// <summary>某插件的暂存目录：&lt;OMCL&gt;/plugin-updates/{id}/new</summary>
    public static string GetStagedDirectory(string pluginsDirectory, string pluginId) =>
        Path.Combine(GetPluginUpdateDirectory(pluginsDirectory, pluginId), StagedFolderName);

    /// <summary>某插件的备份目录：&lt;OMCL&gt;/plugin-updates/{id}/backup</summary>
    public static string GetBackupDirectory(string pluginsDirectory, string pluginId) =>
        Path.Combine(GetPluginUpdateDirectory(pluginsDirectory, pluginId), BackupFolderName);

    /// <summary>某插件的待更新标记文件路径</summary>
    public static string GetPendingFilePath(string pluginsDirectory, string pluginId) =>
        Path.Combine(GetPluginUpdateDirectory(pluginsDirectory, pluginId), PendingFileName);

    private static string GetLastCheckPath(string pluginsDirectory) =>
        Path.Combine(GetCacheRoot(pluginsDirectory), "last-check.json");

    private static string GetDeepCheckCachePath(string pluginsDirectory) =>
        Path.Combine(GetCacheRoot(pluginsDirectory), "check-cache.json");

    #endregion

    #region 待更新状态

    /// <summary>是否存在待重启生效的插件更新</summary>
    public static bool HasPendingUpdates(string pluginsDirectory) =>
        GetPendingUpdates(pluginsDirectory).Count > 0;

    /// <summary>列出全部待更新项（读盘，失败项会被跳过）</summary>
    public static IReadOnlyList<PendingPluginUpdate> GetPendingUpdates(string pluginsDirectory)
    {
        var list = new List<PendingPluginUpdate>();
        try
        {
            var root = GetUpdatesRoot(pluginsDirectory);
            if (!Directory.Exists(root)) return list;

            foreach (var dir in Directory.GetDirectories(root))
            {
                var pendingPath = Path.Combine(dir, PendingFileName);
                if (!File.Exists(pendingPath)) continue;

                var pending = TryReadPending(pendingPath);
                if (pending == null || string.IsNullOrWhiteSpace(pending.PluginId)) continue;

                list.Add(pending);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginUpdate", $"读取待更新列表失败: {ex.Message}");
        }

        return list;
    }

    /// <summary>取某个插件的待更新项；无则返回 null</summary>
    public static PendingPluginUpdate? GetPendingUpdate(string pluginsDirectory, string pluginId)
    {
        if (!PluginLoader.IsValidPluginId(pluginId)) return null;

        var path = GetPendingFilePath(pluginsDirectory, pluginId);
        return File.Exists(path) ? TryReadPending(path) : null;
    }

    /// <summary>
    /// 取消一个尚未应用的更新：删除暂存目录、原始包与待更新标记。
    /// 备份目录保留（它属于上一次已生效的更新）。
    /// </summary>
    public static bool CancelPendingUpdate(string pluginsDirectory, string pluginId)
    {
        if (!PluginLoader.IsValidPluginId(pluginId)) return false;

        try
        {
            var updateDir = GetPluginUpdateDirectory(pluginsDirectory, pluginId);
            if (!Directory.Exists(updateDir)) return false;

            DeleteDirectoryQuiet(GetStagedDirectory(pluginsDirectory, pluginId));
            TryDeleteFile(Path.Combine(updateDir, $"{pluginId}.zip"));
            TryDeleteFile(GetPendingFilePath(pluginsDirectory, pluginId));

            DebugLogger.Info("PluginUpdate", $"已取消待更新: {pluginId}");
            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginUpdate", $"取消待更新失败 [{pluginId}]: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region 检查更新

    /// <summary>
    /// 仅用市场索引的 version 字段比对本地版本，<b>零额外网络请求</b>。
    /// 顺带把结果写回 <see cref="MarketPlugin.HasUpdate"/>，供市场列表页复用。
    /// </summary>
    public static List<PluginUpdateInfo> BuildUpdateInfo(
        string pluginsDirectory,
        IReadOnlyList<LoadedPlugin> installed,
        IReadOnlyList<MarketPlugin> market)
    {
        var marketById = new Dictionary<string, MarketPlugin>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in market)
        {
            if (string.IsNullOrWhiteSpace(m.Id)) continue;

            marketById[m.Id] = m;

            // 市场索引对象会在缓存期内被反复复用，先清零再由下面的比对重新赋值，
            // 否则某个插件更新完/卸载后，市场列表里的"可更新"角标会一直挂着
            m.HasUpdate = false;
        }

        var list = new List<PluginUpdateInfo>();
        foreach (var local in installed)
        {
            marketById.TryGetValue(local.Id, out var entry);

            var available = local.Version;
            var hasUpdate = false;
            var fromRelease = false;

            if (entry != null)
            {
                // 优先用深度检查（GitHub Release）拿到的版本，否则用市场索引登记版本
                var candidate = !string.IsNullOrWhiteSpace(entry.LatestVersion) ? entry.LatestVersion : entry.Version;

                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    hasUpdate = VersionCompare.Compare(candidate, local.Version) > 0;
                    if (hasUpdate)
                    {
                        available = candidate;
                        fromRelease = !string.IsNullOrWhiteSpace(entry.LatestVersion);
                    }
                }

                entry.HasUpdate = hasUpdate;
            }

            list.Add(new PluginUpdateInfo
            {
                PluginId = local.Id,
                Name = local.Name,
                InstalledVersion = local.Version,
                AvailableVersion = available,
                HasUpdate = hasUpdate,
                IsStaged = File.Exists(GetPendingFilePath(pluginsDirectory, local.Id)),
                IsEnabled = !File.Exists(Path.Combine(local.DirectoryPath, ".disabled")),
                IsFromReleaseCheck = fromRelease,
                Market = entry
            });
        }

        return list;
    }

    /// <summary>
    /// 检查更新。<paramref name="deep"/> = true 时先对声明了 releaseUrl 的市场条目并发查一次
    /// GitHub Release（结果落盘缓存，TTL <see cref="DeepCheckCacheDuration"/>），再比对。
    /// </summary>
    public static async Task<List<PluginUpdateInfo>> CheckForUpdatesAsync(
        string pluginsDirectory,
        IReadOnlyList<LoadedPlugin> installed,
        IReadOnlyList<MarketPlugin> market,
        bool deep = false,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (deep)
        {
            await RunDeepCheckAsync(pluginsDirectory, market, forceRefresh, cancellationToken);
        }

        return BuildUpdateInfo(pluginsDirectory, installed, market);
    }

    private static async Task RunDeepCheckAsync(
        string pluginsDirectory,
        IReadOnlyList<MarketPlugin> market,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var cache = LoadDeepCheckCache(pluginsDirectory);
        var cacheById = new Dictionary<string, DeepCheckEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in cache.Entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.Id)) cacheById[entry.Id] = entry;
        }

        var now = DateTime.Now;
        var needsFetch = new List<MarketPlugin>();

        foreach (var item in market)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.ReleaseUrl)) continue;

            if (!forceRefresh &&
                cacheById.TryGetValue(item.Id, out var cached) &&
                now - cached.CheckedAt < DeepCheckCacheDuration)
            {
                item.LatestVersion = cached.Version;
                item.LatestDownloadUrl = cached.DownloadUrl;
            }
            else
            {
                needsFetch.Add(item);
            }
        }

        if (needsFetch.Count == 0) return;

        using var gate = new SemaphoreSlim(DeepCheckConcurrency);
        var lockObj = new object();

        var tasks = needsFetch.Select(async item =>
        {
            try
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var (version, downloadUrl) = await PluginMarketService.GetLatestReleaseInfoAsync(
                    item.ReleaseUrl!, item.AssetPattern, item.Id);

                if (string.IsNullOrWhiteSpace(version)) return;

                item.LatestVersion = version;
                item.LatestDownloadUrl = downloadUrl;

                lock (lockObj)
                {
                    cacheById[item.Id] = new DeepCheckEntry
                    {
                        Id = item.Id,
                        Version = version,
                        DownloadUrl = downloadUrl,
                        CheckedAt = DateTime.Now
                    };
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                DebugLogger.Warn("PluginUpdate", $"深度检查失败 [{item.Id}]: {ex.Message}");
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("PluginUpdate", $"深度检查未全部完成: {ex.Message}");
        }

        cache.Entries = cacheById.Values.ToList();
        SaveDeepCheckCache(pluginsDirectory, cache);
    }

    /// <summary>后台静默检查是否到期（节流 <see cref="BackgroundCheckInterval"/>）</summary>
    public static bool ShouldCheckInBackground(string pluginsDirectory)
    {
        try
        {
            var path = GetLastCheckPath(pluginsDirectory);
            if (!File.Exists(path)) return true;

            var state = JsonSerializer.Deserialize<LastCheckState>(File.ReadAllText(path));
            if (state == null) return true;

            return DateTime.Now - state.CheckedAt >= BackgroundCheckInterval;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>记下本次后台检查时间</summary>
    public static void MarkChecked(string pluginsDirectory)
    {
        try
        {
            Directory.CreateDirectory(GetCacheRoot(pluginsDirectory));
            File.WriteAllText(
                GetLastCheckPath(pluginsDirectory),
                JsonSerializer.Serialize(new LastCheckState { CheckedAt = DateTime.Now }, WriteOptions));
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("PluginUpdate", $"写入检查时间戳失败: {ex.Message}");
        }
    }

    #endregion

    #region 下载并暂存

    /// <summary>
    /// 下载并暂存一个插件更新。成功后会在 <c>plugin-updates/{id}/</c> 下落盘
    /// <c>new/</c> 与 <c>pending.json</c>，等待下次启动应用。
    /// </summary>
    /// <param name="plugin">市场条目（用于取下载地址）</param>
    /// <param name="pluginsDirectory">插件安装目录</param>
    /// <param name="installedVersion">本地当前版本（写入 pending.json 便于展示）</param>
    /// <param name="progress">0-100 进度</param>
    /// <returns>(成功?, 失败原因, 新版本号)</returns>
    public static async Task<(bool Success, string? Error, string? StagedVersion)> StageUpdateAsync(
        MarketPlugin plugin,
        string pluginsDirectory,
        string installedVersion,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!PluginLoader.IsValidPluginId(plugin.Id))
        {
            return (false, "插件 id 非法", null);
        }

        try
        {
            progress?.Report(0);

            // 1. releaseUrl 优先：市场索引的 version 可能滞后，直接问 Release 更准
            if (!string.IsNullOrWhiteSpace(plugin.ReleaseUrl) && string.IsNullOrWhiteSpace(plugin.LatestDownloadUrl))
            {
                var (latestVersion, latestUrl) = await PluginMarketService.GetLatestReleaseInfoAsync(
                    plugin.ReleaseUrl, plugin.AssetPattern, plugin.Id);

                if (!string.IsNullOrWhiteSpace(latestUrl))
                {
                    plugin.LatestVersion = latestVersion;
                    plugin.LatestDownloadUrl = latestUrl;
                }
            }

            var downloadUrl = PluginMarketService.GetDownloadUrl(plugin);
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return (false, "无法获取下载地址", null);
            }

            var updateDir = GetPluginUpdateDirectory(pluginsDirectory, plugin.Id);
            Directory.CreateDirectory(updateDir);

            // 2. 下载原始包（进度 0-60）
            var zipPath = Path.Combine(updateDir, $"{plugin.Id}.zip");
            TryDeleteFile(zipPath);

            if (!await PluginMarketService.DownloadToFileAsync(downloadUrl, zipPath, progress, 0, 60, cancellationToken))
            {
                return (false, "下载失败", null);
            }

            // 3. 包体校验（根目录含 plugin.json 且 id 一致）
            var validationError = PluginMarketService.ValidatePluginPackage(zipPath, plugin.Id);
            if (validationError != null)
            {
                TryDeleteFile(zipPath);
                return (false, validationError, null);
            }

            // 4. 解压到暂存目录（进度 60-100）
            var stagedDir = GetStagedDirectory(pluginsDirectory, plugin.Id);
            DeleteDirectoryQuiet(stagedDir);
            SafeZipExtractor.ExtractToDirectory(zipPath, stagedDir);
            progress?.Report(100);

            var metadata = TryReadManifest(Path.Combine(stagedDir, "plugin.json"));
            if (metadata == null || string.IsNullOrWhiteSpace(metadata.Version))
            {
                DeleteDirectoryQuiet(stagedDir);
                return (false, "新包的 plugin.json 无法解析", null);
            }

            // 5. 落盘待更新标记
            WritePending(pluginsDirectory, new PendingPluginUpdate
            {
                PluginId = plugin.Id,
                FromVersion = installedVersion ?? string.Empty,
                ToVersion = metadata.Version,
                StagedAt = DateTime.Now,
                SourceUrl = downloadUrl
            });

            // 6. 原始包已解压完毕，删掉省体积
            TryDeleteFile(zipPath);

            DebugLogger.Info("PluginUpdate", $"已暂存插件更新: {plugin.Id} {installedVersion} -> {metadata.Version}（重启后生效）");
            return (true, null, metadata.Version);
        }
        catch (OperationCanceledException)
        {
            return (false, "已取消", null);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginUpdate", $"暂存插件更新失败 [{plugin.Id}]: {ex.Message}");
            return (false, ex.Message, null);
        }
    }

    #endregion

    #region 应用待更新（启动时，插件 dll 加载之前）

    /// <summary>
    /// 应用全部待更新。<b>必须</b>在任何插件 dll 被加载之前调用。
    /// 每个插件独立成败，互不影响。
    /// </summary>
    public static IReadOnlyList<PluginUpdateApplyResult> ApplyPendingUpdates(string pluginsDirectory)
    {
        var results = new List<PluginUpdateApplyResult>();
        var pendings = GetPendingUpdates(pluginsDirectory);
        if (pendings.Count == 0) return results;

        foreach (var pending in pendings)
        {
            var result = ApplyOne(pending, pluginsDirectory);
            results.Add(result);

            if (result.Success)
            {
                DebugLogger.Info("PluginUpdate", $"插件更新已生效: {result.PluginId} {result.FromVersion} -> {result.ToVersion}");
            }
            else
            {
                DebugLogger.Error("PluginUpdate", $"插件更新失败 [{result.PluginId}]: {result.Error}（已回滚: {result.RolledBack}）");
            }
        }

        return results;
    }

    private static PluginUpdateApplyResult ApplyOne(PendingPluginUpdate pending, string pluginsDirectory)
    {
        var pluginId = pending.PluginId;

        if (!PluginLoader.IsValidPluginId(pluginId))
        {
            return Failure(pluginId, pending.FromVersion, null, false, "插件 id 非法，已丢弃");
        }

        var stagedDir = GetStagedDirectory(pluginsDirectory, pluginId);
        var targetDir = Path.Combine(pluginsDirectory, pluginId);
        var backupDir = GetBackupDirectory(pluginsDirectory, pluginId);
        var updateDir = GetPluginUpdateDirectory(pluginsDirectory, pluginId);

        if (!Directory.Exists(stagedDir))
        {
            DiscardStaged(pluginsDirectory, pluginId);
            return Failure(pluginId, pending.FromVersion, null, false, "暂存目录不存在，已丢弃该更新");
        }

        // 新包清单校验：必须能解析，且 id 与目标一致（防止把 A 插件塞进 B 插件目录）
        var stagedMetadata = TryReadManifest(Path.Combine(stagedDir, "plugin.json"));
        if (stagedMetadata == null || string.IsNullOrWhiteSpace(stagedMetadata.Version))
        {
            DiscardStaged(pluginsDirectory, pluginId);
            return Failure(pluginId, pending.FromVersion, null, false, "新包的 plugin.json 无法解析，已丢弃该更新");
        }

        if (!string.Equals(stagedMetadata.Id, pluginId, StringComparison.OrdinalIgnoreCase))
        {
            DiscardStaged(pluginsDirectory, pluginId);
            return Failure(pluginId, pending.FromVersion, stagedMetadata.Version, false,
                $"新包的 id ({stagedMetadata.Id}) 与目标目录 ({pluginId}) 不一致，已丢弃该更新");
        }

        var toVersion = stagedMetadata.Version;

        // 替换前读一次本地真实版本（pending.json 里的只是"提交时"的记录，可能已过时）
        var fromVersion = pending.FromVersion;
        if (Directory.Exists(targetDir))
        {
            var currentMetadata = TryReadManifest(Path.Combine(targetDir, "plugin.json"));
            if (currentMetadata != null && !string.IsNullOrWhiteSpace(currentMetadata.Version))
            {
                fromVersion = currentMetadata.Version;
            }
        }

        // 记录旧目录中"新版没有"的文件 = 插件自己的数据，替换后搬回来
        var extraFiles = Directory.Exists(targetDir)
            ? CollectExtraRelativePaths(targetDir, stagedDir)
            : new List<string>();

        // 备份：整目录 Move 到 backup（同卷，秒级且原子）
        var hasBackup = false;
        try
        {
            DeleteDirectoryQuiet(backupDir);
            Directory.CreateDirectory(updateDir);

            if (Directory.Exists(targetDir))
            {
                Directory.Move(targetDir, backupDir);
                hasBackup = true;
            }
        }
        catch (Exception ex)
        {
            return Failure(pluginId, fromVersion, toVersion, false, $"备份旧版本失败: {ex.Message}");
        }

        var wasDisabled = hasBackup && File.Exists(Path.Combine(backupDir, ".disabled"));

        try
        {
            Directory.CreateDirectory(targetDir);
            CopyDirectory(stagedDir, targetDir);
            RestoreExtraFiles(backupDir, targetDir, extraFiles);

            // 保留禁用状态：更新不应该把用户禁用的插件悄悄打开
            if (wasDisabled)
            {
                File.WriteAllText(Path.Combine(targetDir, ".disabled"), DateTime.Now.ToString());
            }

            var installedMetadata = TryReadManifest(Path.Combine(targetDir, "plugin.json"));
            if (installedMetadata == null || string.IsNullOrWhiteSpace(installedMetadata.Version))
            {
                throw new IOException("替换后未能在插件目录读到 plugin.json");
            }

            toVersion = installedMetadata.Version;
        }
        catch (Exception ex)
        {
            var rolledBack = TryRollback(targetDir, backupDir, hasBackup);

            // 保留暂存与 pending.json 以便下次启动重试；连续失败到上限才彻底丢弃
            var attempts = pending.ApplyAttempts + 1;
            if (attempts >= MaxApplyAttempts)
            {
                DiscardStaged(pluginsDirectory, pluginId);
            }
            else
            {
                pending.ApplyAttempts = attempts;
                WritePending(pluginsDirectory, pending);
            }

            return Failure(pluginId, fromVersion, toVersion, rolledBack,
                $"替换插件目录失败: {ex.Message}");
        }

        // 成功：清掉暂存与标记；备份保留一代备查
        DiscardStaged(pluginsDirectory, pluginId);

        return new PluginUpdateApplyResult
        {
            PluginId = pluginId,
            Success = true,
            FromVersion = fromVersion,
            ToVersion = toVersion
        };
    }

    /// <summary>删除暂存目录、原始包与待更新标记（不动备份）</summary>
    private static void DiscardStaged(string pluginsDirectory, string pluginId)
    {
        var updateDir = GetPluginUpdateDirectory(pluginsDirectory, pluginId);
        DeleteDirectoryQuiet(GetStagedDirectory(pluginsDirectory, pluginId));
        TryDeleteFile(Path.Combine(updateDir, $"{pluginId}.zip"));
        TryDeleteFile(GetPendingFilePath(pluginsDirectory, pluginId));
    }

    /// <summary>
    /// 列出旧目录中"新版本包里没有"的文件（相对路径）。
    /// 这些是插件运行期写下的数据（配置、缓存等），替换时必须搬回来。
    /// 启动器自己的标记文件（.disabled / .delete_on_restart）单独处理，不计入。
    /// </summary>
    private static List<string> CollectExtraRelativePaths(string oldDir, string newDir)
    {
        var result = new List<string>();
        try
        {
            var newFull = Path.GetFullPath(newDir);

            foreach (var file in Directory.GetFiles(oldDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(oldDir, file);

                if (LauncherMarkerFiles.Contains(Path.GetFileName(relative), StringComparer.OrdinalIgnoreCase))
                    continue;

                if (!File.Exists(Path.Combine(newFull, relative)))
                    result.Add(relative);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("PluginUpdate", $"扫描插件残留文件失败: {ex.Message}");
        }

        return result;
    }

    private static void RestoreExtraFiles(string backupDir, string targetDir, List<string> relativePaths)
    {
        foreach (var relative in relativePaths)
        {
            try
            {
                var source = Path.Combine(backupDir, relative);
                var destination = Path.Combine(targetDir, relative);

                if (!File.Exists(source) || File.Exists(destination)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
            }
            catch (Exception ex)
            {
                DebugLogger.Warn("PluginUpdate", $"保留插件数据失败 [{relative}]: {ex.Message}");
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destinationDir, Path.GetRelativePath(sourceDir, dir)));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetRelativePath(sourceDir, file)), overwrite: true);
        }
    }

    private static bool TryRollback(string targetDir, string backupDir, bool hasBackup)
    {
        try
        {
            if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);

            if (hasBackup && Directory.Exists(backupDir))
            {
                Directory.Move(backupDir, targetDir);
                return Directory.Exists(targetDir);
            }

            return !hasBackup;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginUpdate", $"回滚失败 [{targetDir}]: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region 内部工具

    private static PluginUpdateApplyResult Failure(
        string pluginId, string? from, string? to, bool rolledBack, string error)
        => new()
        {
            PluginId = pluginId,
            Success = false,
            RolledBack = rolledBack,
            FromVersion = from,
            ToVersion = to,
            Error = error
        };

    private static PluginMetadata? TryReadManifest(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath)) return null;
            return JsonSerializer.Deserialize<PluginMetadata>(File.ReadAllText(manifestPath));
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("PluginUpdate", $"解析 plugin.json 失败 [{manifestPath}]: {ex.Message}");
            return null;
        }
    }

    private static PendingPluginUpdate? TryReadPending(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<PendingPluginUpdate>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("PluginUpdate", $"解析 pending.json 失败 [{path}]: {ex.Message}");
            return null;
        }
    }

    private static void WritePending(string pluginsDirectory, PendingPluginUpdate pending)
    {
        var path = GetPendingFilePath(pluginsDirectory, pending.PluginId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(pending, WriteOptions));
    }

    private static void DeleteDirectoryQuiet(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("PluginUpdate", $"删除目录失败 [{path}]: {ex.Message}");
        }
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("PluginUpdate", $"删除文件失败 [{path}]: {ex.Message}");
        }
    }

    #endregion

    #region 缓存 DTO

    private sealed class LastCheckState
    {
        public DateTime CheckedAt { get; set; }
    }

    private sealed class DeepCheckCache
    {
        public List<DeepCheckEntry> Entries { get; set; } = new();
    }

    private sealed class DeepCheckEntry
    {
        public string Id { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string? DownloadUrl { get; set; }
        public DateTime CheckedAt { get; set; }
    }

    private static DeepCheckCache LoadDeepCheckCache(string pluginsDirectory)
    {
        try
        {
            var path = GetDeepCheckCachePath(pluginsDirectory);
            if (!File.Exists(path)) return new DeepCheckCache();

            return JsonSerializer.Deserialize<DeepCheckCache>(File.ReadAllText(path)) ?? new DeepCheckCache();
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("PluginUpdate", $"读取深度检查缓存失败: {ex.Message}");
            return new DeepCheckCache();
        }
    }

    private static void SaveDeepCheckCache(string pluginsDirectory, DeepCheckCache cache)
    {
        try
        {
            Directory.CreateDirectory(GetCacheRoot(pluginsDirectory));
            File.WriteAllText(GetDeepCheckCachePath(pluginsDirectory), JsonSerializer.Serialize(cache, WriteOptions));
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("PluginUpdate", $"写入深度检查缓存失败: {ex.Message}");
        }
    }

    #endregion
}
