using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Download;
using ObsMCLauncher.Core.Services.Mirror;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services.Modrinth;

public static class ModrinthDownloadService
{
    public static async Task DownloadLatestModAsync(
        string projectId,
        string modsDirectory,
        CancellationToken cancellationToken)
    {
        var service = new ModrinthService();

        var versions = await service.GetProjectVersionsAsync(projectId, cancellationToken: cancellationToken).ConfigureAwait(false);
        var version = versions?.FirstOrDefault();
        var file = version?.Files?.FirstOrDefault();

        if (file == null)
            throw new Exception("未找到可下载的文件");

        var savePath = Path.Combine(modsDirectory, file.Filename);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var task = DownloadTaskManager.Instance.AddTask($"下载MOD: {file.Filename}", DownloadTaskType.Mod, cts);

        try
        {
            var config = LauncherConfig.Load();
            var originalUrl = file.Url;
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
                    DebugLogger.Warn("Modrinth", $"镜像源下载失败: {ex.Message}, 回退到官方源");
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
