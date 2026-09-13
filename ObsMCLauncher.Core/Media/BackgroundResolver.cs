using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ObsMCLauncher.Core.Models;
using SkiaSharp;

namespace ObsMCLauncher.Core.Media;

/// <summary>
/// 壁纸格式探测：容器嗅探（魔数）+ APNG 的 <c>acTL</c> 扫描 + <see cref="SKCodec"/> 逐帧信息。
/// </summary>
/// <remarks>
/// <para>
/// **为什么不能靠扩展名**：APNG 的扩展名就是 <c>.png</c>，动画 WebP 与静态 WebP 同为 <c>.webp</c>，
/// 必须按内容探测，结果只缓存在内存里（按 <c>路径 + 大小 + 最后写入时间</c> 三元组指纹校验）。
/// </para>
/// <para>
/// **为什么不能靠 <c>SKCodec.FrameCount</c> 判 APNG**：SkiaSharp 3.116.1 走的是 libpng 版
/// <c>SkPngCodec</c>（官方文档明确 "No APNG support"），对合法 APNG 返回 <c>FrameCount = 0</c>——
/// 用它判定会把 APNG 误判成静态图，用户拖进来一张不动的图还不知道为什么。所以 PNG 家族必须自己扫块。
/// </para>
/// </remarks>
public static class BackgroundResolver
{
    /// <summary>可选文件扩展名白名单（本地文件路径校验沿用此表）</summary>
    public static readonly string[] SupportedExtensions =
        [".png", ".apng", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"];

    /// <summary>源图长边上限（解码炸弹闸门之一）</summary>
    public const int MaxSourceLongEdge = 8192;

    /// <summary>帧数上限（解码炸弹闸门之一）</summary>
    public const int MaxFrameCount = 2000;

    /// <summary>单帧预估内存上限（解码炸弹闸门之一）</summary>
    public const long MaxEstimatedFrameBytes = 256L * 1024 * 1024;

    /// <summary>PNG 块扫描的迭代上限，防畸形文件构造死循环</summary>
    private const int MaxChunkIterations = 4096;

    /// <summary>探测结果缓存条目上限</summary>
    private const int MaxCachedProbes = 256;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct CacheEntry(long Size, long LastWriteTicks, AnimatedImageInfo Info);

    /// <summary>扩展名是否在白名单内（不区分大小写）</summary>
    public static bool IsSupportedExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        return SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 探测一个文件，**不抛异常**：任何异常都降级为 <see cref="AnimatedImageInfo.Failed"/>。
    /// </summary>
    public static AnimatedImageInfo Probe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return AnimatedImageInfo.Failed("路径为空");

        FileInfo file;
        try
        {
            file = new FileInfo(path);
            if (!file.Exists) return AnimatedImageInfo.Failed("文件不存在");
            if (file.Length <= 0) return AnimatedImageInfo.Failed("文件为空");
        }
        catch (Exception ex)
        {
            return AnimatedImageInfo.Failed(ex.Message);
        }

        var key = SafeFullPath(path);
        var fingerprint = (file.Length, file.LastWriteTimeUtc.Ticks);

        lock (CacheLock)
        {
            if (Cache.TryGetValue(key, out var hit) && hit.Size == fingerprint.Item1 && hit.LastWriteTicks == fingerprint.Item2)
                return hit.Info;
        }

        var info = ProbeCore(path, file.Length);

        lock (CacheLock)
        {
            if (Cache.Count >= MaxCachedProbes) Cache.Clear();
            Cache[key] = new CacheEntry(fingerprint.Item1, fingerprint.Item2, info);
        }

        return info;
    }

    /// <summary>清空探测缓存（文件被外部替换、或需要强制重新探测时调用）</summary>
    public static void InvalidateCache()
    {
        lock (CacheLock) Cache.Clear();
    }

    /// <summary>使单个路径的缓存失效</summary>
    public static void InvalidateCache(string path)
    {
        lock (CacheLock) Cache.Remove(SafeFullPath(path));
    }

    private static AnimatedImageInfo ProbeCore(string path, long fileSize)
    {
        try
        {
            var container = SniffContainer(path);

            if (container == Container.Png)
            {
                // 只有 PNG 家族才需要扫块：命中 acTL 就是 APNG（D7：识别 + 拒收，不播放）
                var apngFrames = TryReadApngFrameCount(path);
                if (apngFrames is > 0)
                    return AnimatedImageInfo.ApngUnsupported(apngFrames.Value, fileSize);

                return ProbeWithCodec(path, WallpaperKind.Static, fileSize);
            }

            var expected = container switch
            {
                Container.Gif => WallpaperKind.Gif,
                Container.Webp => WallpaperKind.AnimatedWebP,
                _ => WallpaperKind.Static
            };
            return ProbeWithCodec(path, expected, fileSize);
        }
        catch (Exception ex)
        {
            return AnimatedImageInfo.Failed(ex.Message, fileSize);
        }
    }

