using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.Models;

namespace ObsMCLauncher.Desktop.Controls;

/// <summary>
/// 壁纸宿主：对外只暴露一个 <see cref="Request"/>，内部用两层叠加实现交叉淡入。
/// </summary>
/// <remarks>
/// <para>
/// 取代原先"把一个 <c>ImageBrush</c> 写进 <c>Application.Resources</c>"的载体。
/// 原来那套结构承载不了动图：<c>ImageBrush.Source</c> 只接受单帧位图，
/// 而且全局可变资源靠 <c>Dispatcher.UIThread.Post</c> 手动推送，时序很难保证。
/// </para>
/// <para>
/// 静态图走 <see cref="Image"/>，动图走 <see cref="AnimatedImagePresenter"/>；
/// 两层各持一套，交叉淡入期间新旧两层各自继续播放（动图不冻结，避免视觉割裂）。
/// </para>
/// <para>
/// 省电策略在这里收口：<see cref="ExternalPause"/> 来自服务（电池 / 配置），
/// 窗口最小化与失焦由控件自己监听（只有控件知道自己在哪个窗口里）。
/// </para>
/// </remarks>
public sealed class WallpaperHost : Panel
{
    /// <summary>要显示的壁纸；<c>null</c> 表示清空</summary>
    public static readonly StyledProperty<WallpaperRenderRequest?> RequestProperty =
        AvaloniaProperty.Register<WallpaperHost, WallpaperRenderRequest?>(nameof(Request));

    /// <summary>外部暂停（电池供电、用户设置等）</summary>
    public static readonly StyledProperty<bool> ExternalPauseProperty =
        AvaloniaProperty.Register<WallpaperHost, bool>(nameof(ExternalPause));

    /// <summary>窗口失焦时是否暂停</summary>
    public static readonly StyledProperty<bool> PauseOnUnfocusedProperty =
        AvaloniaProperty.Register<WallpaperHost, bool>(nameof(PauseOnUnfocused));

    /// <summary>
    /// 仅由**窗口状态**（最小化 / 失焦）引起的暂停，不含外部暂停。
    /// </summary>
    /// <remarks>
    /// 对外只读，靠 <c>Mode=OneWayToSource</c> 回推给 <c>WallpaperService.WindowPause</c>：
    /// 只有控件知道自己挂在哪个窗口上，服务不该去猜窗口状态。
    /// 阶段 5 补这条接线的原因：轮播定时器同样会周期性唤醒 CPU，
    /// 窗口都最小化了还照切不误，与 §6.5 的省电结论冲突。
    /// </remarks>
    public static readonly StyledProperty<bool> IsWindowPausedProperty =
        AvaloniaProperty.Register<WallpaperHost, bool>(nameof(IsWindowPaused));

    private readonly WallpaperLayer _layerA = new();
    private readonly WallpaperLayer _layerB = new();
    private WallpaperLayer _active;
    private int _transitionToken;

    private Window? _window;

    public WallpaperHost()
    {
        // 纯装饰层：绝不能抢走导航栏/内容区的点击
        IsHitTestVisible = false;
        IsVisible = false;

        _active = _layerA;
        Children.Add(_layerA);
        Children.Add(_layerB);
    }

    /// <summary>要显示的壁纸</summary>
    public WallpaperRenderRequest? Request
    {
        get => GetValue(RequestProperty);
        set => SetValue(RequestProperty, value);
    }

    /// <summary>外部暂停源（电池 / 配置）</summary>
    public bool ExternalPause
    {
        get => GetValue(ExternalPauseProperty);
        set => SetValue(ExternalPauseProperty, value);
    }

    /// <summary>窗口失焦时暂停</summary>
    public bool PauseOnUnfocused
    {
        get => GetValue(PauseOnUnfocusedProperty);
        set => SetValue(PauseOnUnfocusedProperty, value);
    }

    /// <summary>仅由窗口状态（最小化 / 失焦）引起的暂停，不含外部暂停</summary>
    public bool IsWindowPaused
    {
        get => GetValue(IsWindowPausedProperty);
        private set => SetValue(IsWindowPausedProperty, value);
    }

    /// <summary>当前生效层上已解码的帧数（诊断）</summary>
    public long DecodedFrames => _active.DecodedFrames;

    /// <summary>当前生效层跳过的帧数（诊断）</summary>
    public long SkippedFrames => _active.SkippedFrames;

