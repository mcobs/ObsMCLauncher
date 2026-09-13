using System;
using System.Collections.Generic;

namespace ObsMCLauncher.Core.Media;

/// <summary>播放时间轴上的一个位置</summary>
/// <param name="Index">当前应展示 / 已解码的帧序号</param>
/// <param name="DueMs">该帧**应当开始展示**的时刻（相对本次播放起点，毫秒）</param>
/// <param name="CompletedLoops">已播完的整轮数（无限循环素材用不到）</param>
/// <param name="SkippedFrames">因解码跟不上时间轴而跳过的帧数（背压计数，用于日志与验收观测）</param>
public readonly record struct FrameCursor(int Index, double DueMs, int CompletedLoops, int SkippedFrames)
{
    /// <summary>播放起点：第 0 帧，t = 0</summary>
    public static FrameCursor Start => new(0, 0, 0, 0);
}

/// <summary>
/// 把"逐帧时长 + 帧率上限"编译成一条可对齐的时间轴。
/// </summary>
/// <remarks>
/// <para>
/// 纯函数、无状态——时间轴本身的推进由调用方持有 <see cref="FrameCursor"/>，
/// 这样跳帧、循环、背压这几条最容易出错的规则可以脱离渲染栈单独测。
/// </para>
/// <para>
/// 帧率上限的落地方式是**给每帧时长设下限**（<c>1000 / maxFps</c> 毫秒），而不是丢帧：
/// 大多数动图本来就是 12–25 fps，上限设 24 不损失观感，却挡住了 60fps 的异常素材白烧 CPU。
/// </para>
/// <para>
/// 跳帧（背压）语义：解码耗时超过帧间隔时，游标按时间轴前进到"此刻本应显示的那一帧"，
/// 期间错过的帧不再补播，从而保证**动画总时长不失真**。由于 <c>PriorFrame</c> 是原地增量，
/// 跳过的帧无法事后补算，所以解码时传入的 <c>priorFrame</c> 必须是"累积缓冲里实际装着的那一帧"
/// （由调用方用上次成功解码的序号传入），而不是 <c>index - 1</c>。
/// </para>
/// </remarks>
public sealed class FramePacingPlan
{
    private readonly double[] _durations;

    /// <param name="frameDurationsMs">逐帧时长（毫秒），顺序与帧序号一致；空或全 0 时按帧率下限兜底</param>
    /// <param name="maxFps">帧率上限（1–60）。≤ 0 表示不限制</param>
    /// <param name="loopCount">循环次数；<c>-1</c> 表示无限。GIF 的 Netscape 语义：额外重复次数</param>
    public FramePacingPlan(IReadOnlyList<int>? frameDurationsMs, int maxFps, int loopCount = -1)
    {
        MinFrameDurationMs = maxFps > 0 ? 1000.0 / Math.Clamp(maxFps, 1, 60) : 0;

        var count = frameDurationsMs?.Count ?? 0;
        if (count <= 0)
        {
            _durations = [Math.Max(MinFrameDurationMs, 100)];
        }
        else
        {
            _durations = new double[count];
            for (var i = 0; i < count; i++)
            {
                var d = frameDurationsMs![i];
                _durations[i] = Math.Max(MinFrameDurationMs, d > 0 ? d : 100);
            }
        }

        LoopCount = loopCount;
    }

    /// <summary>帧数（至少 1）</summary>
    public int FrameCount => _durations.Length;

    /// <summary>循环次数；<c>-1</c> 表示无限循环</summary>
    public int LoopCount { get; }

    /// <summary>由帧率上限推出的单帧时长下限（毫秒）</summary>
    public double MinFrameDurationMs { get; }

    /// <summary>一轮播放的总时长（毫秒）</summary>
    public double TotalDurationMs
    {
        get
        {
            double sum = 0;
            foreach (var d in _durations) sum += d;
            return sum;
        }
    }

    /// <summary>取第 <paramref name="index"/> 帧（已按帧率上限抬升下限）的展示时长</summary>
    public double GetFrameDurationMs(int index)
    {
        if (_durations.Length == 0) return 100;
        if (index < 0) return _durations[0];
        return index >= _durations.Length ? _durations[^1] : _durations[index];
    }

    /// <summary>推进到下一帧（含循环回绕）</summary>
    public FrameCursor Next(FrameCursor cursor)
    {
        var nextIndex = cursor.Index + 1;
        var loops = cursor.CompletedLoops;
        if (nextIndex >= FrameCount)
        {
            nextIndex = 0;
            loops++;
        }

        return new FrameCursor(nextIndex, cursor.DueMs + GetFrameDurationMs(cursor.Index), loops, cursor.SkippedFrames);
    }

    /// <summary>
    /// 按时间轴对齐：把游标推进到"不早于 <paramref name="nowMs"/> 的下一帧"，
    /// 跳过已经错过展示时刻的帧（背压）。单帧素材直接原样返回。
    /// </summary>
    /// <remarks>
    /// 只在**严格晚于**该帧应展示的时刻时才跳（<c>DueMs &lt; nowMs</c>）：
    /// 恰好卡在边界上时保留该帧，避免正常节奏下被误跳。
    /// </remarks>
    public FrameCursor CatchUp(FrameCursor cursor, double nowMs)
    {
        if (FrameCount <= 1) return cursor;

        var result = cursor;
        var skipped = 0;
        // 上限取一轮帧数：跳满一轮后仍落后时，由下一次迭代继续追，避免这里空转
        for (var guard = 0; guard < FrameCount && result.DueMs < nowMs; guard++)
        {
            result = Next(result);
            skipped++;
        }

        return skipped == 0 ? result : result with { SkippedFrames = cursor.SkippedFrames + skipped };
    }

    /// <summary>
    /// 有限循环素材是否已播完（此时应停在最后一帧，不再请求下一帧）。
    /// 无限循环（<see cref="LoopCount"/> = -1）永远返回 <c>false</c>。
    /// </summary>
    public bool IsFinished(FrameCursor cursor)
        => LoopCount >= 0 && cursor.CompletedLoops > LoopCount;

    /// <summary>完全静止的单帧计划（动图被配置为"只显示首帧"时使用）</summary>
    public static FramePacingPlan SingleFrame(int maxFps = 24) => new(null, maxFps, loopCount: 0);
}
