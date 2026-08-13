using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Svg.Skia;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.Converters;

public class BitmapAssetValueConverter : IValueConverter, IMultiValueConverter
{
    public static readonly BitmapAssetValueConverter Instance = new();

    // SVG 图标随主题变色，缓存键需包含主题；PNG 与主题无关
    private static readonly ConcurrentDictionary<string, object> _cache = new();
    private const int MaxCacheEntries = 512;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string rawUri && !string.IsNullOrWhiteSpace(rawUri))
        {
            return LoadImage(rawUri, Avalonia.Application.Current?.ActualThemeVariant ?? Avalonia.Styling.ThemeVariant.Default);
        }

        return null;
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count > 0 && values[0] is string rawUri && !string.IsNullOrWhiteSpace(rawUri))
        {
            var theme = values.Count > 1 && values[1] is Avalonia.Styling.ThemeVariant tv ? tv : (Avalonia.Application.Current?.ActualThemeVariant ?? Avalonia.Styling.ThemeVariant.Default);
            return LoadImage(rawUri, theme);
        }

        return null;
    }

    private static object? LoadImage(string rawUri, Avalonia.Styling.ThemeVariant theme)
    {
        try
        {
            var isSvg = rawUri.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
            var cacheKey = isSvg ? $"{rawUri}|{theme.Key}" : rawUri;

            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            Uri uri;
            if (rawUri.StartsWith("avares://"))
            {
                uri = new Uri(rawUri);
            }
            else
            {
                string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!;
                uri = new Uri($"avares://{assemblyName}{rawUri}");
            }

            var asset = AssetLoader.Open(uri);
            object? result;

            if (isSvg)
            {
                using var reader = new StreamReader(asset);
                var svgContent = reader.ReadToEnd();
                svgContent = SvgThemeHelper.ReplaceCurrentColor(svgContent, theme);

                using var memStream = new MemoryStream(Encoding.UTF8.GetBytes(svgContent));
                var svgSource = SvgSource.LoadFromStream(memStream);
                result = new SvgImage { Source = svgSource };
            }
            else
            {
                result = new Bitmap(asset);
            }

            if (result != null)
            {
                TrimCacheIfNeeded();
                _cache[cacheKey] = result;
            }

            return result;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("BitmapConverter", $"Failed to load bitmap: {rawUri}. Error: {ex.Message}");
            return null;
        }
    }

    private static void TrimCacheIfNeeded()
    {
        if (_cache.Count < MaxCacheEntries) return;

        // 清理一半的旧条目（ConcurrentDictionary 无序，简单移除即可）
        var keysToRemove = new List<string>();
        foreach (var kvp in _cache)
        {
            keysToRemove.Add(kvp.Key);
            if (keysToRemove.Count >= MaxCacheEntries / 2) break;
        }
        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
