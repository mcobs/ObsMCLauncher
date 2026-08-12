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
    private sealed class MirrorEntry
    {
        public string TargetId = "";
        public PropertyChangedEventHandler Handler = null!;
    }

    private static readonly ConcurrentDictionary<string, MirrorEntry> _taskIdMap = new();

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
            // 已结束的任务：同步终态（若镜像仍在）并清理镜像与事件订阅
            if (src.Status is Minecraft.DownloadTaskStatus.Completed
                or Minecraft.DownloadTaskStatus.Failed
                or Minecraft.DownloadTaskStatus.Cancelled)
            {
                if (_taskIdMap.TryGetValue(src.Id, out _))
                {
                    UpdateTarget(src);
                    CleanupMirror(src);
                }
                continue;
            }

            if (!_taskIdMap.ContainsKey(src.Id))
            {
                // 创建镜像任务
                var targetTask = targetManager.AddTask(src.Name, MapType(src.Type), src.CancellationTokenSource);

                var entry = new MirrorEntry { TargetId = targetTask.Id };
                entry.Handler = (s, e) =>
                {
                    if (s is Minecraft.DownloadTask updatedSrc)
                    {
                        UpdateTarget(updatedSrc);
                    }
                };
                src.PropertyChanged += entry.Handler;
                _taskIdMap[src.Id] = entry;
            }
            else
            {
                UpdateTarget(src);
            }
        }
    }

    private static void CleanupMirror(Minecraft.DownloadTask src)
    {
        if (_taskIdMap.TryRemove(src.Id, out var entry))
        {
            src.PropertyChanged -= entry.Handler;
        }
    }

    private static void UpdateTarget(Minecraft.DownloadTask src)
    {
        if (!_taskIdMap.TryGetValue(src.Id, out var entry)) return;

        var targetManager = Download.DownloadTaskManager.Instance;

        // 同步状态和进度
        targetManager.UpdateTaskProgress(
            entry.TargetId,
            src.Progress,
            src.StatusMessage,
            src.DownloadSpeed);

        // 如果源任务结束，同步结束状态
        if (src.Status == Minecraft.DownloadTaskStatus.Completed)
        {
            targetManager.CompleteTask(entry.TargetId);
        }
        else if (src.Status == Minecraft.DownloadTaskStatus.Failed)
        {
            targetManager.FailTask(entry.TargetId, src.StatusMessage);
        }
        else if (src.Status == Minecraft.DownloadTaskStatus.Cancelled)
        {
            targetManager.CancelTask(entry.TargetId);
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
