using System.Collections.Generic;
using System.Linq;
using ObsMCLauncher.Core.Media;
using ObsMCLauncher.Core.Models;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 轮播纯逻辑测试（设计文档阶段 3）。
/// <para>
/// <see cref="WallpaperRotationPlan"/> 刻意做成无副作用的纯函数，就是为了让轮播里最容易出错的
/// 四类边界能在这里测掉（不需要 UI、也不需要 SkiaSharp 原生库）：
/// ① 起点选择（关闭 / 顺序 / 随机 / 每次启动）；
/// ② 下一项推进（顺序环绕、随机不连出同一张）；
/// ③ 单张与空列表不能把调用方带进"无限空转"；
/// ④ "动图播完再切"的驻留时长口径。
/// </para>
/// </summary>
public class WallpaperRotationTests
{
    // ────────────────────────────────────────────────────────────────
    // 构造辅助
    // ────────────────────────────────────────────────────────────────

    /// <summary>构造一个可播放的动图探测结果：<paramref name="frameCount"/> 帧，每帧 <paramref name="frameMs"/> 毫秒</summary>
    private static AnimatedImageInfo Animated(int frameCount, int frameMs, int loopCount) => new()
    {
        Kind = WallpaperKind.Gif,
        Width = 64,
        Height = 64,
        FrameCount = frameCount,
        LoopCount = loopCount,
        FrameDurationsMs = Enumerable.Repeat(frameMs, frameCount).ToList(),
        FileSize = 4096
    };

    /// <summary>构造一个静态图探测结果</summary>
    private static AnimatedImageInfo Static() => new()
    {
        Kind = WallpaperKind.Static,
        Width = 64,
        Height = 64,
        FrameCount = 1,
        FrameDurationsMs = [],
        FileSize = 1024
    };

    /// <summary>跑一批固定种子，返回每次 <see cref="WallpaperRotationPlan.InitialIndex"/> 的结果</summary>
    private static List<int> InitialIndicesOverSeeds(int itemCount, int intervalSeconds, int slideMode, int seeds = 128)
    {
        var result = new List<int>(seeds);
        for (var seed = 0; seed < seeds; seed++)
            result.Add(new WallpaperRotationPlan(itemCount, intervalSeconds, slideMode, new Random(seed)).InitialIndex());
        return result;
    }

