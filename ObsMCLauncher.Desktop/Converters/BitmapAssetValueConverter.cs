using System;
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

public class BitmapAssetValueConverter : IValueConverter
{
    public static readonly BitmapAssetValueConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string rawUri && !string.IsNullOrWhiteSpace(rawUri))
        {
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

                var asset = AssetLoader.Open(uri);

                if (rawUri.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    // 替换 SVG 中的 currentColor 为当前主题前景色
                    using var reader = new StreamReader(asset);
                    var svgContent = reader.ReadToEnd();

                    var themeColor = Colors.White;
                    if (Application.Current is { } app &&
                        app.TryGetResource("TextBrush", app.ActualThemeVariant, out var brush) &&
                        brush is ISolidColorBrush textBrush)
                    {
                        themeColor = textBrush.Color;
                    }
                    svgContent = svgContent.Replace(
                        "currentColor",
                        $"#{themeColor.R:X2}{themeColor.G:X2}{themeColor.B:X2}");

                    using var memStream = new MemoryStream(Encoding.UTF8.GetBytes(svgContent));
                    var svgSource = SvgSource.LoadFromStream(memStream);
                    return new SvgImage { Source = svgSource };
                }

                return new Bitmap(asset);
            }
            catch (Exception ex)
            {
                DebugLogger.Error("BitmapConverter", $"Failed to load bitmap: {rawUri}. Error: {ex.Message}");
                return null;
            }
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
