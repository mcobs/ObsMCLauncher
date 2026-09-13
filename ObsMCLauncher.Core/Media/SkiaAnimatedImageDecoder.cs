using System;
using System.IO;
using System.Threading;
using SkiaSharp;

namespace ObsMCLauncher.Core.Media;

/// <summary>
/// 基于 <see cref="SKCodec"/> 的动图解码器：GIF 与动画 WebP。
/// </summary>
/// <remarks>
/// <para>
/// **默认走 <c>PriorFrame</c> 增量路径**。实测（.temp/probe，1920×1080 GIF）：
/// 独立解码 10.23 ms/帧 → 增量 0.49 ms/帧，约 20–27× 加速，且 300/300 帧像素与独立解码完全一致。
/// 这批素材的脏矩形均值只有 22% 左右，增量解码真正吃到了这个红利。
/// </para>
/// <para>
/// 线程约定：**同一实例不得并发调用**。原生 codec 可跨线程使用但同一实例不能并发读写，
/// 因此本类用一个原子标志做运行时护栏，并发调用直接返回失败而不是让原生层崩掉。
/// 缓冲区协议：调用方提供一块**累积解码缓冲**，本解码器按 <c>priorFrameIndex</c> 原地打增量；
/// 呈现侧由调用方在写入完成后自行拷贝移交（见 <c>AnimatedImagePresenter</c> 的说明）。
/// </para>
/// </remarks>
public sealed class SkiaAnimatedImageDecoder : IAnimatedImageDecoder
{
    private readonly FileStream _stream;
    private readonly SKCodec _codec;
    private readonly SKImageInfo _pixelInfo;

    private int _inUse;
    private bool _disposed;

    private SkiaAnimatedImageDecoder(FileStream stream, SKCodec codec, SKImageInfo pixelInfo, AnimatedImageInfo info)
    {
        _stream = stream;
        _codec = codec;
        _pixelInfo = pixelInfo;
        Info = info;
    }

    /// <inheritdoc />
    public AnimatedImageInfo Info { get; }

    /// <inheritdoc />
    public int DecodeWidth => _pixelInfo.Width;

    /// <inheritdoc />
    public int DecodeHeight => _pixelInfo.Height;

    /// <inheritdoc />
    public int RowBytes => _pixelInfo.RowBytes;

    /// <summary>
    /// 打开文件并确定解码尺寸。
    /// </summary>
    /// <param name="path">本地文件路径</param>
    /// <param name="info">已探测的信息（提供源尺寸与帧数）</param>
    /// <param name="windowLongEdge">窗口物理长边，参与"只能向下夹紧"的解码尺寸决策；≤ 0 表示未知</param>
    /// <param name="maxDecodeEdge">配置的解码长边上限；≤ 0 表示不限制</param>
    /// <param name="error">失败原因</param>
    public static SkiaAnimatedImageDecoder? TryCreate(
        string path,
        AnimatedImageInfo info,
        int windowLongEdge,
        int maxDecodeEdge,
        out string? error)
    {
        error = null;
        FileStream? stream = null;
        SKCodec? codec = null;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
            codec = SKCodec.Create(stream, out var createResult);
            if (codec is null)
            {
                error = $"无法创建解码器（{createResult}）";
                return null;
            }

            var source = codec.Info;
            if (source.Width <= 0 || source.Height <= 0)
            {
                error = "源尺寸无效";
                return null;
            }

            var (width, height) = ResolveDecodeSize(codec, info, source, windowLongEdge, maxDecodeEdge);
            var pixelInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

            var decoder = new SkiaAnimatedImageDecoder(stream, codec, pixelInfo, info);
            stream = null;   // 所有权已移交
            codec = null;
            return decoder;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
        finally
        {
            codec?.Dispose();
            stream?.Dispose();
        }
    }

    /// <summary>
    /// 解码尺寸决策：<c>min(源长边, 窗口物理长边, 上限)</c>，然后交给 codec 做原生降采样。
    /// codec 明确拒绝或返回 0 时回落到源尺寸（不能假设一定拿到目标尺寸）。
    /// </summary>
    private static (int Width, int Height) ResolveDecodeSize(
        SKCodec codec,
        AnimatedImageInfo info,
        SKImageInfo source,
        int windowLongEdge,
        int maxDecodeEdge)
    {
        var decodeEdge = info.ClampDecodeLongEdge(windowLongEdge, maxDecodeEdge);
        var sourceLongEdge = Math.Max(source.Width, source.Height);
        if (decodeEdge <= 0 || decodeEdge >= sourceLongEdge)
            return (source.Width, source.Height);

        var scale = (float)decodeEdge / sourceLongEdge;
        try
        {
            // 实测：GIF / 动画 WebP 支持任意比例降采样（经网格指纹验证是真缩放）；
            // 升采样与 PNG 缩放会被忽略，这里只看返回尺寸是否可用。
            var scaled = codec.GetScaledDimensions(scale);
            if (scaled.Width > 0 && scaled.Height > 0 && scaled.Width <= source.Width && scaled.Height <= source.Height)
                return (scaled.Width, scaled.Height);
        }
        catch
        {
            // 原生层抛错（无对应缩放支持）时按源尺寸处理，由渲染期缩放兜底
        }

        return (source.Width, source.Height);
    }

    /// <inheritdoc />
    public AnimatedDecodeResult DecodeFrame(int index, IntPtr target, int rowBytes, int priorFrameIndex)
    {
        if (_disposed) return AnimatedDecodeResult.Fail("解码器已释放");
        if (target == IntPtr.Zero) return AnimatedDecodeResult.Fail("目标缓冲区为空");

        // 原生 codec 不保证并发安全：宁可这一帧失败，也不要原生层崩溃
        if (Interlocked.Exchange(ref _inUse, 1) == 1)
            return AnimatedDecodeResult.Fail("解码器被并发调用（同一实例必须绑定单一解码线程）");

        try
        {
            var frameCount = Math.Max(1, _codec.FrameCount);
            if (index < 0 || index >= frameCount)
                return AnimatedDecodeResult.Fail($"帧序号越界：{index}（共 {frameCount} 帧）");

            var options = new SKCodecOptions(index, priorFrameIndex);
            var result = _codec.GetPixels(_pixelInfo, target, rowBytes, options);
            return result == SKCodecResult.Success
                ? AnimatedDecodeResult.Ok
                : AnimatedDecodeResult.Fail(result.ToString());
        }
        catch (Exception ex)
        {
            return AnimatedDecodeResult.Fail(ex.Message);
        }
        finally
        {
            Volatile.Write(ref _inUse, 0);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _codec.Dispose(); } catch { }
        try { _stream.Dispose(); } catch { }
    }
}
