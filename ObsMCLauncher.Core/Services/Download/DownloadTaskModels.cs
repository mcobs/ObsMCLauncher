using System;
using System.ComponentModel;
using System.Threading;

namespace ObsMCLauncher.Core.Services.Download;

public enum DownloadTaskType
{
    Version,
    Assets,
    Mod,
    Resource
}

public enum DownloadTaskStatus
{
    Downloading,
    Completed,
    Failed,
    Cancelled
}

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

    private double _progress;
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

    private double _downloadSpeed;
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

    public string StatusText => Status switch
    {
        DownloadTaskStatus.Downloading => "下载中",
        DownloadTaskStatus.Completed => "已完成",
        DownloadTaskStatus.Failed => "失败",
        DownloadTaskStatus.Cancelled => "已取消",
        _ => "未知"
    };

    public string ProgressText => $"{Progress:F0}%";

    public string SpeedText
    {
        get
        {
            if (DownloadSpeed == 0) return "";

            if (DownloadSpeed < 1024)
                return $"{DownloadSpeed:F0} B/s";
            if (DownloadSpeed < 1024 * 1024)
                return $"{DownloadSpeed / 1024:F1} KB/s";

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
