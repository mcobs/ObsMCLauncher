namespace ObsMCLauncher.Core.Media;

/// <summary>轮播顺序（对应 <c>LauncherConfig.WallpaperSlideMode</c>）</summary>
public enum WallpaperSlideOrder
{
    /// <summary>顺序：从首项开始逐项推进并环绕</summary>
    Sequential = 0,

    /// <summary>随机：每次切换都随机挑一项（不与当前重复）</summary>
    Random = 1,

    /// <summary>随机起点：本次启动随机挑一项作为起点，之后仍按顺序推进</summary>
    RandomStart = 2
}

/// <summary>
/// 多图轮播的**纯逻辑**：起点选择、下一项选择、每张图的驻留时长。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="FramePacingPlan"/> 同样的取舍：把"什么时候换下一张、换哪一张"做成
/// 无副作用的纯函数，定时器 / 生命周期留在应用层（<c>Desktop/Services/WallpaperRotationScheduler</c>）。
/// 这样轮播最容易出错的边界（单张、空列表、随机不重复、动图播完再切）可以脱离 UI 单独测。
/// </para>
/// <para>
/// **"动图播完再切"的落地口径**（设计文档 §10 阶段 3）：
/// <list type="bullet">
/// <item>静态图 / 只显示首帧的动图：驻留 = 轮播间隔；</item>
/// <item>有限循环动图：驻留 = <c>max(间隔, 完整播放时长)</c>——绝不在一张图播到一半时切走；</item>
/// <item>无限循环动图：驻留 = <c>max(间隔, 单轮时长)</c>——"播完"对无限循环不存在，
/// 退一步保证**至少完整播一轮**，这与用户对"别在中途切走"的预期一致。</item>
/// </list>
/// </para>
/// </remarks>
public sealed class WallpaperRotationPlan
{
    /// <summary>间隔取值：<c>0</c> = 关闭轮播（固定首项）</summary>
    public const int IntervalDisabled = 0;

    /// <summary>
    /// 间隔取值：<c>-1</c> = 每次启动。
    /// </summary>
    /// <remarks>
    /// 只影响**起点选择**，不启用定时器。注意「顺序 + 每次启动」在无持久化游标的前提下
    /// 仍从首项开始（与"关闭"同效），要继续推进需要额外落盘一个游标——这是刻意的范围收敛，
    /// 见设计文档 §10 阶段 3 的实施说明。
    /// </remarks>
    public const int IntervalEachStartup = -1;

    private readonly Random _random;

    /// <param name="itemCount">候选条目数（已通过"扩展名支持 + 文件存在"的粗筛）</param>
    /// <param name="intervalSeconds">轮播间隔秒数：正数 = 秒数，0 = 关闭，-1 = 每次启动</param>
    /// <param name="slideMode">顺序模式（<c>LauncherConfig.WallpaperSlideMode</c>），越界按顺序处理</param>
    /// <param name="random">随机源；<c>null</c> 表示用默认实例。<b>单测务必传入固定种子</b></param>
    public WallpaperRotationPlan(int itemCount, int intervalSeconds, int slideMode, Random? random = null)
    {
        Count = Math.Max(0, itemCount);
        IntervalSeconds = intervalSeconds;
        Order = ParseOrder(slideMode);
        _random = random ?? new Random();
    }

    /// <summary>候选条目数</summary>
    public int Count { get; }

    /// <summary>轮播间隔秒数（原样保留，含 0 / -1 两个特殊值）</summary>
    public int IntervalSeconds { get; }

    /// <summary>顺序模式</summary>
    public WallpaperSlideOrder Order { get; }

    /// <summary>是否启用定时轮播：至少两张图，且间隔为正数</summary>
    public bool IsRotating => Count > 1 && IntervalSeconds > 0;

    /// <summary>轮播间隔（毫秒）；未启用时为 0</summary>
    public double IntervalMs => IntervalSeconds > 0 ? IntervalSeconds * 1000.0 : 0;

    /// <summary>把配置里的整数解析为顺序模式，越界值兜底为顺序</summary>
    public static WallpaperSlideOrder ParseOrder(int slideMode) => slideMode switch
    {
        1 => WallpaperSlideOrder.Random,
        2 => WallpaperSlideOrder.RandomStart,
        _ => WallpaperSlideOrder.Sequential
    };

    /// <summary>
    /// 本次生效的起点下标；<c>-1</c> 表示没有可用条目。
    /// </summary>
    /// <remarks>
    /// 关闭轮播时固定首项（最可预测，不受顺序模式影响）；随机 / 随机起点模式本次启动随机取一项。
    /// </remarks>
    public int InitialIndex()
    {
        if (Count <= 0) return -1;
        if (Count == 1) return 0;

        // 间隔为 0 = 关闭轮播：固定首项，忽略顺序模式，避免"设了随机却没开轮播"引起误解
        if (IntervalSeconds == IntervalDisabled) return 0;

        return Order == WallpaperSlideOrder.Sequential ? 0 : _random.Next(Count);
    }

    /// <summary>下一项下标（含环绕）。随机模式下不会与 <paramref name="current"/> 相同</summary>
    public int NextIndex(int current)
    {
        if (Count <= 0) return -1;
        if (Count == 1) return 0;

        if (Order != WallpaperSlideOrder.Random)
        {
            // 顺序 / 随机起点：起点之后一律顺序推进
            var normalized = current < 0 ? -1 : current % Count;
            return (normalized + 1) % Count;
        }

        // 随机：在 [0, Count) 里排除当前项后均匀取一个，避免"连续两次同一张"的观感
        var candidate = _random.Next(Count - 1);
        return candidate >= current ? candidate + 1 : candidate;
    }

    /// <summary>
    /// 当前项应驻留多久才切走（毫秒）；<c>0</c> 表示不轮播（调用方不应安排下一次切换）。
    /// </summary>
    /// <param name="intervalSeconds">轮播间隔秒数</param>
    /// <param name="info">当前项在**真正播放动画**时的探测结果；静态图或只显示首帧时传 <c>null</c></param>
    public static double ResolveDwellMs(int intervalSeconds, AnimatedImageInfo? info)
    {
        if (intervalSeconds <= 0) return 0;

        var intervalMs = intervalSeconds * 1000.0;
        if (info is null || !info.IsPlayable) return intervalMs;

        var cycleMs = info.TotalDurationMs;
        if (cycleMs <= 0) return intervalMs;

        // 无限循环（-1）只保证播完一轮；有限循环按 Netscape 语义播 LoopCount + 1 次
        var playMs = info.LoopCount < 0 ? cycleMs : cycleMs * (info.LoopCount + 1);
        return Math.Max(intervalMs, playMs);
    }
}
