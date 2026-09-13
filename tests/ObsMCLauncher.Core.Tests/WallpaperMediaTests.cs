using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Media;
using ObsMCLauncher.Core.Models;
using SkiaSharp;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 动态背景解码层测试（设计文档阶段 1）。
/// 锁定三条最容易回退的契约：
/// ① APNG 必须被**识别并拒收**，不能静默退化成"不动的图"（D7 / 验收 A13）；
/// ② <c>PriorFrame</c> 增量解码是全帧序列可用的主路径（验收 A12 的前置）；
/// ③ 跳帧背压后传给解码器的 priorFrame 必须是"缓冲区里实际那一帧"。
/// </summary>
public class WallpaperMediaTests : IDisposable
{
    private readonly string _tempDir;

    public WallpaperMediaTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "omcl-wallpaper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "WallpaperAssets", name);

    private string TempFile(string name) => Path.Combine(_tempDir, name);

    // ────────────────────────────────────────────────────────────────
    // BackgroundResolver：真实素材探测
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Probe_Gif_DetectsAnimation()
    {
        var info = BackgroundResolver.Probe(Asset("tiny.gif"));

        Assert.Equal(WallpaperKind.Gif, info.Kind);
        Assert.True(info.IsAnimated);
        Assert.True(info.IsPlayable);
        Assert.Equal(8, info.FrameCount);
        Assert.Equal(64, info.Width);
        Assert.Equal(64, info.Height);
        Assert.Equal(8, info.FrameDurationsMs.Count);
        Assert.All(info.FrameDurationsMs, d => Assert.True(d > 0));
        Assert.True(info.TotalDurationMs > 0);
    }

    [Fact]
    public void Probe_AnimatedWebp_DetectsAnimation()
    {
        var info = BackgroundResolver.Probe(Asset("anim_dirty.webp"));

        Assert.Equal(WallpaperKind.AnimatedWebP, info.Kind);
        Assert.True(info.IsAnimated);
        Assert.Equal(40, info.FrameCount);
        Assert.Equal(800, info.Width);
        Assert.Equal(450, info.Height);
        // 探针实测动画 WebP 的 RepetitionCount 为 -1（无限循环）
        Assert.Equal(-1, info.LoopCount);
    }

    [Fact]
    public void Probe_StaticPng_IsStatic_NotRejected()
    {
        var info = BackgroundResolver.Probe(Asset("static.png"));

        Assert.Equal(WallpaperKind.Static, info.Kind);
        Assert.False(info.IsAnimated);
        Assert.False(info.IsRejected);
        Assert.False(info.IsFailed);
        Assert.Equal(1, info.FrameCount);
    }

    [Fact]
    public void Probe_Apng_IsRejectedWithFrameCount_NotSilentlyStatic()
    {
        // D7 的核心断言：APNG 的 FrameCount 在 SkiaSharp 里是 0，
        // 若靠它判定就会得到"一张不动的图"。这里必须走 acTL 扫块路径并明确拒收。
        var info = BackgroundResolver.Probe(Asset("anim_dirty.png"));

        Assert.Equal(WallpaperKind.ApngUnsupported, info.Kind);
        Assert.True(info.IsRejected);
        Assert.False(info.IsAnimated);
        Assert.False(info.IsPlayable);
        Assert.Equal(48, info.FrameCount);   // acTL 声明的 num_frames
        Assert.Contains("APNG", info.Describe());
    }

    [Fact]
    public void Probe_IsCachedByFileFingerprint()
    {
        BackgroundResolver.InvalidateCache();
        var first = BackgroundResolver.Probe(Asset("tiny.gif"));
        var second = BackgroundResolver.Probe(Asset("tiny.gif"));

        // 同一指纹命中缓存时返回同一实例（探测要遍历 FrameInfo，长 GIF 一次 5–80ms）
        Assert.Same(first, second);
    }

