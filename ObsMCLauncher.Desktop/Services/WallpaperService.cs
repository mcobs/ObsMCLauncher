using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ObsMCLauncher.Core.Media;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.Models;

namespace ObsMCLauncher.Desktop.Services;

/// <summary>
/// 壁纸服务：把"配置"翻译成"要绘制什么"，并负责省电策略与导航栏让位。
/// </summary>
/// <remarks>
/// <para>
/// 从 <c>SettingsViewModel.ApplyWallpaper()</c> 抽出来。原先那个方法同时干四件事
/// （读配置 / 解码 / 写全局资源 / 算导航栏透明度），加入动图生命周期与轮播调度前必须拆开：
/// 现在设置页只管"改配置 → 推快照"，绘制与资源写入都收口在这里。
/// </para>
/// <para>
/// 探测（可能耗时 5–80 ms，长 GIF 更久）与静态图解码一律走后台线程，
/// 只有结果落地与全局资源写入回到 UI 线程。
/// </para>
/// </remarks>
public sealed class WallpaperService : INotifyPropertyChanged, IDisposable
{
    private const string LogService = "Wallpaper";

    /// <summary>
    /// "只改外观"的日志合并窗口（毫秒）。
    /// </summary>
    /// <remarks>
    /// 拖滑块时每 10~30ms 就推一次快照，窗口只要比两次触摸事件的间隔长就能把它们收成一条；
    /// 取 700ms 是为了让"松手后马上就能看到那条日志"，同时不至于把两次独立调整也并到一起。
    /// </remarks>
    private const int AppearanceLogQuietMs = 700;

    private readonly PowerStatusMonitor _power = new();
    private readonly WallpaperRotationScheduler _scheduler = new();
    private readonly List<WallpaperRejection> _rejections = new();

    private WallpaperSnapshot? _snapshot;

    // 日志合并状态：上一条已经写出去的请求（用来算净变化）、挂起的那一条、以及它的定时器
    private WallpaperRenderRequest? _lastLoggedRequest;
    private (WallpaperRenderRequest Request, string Delta)? _pendingAppearanceLog;
    private DispatcherTimer? _appearanceLogTimer;

    // 轮播状态：当前快照下"通过探测"的候选、决定换哪张的纯逻辑、以及正显示的下标
    private List<WallpaperCandidate> _candidates = [];
    private WallpaperRotationPlan? _plan;
    private int _currentIndex = -1;

    private int _applyToken;
    private bool _disposed;

    public WallpaperService()
    {
        _power.Changed += OnPowerSourceChanged;
        _power.Start();
    }

    /// <summary>当前要显示的壁纸；<c>null</c> 表示没有可显示的内容</summary>
    public WallpaperRenderRequest? Request { get; private set; }

    /// <summary>外部暂停（电池供电）</summary>
    public bool ExternalPause { get; private set; }

    private bool _windowPause;

    /// <summary>
    /// 由窗口状态（最小化 / 失焦）引起的暂停，宿主通过 <c>OneWayToSource</c> 回推。
    /// </summary>
    /// <remarks>
    /// 阶段 5 补上：轮播定时器同样会周期性唤醒 CPU，窗口已最小化还照切不误说不过去。
    /// 与 <see cref="ExternalPause"/> 分开存，是因为两者的来源与语义不同：
    /// 电池暂停是"省电策略"，窗口暂停是"没人看得见"。
    /// </remarks>
    public bool WindowPause
    {
        get => _windowPause;
        set
        {
            if (_windowPause == value) return;
            _windowPause = value;

            if (value) _scheduler.Stop();
            else if (_snapshot is { } snapshot) ScheduleNext(snapshot);
        }
    }

    /// <summary>窗口失焦时是否暂停</summary>
    public bool PauseOnUnfocused { get; private set; }

    /// <summary>是否正在生效</summary>
    public bool IsActive => Request is not null;

    /// <summary>最近一次解析失败原因</summary>
    public string? LastError { get; private set; }

