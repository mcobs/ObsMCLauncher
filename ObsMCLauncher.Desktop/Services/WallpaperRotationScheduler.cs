using System;
using Avalonia.Threading;

namespace ObsMCLauncher.Desktop.Services;

/// <summary>
/// 轮播定时器：只负责"到点了叫我一声"，不做任何选择或绘制。
/// </summary>
/// <remarks>
/// <para>
/// 刻意用**一次性**计时（触发后自行停下，由调用方决定下次何时再排），而不是固定周期的循环定时器：
/// </para>
/// <list type="bullet">
/// <item>每次驻留时长都可能不同（动图播完再切），固定周期根本表达不了；</item>
/// <item>不会在上一张还没换完时就堆积下一次触发（切换要探测 + 解码，可能慢于间隔）；</item>
/// <item>系统休眠 / 挂起后不会补触发一串——一次性定时器醒来最多算"迟到了"，只触发一次。</item>
/// </list>
/// <para>
/// 与 <c>WallpaperRotationPlan</c> 的分工：那里是纯逻辑（换哪张、待多久），
/// 这里只有"等多久后回调"这一件事。
/// </para>
/// </remarks>
internal sealed class WallpaperRotationScheduler : IDisposable
{
    private DispatcherTimer? _timer;
    private Action? _onElapsed;
    private bool _disposed;

    /// <summary>是否已排定下一次切换</summary>
    public bool IsScheduled => _timer is not null;

    /// <summary>
    /// 排定一次延时回调（会先取消上一次未触发的排定）。
    /// </summary>
    /// <param name="delayMs">延时毫秒；正数才会真正排定</param>
    /// <param name="onElapsed">到点后在 UI 线程回调</param>
    public void Schedule(double delayMs, Action onElapsed)
    {
        if (_disposed) return;

        Stop();
        if (delayMs <= 0) return;

        // 上限兜一下：极端配置（如 86400 秒）不需要精确到毫秒，也避免 TimeSpan 溢出
        var interval = TimeSpan.FromMilliseconds(Math.Min(delayMs, int.MaxValue));

        var timer = new DispatcherTimer { Interval = interval };
        _onElapsed = onElapsed;
        timer.Tick += OnTick;

        _timer = timer;
        timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // 一次性：先停再回调，回调里可以安全地重新 Schedule
        var callback = _onElapsed;
        Stop();
        callback?.Invoke();
    }

    /// <summary>取消未触发的排定</summary>
    public void Stop()
    {
        var timer = _timer;
        _timer = null;
        _onElapsed = null;
        if (timer is null) return;

        timer.Tick -= OnTick;
        timer.Stop();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
