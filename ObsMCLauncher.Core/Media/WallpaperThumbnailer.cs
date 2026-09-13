using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Utils;
using SkiaSharp;

namespace ObsMCLauncher.Core.Media;

/// <summary>
/// 首帧缩略图生成 + 磁盘缓存（设置页卡片网格用，阶段 4 的消费方）。
/// </summary>
/// <remarks>
/// <para>
/// **动图只解首帧**：卡片上不播放（省 CPU），因此这里固定用 <c>SKCodecOptions(0, -1)</c> 独立解码。
/// </para>
/// <para>
/// 缓存键是 <c>路径 + 文件大小 + 最后写入时间 + 目标长边</c> 的 SHA1：
/// 文件被替换后键自然变化，不需要额外的失效逻辑；不同尺寸的缩略图互不覆盖。
/// </para>
/// </remarks>
public sealed class WallpaperThumbnailer
{
    /// <summary>缓存目录内文件数上限，超出后按最后访问时间淘汰最旧的一批</summary>
    private const int MaxCacheFiles = 400;

    /// <summary>并发解码上限：避免 20 张 4K 图同时解缩略图把页面拖卡</summary>
    private static readonly SemaphoreSlim DecodeGate = new(2, 2);

    private readonly string _cacheDirectory;

    /// <param name="cacheDirectory">
    /// 缓存目录；为空时落到 <c>{应用目录}/OMCL/cache/wallpaper-thumbs</c>
    /// </param>
    public WallpaperThumbnailer(string? cacheDirectory = null)
    {
        _cacheDirectory = string.IsNullOrWhiteSpace(cacheDirectory)
            ? Path.Combine(VersionInfo.GetAppBaseDirectory(), "OMCL", "cache", "wallpaper-thumbs")
            : cacheDirectory;
    }

    /// <summary>缓存目录（首次写入时惰性创建）</summary>
    public string CacheDirectory => _cacheDirectory;

    /// <summary>
    /// 取缩略图 PNG 字节；命中磁盘缓存直接返回，否则后台解码生成。
    /// </summary>
    /// <param name="path">壁纸文件路径</param>
    /// <param name="longEdge">缩略图长边（像素）</param>
    /// <returns>PNG 字节；文件不可用或解码失败返回 <c>null</c>，不抛异常</returns>
    public async Task<byte[]?> GetThumbnailAsync(string path, int longEdge = 300, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || longEdge <= 0) return null;

        string cachePath;
        try
        {
            if (!File.Exists(path)) return null;
            cachePath = Path.Combine(_cacheDirectory, BuildCacheFileName(path, longEdge));
            if (File.Exists(cachePath))
            {
                TouchCacheFile(cachePath);
                return await File.ReadAllBytesAsync(cachePath, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            cachePath = string.Empty;
        }

        await DecodeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bytes = await Task.Run(() => RenderThumbnailPng(path, longEdge), cancellationToken).ConfigureAwait(false);
            if (bytes is not null && cachePath.Length > 0) TryWriteCache(cachePath, bytes);
            return bytes;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            DecodeGate.Release();
        }
    }

    /// <summary>
    /// 缓存文件名：<c>{sha1(路径|大小|最后写入|长边)}.png</c>。
    /// 供单测直接校验键的稳定性与区分度。
    /// </summary>
    internal static string BuildCacheFileName(string path, int longEdge)
    {
        var size = 0L;
        var ticks = 0L;
        try
        {
            var file = new FileInfo(path);
            if (file.Exists)
            {
                size = file.Length;
                ticks = file.LastWriteTimeUtc.Ticks;
            }
        }
        catch
        {
            // 拿不到文件属性时退化为"仅按路径 + 长边"做键，不影响可用性
        }

        var raw = $"{Path.GetFullPath(path)}|{size}|{ticks}|{longEdge}";
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant() + ".png";
    }

    /// <summary>解码首帧并编码为 PNG（同步；由调用方放到线程池）</summary>
    private static byte[]? RenderThumbnailPng(string path, int longEdge)
    {
        FileStream? stream = null;
        SKCodec? codec = null;
        SKBitmap? bitmap = null;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
            codec = SKCodec.Create(stream, out _);
            if (codec is null) return null;

            var (width, height) = ResolveThumbnailSize(codec.Info, longEdge);
            var target = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

            bitmap = SKBitmap.Decode(codec, target) ?? SKBitmap.Decode(codec);
            if (bitmap is null) return null;

            if (bitmap.Width != width || bitmap.Height != height)
            {
                var resized = DownscaleBitmap(bitmap, target);
                if (resized is null) return null;
                bitmap.Dispose();
                bitmap = resized;
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            return data?.ToArray();
        }
        catch
        {
            // 缩略图失败不影响壁纸本体：卡片退化为占位图
            return null;
        }
        finally
        {
            bitmap?.Dispose();
            codec?.Dispose();
            stream?.Dispose();
        }
    }

    /// <summary>缩略图目标尺寸：只向下等比收缩，小图不放大</summary>
    private static (int Width, int Height) ResolveThumbnailSize(SKImageInfo source, int longEdge)
    {
        var sourceLongEdge = Math.Max(source.Width, source.Height);
        if (sourceLongEdge <= 0) return (1, 1);
        if (longEdge >= sourceLongEdge) return (source.Width, source.Height);

        var scale = (double)longEdge / sourceLongEdge;
        return (Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
    }

    private static SKBitmap? DownscaleBitmap(SKBitmap source, SKImageInfo target)
    {
        try
        {
            var surface = SKSurface.Create(target);
            if (surface is null) return null;
            using (surface)
            {
                using var image = SKImage.FromBitmap(source);
                using (var canvas = surface.Canvas)
                {
#pragma warning disable CS0618 // SkiaSharp 3.116.1 未提供基于 SKSamplingOptions 的重采样入口，FilterQuality 是唯一可用开关
                    using var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium, IsAntialias = true };
#pragma warning restore CS0618
                    canvas.DrawImage(image, new SKRect(0, 0, target.Width, target.Height), paint);
                }

                using var snapshot = surface.Snapshot();
                return SKBitmap.FromImage(snapshot);
            }
        }
        catch
        {
            return null;
        }
    }

    private static void TryWriteCache(string cachePath, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            // 先写临时文件再改名：避免解码中途被打断留下半个 PNG 被后续当作命中
            var temp = cachePath + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, cachePath, overwrite: true);
            TrimCache(Path.GetDirectoryName(cachePath)!);
        }
        catch
        {
            // 缓存写不进去（只读介质 / 权限）时只是每次重新解码，不构成错误
        }
    }

    private static void TouchCacheFile(string cachePath)
    {
        try { File.SetLastAccessTimeUtc(cachePath, DateTime.UtcNow); } catch { }
    }

    /// <summary>超过上限时按最后访问时间淘汰最旧的文件（写缓存时顺带执行，不另开定时器）</summary>
    private static void TrimCache(string directory)
    {
        try
        {
            var files = new DirectoryInfo(directory).GetFiles("*.png");
            if (files.Length <= MaxCacheFiles) return;

            var stale = files
                .OrderBy(f => f.LastAccessTimeUtc)
                .Take(files.Length - MaxCacheFiles)
                .ToList();

            foreach (var file in stale)
            {
                try { file.Delete(); } catch { }
            }
        }
        catch
        {
            // 淘汰失败不影响正确性
        }
    }
}
