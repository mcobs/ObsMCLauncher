using System;
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

public class PluginIconConverter : IValueConverter
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

        try
        {
            if (iconPath.StartsWith("http://") || iconPath.StartsWith("https://"))
            {
                _ = LoadRemoteIconAsync(iconPath);
                return LoadDefaultIcon();
            }
            else if (File.Exists(iconPath))
            {
                using var stream = File.OpenRead(iconPath);

                if (iconPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    var svgSource = SvgSource.LoadFromStream(stream);
                    if (svgSource != null)
                        svgSource = ApplyThemeColor(svgSource);
                    return svgSource != null ? new SvgImage { Source = svgSource } : null;
                }

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
            var asset = AssetLoader.Open(new Uri(_defaultIcon));
            var svgSource = SvgSource.LoadFromStream(asset);
            if (svgSource != null)
                svgSource = ApplyThemeColor(svgSource);
            return svgSource != null ? new SvgImage { Source = svgSource } : null;
        }
        catch
        {
            return null;
        }
    }

    private static SvgSource ApplyThemeColor(SvgSource source)
    {
        var svgContent = source.ToString();
        if (string.IsNullOrEmpty(svgContent))
            return source;

        if (Application.Current is { } app &&
            app.TryGetResource("TextBrush", app.ActualThemeVariant, out var brush) &&
            brush is ISolidColorBrush textBrush)
        {
            var hexColor = $"#{textBrush.Color.R:X2}{textBrush.Color.G:X2}{textBrush.Color.B:X2}";
            svgContent = svgContent.Replace("currentColor", hexColor);
            using var memStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgContent));
            return SvgSource.LoadFromStream(memStream) ?? source;
        }
        return source;
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
