using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using ObsMCLauncher.Core.Media;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.Models;
using SkiaSharp;

namespace ObsMCLauncher.Desktop.Controls;

/// <summary>
/// 动图呈现控件：把解码好的帧按时间轴画到合成器上。
/// </summary>
/// <remarks>
/// <para>
/// 渲染走 <see cref="CompositionCustomVisualHandler"/>（跑在合成器线程，完全绕开 UI 线程），
/// 与 <c>Avalonia.Labs.Gif</c> 的做法一致——Avalonia 11 没有内置动图播放能力，
/// 维护者给的结论就是"动图类内容用 CompositionCustomVisual"。
/// </para>
/// <para>
/// **缓冲区架构（对设计文档 §5.3 的一处刻意偏离，理由如下）**：
/// 方案给出的是"累积解码缓冲 ×1 + 呈现缓冲 ×2"。这里实现为
/// "累积解码缓冲 ×1（增量解码原地写入）+ 每帧 <c>SKImage.FromPixelCopy</c> 移交"：
/// </para>
/// <list type="bullet">
/// <item>探测实测 <c>FromPixelCopy</c> 在 1100×700 / 2.94 MB 上耗时 0.524 ms，与"预先分配 + memcpy"同价；
/// 它换来的好处是**移交出去的图像自己持有像素**，合成器持有多久都无所谓——比"双呈现缓冲轮换"更强的不变式，
/// 而且省掉 1 帧内存（2 帧常驻 vs 3 帧）与"必须先确认合成器释放才能释放内存"的停止握手。</item>
/// <item>代价是每帧一次 Skia 原生分配（非托管，不进 GC 堆），与 §6.3 "风险不在带宽而在每帧分配"的结论一致——
/// 这里已经验证过分配本身就在 0.5 ms 量级内。</item>
/// <item>累积缓冲**只有解码线程碰**，移交缓冲**只有合成器碰**，两者之间没有任何共享内存，
/// 竞态从架构上消失。</item>
/// </list>
/// <para>
/// 帧率上限与跳帧背压由 <see cref="FramePacingPlan"/> 负责：解码线程按时间轴推进，
/// 落后时丢帧而不是堆积（保证动画总时长不失真）。
/// </para>
/// </remarks>
public sealed class AnimatedImagePresenter : Control
{
    /// <summary>要播放的内容</summary>
    public static readonly StyledProperty<WallpaperRenderRequest?> RequestProperty =
        AvaloniaProperty.Register<AnimatedImagePresenter, WallpaperRenderRequest?>(nameof(Request));

    /// <summary>拉伸方式</summary>
    public static readonly StyledProperty<Stretch> StretchProperty =
        AvaloniaProperty.Register<AnimatedImagePresenter, Stretch>(nameof(Stretch), Stretch.Uniform);

    /// <summary>暂停渲染循环（省电策略；见设计文档 §6.5）</summary>
    public static readonly StyledProperty<bool> IsPausedProperty =
        AvaloniaProperty.Register<AnimatedImagePresenter, bool>(nameof(IsPaused));

    private CompositionCustomVisual? _visual;
    private AnimatedImageVisualHandler? _handler;
    private AnimatedImageSession? _session;

    static AnimatedImagePresenter()
    {
        AffectsMeasure<AnimatedImagePresenter>(RequestProperty);
    }

    /// <summary>要播放的内容；<c>null</c> 表示清空</summary>
    public WallpaperRenderRequest? Request
    {
        get => GetValue(RequestProperty);
        set => SetValue(RequestProperty, value);
    }

    /// <summary>拉伸方式</summary>
    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    /// <summary>是否暂停（暂停时解码线程停止推进，且不产生任何绘制）</summary>
    public bool IsPaused
    {
        get => GetValue(IsPausedProperty);
        set => SetValue(IsPausedProperty, value);
    }

    /// <summary>本次会话已解码的帧数（诊断 / 验收观测）</summary>
    public long DecodedFrames => _session?.DecodedFrames ?? 0;

    /// <summary>因跟不上时间轴而跳过的帧数（诊断）</summary>
    public long SkippedFrames => _session?.SkippedFrames ?? 0;

    /// <summary>最近一次失败原因（成功时为 <c>null</c>）</summary>
    public string? LastError { get; private set; }

