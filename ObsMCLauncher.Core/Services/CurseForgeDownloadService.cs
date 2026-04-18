using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Download;
using ObsMCLauncher.Core.Services.Mirror;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

public static class CurseForgeDownloadService
{
    public static async Task DownloadLatestAsync(
        CurseForgeMod mod,
        string targetDirectory,
        string? gameVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (mod == null) throw new ArgumentNullException(nameof(mod));
        if (string.IsNullOrWhiteSpace(targetDirectory)) throw new ArgumentException("targetDirectory 不能为空", nameof(targetDirectory));

        Directory.CreateDirectory(targetDirectory);

        CurseForgeFileIndex? index = null;

        if (!string.IsNullOrEmpty(gameVersion) && mod.LatestFilesIndexes.Count > 0)
        {
            index = mod.LatestFilesIndexes
                .Where(i => string.Equals(i.GameVersion, gameVersion, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(i => i.ReleaseType)
                .FirstOrDefault();
        }

        if (index == null && mod.LatestFilesIndexes.Count > 0)
        {
            index = mod.LatestFilesIndexes
                .OrderByDescending(i => i.ReleaseType)
                .FirstOrDefault();
        }

        CurseForgeFile? file = null;

        if (index != null)
        {
            file = await CurseForgeService.GetModFileInfoAsync(mod.Id, index.FileId).ConfigureAwait(false);
        }

        file ??= mod.LatestFiles.FirstOrDefault();

        if (file == null)
            throw new Exception("未找到可下载的文件");

        var savePath = Path.Combine(targetDirectory, file.FileName);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var task = DownloadTaskManager.Instance.AddTask($"下载资源: {file.FileName}", DownloadTaskType.Resource, cts);

        try
        {
            var config = LauncherConfig.Load();
            var originalUrl = file.GetDownloadUrl();
            var mirrorUrl = MirrorUrlHelper.RewriteUrl(originalUrl);
            var usedMirror = mirrorUrl != originalUrl;

            if (usedMirror && config.MirrorSourceMode == MirrorSourceMode.PreferMirror && MirrorHealthChecker.IsMirrorAvailable)
            {
                try
                {
                    await HttpDownloadService.DownloadFileToPathAsync(mirrorUrl, savePath, task.Id, cts.Token).ConfigureAwait(false);
                    DownloadTaskManager.Instance.CompleteTask(task.Id);
                    return;
                }
                catch (Exception ex)
                {
                    DebugLogger.Warn("CurseForge", $"镜像源下载失败: {ex.Message}, 回退到官方源");
                    MirrorHealthChecker.MarkUnavailable();

                    if (File.Exists(savePath))
                    {
                        try { File.Delete(savePath); } catch { }
                    }
                }
            }

            await HttpDownloadService.DownloadFileToPathAsync(originalUrl, savePath, task.Id, cts.Token).ConfigureAwait(false);
            DownloadTaskManager.Instance.CompleteTask(task.Id);
        }
        catch (OperationCanceledException)
        {
            DownloadTaskManager.Instance.CancelTask(task.Id);
            throw;
        }
        catch (Exception ex)
        {
            DownloadTaskManager.Instance.FailTask(task.Id, ex.Message);
            throw;
        }
        finally
        {
            cts.Dispose();
        }
    }
}
