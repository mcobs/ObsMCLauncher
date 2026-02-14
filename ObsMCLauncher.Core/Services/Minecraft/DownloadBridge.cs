using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ObsMCLauncher.Core.Services.Download;

namespace ObsMCLauncher.Core.Services.Minecraft;

/// <summary>
/// 下载管理器桥接器
/// 将 Minecraft.DownloadTaskManager 的任务同步到 Download.DownloadTaskManager (右下角面板)
/// </summary>
public sealed class DownloadBridge
{
    private static readonly ConcurrentDictionary<string, string> _taskIdMap = new();

    public static void Initialize()
    {
        var sourceManager = Minecraft.DownloadTaskManager.Instance;
        sourceManager.TasksChanged += (s, e) => SyncTasks();
    }

    private static void SyncTasks()
    {
        var sourceTasks = Minecraft.DownloadTaskManager.Instance.Tasks.ToList();
        var targetManager = Download.DownloadTaskManager.Instance;

        foreach (var src in sourceTasks)
        {
            if (!_taskIdMap.ContainsKey(src.Id))
            {
                // 创建镜像任务
                var targetTask = targetManager.AddTask(src.Name, MapType(src.Type), src.CancellationTokenSource);
                _taskIdMap[src.Id] = targetTask.Id;

                // 监听进度变化
                src.PropertyChanged += (s, e) =>
                {
                    if (s is Minecraft.DownloadTask updatedSrc)
                    {
                        UpdateTarget(updatedSrc);
                    }
                };
            }
            else
            {
                UpdateTarget(src);
            }
        }
    }

    private static void UpdateTarget(Minecraft.DownloadTask src)
    {
        if (_taskIdMap.TryGetValue(src.Id, out var targetId))
        {
            var targetManager = Download.DownloadTaskManager.Instance;
            
            // 同步状态和进度
            targetManager.UpdateTaskProgress(
                targetId, 
                src.Progress, 
                src.StatusMessage, 
                src.DownloadSpeed);

            // 如果源任务结束，同步结束状态
            if (src.Status == Minecraft.DownloadTaskStatus.Completed)
            {
                targetManager.CompleteTask(targetId);
            }
            else if (src.Status == Minecraft.DownloadTaskStatus.Failed)
            {
                targetManager.FailTask(targetId, src.StatusMessage);
            }
        }
    }

    private static Download.DownloadTaskType MapType(Minecraft.DownloadTaskType type)
    {
        return type switch
        {
            Minecraft.DownloadTaskType.Version => Download.DownloadTaskType.Version,
            Minecraft.DownloadTaskType.Assets => Download.DownloadTaskType.Assets,
            Minecraft.DownloadTaskType.Mod => Download.DownloadTaskType.Mod,
            Minecraft.DownloadTaskType.Resource => Download.DownloadTaskType.Resource,
            _ => Download.DownloadTaskType.Version
        };
    }
}
