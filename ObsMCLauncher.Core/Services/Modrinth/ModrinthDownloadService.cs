using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Services.Download;

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
            await HttpDownloadService.DownloadFileToPathAsync(file.Url, savePath, task.Id, cts.Token).ConfigureAwait(false);
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