    [Theory]
    [InlineData("missing.gif")]
    [InlineData("empty.gif")]
    public void Probe_UnavailableFile_FailsWithoutThrowing(string name)
    {
        var path = TempFile(name);
        if (name.StartsWith("empty", StringComparison.Ordinal)) File.WriteAllBytes(path, []);

        var info = BackgroundResolver.Probe(path);

        Assert.Equal(WallpaperKind.Unknown, info.Kind);
        Assert.True(info.IsFailed);
        Assert.False(string.IsNullOrWhiteSpace(info.Error));
    }

    [Theory]
    [InlineData("a.png", true)]
    [InlineData("a.PNG", true)]
    [InlineData("a.gif", true)]
    [InlineData("a.webp", true)]
    [InlineData("a.jfif", false)]
    [InlineData("a.mp4", false)]
    public void IsSupportedExtension_FollowsWhitelist(string name, bool expected)
        => Assert.Equal(expected, BackgroundResolver.IsSupportedExtension(name));

    // ────────────────────────────────────────────────────────────────
    // APNG 扫块：合法与畸形输入
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ApngScan_FindsActlFrameCount()
    {
        var path = TempFile("crafted.png");
        File.WriteAllBytes(path, BuildPng(withActl: true, frameCount: 7));

        Assert.Equal(7, BackgroundResolver.TryReadApngFrameCount(path));
        Assert.Equal(WallpaperKind.ApngUnsupported, BackgroundResolver.Probe(path).Kind);
    }

    [Fact]
    public void ApngScan_NoActl_ReturnsNull()
    {
        var path = TempFile("plain.png");
        File.WriteAllBytes(path, BuildPng(withActl: false));

        Assert.Null(BackgroundResolver.TryReadApngFrameCount(path));
    }

    [Fact]
    public void ApngScan_IgnoresActlAppearingAfterIend()
    {
        // 结构上 acTL 只能出现在 IDAT 之前；IEND 之后的内容必须被忽略，
        // 否则畸形/拼接文件能把静态图伪装成动图
        var path = TempFile("after-iend.png");
        var bytes = BuildPng(withActl: false).Concat(BuildActlChunk(99)).ToArray();
        File.WriteAllBytes(path, bytes);

        Assert.Null(BackgroundResolver.TryReadApngFrameCount(path));
    }

    [Fact]
    public void ApngScan_TruncatedFile_ReturnsNull()
    {
        var path = TempFile("truncated.png");
        File.WriteAllBytes(path, BuildPng(withActl: true, frameCount: 4)[..10]);

        Assert.Null(BackgroundResolver.TryReadApngFrameCount(path));
    }

