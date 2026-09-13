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

    private readonly PowerStatusMonitor _power = new();
    private readonly List<WallpaperRejection> _rejections = new();

    private WallpaperSnapshot? _snapshot;
    private int _applyToken;
    private bool _disposed;

    public WallpaperService()
    {
        _power.Changed += OnPowerSourceChanged;
        _power.Start();
    }

    /// <summary>当前要显示的壁纸；<c>null</c> 表示没有可显示的内容</summary>
    public WallpaperRenderRequest? Request { get; private set; }

    /// <summary>外部暂停（目前只有"电池供电"；远程桌面与游戏进程见设计文档 §6.5，属阶段 5）</summary>
    public bool ExternalPause { get; private set; }

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
            PublishResolved(resolved);
        });
    }

    /// <summary>把解析结果落到属性与全局资源上（UI 线程）</summary>
    private void PublishResolved(ResolvedWallpaper resolved)
    {
        PublishRejections(resolved.Rejections);

        if (resolved.Request is null)
        {
            Request = null;
            LastError = resolved.Error;
            ApplyNavigationResources(active: false);
            Raise(nameof(Request));
            Raise(nameof(IsActive));
            if (resolved.Error is not null) DebugLogger.Warn(LogService, resolved.Error);
            return;
        }

        Request = resolved.Request;
        LastError = null;
        ApplyNavigationResources(active: true);
        Raise(nameof(Request));
        Raise(nameof(IsActive));

        DebugLogger.Info(LogService,
            $"壁纸已应用：{Path.GetFileName(resolved.Request.Path)}（{resolved.Request.Info.Describe()}，" +
            $"动画={(resolved.Request.IsAnimated ? "开" : "关")}）");
    }

    /// <summary>纯函数式的解析（后台线程执行，不做任何 UI 接触）</summary>
    private static ResolvedWallpaper Resolve(WallpaperSnapshot snapshot)
    {
        var rejections = new List<WallpaperRejection>();
        if (!snapshot.Enabled || snapshot.Items.Count == 0)
            return new ResolvedWallpaper(null, rejections, null);

        // 阶段 3 会把"取首个可用项"换成按 WallpaperSlide* 参数的轮播调度
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

            var request = new WallpaperRenderRequest(
                path,
                info,
                WallpaperSnapshot.ToStretch(snapshot.Stretch),
                Math.Clamp(snapshot.Opacity, 0, 1),
                snapshot.ShouldPlayAnimated,
                Math.Clamp(snapshot.MaxFps, 1, 60),
                snapshot.MaxDecodeEdge,
                Math.Clamp(snapshot.TransitionMs, 0, 2000));

            return new ResolvedWallpaper(request, rejections, null);
        }

        return new ResolvedWallpaper(null, rejections, "壁纸列表中没有可用的文件");
    }

    // ────────────────────────────────────────────────────────────────
    // 导航栏让位 / 全局资源
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 壁纸生效时主内容区背景让位（透明），否则恢复主题底色；
    /// 左侧导航栏按 <c>WallpaperExtendToNav</c> / <c>NavBackgroundOpacity</c> 降不透明度。
    /// </summary>
    private void ApplyNavigationResources(bool active)
    {
        if (Application.Current?.Resources is not { } resources) return;

        var baseColor = ResolveNavBackgroundColor();
        resources["NavBackgroundBrush"] = active
            ? new SolidColorBrush(Colors.Transparent)
            : new SolidColorBrush(baseColor);

        var paneOpacity = active && _snapshot?.ExtendToNav == true
            ? Math.Clamp(_snapshot.NavBackgroundOpacity, 0, 1)
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
        var pause = _snapshot?.PauseOnBattery == true && _power.IsOnBattery;
        if (pause == ExternalPause) return;

        ExternalPause = pause;
        DebugLogger.Info(LogService, pause ? "电池供电，动图已暂停" : "恢复市电，动图继续播放");
        Raise(nameof(ExternalPause));
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

        _power.Changed -= OnPowerSourceChanged;
        _power.Dispose();

        Request = null;
        _rejections.Clear();

        // 通知宿主清空：让呈现层（含后台解码线程）在窗口拆除前同步释放
        Raise(nameof(Request));
        Raise(nameof(IsActive));
    }

    private sealed record ResolvedWallpaper(WallpaperRenderRequest? Request, List<WallpaperRejection> Rejections, string? Error);
}
