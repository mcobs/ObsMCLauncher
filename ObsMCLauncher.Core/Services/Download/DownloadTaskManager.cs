using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;

namespace ObsMCLauncher.Core.Services.Download;

public class DownloadTaskManager : INotifyPropertyChanged
{
    private static DownloadTaskManager? _instance;
    public static DownloadTaskManager Instance => _instance ??= new DownloadTaskManager();

    private DownloadTaskManager()
    {
    }

    private readonly ObservableCollection<DownloadTask> _tasks = new();

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

        // Core 不负责切线程；由调用方保证在合适的线程订阅/操作
        _tasks.Insert(0, task);
        NotifyTasksChanged();

        return task;
    }

    public void RemoveTask(string taskId)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task != null)
        {
            _tasks.Remove(task);
            NotifyTasksChanged();
        }
    }

    public void CancelTask(string taskId)
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
    }

    public void UpdateTaskProgress(string taskId, double progress, string? message = null, double speed = 0)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task != null)
        {
            task.Progress = progress;
            if (message != null)
                task.StatusMessage = message;
            task.DownloadSpeed = speed;
        }
    }

    public void CompleteTask(string taskId)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task != null)
        {
            task.Status = DownloadTaskStatus.Completed;
            task.Progress = 100;
            task.DownloadSpeed = 0;
            NotifyTasksChanged();
        }
    }

    public void FailTask(string taskId, string errorMessage)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task != null)
        {
            task.Status = DownloadTaskStatus.Failed;
            task.StatusMessage = errorMessage;
            task.DownloadSpeed = 0;
            NotifyTasksChanged();
        }
    }

    public void ClearInactiveTasks()
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
    }

    private void NotifyTasksChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActiveTasks)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveTaskCount)));
        TasksChanged?.Invoke(this, EventArgs.Empty);
    }
}
