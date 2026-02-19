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

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private double _animationOpacity = 0;

    [ObservableProperty]
    private double _animationScale = 0.3;

    [ObservableProperty]
    private double _animationOffsetY = -50;

    private Timer? _animationTimer;
    private int _animationStep = 0;
    private static readonly double[] ScaleSteps = { 0.3, 1.1, 0.95, 1.02, 1.0 };
    private static readonly double[] OffsetSteps = { -50, 5, -3, 1, 0 };
    private static readonly double[] OpacitySteps = { 0, 1, 1, 1, 1 };

    public void StartEnterAnimation()
    {
        _animationStep = 0;
        _animationTimer?.Dispose();
        _animationTimer = new Timer(AnimateStep, null, 0, 60);
    }

    private void AnimateStep(object? state)
    {
        if (_animationStep >= ScaleSteps.Length)
        {
            _animationTimer?.Dispose();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_animationStep < ScaleSteps.Length)
            {
                AnimationOpacity = OpacitySteps[_animationStep];
                AnimationScale = ScaleSteps[_animationStep];
                AnimationOffsetY = OffsetSteps[_animationStep];
                _animationStep++;
            }
        });
    }

    public void StartExitAnimation()
    {
        _animationTimer?.Dispose();
        AnimationOpacity = 0;
        AnimationScale = 0.8;
        AnimationOffsetY = -30;
    }

    public event Action<string>? CloseRequested;

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(Id);
    }
}
