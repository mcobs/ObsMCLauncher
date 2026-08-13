using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Platform;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.Converters;

/// <summary>
/// 读取 SVG 文件中的 path data 并合并为单个 Geometry。
/// 搭配 PathIconSource 使用，让图标能响应 Foreground（选中态/主题切换自动变色）。
/// 通过读取 viewBox 扩充几何 Bounds，保证非填充类图标（如三点）拥有与 SVG 一致的宽高比。
/// </summary>
public class SvgToGeometryConverter : IValueConverter
{
    public static readonly SvgToGeometryConverter Instance = new();

    private static readonly ConcurrentDictionary<string, Geometry> _cache = new();
    private static readonly Regex _pathDataRegex = new(
        @"<path[^>]*\sd=""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _viewBoxRegex = new(
        @"viewBox\s*=\s*""([-\d.eE+\s,]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string rawUri || string.IsNullOrWhiteSpace(rawUri))
            return null;

        if (_cache.TryGetValue(rawUri, out var cached))
            return cached;

        try
        {
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

            using var asset = AssetLoader.Open(uri);
            using var reader = new StreamReader(asset);
            var svgContent = reader.ReadToEnd();

            var matches = _pathDataRegex.Matches(svgContent);
            if (matches.Count == 0)
                return null;

            var combinedData = string.Join(" ",
                matches.Cast<Match>().Select(m => m.Groups[1].Value));

            // 解析 viewBox，把 (minX,minY,width,height) 四个角各画一条 1/1000 viewBox 尺寸的线段。
            // 这样 Geometry 的 Bounds 会覆盖整个 viewBox，保证 PathIcon 按 Stretch.Uniform 缩放时
            // 图标宽高比与 SVG 设计一致，不会出现扁长/高大的失真（如 more.svg 的三点）。
            // 1/1000 的尺寸在正常显示下肉眼不可见，不影响视觉效果。
            var viewBoxMatch = _viewBoxRegex.Match(svgContent);
            if (viewBoxMatch.Success)
            {
                var parts = viewBoxMatch.Groups[1].Value
                    .Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4 &&
                    double.TryParse(parts[0], CultureInfo.InvariantCulture, out var minX) &&
                    double.TryParse(parts[1], CultureInfo.InvariantCulture, out var minY) &&
                    double.TryParse(parts[2], CultureInfo.InvariantCulture, out var width) &&
                    double.TryParse(parts[3], CultureInfo.InvariantCulture, out var height))
                {
                    var inv = CultureInfo.InvariantCulture;
                    var maxX = minX + width;
                    var maxY = minY + height;
                    var tiny = Math.Max(width, height) * 0.001;
                    var tinyStr = tiny.ToString("R", inv);
                    var negTinyStr = (-tiny).ToString("R", inv);
                    // 四角各一条极小的 l 线段，参与 Bounds 计算但视觉上不可见
                    var viewBoxCorners =
                        $"M{minX.ToString("R", inv)} {minY.ToString("R", inv)}l{tinyStr} 0z " +
                        $"M{maxX.ToString("R", inv)} {minY.ToString("R", inv)}l{negTinyStr} 0z " +
                        $"M{minX.ToString("R", inv)} {maxY.ToString("R", inv)}l{tinyStr} 0z " +
                        $"M{maxX.ToString("R", inv)} {maxY.ToString("R", inv)}l{negTinyStr} 0z ";
                    combinedData = viewBoxCorners + combinedData;
                }
            }

            var geometry = Geometry.Parse(combinedData);
            _cache[rawUri] = geometry;
            return geometry;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("SvgToGeometry", $"Failed to load SVG geometry: {rawUri}. Error: {ex.Message}");
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