    /// <summary>被拒收的条目（APNG 等），阶段 4 的设置页据此弹 InfoBar</summary>
    public IReadOnlyList<WallpaperRejection> Rejections => _rejections;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>拒收集合发生变化</summary>
    public event EventHandler? RejectionsChanged;

    // ────────────────────────────────────────────────────────────────
    // 对外入口
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 按最新快照重新解析并应用壁纸。可在 UI 线程反复调用（每次调用使前一次结果作废）。
    /// </summary>
    public void Apply(WallpaperSnapshot snapshot)
    {
        if (_disposed) return;

        _snapshot = snapshot;
        PauseOnUnfocused = snapshot.PauseOnUnfocused;
        UpdateExternalPause();

        // 新配置到达：作废尚未触发的轮播排定（已在途的异步解析靠 token 作废）
        _scheduler.Stop();

        var token = ++_applyToken;
        _ = ResolveAndApplyAsync(snapshot, token);
    }

    /// <summary>用当前配置对象直接应用（调用方便利方法）</summary>
    public void Apply(LauncherConfig config) => Apply(WallpaperSnapshot.From(config));

    private async Task ResolveAndApplyAsync(WallpaperSnapshot snapshot, int token)
    {
        ResolvedWallpaper resolved;
        try
        {
            // 探测（5–80 ms，长 GIF 更久）全部在后台完成，绝不占 UI 线程（验收 A7）
            resolved = await Task.Run(() => Resolve(snapshot)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                LastError = ex.Message;
                DebugLogger.Error(LogService, $"壁纸解析失败：{ex}");
            });
            return;
        }