    // ────────────────────────────────────────────────────────────────
    // 生命周期
    // ────────────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        // 永远是背景层，铺满可用空间（父容器是 WallpaperHost 的 Panel）
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        return new Size(width, height);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        LayoutUpdated += OnLayoutUpdated;
        EnsureVisual();
        PushStretch();
        TryStartSession();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdated;
        StopSession();
        // 视觉对象随控件一起销毁，这里只需断开引用
        if (_handler is not null) _handler.Session = null;
        _visual = null;
        _handler = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RequestProperty)
        {
            TryStartSession();
        }
        else if (change.Property == StretchProperty)
        {
            PushStretch();
        }
        else if (change.Property == IsPausedProperty)
        {
            _session?.SetPaused(IsPaused);
        }
        else if (change.Property == IsVisibleProperty)
        {
            // 被折叠 / 隐藏时没必要继续解码（最小化场景由 WallpaperHost 统一处理）
            _session?.SetPaused(IsPaused || !IsVisible);
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_visual is null) return;

        var size = Bounds.Size;
        _visual.Size = new Vector2((float)size.Width, (float)size.Height);

        // 首次布局完成前拿不到 Bounds，会话要等到这一刻才能确定解码尺寸
        if (_session is null && Request is not null) TryStartSession();
    }

    private void EnsureVisual()
    {
        if (_visual is not null) return;

        var compositor = ElementComposition.GetElementVisual(this)?.Compositor;
        if (compositor is null)
        {
            // 非 Skia 后端理论上不会出现（Desktop 走 Avalonia.Skia）
            LastError = "无法获取合成器，动图背景不可用";
            DebugLogger.Error("Wallpaper", LastError);
            return;
        }

        _handler = new AnimatedImageVisualHandler();
        _visual = compositor.CreateCustomVisual(_handler);
        ElementComposition.SetElementChildVisual(this, _visual);
        _visual.Size = new Vector2((float)Bounds.Width, (float)Bounds.Height);
    }

    private void PushStretch()
    {
        // 这条消息同时起到"启动帧回调循环"的作用（OnMessage 里会注册下一帧）
        _visual?.SendHandlerMessage(new StretchMessage(Stretch));
    }

    // ────────────────────────────────────────────────────────────────
    // 会话管理
    // ────────────────────────────────────────────────────────────────

    private void TryStartSession()
    {
        var request = Request;
        if (request is null)
        {
            StopSession();
            return;
        }

        if (_visual is null || Bounds.Width < 1 || Bounds.Height < 1) return;   // 等布局

        StopSession();

        // 解码尺寸按**物理像素**夹紧：HiDPI（RenderScaling=2）下物理尺寸翻 4 倍，是防内存爆掉的关键闸门
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var windowLongEdge = (int)Math.Ceiling(Math.Max(Bounds.Width, Bounds.Height) * scaling);

        var session = new AnimatedImageSession(request, windowLongEdge);
        session.Start(IsPaused || !IsVisible);
        _session = session;
        if (_handler is not null) _handler.Session = session;
    }

    private void StopSession()
    {
        var session = _session;
        _session = null;
        if (session is null) return;

        // 先停解码线程（会 Join），再让合成器丢掉手上的那一帧
        session.Dispose();
        _visual?.SendHandlerMessage(new StopMessage());
    }
}

// ────────────────────────────────────────────────────────────────────
// 合成器侧：只在合成器线程执行
// ────────────────────────────────────────────────────────────────────

/// <summary>拉伸方式变更（同时用于启动帧回调循环）</summary>
internal readonly record struct StretchMessage(Stretch Value);

/// <summary>停止并释放当前帧</summary>
internal readonly record struct StopMessage();

/// <summary>
/// 合成器线程上的呈现处理器：从会话里取最新帧，画到目标矩形。
/// </summary>
internal sealed class AnimatedImageVisualHandler : CompositionCustomVisualHandler
{
    private volatile AnimatedImageSession? _session;
    private SKImage? _current;
    private Stretch _stretch = Stretch.Uniform;
    private bool _stopped;

    /// <summary>帧来源（由 UI 线程在会话建立后赋值）</summary>
    public AnimatedImageSession? Session
    {
        get => _session;
        set => _session = value;
    }

    public override void OnMessage(object message)
    {
        switch (message)
        {
            case StretchMessage stretch:
                _stretch = stretch.Value;
                Invalidate();
                break;

            case StopMessage:
                _stopped = true;
                _current?.Dispose();
                _current = null;
                Invalidate();
                return;
        }

        // 消息是唯一的"从静止启动"入口：这里注册下一帧回调，之后由 OnAnimationFrameUpdate 自续
        if (!_stopped) RegisterForNextAnimationFrameUpdate();
    }

    public override void OnAnimationFrameUpdate()
    {
        if (_stopped) return;

        var frame = _session?.TakePendingFrame();
        if (frame is not null)
        {
            _current?.Dispose();
            _current = frame;
            Invalidate();
        }

        // 保持每帧醒来：解码侧只在有新帧时发布，所以"醒来但不画"几乎零成本，
        // 换来的是无需从后台线程唤醒合成器（SendHandlerMessage 不是线程安全的）
        RegisterForNextAnimationFrameUpdate();
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
        var image = _current;
        if (image is null) return;

        // Avalonia 11.3 的 ImmediateDrawingContext 只暴露非泛型的 TryGetFeature(Type)，
        // 没有 out 参数版本，因此这里用 typeof + 模式匹配取租赁接口。
        if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature leaseFeature)
        {
            // 非 Skia 后端（理论上到不了这里）：直接不画，避免抛异常打崩渲染
            return;
        }

