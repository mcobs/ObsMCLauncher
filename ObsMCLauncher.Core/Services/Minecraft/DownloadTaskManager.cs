using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Ui;

namespace ObsMCLauncher.Core.Services.Minecraft;

/// <summary>
/// 下载任务类型
/// </summary>
public enum DownloadTaskType
{
    Version,    // 版本下载
    Assets,     // 资源下载
    Mod,        // 模组下载
    Resource    // 资源包下载
}

/// <summary>
/// 下载任务状态
/// </summary>
public enum DownloadTaskStatus
{
    Downloading,  // 下载中
    Completed,    // 已完成
    Failed,       // 失败
    Cancelled     // 已取消
}

/// <summary>
/// 下载任务模型
/// </summary>
public class DownloadTask : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public DownloadTaskType Type { get; set; }
    public CancellationTokenSource? CancellationTokenSource { get; set; }

    private DownloadTaskStatus _status = DownloadTaskStatus.Downloading;
    public DownloadTaskStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(CanCancel));
            }
        }
    }

    private double _progress = 0;
    public double Progress
    {
        get => _progress;
        set
        {
            if (Math.Abs(_progress - value) > 0.01)
            {
                _progress = value;
                OnPropertyChanged(nameof(Progress));
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }
    }

    private double _downloadSpeed = 0;
    public double DownloadSpeed
    {
        get => _downloadSpeed;
        set
        {
            if (Math.Abs(_downloadSpeed - value) > 0.01)
            {
                _downloadSpeed = value;
                OnPropertyChanged(nameof(DownloadSpeed));
                OnPropertyChanged(nameof(SpeedText));
            }
        }
    }

    public string StatusText
    {
        get
        {
            return Status switch
            {
                DownloadTaskStatus.Downloading => "下载中",
                DownloadTaskStatus.Completed => "已完成",
                DownloadTaskStatus.Failed => "失败",
                DownloadTaskStatus.Cancelled => "已取消",
                _ => "未知"
            };
        }
    }

    public string ProgressText => $"{Progress:F0}%";

    public string SpeedText
    {
        get
        {
            if (DownloadSpeed == 0) return "";
            
            if (DownloadSpeed < 1024)
                return $"{DownloadSpeed:F0} B/s";
            else if (DownloadSpeed < 1024 * 1024)
                return $"{DownloadSpeed / 1024:F1} KB/s";
            else
                return $"{DownloadSpeed / 1024 / 1024:F1} MB/s";
        }
    }

    public bool CanCancel => Status == DownloadTaskStatus.Downloading;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// 下载任务管理器（单例）
/// </summary>
public class DownloadTaskManager : INotifyPropertyChanged
{
    private static DownloadTaskManager? _instance;
    public static DownloadTaskManager Instance => _instance ??= new DownloadTaskManager();

    private IDispatcher _dispatcher = new MinecraftImmediateDispatcher();

    private DownloadTaskManager() { }

    /// <summary>
    /// 设置 UI 派发器（用于 Avalonia 联动）
    /// </summary>
    public void SetDispatcher(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    private readonly ObservableCollection<DownloadTask> _tasks = new ObservableCollection<DownloadTask>();
    public ObservableCollection<DownloadTask> Tasks => _tasks;

    public bool HasActiveTasks => _tasks.Any(t => t.Status == DownloadTaskStatus.Downloading);

    public int ActiveTaskCount => _tasks.Count(t => t.Status == DownloadTaskStatus.Downloading);

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? TasksChanged;

    /// <summary>
    /// 添加下载任务
    /// </summary>
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

    /// <summary>
    /// 移除任务
    /// </summary>
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

    /// <summary>
    /// 取消任务
    /// </summary>
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
                catch (ObjectDisposedException) { }
                
                task.Status = DownloadTaskStatus.Cancelled;
                NotifyTasksChanged();
            }
        });
    }

    /// <summary>
    /// 更新任务进度
    /// </summary>
    public void UpdateTaskProgress(string taskId, double progress, string? message = null, double speed = 0)
    {
        _dispatcher.Post(() =>
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null)
            {
                task.Progress = progress;
                if (message != null)
                    task.StatusMessage = message;
                task.DownloadSpeed = speed;
            }
        });
    }

    /// <summary>
    /// 完成任务
    /// </summary>
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

                System.Threading.Tasks.Task.Delay(5000).ContinueWith(_ =>
                {
                    RemoveTask(taskId);
                });
            }
        });
    }

    /// <summary>
    /// 任务失败
    /// </summary>
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
            }
        });
    }

    /// <summary>
    /// 清除所有已完成/已取消/失败的任务
    /// </summary>
    public void ClearInactiveTasks()
    {
        _dispatcher.Post(() =>
        {
            var inactiveTasks = _tasks.Where(t =>
                t.Status == DownloadTaskStatus.Completed ||
                t.Status == DownloadTaskStatus.Cancelled ||
                t.Status == DownloadTaskStatus.Failed).ToList();

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

    private class MinecraftImmediateDispatcher : IDispatcher
    {
        public void Post(Action action) => action();
        public System.Threading.Tasks.Task InvokeAsync(Action action)
        {
            action();
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
