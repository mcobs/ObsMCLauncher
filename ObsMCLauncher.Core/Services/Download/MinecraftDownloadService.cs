using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ObsMCLauncher.Core.Services.Download;

public static class MinecraftDownloadService
{
    /// <summary>
    /// 仅用于打通“下载任务→任务列表UI”链路的最小实现：模拟一个下载流程。
    /// 后续会替换为真实的版本/资源/库下载。
    /// </summary>
    public static async Task SimulateDownloadAsync(string name, CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var task = DownloadTaskManager.Instance.AddTask(name, DownloadTaskType.Version, cts);

        try
        {
            for (var i = 0; i <= 100; i += 5)
            {
                cts.Token.ThrowIfCancellationRequested();

                DownloadTaskManager.Instance.UpdateTaskProgress(
                    task.Id,
                    i,
                    message: "模拟下载中...",
                    speed: 1024 * 1024 * 2
                );

                await Task.Delay(150, cts.Token).ConfigureAwait(false);
            }

            DownloadTaskManager.Instance.CompleteTask(task.Id);
        }
        catch (OperationCanceledException)
        {
            DownloadTaskManager.Instance.CancelTask(task.Id);
        }
        catch (Exception ex)
        {
            DownloadTaskManager.Instance.FailTask(task.Id, ex.Message);
        }
        finally
        {
            cts.Dispose();
        }
    }

    public static string GetDefaultGameDirectory()
    {
        // 与 LauncherConfig.GameDirectory 的默认逻辑保持一致（简化版）
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
    }
}