        // 结果落地与全局资源写入必须在 UI 线程（这里不依赖隐式同步上下文，
        // 因为构造期的调用发生在 Dispatcher 尚未进入消息循环时）
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed || token != _applyToken) return;   // 已有更新的请求，丢弃这次结果
            PublishResolved(snapshot, resolved);
        });
    }

    /// <summary>把解析结果落到属性与全局资源上（UI 线程）</summary>
    private void PublishResolved(WallpaperSnapshot snapshot, ResolvedWallpaper resolved)
    {
        PublishRejections(resolved.Rejections);

        if (resolved.Candidates.Count == 0)
        {
            _candidates = [];
            _plan = null;
            _currentIndex = -1;
            _scheduler.Stop();

            Request = null;
            // 壁纸整体撤掉了：挂起的合并日志与"上一条日志"都失去对照意义，清掉
            CancelPendingAppearanceLog();
            _lastLoggedRequest = null;

            LastError = resolved.Error;
            ApplyNavigationResources(snapshot, active: false);
            Raise(nameof(Request));
            Raise(nameof(IsActive));
            if (resolved.Error is not null) DebugLogger.Warn(LogService, resolved.Error);
            return;
        }

        var previousCandidates = _candidates;
        _candidates = resolved.Candidates;

        // 轮播只在候选集规模已知时才有意义，所以起点由这里（而非探测线程）决定
        _plan = new WallpaperRotationPlan(_candidates.Count, snapshot.SlideIntervalSeconds, snapshot.SlideMode);

        // **候选集没变（例如只是拖了透明度/模糊）时必须保留当前项**。
        // 重掷起点会让壁纸在拖滑块时乱跳：顺序模式下跳回第一张，随机模式下每格换一张。
        // 而且"乱跳"本质是换图，日志合并也就无从谈起——每次推送都会变成一次内容变化。
        _currentIndex = HasSameCandidates(previousCandidates, _candidates)
            ? Math.Clamp(_currentIndex, 0, _candidates.Count - 1)
            : Math.Max(0, _plan.InitialIndex());

        ShowCurrent(snapshot);
        ScheduleNext(snapshot);
    }

    /// <summary>
    /// 候选文件列表是否没变（路径序列逐项相等；顺序敏感——顺序即轮播顺序）。
    /// </summary>
    /// <remarks>
    /// 只比路径、不比 <see cref="AnimatedImageInfo"/>：文件本身被换掉时当下标也该保留
    /// （换的是同一张图的内容，不是换了一张图）。
    /// </remarks>
    private static bool HasSameCandidates(IReadOnlyList<WallpaperCandidate> a, IReadOnlyList<WallpaperCandidate> b)
    {
        if (a.Count != b.Count) return false;

        for (var i = 0; i < a.Count; i++)
            if (!string.Equals(a[i].Path, b[i].Path, StringComparison.Ordinal)) return false;

        return true;
    }

    /// <summary>把当前项推给呈现层（UI 线程）；首次应用与轮播切换共用这条路径</summary>
    private void ShowCurrent(WallpaperSnapshot snapshot)
    {
        if (_currentIndex < 0 || _currentIndex >= _candidates.Count) return;

        var candidate = _candidates[_currentIndex];
        var previous = Request;

        Request = new WallpaperRenderRequest(
            candidate.Path,
            candidate.Info,
            WallpaperSnapshot.ToStretch(snapshot.Stretch),
            Math.Clamp(snapshot.Opacity, 0, 1),
            snapshot.EffectiveBlurRadius,
            snapshot.ShouldPlayAnimated,
            Math.Clamp(snapshot.MaxFps, 1, 60),
            snapshot.MaxDecodeEdge,
            Math.Clamp(snapshot.TransitionMs, 0, 2000));

        LastError = null;
        ApplyNavigationResources(snapshot, active: true);
        Raise(nameof(Request));
        Raise(nameof(IsActive));

        ReportApplied(candidate, previous);
    }

    /// <summary>
    /// 写"已应用"日志——**内容与外观分开处理**。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 拖「不透明度」「背景模糊」这类滑块时，每过一格都会推一次快照（这是刻意的：拖动过程中要实时看到效果），
    /// 但"每推一次写一条日志"会让调试输出被刷爆（一次拖动最多 30 格 = 30 条，
    /// 而 <c>DebugLogger</c> 最终落到 <c>OutputDebugString</c>，调试器每条都要处理，拖动会明显发涩）。
    /// </para>
    /// <list type="bullet">
    /// <item><b>内容真的换了</b>（换图 / 轮播切换 / 首次应用）→ 立刻写一条，行为与以前完全一致；</item>
    /// <item><b>只改了外观</b>（<see cref="WallpaperRenderRequest.IsSameContentAs"/> 为真）→ 不逐条写，
    /// 合并成"安静 <see cref="AppearanceLogQuietMs"/> 毫秒后"的一条，内容是相对上一条日志的**净变化**；</item>
    /// <item><b>什么都没变</b>（例如只改了「扩展到导航栏」，请求原封不动）→ 一条都不写。</item>
    /// </list>
    /// <para>
    /// 合并只影响日志，请求仍然逐次推送——**拖动过程中的实时效果不变**。
    /// </para>
    /// </remarks>
    private void ReportApplied(WallpaperCandidate candidate, WallpaperRenderRequest? previous)
    {
        var isSameContent = previous is not null && Request!.IsSameContentAs(previous);

        if (!isSameContent)
        {
            // 内容换了：挂起的合并日志直接作废（下面这条已经含最新值），并立刻写日志
            CancelPendingAppearanceLog();
            _lastLoggedRequest = Request;
            DebugLogger.Info(LogService, DescribeApplied(candidate, Request!));
            return;
        }

        // 只改外观：与"上一条已写出去的日志"比，没净变化就什么都不写。
        // 拿 _lastLoggedRequest 而不是 previous 来比，是因为一次拖动里 previous 只差一格，
        // 逐格比只会得到"这一格改了什么"，而用户想看的是"这次调整总共改了什么"。
        if (_lastLoggedRequest is null || !CanDiffAppearance(_lastLoggedRequest))
        {
            // 上一条日志记的是别的内容（理论上到不了这里）：退化成立刻写一条完整日志
            CancelPendingAppearanceLog();
            _lastLoggedRequest = Request;
            DebugLogger.Info(LogService, DescribeApplied(candidate, Request!));
            return;
        }

        if (DescribeAppearanceDelta(_lastLoggedRequest, Request!) is not { Length: > 0 } delta)
        {
            CancelPendingAppearanceLog();
            return;
        }

        _pendingAppearanceLog = (Request!, delta);
        _appearanceLogTimer ??= CreateAppearanceLogTimer();
        _appearanceLogTimer.Stop();
        _appearanceLogTimer.Start();
    }

    /// <summary>上一条日志记的是不是"当前这张图"（不同就不该做外观净变化对比）</summary>
    private bool CanDiffAppearance(WallpaperRenderRequest last)
        => string.Equals(last.Path, Request?.Path, StringComparison.Ordinal);

    /// <summary>合并窗口到点：把挂起的那一条写出去</summary>
    private void WritePendingAppearanceLog()
    {
        _appearanceLogTimer?.Stop();

        var pending = _pendingAppearanceLog;
        _pendingAppearanceLog = null;
        if (pending is not { } entry) return;
        if (!ReferenceEquals(entry.Request, Request)) return;   // 期间又推了新的（已重新排定）或换图了

        _lastLoggedRequest = entry.Request;
        DebugLogger.Info(LogService,
            $"壁纸外观已更新：{Path.GetFileName(entry.Request.Path)}（{entry.Delta}）");
    }

    private void CancelPendingAppearanceLog()
    {
        _appearanceLogTimer?.Stop();
        _pendingAppearanceLog = null;
    }

    private DispatcherTimer CreateAppearanceLogTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AppearanceLogQuietMs) };
        timer.Tick += (_, _) => WritePendingAppearanceLog();
        return timer;
    }

    /// <summary>完整的一条"壁纸已应用"（内容变化时用）</summary>
    private string DescribeApplied(WallpaperCandidate candidate, WallpaperRenderRequest request)
    {
        // 模糊只在开启时才写：0 是绝大多数情况，没必要让每行都带一个"模糊=0"
        var blurNote = request.BlurRadius > 0 ? $"，模糊={request.BlurRadius:0.#}" : string.Empty;
        return $"壁纸已应用：[{_currentIndex + 1}/{_candidates.Count}] {Path.GetFileName(candidate.Path)}" +
               $"（{candidate.Info.Describe()}，动画={(request.IsAnimated ? "开" : "关")}{blurNote}）";
    }

    /// <summary>外观参数的净变化（枚举名用英文，日志按可检索性优先）</summary>
    private static string DescribeAppearanceDelta(WallpaperRenderRequest from, WallpaperRenderRequest to)
    {
        var parts = new List<string>(3);

        if (Math.Abs(from.Opacity - to.Opacity) > 0.001)
            parts.Add($"不透明度={from.Opacity:P0}→{to.Opacity:P0}");
        if (Math.Abs(from.BlurRadius - to.BlurRadius) > 0.01)
            parts.Add($"模糊={from.BlurRadius:0.#}→{to.BlurRadius:0.#}");
        if (from.Stretch != to.Stretch)
            parts.Add($"显示方式={from.Stretch}→{to.Stretch}");

        return string.Join("，", parts);
    }

    /// <summary>
    /// 按当前项的驻留时长排定下一次切换；不轮播（单张 / 间隔为 <c>0</c> 或 <c>-1</c>）或已暂停时取消排定。
    /// </summary>
    /// <remarks>
    /// 只在**真正播放动画**时按"动图播完再切"计算驻留，否则（关闭动图 / 全局动画级别为 0）
    /// 就是普通间隔——此时只显示首帧，谈不上"播完"。
    /// </remarks>
    private void ScheduleNext(WallpaperSnapshot snapshot)
    {
        // 暂停期间不排定：轮播定时器同样会周期性唤醒 CPU，与省电策略（§6.5）冲突。
        // 窗口暂停（最小化 / 失焦）同理——那时没人看得见切换，白耗电。
        if (ExternalPause || WindowPause)
        {
            _scheduler.Stop();
            return;
        }

        if (_plan is not { IsRotating: true } plan || _currentIndex < 0 || _currentIndex >= _candidates.Count)
        {
            _scheduler.Stop();
            return;
        }

        var info = snapshot.ShouldPlayAnimated ? _candidates[_currentIndex].Info : null;
        var dwellMs = WallpaperRotationPlan.ResolveDwellMs(plan.IntervalSeconds, info);
        if (dwellMs <= 0)
        {
            _scheduler.Stop();
            return;
        }

        var token = _applyToken;
        _scheduler.Schedule(dwellMs, () => Advance(snapshot, token));
    }

    /// <summary>
    /// 立刻切到下一张，不等定时器到点。
    /// </summary>
    /// <remarks>
    /// 给设置页的「切换下一张」按钮用：轮播间隔以分钟计，
    /// 让用户干等一个完整间隔才能确认"轮播到底有没有在工作"是不合理的。
    /// 未启用轮播时什么都不做（语义与定时器一致：<c>IsRotating</c> 为假就不推进）。
    /// </remarks>
    public void AdvanceNow()
    {
        if (_disposed || _snapshot is not { } snapshot) return;
        Dispatcher.UIThread.Post(() => Advance(snapshot, _applyToken));
    }

    /// <summary>当前是否真的在轮播（≥2 个候选且间隔为正数）</summary>
    public bool IsRotating => _plan is { IsRotating: true };

    /// <summary>轮播定时器到点：切到下一项，并排定再下一次</summary>
    private void Advance(WallpaperSnapshot snapshot, int token)
    {
        if (_disposed || token != _applyToken) return;
        if (_plan is not { IsRotating: true } plan) return;

        var next = plan.NextIndex(_currentIndex);
        if (next < 0 || next >= _candidates.Count || next == _currentIndex) return;

        _currentIndex = next;
        ShowCurrent(snapshot);     // 换 Path → WallpaperHost 走交叉淡入
        ScheduleNext(snapshot);
    }

    /// <summary>
    /// 纯函数式的解析（后台线程执行，不做任何 UI 接触）：把条目列表过滤成候选集。
    /// </summary>
    /// <remarks>
    /// 这里**一次性探测全部条目**而不是"找到第一个能用的就返回"，有两个原因：
    /// 轮播要在启动时就确定候选集规模（否则随机模式可能挑到不可用项、表现为"同一张连出两次"），
    /// 拒收提示也需要一次给全（阶段 4 的 InfoBar 只弹一次）。
    /// 探测结果按「路径 + 大小 + 最后写入时间」在 <see cref="BackgroundResolver"/> 内缓存，
    /// 因此这次全量探测只在快照变化时付一次，之后每次轮播切换都是缓存命中。
    /// </remarks>
    private static ResolvedWallpaper Resolve(WallpaperSnapshot snapshot)
    {
        var rejections = new List<WallpaperRejection>();
        if (!snapshot.Enabled || snapshot.Items.Count == 0)
            return new ResolvedWallpaper([], rejections, null);

        var candidates = new List<WallpaperCandidate>(snapshot.Items.Count);

        foreach (var path in snapshot.Items)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            if (!BackgroundResolver.IsSupportedExtension(path))
            {
                rejections.Add(new WallpaperRejection(path, $"「{Path.GetFileName(path)}」不是支持的图片格式，已跳过。"));
                continue;
            }

            if (!File.Exists(path))
            {
                rejections.Add(new WallpaperRejection(path, $"「{Path.GetFileName(path)}」不存在或已被移动，已跳过。"));
                continue;
            }

            var info = BackgroundResolver.Probe(path);

            if (info.IsRejected)
            {
                // D7：明确拒绝 + 给出可操作提示，不静默退化成"一张不动的图"
                rejections.Add(new WallpaperRejection(path,
                    $"「{Path.GetFileName(path)}」是动画 PNG（APNG），当前版本不支持播放，已跳过。" +
                    "另存为 GIF 或动画 WebP 后即可添加。"));
                continue;
            }

            if (info.IsFailed)
            {
                rejections.Add(new WallpaperRejection(path, $"「{Path.GetFileName(path)}」无法读取：{info.Error}"));
                continue;
            }

            candidates.Add(new WallpaperCandidate(path, info));
        }

        return candidates.Count == 0
            ? new ResolvedWallpaper(candidates, rejections, "壁纸列表中没有可用的文件")
            : new ResolvedWallpaper(candidates, rejections, null);
    }

    // ────────────────────────────────────────────────────────────────
    // 导航栏让位 / 全局资源
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 壁纸生效时主内容区背景让位（透明），否则恢复主题底色；
    /// 左侧导航栏按 <c>WallpaperExtendToNav</c> / <c>NavBackgroundOpacity</c> 降不透明度。
    /// </summary>
    private void ApplyNavigationResources(WallpaperSnapshot? snapshot, bool active)
    {
        if (Application.Current?.Resources is not { } resources) return;

        var baseColor = ResolveNavBackgroundColor();
        resources["NavBackgroundBrush"] = active
            ? new SolidColorBrush(Colors.Transparent)
            : new SolidColorBrush(baseColor);

        var paneOpacity = active && snapshot?.ExtendToNav == true
            ? Math.Clamp(snapshot.NavBackgroundOpacity, 0, 1)
            : 1.0;

        // FA 展开态实际读取 ExpandedPaneBackground，DefaultPaneBackground 只覆盖左迷你/顶栏场景，两个键必须同步写
        resources["NavigationViewDefaultPaneBackground"] = new SolidColorBrush(baseColor) { Opacity = paneOpacity };
        resources["NavigationViewExpandedPaneBackground"] = new SolidColorBrush(baseColor) { Opacity = paneOpacity };
    }

    /// <summary>导航栏背景基准色（深/浅跟随主题）</summary>
    private static Color ResolveNavBackgroundColor()
        => Application.Current?.ActualThemeVariant == ThemeVariant.Light
            ? Color.Parse("#FFFFFF")
            : Color.Parse("#141619");

    // ────────────────────────────────────────────────────────────────
    // 省电策略
    // ────────────────────────────────────────────────────────────────

    private void OnPowerSourceChanged(object? sender, EventArgs e) => UpdateExternalPause();

    private void UpdateExternalPause()
    {
        // 电池暂停走用户设置（PauseOnBattery），远程桌面是 §6.5 的强制项，不看设置
        var pause = (_snapshot?.PauseOnBattery == true && _power.IsOnBattery) || _power.IsRemoteSession;
        if (pause == ExternalPause) return;

        ExternalPause = pause;
        DebugLogger.Info(LogService, pause
            ? _power.IsRemoteSession ? "远程桌面会话，动图已暂停" : "电池供电，动图已暂停"
            : "恢复本地/市电，动图继续播放");
        Raise(nameof(ExternalPause));

        // 轮播跟随同一个暂停开关：暂停时不再排定，恢复后按当前项重新排定
        if (pause) _scheduler.Stop();
        else if (_snapshot is { } snapshot) ScheduleNext(snapshot);
    }

    // ────────────────────────────────────────────────────────────────
    // 拒收提示
    // ────────────────────────────────────────────────────────────────

    private void PublishRejections(List<WallpaperRejection> rejections)
    {
        if (_rejections.SequenceEqual(rejections)) return;

        _rejections.Clear();
        _rejections.AddRange(rejections);

        foreach (var rejection in rejections) DebugLogger.Warn(LogService, rejection.Message);
        RejectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Raise(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _applyToken++;

        _scheduler.Dispose();
        CancelPendingAppearanceLog();
        _appearanceLogTimer = null;
        _lastLoggedRequest = null;

        _power.Changed -= OnPowerSourceChanged;
        _power.Dispose();

        Request = null;
        _candidates = [];
        _plan = null;
        _currentIndex = -1;
        _rejections.Clear();

        // 通知宿主清空：让呈现层（含后台解码线程）在窗口拆除前同步释放
        Raise(nameof(Request));
        Raise(nameof(IsActive));
    }

    private sealed record ResolvedWallpaper(List<WallpaperCandidate> Candidates, List<WallpaperRejection> Rejections, string? Error);

    /// <summary>一个通过扩展名粗筛与内容探测的候选条目（轮播就是在这些条目间推进）</summary>
    private sealed record WallpaperCandidate(string Path, AnimatedImageInfo Info);
}