    private static AnimatedImageInfo ProbeWithCodec(string path, WallpaperKind expected, long fileSize)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        using var codec = SKCodec.Create(stream, out var createResult);
        if (codec is null)
            return AnimatedImageInfo.Failed($"无法解析图像（{createResult}）", fileSize);

        var source = codec.Info;
        if (source.Width <= 0 || source.Height <= 0)
            return AnimatedImageInfo.Failed("图像尺寸无效", fileSize);

        var longEdge = Math.Max(source.Width, source.Height);
        if (longEdge > MaxSourceLongEdge)
            return AnimatedImageInfo.Failed($"源图长边 {longEdge} 超过上限 {MaxSourceLongEdge}", fileSize);

        // FrameCount 对非动画图返回 0（静态 PNG / 静态 WebP 都是），所以统一按 max(1, n) 处理
        var frameCount = Math.Max(1, codec.FrameCount);

        if (frameCount > MaxFrameCount)
            return AnimatedImageInfo.Failed($"帧数 {frameCount} 超过上限 {MaxFrameCount}", fileSize);

        if (frameCount > 1)
        {
            var estimated = (long)source.Width * source.Height * 4;
            if (estimated > MaxEstimatedFrameBytes)
                return AnimatedImageInfo.Failed(
                    $"单帧预估占用 {estimated / 1024 / 1024} MB 超过上限 {MaxEstimatedFrameBytes / 1024 / 1024} MB", fileSize);
        }

        var durations = Array.Empty<int>();
        if (frameCount > 1)
        {
            var frameInfo = codec.FrameInfo;
            if (frameInfo is { Length: > 0 })
            {
                durations = new int[frameCount];
                for (var i = 0; i < frameCount; i++)
                    durations[i] = i < frameInfo.Length ? frameInfo[i].Duration : 0;
            }
        }

        return new AnimatedImageInfo
        {
            Kind = frameCount > 1 ? expected : WallpaperKind.Static,
            Width = source.Width,
            Height = source.Height,
            FrameCount = frameCount,
            LoopCount = frameCount > 1 ? codec.RepetitionCount : 0,
            FrameDurationsMs = durations,
            FileSize = fileSize
        };
    }

    // ────────────────────────────────────────────────────────────────
    // 容器嗅探与 APNG 扫块
    // ────────────────────────────────────────────────────────────────

    private enum Container { Unknown, Png, Gif, Webp }

    private static Container SniffContainer(string path)
    {
        Span<byte> head = stackalloc byte[12];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16, FileOptions.SequentialScan);
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        if (read < 8) return Container.Unknown;

        if (head[..8].SequenceEqual(PngSignature)) return Container.Png;
        if (head[0] == 'G' && head[1] == 'I' && head[2] == 'F' && head[3] == '8') return Container.Gif;
        if (read >= 12
            && head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F'
            && head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P')
        {
            return Container.Webp;
        }

        return Container.Unknown;
    }

    /// <summary>
    /// 扫 PNG 块结构找 <c>acTL</c>，返回声明的帧数。
    /// </summary>
    /// <returns>
    /// 命中 <c>acTL</c> 返回其 <c>num_frames</c>；确定不是 APNG 或结构不可解析返回 <c>null</c>。
    /// **只读块头，不碰帧数据**——畸形文件最多让判定失效（按静态图处理），不会越界或死循环。
    /// </returns>
    internal static int? TryReadApngFrameCount(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            Span<byte> signature = stackalloc byte[8];
            if (stream.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false) != signature.Length)
                return null;
            if (!signature.SequenceEqual(PngSignature)) return null;

            Span<byte> header = stackalloc byte[8];
            var remaining = stream.Length - signature.Length;
            var iterations = 0;

            while (remaining >= header.Length && iterations++ < MaxChunkIterations)
            {
                if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length)
                    return null;
                remaining -= header.Length;

                var length = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
                var type = header[4..8];

                // 块长度必须能在剩余字节里放得下（含 4 字节 CRC），否则判定为畸形
                if (length > int.MaxValue || length + 4 > remaining)
                    return null;

                if (type.SequenceEqual("acTL"u8))
                {
                    if (length < 8) return null;
                    Span<byte> acTl = stackalloc byte[8];
                    if (stream.ReadAtLeast(acTl, acTl.Length, throwOnEndOfStream: false) != acTl.Length)
                        return null;
                    var numFrames = BinaryPrimitives.ReadUInt32BigEndian(acTl[..4]);
                    // num_frames = 0 结构上非法：按"不是 APNG"处理，不给调用方一个 0 帧的动图
                    if (numFrames == 0 || numFrames > int.MaxValue) return null;
                    return (int)numFrames;
                }

                // IEND 之后不会再有 acTL，直接收尾
                if (type.SequenceEqual("IEND"u8)) return null;

                var skip = (long)length + 4;   // 数据 + CRC
                stream.Seek(skip, SeekOrigin.Current);
                remaining -= skip;
            }

            return null;
        }
        catch
        {
            // 任何解析异常都降级为"不是 APNG"，交由静态图路径处理
            return null;
        }
    }

    private static string SafeFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }
}
