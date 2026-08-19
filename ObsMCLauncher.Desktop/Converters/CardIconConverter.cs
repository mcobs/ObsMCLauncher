using System;
using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ObsMCLauncher.Desktop.Converters;

/// <summary>
/// 主页卡片图标转换：SVG path data 渲染为矢量图标，
/// 其余字符串（如插件传入的文本/emoji）回退为文本显示。
/// 参数 IsPath / IsText 用于切换 Path 与 TextBlock 的可见性。
/// </summary>
public class CardIconConverter : IValueConverter
{
    public static readonly CardIconConverter Instance = new();

    private static readonly ConcurrentDictionary<string, Geometry?> _cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var mode = parameter?.ToString();

        if (value is not string s || string.IsNullOrWhiteSpace(s))
        {
            return mode == "IsText";
        }

        var isPath = TryParsePath(s, out _);
        if (mode == "IsPath") return isPath;
        if (mode == "IsText") return !isPath;

        return TryParsePath(s, out var geometry) ? geometry : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool TryParsePath(string data, out Geometry? geometry)
    {
        if (_cache.TryGetValue(data, out geometry))
        {
            return geometry != null;
        }

        try
        {
            var parsed = Geometry.Parse(data);
            _cache[data] = parsed;
            geometry = parsed;
            return true;
        }
        catch
        {
            _cache[data] = null;
            geometry = null;
            return false;
        }
    }
}
