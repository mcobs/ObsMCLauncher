using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ObsMCLauncher.Desktop.ViewModels.Notifications;

public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error,
    Progress,
    Countdown
}

public partial class NotificationItemViewModel : ObservableObject
{
    public string Id { get; } = Guid.NewGuid().ToString();

    public string Title { get; set; } = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    public NotificationType Type { get; set; }

    public bool IsProgress => Type == NotificationType.Progress;

    public bool IsCountdown => Type == NotificationType.Countdown;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private int _countdownSeconds;

    [ObservableProperty]
    private int _countdownRemaining;

    [ObservableProperty]
    private double _countdownProgress;

    public bool CanClose { get; set; } = true;

    public CancellationTokenSource? Cts { get; set; }

    public Action? OnCountdownComplete { get; set; }

    // 关闭事件
    public event Action<string>? CloseRequested;

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(Id);
    }
}
