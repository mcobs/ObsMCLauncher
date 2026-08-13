using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Svg.Skia;
using Avalonia.Threading;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.Converters;

public class PluginIconConverter : IValueConverter, IMultiValueConverter
{
    public static readonly PluginIconConverter Instance = new();

    private static readonly HttpClient _httpClient = new();
    private static readonly string _defaultIcon = "avares://ObsMCLauncher.Desktop/Assets/default_plugin.svg";

    private static readonly ConcurrentDictionary<string, IImage?> _iconCache = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _inflightLocks = new();
    private static readonly object _defaultIconLock = new();
    private static IImage? _cachedDefaultIcon;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string iconPath || string.IsNullOrWhiteSpace(iconPath))
        {
            return LoadDefaultIcon();
        }

        return LoadIcon(iconPath);
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count > 0 && values[0] is string iconPath && !string.IsNullOrWhiteSpace(iconPath))
        {
            return LoadIcon(iconPath);
        }

        return LoadDefaultIcon();
    }

    private static object? LoadIcon(string iconPath)
    {
        try
        {
            if (iconPath.StartsWith("http://") || iconPath.StartsWith("https://"))
            {
                if (_iconCache.TryGetValue(iconPath, out var cached))
                {
                    return cached;
                }

                _ = DownloadRemoteIconAsync(iconPath);
                return LoadDefaultIcon();
            }

            if (_iconCache.TryGetValue(iconPath, out var fileCached))
            {
                return fileCached;
            }

            IImage? icon = null;
            if (File.Exists(iconPath))
            {
                if (iconPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    var svgContent = File.ReadAllText(iconPath);
                svgContent = SvgThemeHelper.ReplaceCurrentColor(svgContent, Avalonia.Application.Current?.ActualThemeVariant);
                    using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgContent));
                    var svgSource = SvgSource.LoadFromStream(ms);
                    icon = svgSource != null ? new SvgImage { Source = svgSource } : null;
                }
                else
                {
                    using var stream = File.OpenRead(iconPath);
                    icon = new Bitmap(stream);
                }
            }

            if (icon != null)
            {
                _iconCache[iconPath] = icon;
            }
            return icon;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginIconConverter", $"加载图标失败: {iconPath}. Error: {ex.Message}");
        }

        return LoadDefaultIcon();
    }

    private static IImage? LoadDefaultIcon()
    {
        if (_cachedDefaultIcon != null) return _cachedDefaultIcon;

        lock (_defaultIconLock)
        {
            if (_cachedDefaultIcon != null) return _cachedDefaultIcon;

            try
            {
                using var asset = AssetLoader.Open(new Uri(_defaultIcon));
                using var reader = new StreamReader(asset);
                var svgContent = reader.ReadToEnd();
                svgContent = SvgThemeHelper.ReplaceCurrentColor(svgContent, Avalonia.Application.Current?.ActualThemeVariant);
                using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgContent));
                var svgSource = SvgSource.LoadFromStream(ms);
                _cachedDefaultIcon = svgSource != null ? new SvgImage { Source = svgSource } : null;
            }
            catch
            {
                _cachedDefaultIcon = null;
            }
        }

        return _cachedDefaultIcon;
    }

    private static async Task DownloadRemoteIconAsync(string url)
    {
        // 同一 URL 并发去重，避免滚动时重复请求
        var semaphore = _inflightLocks.GetOrAdd(url, _ => new SemaphoreSlim(1, 1));
        if (!await semaphore.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (_iconCache.ContainsKey(url)) return;

            var imageData = await _httpClient.GetByteArrayAsync(url).ConfigureAwait(false);
            IImage? bitmap = null;
            try
            {
                using var stream = new MemoryStream(imageData);
                bitmap = new Bitmap(stream);
            }
            catch
            {
                // 非位图格式（如 SVG），忽略
            }

            if (bitmap != null)
            {
                _iconCache[url] = bitmap;
                // 通知 UI 线程重新评估该图标的绑定
                Dispatcher.UIThread.Post(() => IconDownloaded?.Invoke(url));
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginIconConverter", $"加载远程图标失败: {url}. Error: {ex.Message}");
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>远程图标下载完成事件（由绑定源据此触发刷新）</summary>
    public static event Action<string>? IconDownloaded;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class PluginIconVisibleConverter : IValueConverter
{
    public static readonly PluginIconVisibleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrWhiteSpace(value as string);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
