using System.Collections.ObjectModel;
using System.ComponentModel;
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

    public DownloadManagerViewModel(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _manager = DownloadTaskManager.Instance;

        _manager.PropertyChanged += OnManagerPropertyChanged;
    }

    private void OnManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Ensure property changes are raised on the UI thread
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
        });
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
