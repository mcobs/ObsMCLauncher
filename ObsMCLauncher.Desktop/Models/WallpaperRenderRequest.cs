using System;
using System.Collections.Generic;
using Avalonia.Media;
using ObsMCLauncher.Core.Media;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.Models;

/// <summary>
/// 一次"要显示什么"的完整描述（不可变值对象）。
/// </summary>
/// <remarks>
/// 渲染宿主只认这个对象，不认识 <see cref="LauncherConfig"/>：
/// 这样"读配置"与"画壁纸"彻底分开，轮播（阶段 3）只是不断换一个新 request。
/// </remarks>
/// <param name="Path">本地文件绝对路径</param>
/// <param name="Info">探测结果（类型 / 尺寸 / 帧数 / 帧时长）</param>
/// <param name="Stretch">拉伸方式</param>
/// <param name="Opacity">壁纸整体不透明度（0–1）</param>
/// <param name="BlurRadius">模糊半径（DIP，0 = 不模糊）</param>
/// <param name="PlayAnimated">是否播放动画；<c>false</c> 时只显示首帧</param>
/// <param name="MaxFps">帧率上限</param>
/// <param name="MaxDecodeEdge">解码长边上限（0 = 不限制）</param>
/// <param name="TransitionMs">交叉淡入时长（毫秒）</param>
public sealed record WallpaperRenderRequest(
    string Path,
    AnimatedImageInfo Info,
    Stretch Stretch,
    double Opacity,
    double BlurRadius,
    bool PlayAnimated,
    int MaxFps,
    int MaxDecodeEdge,
    int TransitionMs)
{
    /// <summary>需要走动图渲染路径（否则交给静态 Image）</summary>
    public bool IsAnimated => Info.IsPlayable;

    /// <summary>
    /// 两份请求是否指向"同一段内容"（同文件 + 同样的解码与播放参数）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 刻意不比较 <see cref="Opacity"/>、<see cref="BlurRadius"/> 与 <c>TransitionMs</c>：
    /// 这三个是纯外观参数，改了不需要重建解码会话（渲染宿主据此走"只更新外观"的快路径），
    /// 日志也可以据此把它们合并——拖滑块每格推一次快照，逐条写会把调试输出刷爆。
    /// </para>
    /// <para>
    /// <see cref="Info"/> 是普通类（引用相等），但它由 <c>BackgroundResolver</c>
    /// 按 {路径, 大小, 最后写入} 缓存，同一文件在同一时刻只会有一个实例，
    /// 所以引用相等恰好就是"文件内容没换"的判据。
    /// </para>
    /// </remarks>
    public bool IsSameContentAs(WallpaperRenderRequest other)
        => string.Equals(Path, other.Path, StringComparison.Ordinal)
           && IsAnimated == other.IsAnimated
           && PlayAnimated == other.PlayAnimated
           && Stretch == other.Stretch
           && MaxFps == other.MaxFps
           && MaxDecodeEdge == other.MaxDecodeEdge
           && ReferenceEquals(Info, other.Info);
}