        var size = EffectiveSize;
        var destination = ComputeDestination(image, size);
        if (destination.Width <= 0 || destination.Height <= 0) return;

        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, (float)size.X, (float)size.Y));
        canvas.DrawImage(image, destination);
        canvas.Restore();
    }

    /// <summary>按拉伸方式算目标矩形（与 Avalonia <see cref="Stretch"/> 语义一致）</summary>
    private SKRect ComputeDestination(SKImage image, Avalonia.Vector size)
    {
        float viewWidth = (float)size.X, viewHeight = (float)size.Y;
        float imageWidth = image.Width, imageHeight = image.Height;
        if (viewWidth <= 0 || viewHeight <= 0 || imageWidth <= 0 || imageHeight <= 0) return SKRect.Empty;

        switch (_stretch)
        {
            case Stretch.None:
                return new SKRect(
                    (viewWidth - imageWidth) / 2f,
                    (viewHeight - imageHeight) / 2f,
                    (viewWidth + imageWidth) / 2f,
                    (viewHeight + imageHeight) / 2f);

            case Stretch.Fill:
                return new SKRect(0, 0, viewWidth, viewHeight);

            default:
            {
                var scaleX = viewWidth / imageWidth;
                var scaleY = viewHeight / imageHeight;
                var scale = _stretch == Stretch.UniformToFill ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
                var width = imageWidth * scale;
                var height = imageHeight * scale;
                var left = (viewWidth - width) / 2f;
                var top = (viewHeight - height) / 2f;
                return new SKRect(left, top, left + width, top + height);
            }
        }
    }
}

// ────────────────────────────────────────────────────────────────────
// 解码侧：独立后台线程
// ────────────────────────────────────────────────────────────────────

/// <summary>
/// 一次动图播放会话：解码线程 + 累积缓冲 + 单槽移交信箱。
/// </summary>
/// <remarks>
/// 线程约定：<see cref="Loop"/> 只在解码线程上跑，写 <see cref="_accumulate"/>；
/// 合成器线程只通过 <see cref="TakePendingFrame"/> 取一份**自己持有像素**的帧。
/// 两侧不共享任何可写内存。
/// </remarks>
internal sealed class AnimatedImageSession : IDisposable
{
    private const string LogService = "Wallpaper";

    private readonly WallpaperRenderRequest _request;
    private readonly int _windowLongEdge;
    private readonly ManualResetEventSlim _stopEvent = new(false);

    private Thread? _thread;
    private IAnimatedImageDecoder? _decoder;
    private IntPtr _accumulate = IntPtr.Zero;
    private SKImageInfo _pixelInfo;
    private int _rowBytes;
    private SKImage? _pending;

    private readonly Stopwatch _clock = new();
    private double _pausedMs;

    private volatile bool _stopRequested;
    private volatile bool _paused;
    private bool _disposed;

    public AnimatedImageSession(WallpaperRenderRequest request, int windowLongEdge)
    {
        _request = request;
        _windowLongEdge = windowLongEdge;
    }

    /// <summary>已解码帧数</summary>
    public long DecodedFrames { get; private set; }

    /// <summary>跳帧数（背压）</summary>
    public long SkippedFrames { get; private set; }

    /// <summary>最近一次失败原因</summary>
    public string? LastError { get; private set; }

    /// <summary>解码是否已停下（播完有限的循环 / 出错）</summary>
    public bool IsCompleted { get; private set; }

