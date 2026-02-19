using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.ViewModels.Notifications;

public partial class NotificationService : ObservableObject, IDisposable
{
    private const int MaxNotifications = 3;
    private const int DefaultDurationSeconds = 3;
    private readonly ConcurrentDictionary<string, Timer> _countdownTimers = new();
    private bool _disposed;

    public ObservableCollection<NotificationItemViewModel> Items { get; } = new();

    public string Show(string title, string message, NotificationType type = NotificationType.Info, int? durationSeconds = null, CancellationTokenSource? cts = null)
    {
        if (!durationSeconds.HasValue && type != NotificationType.Progress && type != NotificationType.Countdown)
            durationSeconds = DefaultDurationSeconds;

        var same = Items.Where(x => x.Title == title && x.Message == message && x.Type == type).ToList();
        if (same.Count >= MaxNotifications)
        {
            Items.Remove(same.Last());
        }

        var item = new NotificationItemViewModel
        {
            Title = title,
            Message = message,
            Type = type,
            CanClose = true,
            Cts = cts
        };

        // 设置关闭事件处理器
        item.CloseRequested += (id) => Remove(id);

        Items.Insert(0, item);

        while (Items.Count > MaxNotifications)
            Items.RemoveAt(Items.Count - 1);

        if (durationSeconds.HasValue && type != NotificationType.Progress && type != NotificationType.Countdown)
        {
            var capturedId = item.Id;
            var timer = new Timer(_ =>
            {
                Dispatcher.UIThread.Post(() => Remove(capturedId));
            }, null, TimeSpan.FromSeconds(durationSeconds.Value), Timeout.InfiniteTimeSpan);

            _ = timer;
        }

        return item.Id;
    }

    public string ShowCountdown(string title, string message, int countdownSeconds = 3, Action? onComplete = null)
    {
        var item = new NotificationItemViewModel
        {
            Title = title,
            Message = message,
            Type = NotificationType.Countdown,
            CanClose = true,
            CountdownSeconds = countdownSeconds,
            CountdownRemaining = countdownSeconds,
            CountdownProgress = 100.0,
            OnCountdownComplete = onComplete
        };

        // 设置关闭事件处理器
        item.CloseRequested += (id) => Remove(id);

        Items.Insert(0, item);

        while (Items.Count > MaxNotifications)
            Items.RemoveAt(Items.Count - 1);

        var capturedId = item.Id;
        var interval = TimeSpan.FromMilliseconds(100);
        var totalMs = countdownSeconds * 1000;
        var elapsedMs = 0;

        // 创建并保存计时器
        var timer = new Timer(state =>
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var target = Items.FirstOrDefault(x => x.Id == capturedId);
                        if (target != null)
                        {
                            elapsedMs += 100;
                            target.CountdownProgress = Math.Max(0, 100.0 * (1.0 - (double)elapsedMs / totalMs));
                            target.CountdownRemaining = Math.Max(0, (int)Math.Ceiling((totalMs - elapsedMs) / 1000.0));

                            if (elapsedMs >= totalMs)
                            {
                                // 计时器完成，移除并清理
                                if (_countdownTimers.TryRemove(capturedId, out var completedTimer))
                                {
                                    completedTimer?.Dispose();
                                }
                                Remove(capturedId);
                                target.OnCountdownComplete?.Invoke();
                            }
                        }
                        else
                        {
                            // 通知项已被移除，清理计时器
                            if (_countdownTimers.TryRemove(capturedId, out var orphanedTimer))
                            {
                                orphanedTimer?.Dispose();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Error("Notification", $"UI线程更新倒计时失败: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Notification", $"倒计时计时器回调失败: {ex.Message}");
            }
        }, null, interval, interval);

        // 保存计时器引用
        _countdownTimers[capturedId] = timer;

        return item.Id;
    }

    public void Update(string id, string message, double? progress = null)
    {
        var item = Items.FirstOrDefault(x => x.Id == id);
        if (item != null)
        {
            item.Message = message;
            if (progress.HasValue)
            {
                item.Progress = progress.Value;
            }
        }
    }

    public void Remove(string id)
    {
        var item = Items.FirstOrDefault(x => x.Id == id);
        if (item != null)
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

            // 清理倒计时计时器
            if (_countdownTimers.TryRemove(id, out var timer))
            {
                timer?.Dispose();
            }

            Items.Remove(item);
        }
    }

    [RelayCommand]
    private void Close(string? id)
    {
        if (!string.IsNullOrEmpty(id))
            Remove(id);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 清理所有计时器
                foreach (var timer in _countdownTimers.Values)
                {
                    timer?.Dispose();
                }
                _countdownTimers.Clear();
            }
            _disposed = true;
        }
    }

    ~NotificationService()
    {
        Dispose(false);
    }
}