    // ────────────────────────────────────────────────────────────────
    // 属性与生命周期
    // ────────────────────────────────────────────────────────────────

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RequestProperty)
        {
            ApplyRequest(change.GetNewValue<WallpaperRenderRequest?>());
        }
        else if (change.Property == ExternalPauseProperty || change.Property == PauseOnUnfocusedProperty)
        {
            UpdatePauseState();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        HookWindow();
        UpdatePauseState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnhookWindow();
        base.OnDetachedFromVisualTree(e);
    }

    private void HookWindow()
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (ReferenceEquals(window, _window)) return;

        UnhookWindow();
        _window = window;
        if (_window is null) return;

        _window.Activated += OnWindowActivationChanged;
        _window.Deactivated += OnWindowActivationChanged;
        _window.PropertyChanged += OnWindowPropertyChanged;
    }

    private void UnhookWindow()
    {
        if (_window is null) return;
        _window.Activated -= OnWindowActivationChanged;
        _window.Deactivated -= OnWindowActivationChanged;
        _window.PropertyChanged -= OnWindowPropertyChanged;
        _window = null;
    }

    private void OnWindowActivationChanged(object? sender, EventArgs e) => UpdatePauseState();

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // Window 没有 StateChanged 事件，用属性变更监听（最小化 → 停止渲染是最省电的一档）
        if (e.Property == Window.WindowStateProperty) UpdatePauseState();
    }

    private void UpdatePauseState()
    {
        var minimized = _window?.WindowState == WindowState.Minimized;
        var unfocused = PauseOnUnfocused && _window is { IsActive: false };
        var paused = ExternalPause || minimized || unfocused;

        // 回推给服务，让轮播定时器也跟着停（只有控件知道窗口状态）
        IsWindowPaused = minimized || unfocused;

        _layerA.SetPaused(paused);
        _layerB.SetPaused(paused);
    }

    // ────────────────────────────────────────────────────────────────
    // 内容切换（交叉淡入）
    // ────────────────────────────────────────────────────────────────

    private void ApplyRequest(WallpaperRenderRequest? request)
    {
        var token = ++_transitionToken;

        if (request is null)
        {
            IsVisible = false;
            _layerA.Clear();
            _layerB.Clear();
            return;
        }

        IsVisible = true;
        Opacity = Math.Clamp(request.Opacity, 0, 1);

        // 内容没变、只是外观参数被重新推过来（拖透明度/导航栏让位滑杆时会高频发生）：
        // 只更新外观，不重建解码会话——否则动图会被反复拽回第 0 帧
        if (_active.Request is { } current && IsSameContent(current, request))
        {
            _active.UpdateAppearance(request);
            return;
        }

        var incoming = ReferenceEquals(_active, _layerA) ? _layerB : _layerA;

        incoming.SetRequest(request);
        // 用属性而不是再算一遍：属性在 UpdatePauseState 里统一更新，
        // 两条路径各算一次迟早会算出不一样的结果
        UpdatePauseState();
        incoming.SetPaused(ExternalPause || IsWindowPaused);

        var previous = _active;
        _active = incoming;
        var duration = TimeSpan.FromMilliseconds(Math.Clamp(request.TransitionMs, 0, 2000));

        if (duration <= TimeSpan.Zero)
        {
            incoming.Opacity = 1;
            previous.Clear();
            return;
        }

        // 从 0 淡入：用 Avalonia 的 Transitions，动画跑在合成器上，不占 UI 线程
        incoming.Opacity = 0;
        incoming.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = duration, Easing = new SineEaseOut() }
        };
        incoming.Opacity = 1;

        DispatcherTimer.RunOnce(() =>
        {
            if (token != _transitionToken) return;
            previous.Clear();
        }, duration + TimeSpan.FromMilliseconds(80));
    }

    /// <summary>
    /// 两份请求是否指向"同一段内容"（同文件 + 同样的解码与播放参数）。
    /// </summary>
    /// <remarks>
    /// 刻意不比较 <see cref="WallpaperRenderRequest.Opacity"/> 与 <c>TransitionMs</c>：
    /// 这两个是纯外观参数，改了不需要重建解码会话。
    /// <see cref="WallpaperRenderRequest.Info"/> 是普通类（引用相等），但它由
    /// <c>BackgroundResolver</c> 按 {路径, 大小, 最后写入} 缓存，同一文件在同一时刻只会有一个实例，
    /// 所以引用相等恰好就是"文件内容没换"的判据。
    /// </remarks>
    private static bool IsSameContent(WallpaperRenderRequest a, WallpaperRenderRequest b)
        => string.Equals(a.Path, b.Path, StringComparison.Ordinal)
           && a.IsAnimated == b.IsAnimated
           && a.PlayAnimated == b.PlayAnimated
           && a.Stretch == b.Stretch
           && a.MaxFps == b.MaxFps
           && a.MaxDecodeEdge == b.MaxDecodeEdge
           && ReferenceEquals(a.Info, b.Info);
}

