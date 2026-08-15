using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.ViewModels.Notifications;

/// <summary>
/// 全局通知服务：维护通知条目集合与自动关闭/倒计时计时器。
/// 视觉与补间动画由视图层（NotificationCardView 的 XAML Transitions）负责。
/// </summary>
public partial class NotificationService : ObservableObject, IDisposable
{
    private const int MaxNotifications = 3;
    private const int ExitAnimationDelayMs = 300;
    private const int MinAutoCloseSeconds = 3;
    private const int MaxAutoCloseSeconds = 30;
    private readonly ConcurrentDictionary<string, DispatcherTimer> _autoCloseTimers = new();
    private readonly ConcurrentDictionary<string, DispatcherTimer> _countdownTimers = new();
    private bool _disposed;

    private NotificationPosition _notificationPosition = NotificationPosition.Center;
    public NotificationPosition NotificationPosition
    {
        get => _notificationPosition;
        set
        {
            if (SetProperty(ref _notificationPosition, value))
            {
                // 位置切换时同步已显示的通知，保证交互逻辑与显示位置一致
                foreach (var item in Items)
                {
                    item.Position = value;
                }
            }
        }
    }

    private int _autoCloseSeconds = 5;
    public int AutoCloseSeconds
    {
        get => _autoCloseSeconds;
        set => SetProperty(ref _autoCloseSeconds, Math.Clamp(value, MinAutoCloseSeconds, MaxAutoCloseSeconds));
    }

    public ObservableCollection<NotificationItemViewModel> Items { get; } = new();

    private int GetDefaultDuration(NotificationType type)
    {
        if (type is NotificationType.Progress or NotificationType.Countdown)
            return 0;

        // 右下角：尊重用户配置；居中横幅：按严重级别给足阅读时间
        return NotificationPosition == NotificationPosition.BottomRight
            ? AutoCloseSeconds
            : type switch
            {
                NotificationType.Error => 5,
                NotificationType.Warning => 4,
                _ => 3
            };
    }

    public string Show(string title, string message, NotificationType type = NotificationType.Info, int? durationSeconds = null, CancellationTokenSource? cts = null)
    {
        var duration = durationSeconds ?? GetDefaultDuration(type);

        var item = new NotificationItemViewModel(NotificationPosition)
        {
            Title = title,
            Message = message,
            Type = type,
            Cts = cts
        };
        item.CloseRequested += OnItemCloseRequested;

        // 堆叠行为：新通知置顶，超出上限时移除最旧一条
        Items.Insert(0, item);
        TrimToLimit();

        // 等待布局完成后再播放进场动画，保证过渡生效
        DispatcherTimer.RunOnce(() => item.StartEnterAnimation(), TimeSpan.FromMilliseconds(50));

        if (duration > 0)
            StartAutoCloseTimer(item, duration);

        return item.Id;
    }

    public string ShowCountdown(string title, string message, int countdownSeconds = 3, Action? onComplete = null)
    {
        var item = new NotificationItemViewModel(NotificationPosition)
        {
            Title = title,
            Message = message,
            Type = NotificationType.Countdown,
            CountdownProgress = 100.0
        };
        item.CloseRequested += OnItemCloseRequested;

        Items.Insert(0, item);
        TrimToLimit();

        DispatcherTimer.RunOnce(() => item.StartEnterAnimation(), TimeSpan.FromMilliseconds(50));

        StartCountdownTimer(item, countdownSeconds, onComplete);
        return item.Id;
    }

    public void Update(string id, string message, double? progress = null)
    {
        var item = Items.FirstOrDefault(x => x.Id == id);
        if (item == null) return;

        item.Message = message;
        if (progress.HasValue)
        {
            item.Progress = progress.Value;
        }
    }

    public void Remove(string id)
    {
        var item = Items.FirstOrDefault(x => x.Id == id);
        if (item == null) return;

        CancelAssociatedTask(item);
        Detach(item);

        item.StartExitAnimation();
        _ = RemoveAfterExitAsync(item);
    }

    private void OnItemCloseRequested(string id) => Remove(id);

    private void StartAutoCloseTimer(NotificationItemViewModel item, int durationSeconds)
    {
        var remaining = durationSeconds;
        var id = item.Id;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        timer.Tick += (_, _) =>
        {
            if (_disposed || !Items.Any(x => x.Id == id))
            {
                StopTimer(_autoCloseTimers, id);
                return;
            }

            if (item.IsPaused) return; // 悬停时暂停倒计时

            if (--remaining <= 0)
            {
                StopTimer(_autoCloseTimers, id);
                Remove(id);
            }
        };

        _autoCloseTimers[id] = timer;
        timer.Start();
    }

    private void StartCountdownTimer(NotificationItemViewModel item, int countdownSeconds, Action? onComplete)
    {
        // 用 Stopwatch 计算剩余时间，避免累加式计时产生漂移
        var stopwatch = Stopwatch.StartNew();
        var totalMs = countdownSeconds * 1000.0;
        var id = item.Id;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };

        timer.Tick += (_, _) =>
        {
            if (_disposed || !Items.Any(x => x.Id == id))
            {
                StopTimer(_countdownTimers, id);
                return;
            }

            var remainingMs = Math.Max(0, totalMs - stopwatch.Elapsed.TotalMilliseconds);
            item.CountdownProgress = remainingMs / totalMs * 100.0;

            if (remainingMs <= 0)
            {
                StopTimer(_countdownTimers, id);
                Remove(id);
                onComplete?.Invoke();
            }
        };

        _countdownTimers[id] = timer;
        timer.Start();
    }

    private void CancelAssociatedTask(NotificationItemViewModel item)
    {
        try
        {
            if (item.Cts != null && !item.Cts.IsCancellationRequested)
            {
                item.Cts.Cancel();
                DebugLogger.Info("Notification", $"已通过关闭通知终止任务: {item.Title}");
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("Notification", $"终止关联任务失败: {ex.Message}");
        }
    }

    /// <summary>解除条目关联的计时器（不播放动画、不取消任务）</summary>
    private void Detach(NotificationItemViewModel item)
    {
        StopTimer(_autoCloseTimers, item.Id);
        StopTimer(_countdownTimers, item.Id);
    }

    private static void StopTimer(ConcurrentDictionary<string, DispatcherTimer> timers, string id)
    {
        if (timers.TryRemove(id, out var timer))
        {
            timer.Stop();
        }
    }

    private void TrimToLimit()
    {
        while (Items.Count > MaxNotifications)
        {
            var last = Items[^1];
            Detach(last);
            Items.Remove(last);
        }
    }

    private async Task RemoveAfterExitAsync(NotificationItemViewModel item)
    {
        await Task.Delay(ExitAnimationDelayMs);
        if (_disposed) return;
        Items.Remove(item);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var timer in _autoCloseTimers.Values) timer.Stop();
        foreach (var timer in _countdownTimers.Values) timer.Stop();
        _autoCloseTimers.Clear();
        _countdownTimers.Clear();
    }
}