    // ────────────────────────────────────────────────────────────────
    // SlideMode → 顺序模式：越界必须兜底，不能让配置值把轮播卡死
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, WallpaperSlideOrder.Sequential)]
    [InlineData(1, WallpaperSlideOrder.Random)]
    [InlineData(2, WallpaperSlideOrder.RandomStart)]
    [InlineData(3, WallpaperSlideOrder.Sequential)]
    [InlineData(-1, WallpaperSlideOrder.Sequential)]
    [InlineData(int.MaxValue, WallpaperSlideOrder.Sequential)]
    [InlineData(int.MinValue, WallpaperSlideOrder.Sequential)]
    public void ParseOrder_OutOfRange_FallsBackToSequential(int slideMode, WallpaperSlideOrder expected)
        => Assert.Equal(expected, WallpaperRotationPlan.ParseOrder(slideMode));

    // ────────────────────────────────────────────────────────────────
    // IsRotating：只有"≥ 2 项且间隔为正数"才启用定时轮播
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 30, false)]    // 没有候选
    [InlineData(1, 30, false)]    // 只有一张，轮播无意义
    [InlineData(2, 30, true)]
    [InlineData(5, 30, true)]
    [InlineData(3, 1, true)]
    [InlineData(3, 86400, true)]
    [InlineData(3, 0, false)]     // 关闭
    [InlineData(3, -1, false)]    // 每次启动：只影响起点，不启用定时器
    [InlineData(3, -99, false)]   // 非法负数一律按"不轮播"处理
    public void IsRotating_OnlyWhenMultipleItemsAndPositiveInterval(int itemCount, int intervalSeconds, bool expected)
    {
        var plan = new WallpaperRotationPlan(itemCount, intervalSeconds, slideMode: 0);
        Assert.Equal(expected, plan.IsRotating);
    }

    [Fact]
    public void IntervalMs_IsZeroWhenNotRotating()
    {
        Assert.Equal(0, new WallpaperRotationPlan(5, 0, 0).IntervalMs);
        Assert.Equal(0, new WallpaperRotationPlan(5, -1, 0).IntervalMs);
        Assert.Equal(90_000, new WallpaperRotationPlan(5, 90, 0).IntervalMs);
    }

    [Fact]
    public void NegativeItemCount_IsClampedToZero()
    {
        var plan = new WallpaperRotationPlan(-3, 30, 0);
        Assert.Equal(0, plan.Count);
        Assert.False(plan.IsRotating);
        Assert.Equal(-1, plan.InitialIndex());
    }

    // ────────────────────────────────────────────────────────────────
    // InitialIndex：起点选择
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void InitialIndex_EmptyList_ReturnsMinusOne()
        => Assert.Equal(-1, new WallpaperRotationPlan(0, 30, 0).InitialIndex());

    [Fact]
    public void InitialIndex_SingleItem_ReturnsZero()
        => Assert.Equal(0, new WallpaperRotationPlan(1, 30, 1).InitialIndex());

    [Fact]
    public void InitialIndex_Sequential_StartsAtFirstItem()
        => Assert.Equal(0, new WallpaperRotationPlan(8, 30, slideMode: 0).InitialIndex());

    [Fact]
    public void InitialIndex_RotationDisabled_PinsFirstItemEvenIfOrderIsRandom()
    {
        // 间隔为 0 就是"关闭轮播"。此时即便 slideMode 留着随机，也必须固定首项，
        // 否则用户会看到"没开轮播但每次启动换了一张"，误以为设置没生效。
        Assert.Equal(0, new WallpaperRotationPlan(8, WallpaperRotationPlan.IntervalDisabled, slideMode: 1).InitialIndex());
        Assert.Equal(0, new WallpaperRotationPlan(8, WallpaperRotationPlan.IntervalDisabled, slideMode: 2).InitialIndex());
    }

    [Theory]
    [InlineData(1)]   // 随机
    [InlineData(2)]   // 随机起点
    public void InitialIndex_RandomModes_AlwaysStayInRange(int slideMode)
    {
        foreach (var index in InitialIndicesOverSeeds(itemCount: 7, intervalSeconds: 30, slideMode: slideMode))
            Assert.InRange(index, 0, 6);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void InitialIndex_RandomModes_ActuallyVary(int slideMode)
    {
        var distinct = InitialIndicesOverSeeds(itemCount: 7, intervalSeconds: 30, slideMode: slideMode).Distinct().Count();
        Assert.True(distinct > 1, $"随机起点应当能取到不同项，实际只取到 {distinct} 种");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void InitialIndex_RandomModes_WorkWithEachStartupInterval(int slideMode)
    {
        // 间隔 = -1（每次启动）：不轮播，但起点仍按随机模式决定
        foreach (var index in InitialIndicesOverSeeds(7, WallpaperRotationPlan.IntervalEachStartup, slideMode))
            Assert.InRange(index, 0, 6);
    }

    // ────────────────────────────────────────────────────────────────
    // NextIndex：推进与环绕
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void NextIndex_Sequential_WalksForwardAndWrapsAround()
    {
        var plan = new WallpaperRotationPlan(4, 30, slideMode: 0);
        var current = plan.InitialIndex();

        var walked = new List<int>();
        for (var step = 0; step < 4; step++)
        {
            current = plan.NextIndex(current);
            walked.Add(current);
        }

        Assert.Equal([1, 2, 3, 0], walked);
    }

    [Fact]
    public void NextIndex_Sequential_OneFullCycleVisitsEveryItemExactlyOnce()
    {
        const int count = 6;
        var plan = new WallpaperRotationPlan(count, 30, slideMode: 0);

        var visited = new List<int>();
        var current = plan.InitialIndex();
        visited.Add(current);
        for (var step = 1; step < count; step++)
        {
            current = plan.NextIndex(current);
            visited.Add(current);
        }

        Assert.Equal(count, visited.Distinct().Count());
        Assert.Equal(0, plan.NextIndex(current));   // 第 count 次推进回到起点
    }

    [Fact]
    public void NextIndex_RandomStart_KeepsWalkingSequentiallyAfterTheRandomStart()
    {
        var plan = new WallpaperRotationPlan(4, 30, slideMode: 2, new Random(2026));
        var start = plan.InitialIndex();

        Assert.Equal((start + 1) % 4, plan.NextIndex(start));
        Assert.Equal((start + 2) % 4, plan.NextIndex((start + 1) % 4));
    }

    [Fact]
    public void NextIndex_Random_NeverRepeatsCurrentItem()
    {
        for (var seed = 0; seed < 128; seed++)
        {
            var plan = new WallpaperRotationPlan(6, 30, slideMode: 1, new Random(seed));
            for (var current = 0; current < 6; current++)
            {
                var next = plan.NextIndex(current);
                Assert.InRange(next, 0, 5);
                Assert.NotEqual(current, next);
            }
        }
    }

    [Fact]
    public void NextIndex_Random_TwoItemsAlwaysSwitchesToTheOther()
    {
        for (var seed = 0; seed < 64; seed++)
        {
            var plan = new WallpaperRotationPlan(2, 30, slideMode: 1, new Random(seed));
            Assert.Equal(1, plan.NextIndex(0));
            Assert.Equal(0, plan.NextIndex(1));
        }
    }

    [Fact]
    public void NextIndex_SingleItem_StaysAtZero()
    {
        var plan = new WallpaperRotationPlan(1, 30, slideMode: 1);
        Assert.Equal(0, plan.NextIndex(0));
    }

    [Fact]
    public void NextIndex_EmptyList_ReturnsMinusOne()
        => Assert.Equal(-1, new WallpaperRotationPlan(0, 30, 1).NextIndex(0));

    [Fact]
    public void NextIndex_OutOfRangeCurrent_IsNormalizedBeforeAdvancing()
    {
        var plan = new WallpaperRotationPlan(4, 30, slideMode: 0);

        Assert.Equal(0, plan.NextIndex(-1));   // 未显示任何项（-1 → 归一为 -1）→ 从首项开始
        Assert.Equal(2, plan.NextIndex(5));    // 5 % 4 == 1 → 下一项 2
    }

    // ────────────────────────────────────────────────────────────────
    // ResolveDwellMs：驻留时长（"动图播完再切"）
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-120)]
    public void ResolveDwellMs_NonPositiveInterval_MeansNoRotation(int intervalSeconds)
        => Assert.Equal(0, WallpaperRotationPlan.ResolveDwellMs(intervalSeconds, Animated(20, 100, -1)));

    [Fact]
    public void ResolveDwellMs_StaticImage_UsesInterval()
    {
        Assert.Equal(30_000, WallpaperRotationPlan.ResolveDwellMs(30, null));
        Assert.Equal(30_000, WallpaperRotationPlan.ResolveDwellMs(30, Static()));
    }

    [Fact]
    public void ResolveDwellMs_FiniteLoop_NetscapeSemantics_PlaysLoopCountPlusOneCycles()
    {
        // 20 帧 × 100ms = 2000ms 一轮；LoopCount = 2 表示"额外重复 2 次"，共播 3 轮 = 6000ms
        var info = Animated(frameCount: 20, frameMs: 100, loopCount: 2);

        Assert.Equal(30_000, WallpaperRotationPlan.ResolveDwellMs(30, info)); // 间隔 30s > 6s → 按间隔
        Assert.Equal(6000, WallpaperRotationPlan.ResolveDwellMs(6, info));    // 相等 → 取 6s（至少播完）
        Assert.Equal(6000, WallpaperRotationPlan.ResolveDwellMs(3, info));    // 间隔太短 → 抬到播完时长
        Assert.Equal(10_000, WallpaperRotationPlan.ResolveDwellMs(10, info)); // 间隔更长 → 按间隔
    }

    [Fact]
    public void ResolveDwellMs_InfiniteLoop_GuaranteesAtLeastOneFullCycle()
    {
        // 无限循环（-1）："播完"不存在，退一步保证至少完整播一轮
        var info = Animated(frameCount: 30, frameMs: 100, loopCount: -1);

        Assert.Equal(3000, WallpaperRotationPlan.ResolveDwellMs(1, info));
        Assert.Equal(3000, WallpaperRotationPlan.ResolveDwellMs(3, info));
        Assert.Equal(5000, WallpaperRotationPlan.ResolveDwellMs(5, info));
    }

    [Fact]
    public void ResolveDwellMs_SingleFrameAnimationLikeInfo_FallsBackToInterval()
    {
        // FrameCount == 1 时 TotalDurationMs 为 0（没有"一轮"可言），必须退回按间隔驻留
        var notPlayable = new AnimatedImageInfo
        {
            Kind = WallpaperKind.Static,
            FrameCount = 1,
            FrameDurationsMs = [],
        };

        Assert.Equal(30_000, WallpaperRotationPlan.ResolveDwellMs(30, notPlayable));
    }

    [Fact]
    public void ResolveDwellMs_MissingFrameDurations_UsesPerFrameFallback()
    {
        // 时长字段缺失时 AnimatedImageInfo 按 100ms/帧兜底；这里确认该兜底会传导到驻留时长
        var info = new AnimatedImageInfo
        {
            Kind = WallpaperKind.Gif,
            Width = 64,
            Height = 64,
            FrameCount = 10,
            LoopCount = 0,           // 播 1 轮
            FrameDurationsMs = [],   // 无时长信息 → 每帧 100ms → 一轮 1000ms
        };

        Assert.Equal(1000, WallpaperRotationPlan.ResolveDwellMs(1, info));
    }
}
