#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Media;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 卡片对外提供的操作入口。由 <see cref="SettingsViewModel"/> 实现，
/// 目的是让 <c>DataTemplate</c> 里的按钮直接绑 <c>{Binding MoveUpCommand}</c>，
/// 不必在编译期绑定下写 <c>$parent</c> 强制转型。
/// </summary>
public interface IWallpaperCardHost
{
    /// <summary>把条目在列表中挪动 <paramref name="delta"/> 位（-1 上移 / +1 下移）</summary>
    void MoveWallpaper(WallpaperItemViewModel item, int delta);

    /// <summary>从列表移除条目</summary>
    void RemoveWallpaper(WallpaperItemViewModel item);

    /// <summary>把 <paramref name="source"/> 拖到 <paramref name="target"/> 原先占的位置（拖拽排序，§8.5）</summary>
    void MoveWallpaperTo(WallpaperItemViewModel source, WallpaperItemViewModel target);

    /// <summary>把 <paramref name="source"/> 拖到列表末尾（落在"添加"卡上时走这条）</summary>
    void MoveWallpaperToEnd(WallpaperItemViewModel source);

    /// <summary>拖拽落点高亮：同一时刻至多一张，传 <c>null</c> 清除（§8.5）</summary>
    void SetWallpaperDropTarget(WallpaperItemViewModel? item);
}

/// <summary>
/// 壁纸卡片（设置页 §8.4）。负责一件事：**把一张图变成卡片上能看的东西**——
/// 探测结果、首帧缩略图、三种视觉状态（正常 / 加载中 / 不可用）。
/// </summary>
/// <remarks>
/// <para>
/// 缩略图走 <see cref="WallpaperThumbnailer"/>（**只解首帧，卡片上不播放动图**，省 CPU）。
/// 探测与解码都在后台线程，只有把结果写进属性这一下回到 UI 线程。
/// </para>
/// <para>
/// 动图（GIF / 动画 WebP）的判定一律以 <see cref="BackgroundResolver.Probe"/> 的**内容探测**为准，
/// 不看扩展名——动画 WebP 与静态 WebP 同为 <c>.webp</c>，靠扩展名区分必然错。
/// </para>
/// </remarks>
public partial class WallpaperItemViewModel : ViewModelBase
{
    /// <summary>缩略图长边（像素）。卡片高 150，300 已足够 2x DPI 下的清晰度</summary>
    private const int ThumbnailLongEdge = 300;

    /// <summary>帧数超过此值 → 卡片追加资源占用提示（§8.5）</summary>
    private const int HeavyFrameCount = 300;

    /// <summary>单帧解码后超过此字节数 → 卡片追加资源占用提示（§8.5）</summary>
    private const long HeavyFrameBytes = 16L * 1024 * 1024;

    private readonly WallpaperThumbnailer _thumbnailer;
    private readonly IWallpaperCardHost _host;

    public WallpaperItemViewModel(string path, WallpaperThumbnailer thumbnailer, IWallpaperCardHost host)
    {
        Path = path;
        _thumbnailer = thumbnailer;
        _host = host;

        FileName = System.IO.Path.GetFileName(path);

        MoveUpCommand = new RelayCommand(() => _host.MoveWallpaper(this, -1), () => CanMoveUp);
        MoveDownCommand = new RelayCommand(() => _host.MoveWallpaper(this, +1), () => CanMoveDown);
        RemoveCommand = new RelayCommand(() => _host.RemoveWallpaper(this));
    }

    /// <summary>本地文件绝对路径</summary>
    public string Path { get; }

    /// <summary>文件名（卡片标题，过长由 XAML 侧省略号截断）</summary>
    public string FileName { get; }

    /// <summary>首帧缩略图；未就绪或解码失败为 <c>null</c></summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowThumbnail))]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    private Bitmap? _thumbnail;

    /// <summary>正在探测 / 解码首帧</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowThumbnail))]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    [NotifyPropertyChangedFor(nameof(ShowErrorIcon))]
    private bool _isLoading = true;

    /// <summary>文件缺失或解码失败（§8.4 错误态）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowThumbnail))]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    [NotifyPropertyChangedFor(nameof(ShowErrorIcon))]
    private bool _hasError;

    /// <summary>错误原因（ToolTip / 元信息行占位）</summary>
    [ObservableProperty]
    private string _errorDetail = "";

    /// <summary>当前正在显示的那一张（由配置与轮播状态推导，非用户手选，见 <see cref="SettingsViewModel"/>）</summary>
    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>拖拽过程中指针停在这张卡片上（落点高亮，§8.5）</summary>
    [ObservableProperty]
    private bool _isDropTarget;

    /// <summary>格式徽标文字：<c>GIF</c> / <c>WEBP</c> / <c>PNG</c> …</summary>
    [ObservableProperty]
    private string _kindLabel = "";

    /// <summary>元信息行：<c>1920×1080 · 24 帧</c>（静态图省略帧数）</summary>
    [ObservableProperty]
    private string _metaLabel = "";

    /// <summary>内容探测判定为可播放动图（徽标描边走高亮色）</summary>
    [ObservableProperty]
    private bool _isAnimated;

    /// <summary>帧数或单帧内存偏大，卡片追加警示图标（§8.5）</summary>
    [ObservableProperty]
    private bool _isHeavy;