    [Fact]
    public void ApngScan_OversizedChunkLength_ReturnsNull_AndDoesNotThrow()
    {
        // 块长度字段被构造成 0xFFFFFFFF 时不得越界读，也不得死循环
        var path = TempFile("huge-chunk.png");
        var bytes = new List<byte>();
        bytes.AddRange(PngSignature);
        bytes.AddRange(BuildChunk("IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0]));
        var bogus = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(bogus, uint.MaxValue);
        Encoding.ASCII.GetBytes("IDAT").CopyTo(bogus, 4);
        bytes.AddRange(bogus);
        File.WriteAllBytes(path, bytes.ToArray());

        Assert.Null(BackgroundResolver.TryReadApngFrameCount(path));
        Assert.NotNull(BackgroundResolver.Probe(path).Error);   // 降级为失败，而不是抛异常
    }

    [Fact]
    public void ApngScan_NonPngContent_ReturnsNull()
    {
        var path = TempFile("not-png.dat");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("GIF89a-not-a-png"));

        Assert.Null(BackgroundResolver.TryReadApngFrameCount(path));
    }

    [Fact]
    public void ApngScan_ZeroFrameCount_IsNotTreatedAsApng()
    {
        var path = TempFile("zero-frames.png");
        File.WriteAllBytes(path, BuildPng(withActl: true, frameCount: 0));

        // num_frames = 0 在结构上非法，按"不是 APNG"处理，交给静态图路径
        Assert.Null(BackgroundResolver.TryReadApngFrameCount(path));
    }

    // ────────────────────────────────────────────────────────────────
    // 解码尺寸夹紧
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1920, 1080, 1100, 1920, 1100)]  // 窗口比配置更紧
    [InlineData(1920, 1080, 0, 800, 800)]       // 窗口未知时由配置夹紧
    [InlineData(1920, 1080, 0, 0, 1920)]        // 两者都放开 → 源尺寸（不做升采样）
    [InlineData(1920, 1080, 4096, 4096, 1920)]  // 想放大也不给（实测升采样返回 InvalidScale）
    [InlineData(800, 450, 4096, 4096, 800)]     // 小图保持原样
    public void ClampDecodeLongEdge_OnlyClampsDownward(
        int width, int height, int windowLongEdge, int maxDecodeEdge, int expected)
    {
        var info = new AnimatedImageInfo { Width = width, Height = height, FrameCount = 2 };

        Assert.Equal(expected, info.ClampDecodeLongEdge(windowLongEdge, maxDecodeEdge));
    }

    [Fact]
    public void ScaleTo_KeepsAspectRatio()
    {
        var info = new AnimatedImageInfo { Width = 1920, Height = 1080, FrameCount = 2 };

        Assert.Equal((1100, 619), info.ScaleTo(1100));
        Assert.Equal((1920, 1080), info.ScaleTo(5000));   // 不放大
    }

    // ────────────────────────────────────────────────────────────────
    // SkiaAnimatedImageDecoder：增量解码主路径
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Decoder_DecodesAllGifFrames_WithIncrementalPriorFrame()
    {
        var info = BackgroundResolver.Probe(Asset("tiny.gif"));
        using var decoder = SkiaAnimatedImageDecoder.TryCreate(Asset("tiny.gif"), info, 0, 0, out var error);

        Assert.NotNull(decoder);
        Assert.Null(error);
        Assert.Equal(64, decoder!.DecodeWidth);
        Assert.Equal(64, decoder.DecodeHeight);

        var buffer = Marshal.AllocHGlobal(decoder.RowBytes * decoder.DecodeHeight);
        try
        {
            var prior = -1;
            for (var i = 0; i < info.FrameCount; i++)
            {
                var result = decoder.DecodeFrame(i, buffer, decoder.RowBytes, prior);
                Assert.True(result.IsSuccess, $"第 {i} 帧解码失败：{result.Error}");
                prior = i;   // 增量路径：缓冲区里现在是第 i 帧
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [Fact]
    public void Decoder_DownscalesToRequestedWindowEdge()
    {
        var info = BackgroundResolver.Probe(Asset("anim_dirty.webp"));
        using var decoder = SkiaAnimatedImageDecoder.TryCreate(Asset("anim_dirty.webp"), info, windowLongEdge: 400, maxDecodeEdge: 0, out _);

        Assert.NotNull(decoder);
        Assert.True(decoder!.DecodeWidth <= 400, $"解码宽 {decoder.DecodeWidth} 未按窗口夹紧");
        Assert.True(decoder.DecodeHeight <= 225);
    }

    [Fact]
    public void Decoder_RejectsOutOfRangeFrameIndex()
    {
        var info = BackgroundResolver.Probe(Asset("tiny.gif"));
        using var decoder = SkiaAnimatedImageDecoder.TryCreate(Asset("tiny.gif"), info, 0, 0, out _)!;

        var buffer = Marshal.AllocHGlobal(decoder.RowBytes * decoder.DecodeHeight);
        try
        {
            Assert.False(decoder.DecodeFrame(info.FrameCount, buffer, decoder.RowBytes, -1).IsSuccess);
            Assert.False(decoder.DecodeFrame(-1, buffer, decoder.RowBytes, -1).IsSuccess);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // ────────────────────────────────────────────────────────────────
    // FramePacingPlan：时间轴、帧率上限与跳帧背压
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Pacing_RaisesShortDurationsToFpsCeiling()
    {
        var plan = new FramePacingPlan([10, 10, 10], maxFps: 24);

        Assert.Equal(1000.0 / 24, plan.MinFrameDurationMs, 3);
        Assert.Equal(1000.0 / 24, plan.GetFrameDurationMs(0), 3);
    }

    [Fact]
    public void Pacing_KeepsLongerDurations()
    {
        var plan = new FramePacingPlan([100, 250], maxFps: 24);

        Assert.Equal(100, plan.GetFrameDurationMs(0), 3);
        Assert.Equal(250, plan.GetFrameDurationMs(1), 3);
        Assert.Equal(350, plan.TotalDurationMs, 3);
    }

    [Fact]
    public void Pacing_Next_WrapsAndCountsLoops()
    {
        var plan = new FramePacingPlan([100, 100, 100], maxFps: 24);

        var c0 = FrameCursor.Start;
        var c1 = plan.Next(c0);
        var c2 = plan.Next(c1);
        var wrapped = plan.Next(c2);

        Assert.Equal((1, 100.0), (c1.Index, c1.DueMs));
        Assert.Equal((2, 200.0), (c2.Index, c2.DueMs));
        Assert.Equal(0, wrapped.Index);
        Assert.Equal(300.0, wrapped.DueMs, 3);
        Assert.Equal(1, wrapped.CompletedLoops);
    }

    [Fact]
    public void Pacing_CatchUp_SkipsMissedFrames_KeepingTimelineAligned()
    {
        var plan = new FramePacingPlan([100, 100, 100], maxFps: 24);
        var next = plan.Next(FrameCursor.Start);          // 帧 1，应于 t=100 展示

        // 解码耗时把时间推到了 t=350：帧 1(100)/2(200)/0(300) 都已错过
        var caught = plan.CatchUp(next, nowMs: 350);

        Assert.Equal(1, caught.Index);                    // 时间轴上 t=350 对应的下一帧
        Assert.Equal(400.0, caught.DueMs, 3);
        Assert.Equal(1, caught.CompletedLoops);
        Assert.True(caught.SkippedFrames >= 3);           // 背压计数可观测
    }

    [Fact]
    public void Pacing_CatchUp_DoesNotSkipOnExactBoundary()
    {
        var plan = new FramePacingPlan([100, 100], maxFps: 24);
        var next = plan.Next(FrameCursor.Start);          // 帧 1，due = 100

        var caught = plan.CatchUp(next, nowMs: 100);

        Assert.Equal(1, caught.Index);                    // 恰好卡边界：保留该帧
        Assert.Equal(0, caught.SkippedFrames);
    }

    [Theory]
    [InlineData(0, 1, true)]    // 只播一遍：回绕一次即结束
    [InlineData(0, 0, false)]
    [InlineData(1, 1, false)]   // 额外重复一次：第一次回绕后还要再播一轮
    [InlineData(1, 2, true)]
    [InlineData(-1, 99, false)] // 无限循环永不结束
    public void Pacing_IsFinished_FollowsLoopCount(int loopCount, int completedLoops, bool expected)
    {
        var plan = new FramePacingPlan([100], maxFps: 24, loopCount: loopCount);

        Assert.Equal(expected, plan.IsFinished(new FrameCursor(0, 0, completedLoops, 0)));
    }

    [Fact]
    public void Pacing_SingleFrame_IsAlreadyFinished_AndNeverSkips()
    {
        var plan = FramePacingPlan.SingleFrame(24);

        Assert.Equal(1, plan.FrameCount);
        Assert.Equal(FrameCursor.Start, plan.CatchUp(FrameCursor.Start, 10_000));
        Assert.Equal(0, plan.CatchUp(FrameCursor.Start, 10_000).SkippedFrames);

        // "只显示首帧"的语义：单帧计划画完第 0 帧后即进入完成态
        // （渲染循环因此停掉，而不是每帧醒来空转 —— 省电策略见设计文档 §6.5）
        Assert.False(plan.IsFinished(FrameCursor.Start));
        Assert.True(plan.IsFinished(plan.Next(FrameCursor.Start)));
    }

    // ────────────────────────────────────────────────────────────────
    // 缩略图
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Thumbnail_CacheKey_IsStableAndDistinguishesEdge()
    {
        var path = Asset("static.png");

        Assert.Equal(WallpaperThumbnailer.BuildCacheFileName(path, 300), WallpaperThumbnailer.BuildCacheFileName(path, 300));
        Assert.NotEqual(WallpaperThumbnailer.BuildCacheFileName(path, 300), WallpaperThumbnailer.BuildCacheFileName(path, 150));
    }

    [Fact]
    public async Task Thumbnail_GeneratesScalesAndCaches()
    {
        var source = TempFile("big.png");
        using (var bitmap = new SKBitmap(new SKImageInfo(600, 400, SKColorType.Bgra8888, SKAlphaType.Premul)))
        {
            using (var canvas = new SKCanvas(bitmap)) canvas.Clear(new SKColor(20, 200, 120));
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            File.WriteAllBytes(source, data.ToArray());
        }

        var cacheDir = Path.Combine(_tempDir, "thumbs");
        var thumbnailer = new WallpaperThumbnailer(cacheDir);

        var first = await thumbnailer.GetThumbnailAsync(source, 100);
        Assert.NotNull(first);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, first!.Take(4).ToArray());

        using (var thumb = SKBitmap.Decode(first))
        {
            Assert.NotNull(thumb);
            Assert.Equal(100, Math.Max(thumb!.Width, thumb.Height));
            Assert.Equal(100, thumb.Width);   // 600×400 → 100×67，长边恰好 100
        }

        Assert.Single(Directory.GetFiles(cacheDir, "*.png"));

        var second = await thumbnailer.GetThumbnailAsync(source, 100);
        Assert.Equal(first, second);          // 第二次走磁盘缓存，字节一致
    }

    [Fact]
    public async Task Thumbnail_UnavailableFile_ReturnsNull()
    {
        var thumbnailer = new WallpaperThumbnailer(Path.Combine(_tempDir, "thumbs2"));

        Assert.Null(await thumbnailer.GetThumbnailAsync(TempFile("nope.png"), 100));
    }

    [Fact]
    public async Task Thumbnail_DecodesFirstFrameOfAnimation()
    {
        var thumbnailer = new WallpaperThumbnailer(Path.Combine(_tempDir, "thumbs3"));

        var bytes = await thumbnailer.GetThumbnailAsync(Asset("tiny.gif"), 32, CancellationToken.None);

        Assert.NotNull(bytes);
        using var thumb = SKBitmap.Decode(bytes!);
        Assert.Equal(32, Math.Max(thumb!.Width, thumb.Height));
    }

    // ────────────────────────────────────────────────────────────────
    // PNG 构造工具
    // ────────────────────────────────────────────────────────────────

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static byte[] BuildPng(bool withActl, uint frameCount = 3)
    {
        var bytes = new List<byte>();
        bytes.AddRange(PngSignature);
        bytes.AddRange(BuildChunk("IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0]));
        if (withActl) bytes.AddRange(BuildActlChunk(frameCount));
        bytes.AddRange(BuildChunk("IEND", []));
        return bytes.ToArray();
    }

    private static byte[] BuildActlChunk(uint frameCount)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), frameCount);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4, 4), 0);   // num_plays = 0（无限）
        return BuildChunk("acTL", payload);
    }

    /// <summary>块结构：长度(4, BE) + 类型(4) + 数据 + CRC(4)。CRC 不参与解析，填 0 即可</summary>
    private static byte[] BuildChunk(string type, byte[] payload)
    {
        var chunk = new byte[12 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(0, 4), (uint)payload.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        payload.CopyTo(chunk, 8);
        return chunk;
    }
}
