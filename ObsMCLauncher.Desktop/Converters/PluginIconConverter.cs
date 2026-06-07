using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
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
                _ = LoadRemoteIconAsync(iconPath);
                return LoadDefaultIcon();
            }
            else if (File.Exists(iconPath))
            {
                if (iconPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    var svgContent = File.ReadAllText(iconPath);
                    svgContent = SvgThemeHelper.ReplaceCurrentColor(svgContent);
                    using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgContent));
                    var svgSource = SvgSource.LoadFromStream(ms);
                    return svgSource != null ? new SvgImage { Source = svgSource } : null;
                }

                using var stream = File.OpenRead(iconPath);
                return new Bitmap(stream);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginIconConverter", $"加载图标失败: {iconPath}. Error: {ex.Message}");
        }

        return LoadDefaultIcon();
    }

    private static IImage? LoadDefaultIcon()
    {
        try
        {
            using var asset = AssetLoader.Open(new Uri(_defaultIcon));
            using var reader = new StreamReader(asset);
            var svgContent = reader.ReadToEnd();
            svgContent = SvgThemeHelper.ReplaceCurrentColor(svgContent);
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgContent));
            var svgSource = SvgSource.LoadFromStream(ms);
            return svgSource != null ? new SvgImage { Source = svgSource } : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task LoadRemoteIconAsync(string url)
    {
        try
        {
            var imageData = await _httpClient.GetByteArrayAsync(url);
            Dispatcher.UIThread.Post(() =>
        {
                using var stream = new MemoryStream(imageData);
                var bitmap = new Bitmap(stream);
            });
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginIconConverter", $"加载远程图标失败: {url}. Error: {ex.Message}");
        }
    }

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