/// <summary>
/// 从配置里抽出与壁纸渲染相关的全部字段。
/// </summary>
/// <remarks>
/// 刻意做成快照而不是共享 <see cref="LauncherConfig"/> 实例：
/// 设置页持有自己的配置副本并在每次改动后推一次快照，
/// 服务端不需要读盘、也不会读到"尚未保存"的中间状态。
/// </remarks>
/// <param name="Enabled">总开关</param>
/// <param name="Items">条目列表（顺序即轮播顺序）</param>
/// <param name="Opacity">不透明度</param>
/// <param name="BlurRadius">模糊半径（DIP，0 = 不模糊）</param>
/// <param name="Stretch">显示方式（0=Fill 1=Uniform 2=UniformToFill 3=None）</param>
/// <param name="ExtendToNav">是否扩展到导航栏</param>
/// <param name="NavBackgroundOpacity">导航栏背景透明度</param>
/// <param name="PlayAnimated">是否播放动图</param>
/// <param name="MaxFps">帧率上限</param>
/// <param name="MaxDecodeEdge">解码长边上限</param>
/// <param name="TransitionMs">切换过渡时长</param>
/// <param name="PauseOnUnfocused">失焦时暂停</param>
/// <param name="PauseOnBattery">电池供电时暂停</param>
/// <param name="AnimationLevel">全局动画级别（0 = 禁用，壁纸只显示首帧）</param>
/// <param name="SlideIntervalSeconds">轮播间隔秒数（0 = 关闭，-1 = 每次启动）</param>
/// <param name="SlideMode">轮播顺序（0 = 顺序，1 = 随机，2 = 随机起点）</param>
public sealed record WallpaperSnapshot(
    bool Enabled,
    IReadOnlyList<string> Items,
    double Opacity,
    double BlurRadius,
    int Stretch,
    bool ExtendToNav,
    double NavBackgroundOpacity,
    bool PlayAnimated,
    int MaxFps,
    int MaxDecodeEdge,
    int TransitionMs,
    bool PauseOnUnfocused,
    bool PauseOnBattery,
    int AnimationLevel,
    int SlideIntervalSeconds,
    int SlideMode)
{
    /// <summary>
    /// 模糊半径上限（DIP）。设置页滑块、配置夹紧与渲染层共用同一个数——
    /// 三处各写一遍必然会出现"滑块能拖到 80、渲染层却按 60 截断"这类对不上的现象。
    /// </summary>
    public const double MaxBlurRadius = 60;

    /// <summary>夹紧后的模糊半径；<c>0</c> 表示不需要模糊（渲染层据此完全跳过离屏合成）</summary>
    public double EffectiveBlurRadius => Math.Clamp(BlurRadius, 0, MaxBlurRadius);

    /// <summary>从配置对象取快照</summary>
    public static WallpaperSnapshot From(LauncherConfig config)
    {
        var items = new List<string>(config.WallpaperItems.Count);
        foreach (var item in config.WallpaperItems) items.Add(item.Path);

        return new WallpaperSnapshot(
            config.IsWallpaperActive,
            items,
            config.WallpaperOpacity,
            config.WallpaperBlurRadius,
            config.WallpaperStretch,
            config.WallpaperExtendToNav,
            config.NavBackgroundOpacity,
            config.WallpaperPlayAnimated,
            config.WallpaperMaxFps,
            config.WallpaperMaxDecodeEdge,
            config.WallpaperTransitionMs,
            config.WallpaperPauseOnUnfocused,
            config.WallpaperPauseOnBattery,
            config.AnimationLevel,
            config.WallpaperSlideIntervalSeconds,
            config.WallpaperSlideMode);
    }

    /// <summary>
    /// 是否真正播放动画：开关打开且全局动画未被禁用（<c>AnimationLevel == 0</c> 强制只显示首帧）。
    /// </summary>
    public bool ShouldPlayAnimated => PlayAnimated && AnimationLevel != 0;

    /// <summary>
    /// 显示方式 → Avalonia 拉伸枚举。
    /// 注意：本记录类型的主构造参数就叫 <c>Stretch</c>（int），会把 <c>Stretch</c> 这个类型名遮住，
    /// 因此这里必须写全名 <c>Avalonia.Media.Stretch</c>。
    /// </summary>
    public static Stretch ToStretch(int mode) => mode switch
    {
        0 => Avalonia.Media.Stretch.Fill,
        2 => Avalonia.Media.Stretch.UniformToFill,
        3 => Avalonia.Media.Stretch.None,
        _ => Avalonia.Media.Stretch.Uniform
    };
}

/// <summary>被拒收的条目（当前只有 APNG），阶段 4 的设置页据此弹 InfoBar</summary>
/// <param name="Path">文件路径</param>
/// <param name="Message">可操作的提示文案</param>
public sealed record WallpaperRejection(string Path, string Message)
{
    /// <summary>文件名（提示文案用）</summary>
    public string FileName => System.IO.Path.GetFileName(Path);
}