    /// <summary>偏大提示的 ToolTip 文案</summary>
    [ObservableProperty]
    private string _heavyTip = "";

    /// <summary>卡片错误态文字：元信息行的红字内容</summary>
    public string ErrorText => HasError ? "文件不可用" : MetaLabel;

    /// <summary>列表内的位置（由宿主维护，用于决定上移 / 下移按钮是否可用）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMoveUp))]
    [NotifyPropertyChangedFor(nameof(CanMoveDown))]
    private int _index;

    /// <summary>列表条目总数（由宿主维护）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMoveUp))]
    [NotifyPropertyChangedFor(nameof(CanMoveDown))]
    private int _total;

    public bool CanMoveUp => Index > 0;

    public bool CanMoveDown => Index >= 0 && Index < Total - 1;

    public bool ShowThumbnail => !IsLoading && !HasError && Thumbnail is not null;

    public bool ShowPlaceholder => !IsLoading && !HasError && Thumbnail is null;

    public bool ShowErrorIcon => !IsLoading && HasError;

    public IRelayCommand MoveUpCommand { get; }

    public IRelayCommand MoveDownCommand { get; }

    public IRelayCommand RemoveCommand { get; }

    partial void OnHasErrorChanged(bool value) => OnPropertyChanged(nameof(ErrorText));

    partial void OnMetaLabelChanged(string value) => OnPropertyChanged(nameof(ErrorText));

    /// <summary>
    /// 探测 + 生成缩略图。可在任意线程调用；取消后不再回写属性。
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        AnimatedImageInfo info;
        byte[]? thumbnailBytes;

        try
        {
            info = await Task.Run(() => BackgroundResolver.Probe(Path), cancellationToken).ConfigureAwait(false);
            thumbnailBytes = await _thumbnailer.GetThumbnailAsync(Path, ThumbnailLongEdge, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested) return;
                Apply(info: null, bitmap: null, failure: ex.Message);
            });
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested) return;

            Bitmap? bitmap = null;
            if (thumbnailBytes is not null)
            {
                try
                {
                    using var stream = new MemoryStream(thumbnailBytes);
                    bitmap = new Bitmap(stream);
                }
                catch
                {
                    // 缩略图构造失败只是卡片退化为占位图，不影响壁纸本体
                    bitmap = null;
                }
            }

            Apply(info, bitmap, failure: null);
        });
    }

    /// <summary>释放缩略图位图（列表重建 / 页面离开时调用）</summary>
    public void ReleaseThumbnail()
    {
        var bitmap = Thumbnail;
        Thumbnail = null;
        bitmap?.Dispose();
    }

    private void Apply(AnimatedImageInfo? info, Bitmap? bitmap, string? failure)
    {
        Thumbnail = bitmap;
        IsLoading = false;

        if (failure is not null)
        {
            HasError = true;
            ErrorDetail = failure;
            KindLabel = "错误";
            MetaLabel = "";
            return;
        }

        if (info is null || info.IsFailed)
        {
            HasError = true;
            ErrorDetail = info?.Error ?? "无法读取";
            KindLabel = "错误";
            MetaLabel = "";
            return;
        }

        if (info.IsRejected)
        {
            // APNG 在添加阶段就该被拒收（D7）。能走到这里说明文件在添加之后被换成了 APNG
            HasError = true;
            ErrorDetail = "这是动画 PNG（APNG），当前版本不支持播放";
            KindLabel = "APNG";
            MetaLabel = "";
            return;
        }

        HasError = false;
        ErrorDetail = "";
        IsAnimated = info.IsPlayable;
        KindLabel = KindLabelOf(info, Path);
        MetaLabel = MetaLabelOf(info);

        var frameBytes = (long)info.Width * info.Height * 4;
        IsHeavy = info.FrameCount > HeavyFrameCount || (info.IsPlayable && frameBytes > HeavyFrameBytes);
        HeavyTip = IsHeavy
            ? $"这张动图较大（{info.FrameCount} 帧 · 单帧约 {frameBytes / 1024 / 1024} MB），播放时会占用较多内存与 CPU"
            : "";
    }

    /// <summary>
    /// 格式徽标：动图按**探测结果**取，静态图按扩展名取（静态图探测结果只有"静态"，
    /// 展示"PNG"/"JPG" 比"图片"更有信息量）。
    /// </summary>
    private static string KindLabelOf(AnimatedImageInfo info, string path) => info.Kind switch
    {
        WallpaperKind.Gif => "GIF",
        WallpaperKind.AnimatedWebP => "WEBP",
        WallpaperKind.ApngUnsupported => "APNG",
        _ => ExtensionLabel(path)
    };

    private static string ExtensionLabel(string path)
    {
        var ext = System.IO.Path.GetExtension(path).TrimStart('.');
        return ext.Length == 0 ? "图片" : ext.ToUpperInvariant();
    }

    /// <summary>元信息行：动图带帧数，静态图只有尺寸（§8.4 `GIF · 1920×1080 · 24 帧` 的后半段）</summary>
    private static string MetaLabelOf(AnimatedImageInfo info)
    {
        if (info.Width <= 0 || info.Height <= 0) return "";
        return info.IsPlayable
            ? $"{info.Width}×{info.Height} · {info.FrameCount} 帧"
            : $"{info.Width}×{info.Height}";
    }
}
