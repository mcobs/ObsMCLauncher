using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;

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

    private NotificationPosition _position = NotificationPosition.Center;
    public NotificationPosition Position
    {
        get => _position;
        set
        {
            _position = value;
            CanClose = value != NotificationPosition.BottomRight;
        }
    }

    public double CardWidth => Position == NotificationPosition.BottomRight ? 300 : 340;

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

    private bool _canClose = true;
    public bool CanClose
    {
        get => _canClose;
        set
        {
            _canClose = value;
            OnPropertyChanged();
        }
    }

    public CancellationTokenSource? Cts { get; set; }

    public Action? OnCountdownComplete { get; set; }

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private double _animationOpacity = 0;

    [ObservableProperty]
    private double _animationScale = 1.0;

    [ObservableProperty]
    private double _animationOffsetY = 0;

    [ObservableProperty]
    private double _animationOffsetX = 0;

    private Timer? _animationTimer;
    private int _animationStep = 0;

    // Center mode: bounce-in from top
    private static readonly double[] CenterScaleSteps = { 0.3, 1.12, 0.96, 1.03, 1.0 };
    private static readonly double[] CenterOffsetYSteps = { -50, 6, -4, 2, 0 };
    private static readonly double[] CenterOpacitySteps = { 0, 1, 1, 1, 1 };
    private static readonly int CenterStepCount = 5;
    private static readonly int CenterStepMs = 60;

    // BottomRight mode: smooth slide-in from right
    private static readonly int BottomRightStepCount = 8;
    private static readonly int BottomRightStepMs = 50;

    public void StartEnterAnimation()
    {
        _animationStep = 0;
        _animationTimer?.Dispose();

        if (Position == NotificationPosition.BottomRight)
        {
            AnimationOpacity = 0;
            AnimationScale = 1.0;
            AnimationOffsetY = 0;
            AnimationOffsetX = 60;
            _animationTimer = new Timer(AnimateStepBottomRight, null, 0, BottomRightStepMs);
        }
        else
        {
            AnimationOpacity = 0;
            AnimationScale = CenterScaleSteps[0];
            AnimationOffsetY = CenterOffsetYSteps[0];
            AnimationOffsetX = 0;
            _animationTimer = new Timer(AnimateStepCenter, null, 0, CenterStepMs);
        }
    }

    private void AnimateStepCenter(object? state)
    {
        if (_animationStep >= CenterStepCount)
        {
            _animationTimer?.Dispose();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_animationStep < CenterStepCount)
            {
                AnimationOpacity = CenterOpacitySteps[_animationStep];
                AnimationScale = CenterScaleSteps[_animationStep];
                AnimationOffsetY = CenterOffsetYSteps[_animationStep];
                AnimationOffsetX = 0;
                _animationStep++;
            }
        });
    }

    private void AnimateStepBottomRight(object? state)
    {
        if (_animationStep >= BottomRightStepCount)
        {
            AnimationOffsetX = 0;
            AnimationOpacity = 1;
            _animationTimer?.Dispose();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_animationStep < BottomRightStepCount)
            {
                var t = (double)_animationStep / (BottomRightStepCount - 1);
                t = 1.0 - Math.Pow(1.0 - t, 3);
                AnimationOpacity = t;
                AnimationOffsetX = 60 * (1.0 - t);
                AnimationScale = 1.0;
                AnimationOffsetY = 0;
                _animationStep++;
            }
        });
    }

    public void StartExitAnimation()
    {
        _animationTimer?.Dispose();

        if (Position == NotificationPosition.BottomRight)
        {
            AnimationOpacity = 0;
            AnimationScale = 1.0;
            AnimationOffsetY = 0;
            AnimationOffsetX = 50;
        }
        else
        {
            AnimationOpacity = 0;
            AnimationScale = 0.8;
            AnimationOffsetY = -30;
            AnimationOffsetX = 0;
        }
    }

    public event Action<string>? CloseRequested;

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(Id);
    }
}
