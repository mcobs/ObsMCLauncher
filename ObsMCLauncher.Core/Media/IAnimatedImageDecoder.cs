using System;

namespace ObsMCLauncher.Core.Media;

/// <summary>单帧解码的结果码</summary>
public enum AnimatedDecodeStatus
{
    /// <summary>解码成功，目标缓冲区已是该帧的完整像素</summary>
    Success,

    /// <summary>解码失败（文件损坏、帧序号越界、原生库报错等）</summary>
    Failed,

    /// <summary>该帧无法增量解码，调用方应回退到独立解码</summary>
    FallbackRequired
}

/// <summary>单帧解码结果</summary>
public readonly struct AnimatedDecodeResult
{
    public AnimatedDecodeStatus Status { get; init; }

    /// <summary>失败原因（成功时为 <c>null</c>）</summary>
    public string? Error { get; init; }

    public bool IsSuccess => Status == AnimatedDecodeStatus.Success;

    public static AnimatedDecodeResult Ok { get; } = new() { Status = AnimatedDecodeStatus.Success };

    public static AnimatedDecodeResult Fail(string error) => new() { Status = AnimatedDecodeStatus.Failed, Error = error };

    public static AnimatedDecodeResult NeedFallback(string reason)
        => new() { Status = AnimatedDecodeStatus.FallbackRequired, Error = reason };
}

/// <summary>
/// 动图解码接缝。当前只有 Skia 一个实现（D7 之后 APNG 不参与解码）。
/// </summary>
/// <remarks>
/// 保留这个接口的唯一理由是**单测**：帧调度、背压、暂停策略这些逻辑可以在不加载
/// SkiaSharp 原生库的前提下用假解码器测掉；上层（渲染宿主、轮播调度）也因此不感知格式差异。
/// </remarks>
public interface IAnimatedImageDecoder : IDisposable
{
    /// <summary>探测结果</summary>
    AnimatedImageInfo Info { get; }

    /// <summary>解码输出的宽度（像素），等于调用方分配的缓冲区宽度</summary>
    int DecodeWidth { get; }

    /// <summary>解码输出的高度（像素），等于调用方分配的缓冲区高度</summary>
    int DecodeHeight { get; }

    /// <summary>解码输出的行距（字节）。调用方按此分配缓冲区</summary>
    int RowBytes { get; }

    /// <summary>
    /// 把第 <paramref name="index"/> 帧解码到 <paramref name="target"/> 指向的缓冲区（**原地**增量）。
    /// </summary>
    /// <param name="index">目标帧序号</param>
    /// <param name="target">目标缓冲区首地址，大小必须 ≥ <see cref="RowBytes"/> × <see cref="DecodeHeight"/></param>
    /// <param name="rowBytes">目标缓冲区行距</param>
    /// <param name="priorFrameIndex">
    /// 目标缓冲区里**当前装着**的已合成帧序号；<c>-1</c> 表示内容未定义。
    /// 语义来自 Skia：<c>fPriorFrame</c> = "pixmap contains the first frame before getPixels call"，
    /// 因此跳帧后必须传"上次成功解码的帧序号"，而不是 <c>index - 1</c>。
    /// </param>
    AnimatedDecodeResult DecodeFrame(int index, IntPtr target, int rowBytes, int priorFrameIndex);
}
