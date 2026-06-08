using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Services.Download;
using ObsMCLauncher.Core.Services.Ui;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class DownloadManagerViewModel : ViewModelBase
{
    private readonly DownloadTaskManager _manager;
    private readonly IDispatcher _dispatcher;

    [ObservableProperty]
    private bool _isPanelVisible;

    public ObservableCollection<DownloadTask> Tasks => _manager.Tasks;

    public bool HasActiveTasks => _manager.HasActiveTasks;

    public int ActiveTaskCount => _manager.ActiveTaskCount;

    public int CompletedCount => _manager.Tasks.Count(t => t.Status == DownloadTaskStatus.Completed);

    public int FailedCount => _manager.Tasks.Count(t => t.Status == DownloadTaskStatus.Failed);

    public bool HasAnyTasks => _manager.Tasks.Count > 0;

    public bool HasCompletedOrCancelledTasks =>
        _manager.Tasks.Any(t => t.Status == DownloadTaskStatus.Completed ||
                                 t.Status == DownloadTaskStatus.Cancelled ||
                                 t.Status == DownloadTaskStatus.Failed);

    public string TaskSummary
    {
        get
        {
            var downloading = ActiveTaskCount;
            var completed = CompletedCount;
            var failed = FailedCount;
            var parts = new System.Collections.Generic.List<string>();
            if (downloading > 0) parts.Add($"下载中: {downloading}");
            if (completed > 0) parts.Add($"已完成: {completed}");
            if (failed > 0) parts.Add($"失败: {failed}");
            return parts.Count > 0 ? string.Join("  |  ", parts) : "";
        }
    }

    public DownloadManagerViewModel(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _manager = DownloadTaskManager.Instance;

        _manager.PropertyChanged += OnManagerPropertyChanged;
        _manager.TasksChanged += OnTasksCollectionChanged;
    }

    private void OnManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _dispatcher.Post(() =>
        {
            if (e.PropertyName == nameof(DownloadTaskManager.HasActiveTasks))
            {
                OnPropertyChanged(nameof(HasActiveTasks));
            }
            else if (e.PropertyName == nameof(DownloadTaskManager.ActiveTaskCount))
            {
                OnPropertyChanged(nameof(ActiveTaskCount));
            }
            RefreshStatistics();
        });
    }

    private void OnTasksCollectionChanged(object? sender, System.EventArgs e)
    {
        _dispatcher.Post(RefreshStatistics);
    }

    private void RefreshStatistics()
    {
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(HasAnyTasks));
        OnPropertyChanged(nameof(HasCompletedOrCancelledTasks));
        OnPropertyChanged(nameof(TaskSummary));
    }

    [RelayCommand]
    private void TogglePanelVisibility()
    {
        IsPanelVisible = !IsPanelVisible;
    }

    [RelayCommand]
    private void ClosePanel()
    {
        IsPanelVisible = false;
    }

    [RelayCommand]
    private void ClearInactiveTasks()
    {
        _manager.ClearInactiveTasks();
    }

    [RelayCommand]
    private void CancelTask(string? taskId)
    {
        if (!string.IsNullOrEmpty(taskId))
        {
            _manager.CancelTask(taskId);
        }
    }
}