    public void Start(bool paused)
    {
        _paused = paused;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "OMCL-WallpaperDecode",
            // 背景解码不该和游戏抢 CPU
            Priority = ThreadPriority.BelowNormal
        };
        _thread.Start();
    }

    public void SetPaused(bool paused) => _paused = paused;

    /// <summary>取走待呈现的帧（合成器线程调用）；调用方负责释放返回的对象</summary>
    public SKImage? TakePendingFrame() => Interlocked.Exchange(ref _pending, null);

    private void Loop()
    {
        try
        {
            RunCore();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            DebugLogger.Error(LogService, $"动图解码线程异常：{ex}");
        }
        finally
        {
            IsCompleted = true;
            _stopEvent.Set();
        }
    }

    private void RunCore()
    {
        var info = _request.Info;
        _decoder = SkiaAnimatedImageDecoder.TryCreate(
            _request.Path, info, _windowLongEdge, _request.MaxDecodeEdge, out var createError);

        if (_decoder is null)
        {
            LastError = createError ?? "解码器创建失败";
            return;
        }

        _pixelInfo = new SKImageInfo(_decoder.DecodeWidth, _decoder.DecodeHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        _rowBytes = _pixelInfo.RowBytes;
        _accumulate = Marshal.AllocHGlobal(_rowBytes * _decoder.DecodeHeight);
        if (_accumulate == IntPtr.Zero)
        {
            LastError = "无法分配解码缓冲";
            return;
        }

        var plan = _request.PlayAnimated && info.IsAnimated
            ? new FramePacingPlan(info.FrameDurationsMs, _request.MaxFps, info.LoopCount)
            : FramePacingPlan.SingleFrame(_request.MaxFps);

        _clock.Restart();
        _pausedMs = 0;
        var cursor = FrameCursor.Start;
        var lastDecoded = -1;

        while (!_stopRequested)
        {
            if (_paused)
            {
                if (!WaitWhilePaused()) return;
                continue;
            }

            var result = _decoder.DecodeFrame(cursor.Index, _accumulate, _rowBytes, lastDecoded);
            if (result.IsSuccess)
            {
                lastDecoded = cursor.Index;
                Publish();
                DecodedFrames++;
            }
            else
            {
                LastError = result.Error;
                if (lastDecoded < 0)
                {
                    // 首帧就解不出来：继续循环只会白烧 CPU
                    DebugLogger.Error(LogService, $"首帧解码失败，停止播放：{result.Error}（{_request.Path}）");
                    return;
                }

                // 中途失败：丢掉"累积缓冲里是哪一帧"的前提，
                // 下一帧改用独立解码（prior = -1）自愈，避免错误逐帧累积
                lastDecoded = -1;
                SkippedFrames++;
            }

            var next = plan.CatchUp(plan.Next(cursor), ElapsedMs);
            SkippedFrames += next.SkippedFrames - cursor.SkippedFrames;
            cursor = next;

            if (plan.IsFinished(cursor))
            {
                // 播完有限循环：停在最后一帧，不再请求帧回调（省电）
                DebugLogger.Info(LogService, $"动图播放完成（{DecodedFrames} 帧，跳过 {SkippedFrames} 帧）");
                return;
            }

            if (!WaitUntil(cursor.DueMs)) return;
        }
    }

    /// <summary>当前播放时刻（毫秒，已扣除暂停时长）</summary>
    private double ElapsedMs => _clock.Elapsed.TotalMilliseconds - _pausedMs;

    /// <summary>阻塞直到恢复播放；返回 <c>false</c> 表示收到停止信号</summary>
    private bool WaitWhilePaused()
    {
        var pausedAt = _clock.Elapsed.TotalMilliseconds;
        while (_paused && !_stopRequested) _stopEvent.Wait(50);
        _pausedMs += _clock.Elapsed.TotalMilliseconds - pausedAt;
        return !_stopRequested;
    }

    /// <summary>分片等待到目标时刻；返回 <c>false</c> 表示应结束循环</summary>
    private bool WaitUntil(double dueMs)
    {
        while (!_stopRequested)
        {
            var remaining = dueMs - ElapsedMs;
            if (remaining <= 0) return true;
            if (_paused) return true;   // 暂停交给循环顶部统一处理
            if (_stopEvent.Wait((int)Math.Min(remaining, 250))) return false;
        }

        return false;
    }

    /// <summary>把累积缓冲的当前内容拷贝移交（<c>FromPixelCopy</c> 之后两侧不再共享内存）</summary>
    private void Publish()
    {
        var image = SKImage.FromPixelCopy(_pixelInfo, _accumulate, _rowBytes);
        if (image is null) return;

        // 信箱只有一格：合成器还没取走旧帧时直接替换，永远是"最新的一帧"
        Interlocked.Exchange(ref _pending, image)?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _stopRequested = true;
        _stopEvent.Set();

        var thread = _thread;
        _thread = null;

        // 解码线程是 _decoder / _accumulate / _stopEvent 的唯一访问者（除了这里），
        // 所以"是否已经退出"决定了释放是否安全：退出后这些对象再无人碰，可以放心释放。
        var joined = thread is null || thread == Thread.CurrentThread || thread.Join(TimeSpan.FromSeconds(2));
        if (!joined)
        {
            // 线程卡在原生解码调用里（畸形文件可以让单次解码异常缓慢）。
            // 此时它仍在使用上述三块资源，释放其中任何一块都是 use-after-free，
            // 宁可让它们随本对象一起被 GC 收走，也不要制造一个必崩的释放路径。
            DebugLogger.Error(LogService, "解码线程未在 2 秒内退出，跳过资源释放以避免释放竞态");
            return;
        }

        _decoder?.Dispose();
        _decoder = null;

        Interlocked.Exchange(ref _pending, null)?.Dispose();

        if (_accumulate != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_accumulate);
            _accumulate = IntPtr.Zero;
        }

        _stopEvent.Dispose();
    }
}
