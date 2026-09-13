using System;
using System.Text.Json.Serialization;

namespace ObsMCLauncher.Core.Models;

/// <summary>
/// 一条背景壁纸。列表顺序即显示 / 轮播顺序。
/// 只有 <see cref="Path"/> 参与持久化——尺寸、帧数、类型都按文件内容实时探测，不落盘。
/// </summary>
public class WallpaperItem
{
    /// <summary>本地文件绝对路径</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// 会话内唯一标识，用于 UI 选中态与拖拽排序的稳定引用。
    /// 刻意不持久化：重启后 Id 变化不影响任何已保存语义，也避免配置文件里堆一堆与文件无关的随机串。
    /// </summary>
    [JsonIgnore]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
}

/// <summary>
/// 背景壁纸的实际类型。按**文件内容**探测，不持久化（每次按文件指纹重新解析）。
/// </summary>
/// <remarks>
/// 不能靠扩展名判断：APNG 的扩展名就是 <c>.png</c>，动画 WebP 与静态 WebP 同为 <c>.webp</c>。
/// </remarks>
public enum WallpaperKind
{
    /// <summary>尚未探测，或探测失败</summary>
    Unknown,

    /// <summary>单帧静态图</summary>
    Static,

    /// <summary>GIF 动图</summary>
    Gif,

    /// <summary>动画 WebP</summary>
    AnimatedWebP,

    /// <summary>
    /// 识别出是 APNG（动画 PNG），但当前版本不播放。
    /// SkiaSharp 底层不支持 APNG（见 DYNAMIC_BACKGROUND_DESIGN.md §3.4 P0-1），
    /// 该值只用于在添加时给出明确提示，不参与解码。
    /// </summary>
    ApngUnsupported
}
