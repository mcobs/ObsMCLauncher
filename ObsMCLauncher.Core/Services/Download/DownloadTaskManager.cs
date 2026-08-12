using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using ObsMCLauncher.Core.Plugins;
using ObsMCLauncher.Core.Plugins.Events;
using ObsMCLauncher.Core.Services.Ui;

namespace ObsMCLauncher.Core.Services.Download;

public class DownloadTaskManager : INotifyPropertyChanged
{
    private static DownloadTaskManager? _instance;
    public static DownloadTaskManager Instance => _instance ??= new DownloadTaskManager();

    private IDispatcher _dispatcher = new ImmediateDispatcher();

    private DownloadTaskManager()
    {
    }

    /// <summary>
    /// 设置 UI 派发器（用于 Avalonia 联动）
    /// </summary>
    public void SetDispatcher(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    private readonly ObservableCollection<DownloadTask> _tasks = new();

    // 进度上报节流：同一任务 100ms 内最多派发一次，避免高频下载刷新压满 UI 线程
    private const long ProgressThrottleMs = 100;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _lastProgressReportAt = new();

    public ObservableCollection<DownloadTask> Tasks => _tasks;

    public bool HasActiveTasks => _tasks.Any(t => t.Status == DownloadTaskStatus.Downloading);

    public int ActiveTaskCount => _tasks.Count(t => t.Status == DownloadTaskStatus.Downloading);

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? TasksChanged;

    public DownloadTask AddTask(string name, DownloadTaskType type, CancellationTokenSource? cts = null)
    {
        var task = new DownloadTask
        {
            Name = name,
            Type = type,
            CancellationTokenSource = cts,
            Status = DownloadTaskStatus.Downloading
        };

        _dispatcher.Post(() =>
        {
            _tasks.Insert(0, task);
            NotifyTasksChanged();
        });

        return task;
    }

    public void RemoveTask(string taskId)
    {
        _dispatcher.Post(() =>
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null)
            {
                _tasks.Remove(task);
                NotifyTasksChanged();
            }
        });
    }

    public void CancelTask(string taskId)
    {
        _dispatcher.Post(() =>
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null && task.CanCancel)
            {
                try
                {
                    if (task.CancellationTokenSource != null && !task.CancellationTokenSource.IsCancellationRequested)
                    {
                        task.CancellationTokenSource.Cancel();
                    }
                }
                catch (ObjectDisposedException)
                {
                }

                task.Status = DownloadTaskStatus.Cancelled;
                NotifyTasksChanged();
            }
        });
    }

    public void UpdateTaskProgress(string taskId, double progress, string? message = null, double speed = 0)
    {
        // 节流：丢弃高频中间值，最终状态由 CompleteTask/FailTask/CancelTask 保证
        // 首次上报不丢弃（GetOrAdd 初始值为 now - 阈值），避免快速完成的任务详情从未显示
        var now = Environment.TickCount64;
        var lastReport = _lastProgressReportAt.GetOrAdd(taskId, now - ProgressThrottleMs);
        if (now - lastReport < ProgressThrottleMs)
        {
            return;
        }
        _lastProgressReportAt[taskId] = now;

        _dispatcher.Post(() =>
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null)
            {
                task.Progress = progress;
                if (message != null)
                    task.StatusMessage = message;
                task.DownloadSpeed = speed;

                PluginContext.TriggerGlobalEvent(IPluginContext.EventNames.DownloadProgress,
                    new DownloadProgressEventArgs
                    {
                        TaskId = taskId,
                        TaskName = task.Name,
                        TaskType = task.Type.ToString(),
                        Progress = progress,
                        StatusMessage = message,
                        DownloadSpeed = speed,
                        Status = DownloadStatus.Downloading
                    });
            }
        });
    }

    public void CompleteTask(string taskId)
    {
        _dispatcher.Post(() =>
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null)
            {
                task.Status = DownloadTaskStatus.Completed;
                task.Progress = 100;
                task.DownloadSpeed = 0;
                NotifyTasksChanged();

                PluginContext.TriggerGlobalEvent(IPluginContext.EventNames.DownloadProgress,
                    new DownloadProgressEventArgs
                    {
                        TaskId = taskId,
                        TaskName = task.Name,
                        TaskType = task.Type.ToString(),
                        Progress = 100,
                        Status = DownloadStatus.Completed
                    });
            }
        });
    }

    public void FailTask(string taskId, string errorMessage)
    {
        _dispatcher.Post(() =>
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null)
            {
                task.Status = DownloadTaskStatus.Failed;
                task.StatusMessage = errorMessage;
                task.DownloadSpeed = 0;
                NotifyTasksChanged();

                PluginContext.TriggerGlobalEvent(IPluginContext.EventNames.DownloadProgress,
                    new DownloadProgressEventArgs
                    {
                        TaskId = taskId,
                        TaskName = task.Name,
                        TaskType = task.Type.ToString(),
                        Progress = task.Progress,
                        StatusMessage = errorMessage,
                        Status = DownloadStatus.Failed
                    });
            }
        });
    }

    public void ClearInactiveTasks()
    {
        _dispatcher.Post(() =>
        {
            var inactiveTasks = _tasks.Where(t =>
                    t.Status == DownloadTaskStatus.Completed ||
                    t.Status == DownloadTaskStatus.Cancelled ||
                    t.Status == DownloadTaskStatus.Failed)
                .ToList();

            foreach (var task in inactiveTasks)
            {
                _tasks.Remove(task);
            }

            NotifyTasksChanged();
        });
    }

    private void NotifyTasksChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActiveTasks)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveTaskCount)));
        TasksChanged?.Invoke(this, EventArgs.Empty);
    }

    private class ImmediateDispatcher : IDispatcher
    {
        public void Post(Action action) => action();
        public System.Threading.Tasks.Task InvokeAsync(Action action)
        {
            action();
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
