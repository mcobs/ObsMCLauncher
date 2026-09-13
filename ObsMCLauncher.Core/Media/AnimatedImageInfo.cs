using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Media;

/// <summary>
/// 一张背景壁纸的探测结果（由 <see cref="BackgroundResolver"/> 产出）。
/// </summary>
/// <remarks>
/// 刻意不持久化：<c>Kind</c>、尺寸、帧数都按文件内容实时探测。
/// APNG 的扩展名就是 <c>.png</c>，动画 WebP 与静态 WebP 同为 <c>.webp</c>，
/// 落盘一份可能过期的类型只会在文件被替换后产生不一致。
/// </remarks>
public sealed class AnimatedImageInfo
{
    /// <summary>按文件内容判定的类型</summary>
    public WallpaperKind Kind { get; init; } = WallpaperKind.Unknown;

    /// <summary>源图宽度（像素）。探测失败时为 0</summary>
    public int Width { get; init; }

    /// <summary>源图高度（像素）。探测失败时为 0</summary>
    public int Height { get; init; }

    /// <summary>帧数。静态图为 1；识别到但不可播放的 APNG 为 <c>acTL</c> 声明的帧数</summary>
    public int FrameCount { get; init; }

    /// <summary>循环次数。<c>-1</c> 表示无限循环（GIF 的 Netscape 语义：额外重复次数）</summary>
    public int LoopCount { get; init; }

    /// <summary>逐帧展示时长（毫秒），顺序与帧序号一致。静态图为空</summary>
    public IReadOnlyList<int> FrameDurationsMs { get; init; } = Array.Empty<int>();

    /// <summary>文件字节数（探测时的快照）</summary>
    public long FileSize { get; init; }

    /// <summary>探测失败时的原因；成功时为 <c>null</c></summary>
    public string? Error { get; init; }

    /// <summary>文件内容确实是多帧可播放动图</summary>
    public bool IsAnimated => FrameCount > 1 && Kind is WallpaperKind.Gif or WallpaperKind.AnimatedWebP;

    /// <summary>可以交给解码器播放</summary>
    public bool IsPlayable => IsAnimated;

    /// <summary>识别到但当前版本不播放（APNG，见决策 D7）。调用方应据此拒收并给出提示</summary>
    public bool IsRejected => Kind == WallpaperKind.ApngUnsupported;

    /// <summary>探测失败</summary>
    public bool IsFailed => Kind == WallpaperKind.Unknown;

    /// <summary>源图长边</summary>
    public int LongEdge => Math.Max(Width, Height);

    /// <summary>一轮播放的总时长（毫秒）。时长缺失时按 100ms/帧兜底</summary>
    public double TotalDurationMs
    {
        get
        {
            if (FrameCount <= 1) return 0;
            double sum = 0;
            for (var i = 0; i < FrameCount; i++) sum += GetFrameDurationMs(i);
            return sum;
        }
    }

    /// <summary>取第 <paramref name="index"/> 帧的展示时长（毫秒）</summary>
    public int GetFrameDurationMs(int index)
    {
        if (index < 0) return 100;
        if (FrameDurationsMs.Count == 0) return 100;
        if (index >= FrameDurationsMs.Count) return FrameDurationsMs[^1] <= 0 ? 100 : FrameDurationsMs[^1];
        var d = FrameDurationsMs[index];
        return d <= 0 ? 100 : d;
    }

    /// <summary>
    /// 计算实际解码尺寸（长边）。这是本方案唯一的"向下夹紧"入口：
    /// <c>min(源长边, 窗口物理长边, 配置上限)</c>。
    /// </summary>
    /// <remarks>
    /// 实测 <c>SKCodec.GetScaledDimensions</c> 只支持降采样（升采样返回 <c>InvalidScale</c>，PNG 完全忽略缩放），
    /// 因此结果必然 ≤ 源长边——小图不会被放大解码，交给渲染期缩放（GPU 采样，本来就免费）。
    /// </remarks>
    /// <param name="windowLongEdge">窗口物理长边（物理像素，已含 RenderScaling）；≤ 0 表示未知，不参与夹紧</param>
    /// <param name="maxDecodeEdge">配置的解码长边上限；≤ 0 表示不限制</param>
    public int ClampDecodeLongEdge(int windowLongEdge, int maxDecodeEdge)
    {
        var target = LongEdge;
        if (target <= 0) return 0;
        if (windowLongEdge > 0) target = Math.Min(target, windowLongEdge);
        if (maxDecodeEdge > 0) target = Math.Min(target, maxDecodeEdge);
        return Math.Max(1, target);
    }

    /// <summary>按解码长边换算等比尺寸</summary>
    public (int Width, int Height) ScaleTo(int decodeLongEdge)
    {
        if (Width <= 0 || Height <= 0) return (0, 0);
        if (decodeLongEdge <= 0 || decodeLongEdge >= LongEdge) return (Width, Height);
        var scale = (double)decodeLongEdge / LongEdge;
        return (Math.Max(1, (int)Math.Round(Width * scale)), Math.Max(1, (int)Math.Round(Height * scale)));
    }

    /// <summary>一行元信息（设置页卡片徽标 / 日志），例：<c>GIF · 1920×1080 · 24 帧</c></summary>
    public string Describe()
    {
        var label = Kind switch
        {
            WallpaperKind.Gif => "GIF",
            WallpaperKind.AnimatedWebP => "WEBP",
            WallpaperKind.ApngUnsupported => "APNG",
            WallpaperKind.Static => "图片",
            _ => "未知"
        };
        if (Width <= 0 || Height <= 0) return label;
        return IsPlayable || IsRejected
            ? $"{label} · {Width}×{Height} · {FrameCount} 帧"
            : $"{label} · {Width}×{Height}";
    }

    /// <summary>探测失败</summary>
    public static AnimatedImageInfo Failed(string error, long fileSize = 0)
        => new() { Kind = WallpaperKind.Unknown, Error = error, FileSize = fileSize };

    /// <summary>单帧静态图</summary>
    public static AnimatedImageInfo Static(int width, int height, long fileSize)
        => new() { Kind = WallpaperKind.Static, Width = width, Height = height, FrameCount = 1, FileSize = fileSize };

    /// <summary>识别到 APNG（决策 D7：只识别、不播放）</summary>
    public static AnimatedImageInfo ApngUnsupported(int frameCount, long fileSize)
        => new()
        {
            Kind = WallpaperKind.ApngUnsupported,
            FrameCount = frameCount,
            LoopCount = -1,
            FileSize = fileSize
        };
}
