using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
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

/// <summary>
/// 单条通知的数据模型。
/// 视觉呈现由 FluentAvalonia InfoBar 提供；进场/退场/滑动只改变动画属性，
/// 补间动画由视图层的 XAML Transitions 驱动（不再使用 Timer 帧动画）。
/// </summary>
public partial class NotificationItemViewModel : ObservableObject
{
    public string Id { get; } = Guid.NewGuid().ToString();

    public string Title { get; set; } = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    public NotificationType Type { get; set; }

    /// <summary>映射到 Fluent InfoBar 的严重级别（InfoBar 自带语义色与图标，随主题变体切换）</summary>
    public InfoBarSeverity Severity => Type switch
    {
        NotificationType.Success => InfoBarSeverity.Success,
        NotificationType.Warning => InfoBarSeverity.Warning,
        NotificationType.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational
    };

    [ObservableProperty]
    private NotificationPosition _position;

    partial void OnPositionChanged(NotificationPosition value)
    {
        OnPropertyChanged(nameof(CardWidth));
    }

    public double CardWidth => Position == NotificationPosition.BottomRight ? 300 : 340;

    public bool IsProgress => Type == NotificationType.Progress;

    public bool IsCountdown => Type == NotificationType.Countdown;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private double _countdownProgress;

    public CancellationTokenSource? Cts { get; set; }

    /// <summary>悬停时置为 true，自动关闭倒计时暂停（由 NotificationService 读取）</summary>
    [ObservableProperty]
    private bool _isPaused;

    // ===== 进场/退场/滑动动画属性（由视图层 XAML Transitions 补间） =====

    [ObservableProperty]
    private double _animationOpacity;

    [ObservableProperty]
    private double _animationScale = 1.0;

    [ObservableProperty]
    private double _animationOffsetY;

    [ObservableProperty]
    private double _animationOffsetX;

    private const double SwipeCloseThreshold = 60;

    public NotificationItemViewModel(NotificationPosition position)
    {
        _position = position;

        // 初始状态即进场起点：右下角从右侧滑入，居中从上方滑入
        if (position == NotificationPosition.BottomRight)
        {
            AnimationOpacity = 0;
            AnimationOffsetX = 60;
        }
        else
        {
            AnimationOpacity = 0;
            AnimationOffsetY = -30;
        }
    }

    /// <summary>设置进场终点，由 XAML 过渡动画平滑补间</summary>
    public void StartEnterAnimation()
    {
        AnimationOpacity = 1;
        AnimationOffsetX = 0;
        AnimationOffsetY = 0;
        AnimationScale = 1.0;
    }

    /// <summary>设置退场终点，由 XAML 过渡动画平滑补间；服务在动画结束后移除条目</summary>
    public void StartExitAnimation()
    {
        if (Position == NotificationPosition.BottomRight)
        {
            AnimationOpacity = 0;
            AnimationOffsetX = 80;
        }
        else
        {
            AnimationOpacity = 0;
            AnimationOffsetY = -30;
        }
    }

    /// <summary>滑动拖拽：直接跟手（视图层在拖拽期间禁用过渡）</summary>
    public void UpdateSwipeDrag(double deltaX)
    {
        AnimationOpacity = 1.0;
        AnimationScale = 1.0;
        AnimationOffsetY = 0;
        AnimationOffsetX = Math.Max(0, deltaX);
    }

    /// <summary>结束拖拽：超过阈值触发关闭，否则由过渡动画回弹归位</summary>
    public bool EndSwipeDrag(double totalDeltaX)
    {
        if (totalDeltaX >= SwipeCloseThreshold)
        {
            CloseRequested?.Invoke(Id);
            return true;
        }

        AnimationOffsetX = 0;
        return false;
    }

    public event Action<string>? CloseRequested;

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(Id);
    }
}