/// <summary>
/// 单层内容：同时持有一个静态 <see cref="Image"/> 与一个动图呈现器，按类型二选一显示。
/// </summary>
/// <remarks>
/// 两层都是常驻子控件（而不是按需增删），是为了让交叉淡入期间新旧内容都能继续播放，
/// 也避免在过渡中做视觉树增删带来的闪烁。
/// </remarks>
internal sealed class WallpaperLayer : Panel
{
    private readonly Image _image = new()
    {
        Stretch = Stretch.Uniform,
        StretchDirection = StretchDirection.Both,
        IsVisible = false
    };

    private readonly AnimatedImagePresenter _presenter = new() { IsVisible = false };

    private int _loadToken;
    private Bitmap? _bitmap;
    private bool _isPaused;

    public WallpaperLayer()
    {
        IsHitTestVisible = false;
        Children.Add(_image);
        Children.Add(_presenter);
    }

    /// <summary>本层已解码帧数</summary>
    public long DecodedFrames => _presenter.DecodedFrames;

    /// <summary>本层跳过的帧数</summary>
    public long SkippedFrames => _presenter.SkippedFrames;

    /// <summary>本层当前内容；<c>null</c> 表示空层</summary>
    public WallpaperRenderRequest? Request { get; private set; }

    public void SetRequest(WallpaperRenderRequest request)
    {
        Request = request;

        if (request.IsAnimated)
        {
            // 切到动图：停掉静态图并释放位图，避免两套内容各占一份内存
            _loadToken++;
            _image.IsVisible = false;
            _image.Source = null;
            DisposeBitmap();

            _presenter.Stretch = request.Stretch;
            _presenter.IsPaused = _isPaused;
            _presenter.IsVisible = true;
            _presenter.Request = request;
        }
        else
        {
            _presenter.Request = null;
            _presenter.IsVisible = false;

            _image.Stretch = request.Stretch;
            _image.IsVisible = true;
            LoadStaticBitmap(request.Path);
        }
    }

    /// <summary>
    /// 内容不变、只改了外观参数时调用：不碰解码会话，只同步拉伸/暂停等显示状态。
    /// </summary>
    public void UpdateAppearance(WallpaperRenderRequest request)
    {
        Request = request;
        _image.Stretch = request.Stretch;
        _presenter.Stretch = request.Stretch;
    }

    public void SetPaused(bool paused)
    {
        _isPaused = paused;
        _presenter.IsPaused = paused;
    }

    public void Clear()
    {
        Request = null;
        _loadToken++;
        _presenter.Request = null;
        _presenter.IsVisible = false;
        _image.IsVisible = false;
        _image.Source = null;
        DisposeBitmap();
    }

    /// <summary>
    /// 后台解码静态图再回 UI 线程赋值：<c>new Bitmap(path)</c> 是同步文件 IO + 解码，
    /// 不能出现在窗口构造或属性变更的调用栈上（验收 A7：主线程不得有 &gt; 100ms 的单次阻塞）。
    /// </summary>
    private void LoadStaticBitmap(string path)
    {
        var token = ++_loadToken;
        _ = LoadAsync(token, path);

        async Task LoadAsync(int expected, string file)
        {
            try
            {
                var bitmap = await Task.Run(() => new Bitmap(file)).ConfigureAwait(true);
                if (expected != _loadToken)
                {
                    bitmap.Dispose();
                    return;
                }

                DisposeBitmap();
                _bitmap = bitmap;
                _image.Source = bitmap;
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Wallpaper", $"静态壁纸加载失败：{ex.Message}（{file}）");
            }
        }
    }

    private void DisposeBitmap()
    {
        var bitmap = _bitmap;
        _bitmap = null;
        bitmap?.Dispose();
    }
}
