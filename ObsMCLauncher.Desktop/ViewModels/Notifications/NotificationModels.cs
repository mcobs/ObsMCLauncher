using System;
using System.Threading;
using Avalonia.Media;
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

    public IBrush TypeAccentColor => Type switch
    {
        NotificationType.Error => new SolidColorBrush(Color.Parse("#c58787")),
        NotificationType.Success => new SolidColorBrush(Color.Parse("#87c5a8")),
        NotificationType.Warning => new SolidColorBrush(Color.Parse("#c5a487")),
        NotificationType.Info => new SolidColorBrush(Color.Parse("#87a7c5")),
        NotificationType.Progress => new SolidColorBrush(Color.Parse("#87a7c5")),
        NotificationType.Countdown => new SolidColorBrush(Color.Parse("#87a7c5")),
        _ => new SolidColorBrush(Color.Parse("#87a7c5"))
    };

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

    [ObservableProperty]
    private double _displayProgress;

    [ObservableProperty]
    private double _displayCountdownProgress;

    private double _targetProgress;
    private double _targetCountdownProgress;
    private Timer? _progressAnimTimer;
    private bool _progressAnimRunning;

    partial void OnProgressChanged(double value)
    {
        _targetProgress = value;
        StartProgressAnimation();
    }

    partial void OnCountdownProgressChanged(double value)
    {
        _targetCountdownProgress = value;
        StartProgressAnimation();
    }

    private void StartProgressAnimation()
    {
        if (_progressAnimRunning) return;
        _progressAnimRunning = true;
        _progressAnimTimer?.Dispose();
        _progressAnimTimer = new Timer(LerpProgress, null, 0, 16);
    }

    private void StopProgressAnimation()
    {
        _progressAnimRunning = false;
        _progressAnimTimer?.Dispose();
    }

    private void LerpProgress(object? state)
    {
        var diff = _targetProgress - DisplayProgress;
        var countdownDiff = _targetCountdownProgress - DisplayCountdownProgress;
        var maxDiff = Math.Max(Math.Abs(diff), Math.Abs(countdownDiff));

        if (maxDiff < 0.3)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                DisplayProgress = _targetProgress;
                DisplayCountdownProgress = _targetCountdownProgress;
            });
            StopProgressAnimation();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            DisplayProgress += diff * 0.18;
            DisplayCountdownProgress += countdownDiff * 0.18;
        });
    }

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
    private double _exitStartOffsetX;
    private double _exitStartOpacity;
    private int _exitStep;
    private const int ExitStepCount = 5;
    private const int ExitStepMs = 50;
    private const double SwipeCloseThreshold = 60;

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
        StopProgressAnimation();

        if (Position == NotificationPosition.BottomRight)
        {
            _exitStep = 0;
            _exitStartOffsetX = AnimationOffsetX;
            _exitStartOpacity = AnimationOpacity;
            _animationTimer = new Timer(AnimateExitBottomRight, null, 0, ExitStepMs);
        }
        else
        {
            AnimationOpacity = 0;
            AnimationScale = 0.8;
            AnimationOffsetY = -30;
            AnimationOffsetX = 0;
        }
    }

    private void AnimateExitBottomRight(object? state)
    {
        if (_exitStep >= ExitStepCount)
        {
            AnimationOpacity = 0;
            AnimationOffsetX = 80;
            _animationTimer?.Dispose();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_exitStep < ExitStepCount)
            {
                var t = (double)_exitStep / (ExitStepCount - 1);
                t = 1.0 - Math.Pow(1.0 - t, 2);
                AnimationOpacity = _exitStartOpacity * (1.0 - t);
                AnimationOffsetX = _exitStartOffsetX + (80 - _exitStartOffsetX) * t;
                AnimationScale = 1.0;
                AnimationOffsetY = 0;
                _exitStep++;
            }
        });
    }

    public void UpdateSwipeDrag(double deltaX)
    {
        _animationTimer?.Dispose();
        AnimationOffsetX = Math.Max(0, deltaX);
        AnimationOpacity = 1.0;
        AnimationScale = 1.0;
        AnimationOffsetY = 0;
    }

    public bool EndSwipeDrag(double totalDeltaX)
    {
        if (totalDeltaX >= SwipeCloseThreshold)
        {
            CloseRequested?.Invoke(Id);
            return true;
        }

        _exitStep = 0;
        _exitStartOffsetX = AnimationOffsetX;
        _exitStartOpacity = AnimationOpacity;
        _animationTimer = new Timer(AnimateSwipeBack, null, 0, ExitStepMs);
        return false;
    }

    private void AnimateSwipeBack(object? state)
    {
        if (_exitStep >= ExitStepCount)
        {
            AnimationOffsetX = 0;
            AnimationOpacity = 1;
            _animationTimer?.Dispose();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_exitStep < ExitStepCount)
            {
                var t = (double)_exitStep / (ExitStepCount - 1);
                t = 1.0 - Math.Pow(1.0 - t, 2);
                AnimationOffsetX = _exitStartOffsetX * (1.0 - t);
                AnimationOpacity = 1.0 - 0.3 * t;
                AnimationScale = 1.0;
                AnimationOffsetY = 0;
                _exitStep++;
            }
        });
    }

    public event Action<string>? CloseRequested;

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(Id);
    }
}
