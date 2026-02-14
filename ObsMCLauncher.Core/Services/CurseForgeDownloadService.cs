using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Download;

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

        // 优先使用 LatestFilesIndexes 选择更匹配的文件
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

        // 兜底：取 latestFiles 里第一个
        file ??= mod.LatestFiles.FirstOrDefault();

        if (file == null)
            throw new Exception("未找到可下载的文件");

        var savePath = Path.Combine(targetDirectory, file.FileName);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var task = DownloadTaskManager.Instance.AddTask($"下载资源: {file.FileName}", DownloadTaskType.Resource, cts);

        try
        {
            var url = file.GetDownloadUrl();
            await HttpDownloadService.DownloadFileToPathAsync(url, savePath, task.Id, cts.Token).ConfigureAwait(false);
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
