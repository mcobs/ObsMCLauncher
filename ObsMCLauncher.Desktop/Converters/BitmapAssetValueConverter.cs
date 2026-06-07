using System;
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

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string rawUri && !string.IsNullOrWhiteSpace(rawUri))
        {
            return LoadImage(rawUri);
        }

        return null;
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count > 0 && values[0] is string rawUri && !string.IsNullOrWhiteSpace(rawUri))
        {
            return LoadImage(rawUri);
        }

        return null;
    }

    private static object? LoadImage(string rawUri)
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
                using var reader = new StreamReader(asset);
                var svgContent = reader.ReadToEnd();
                svgContent = SvgThemeHelper.ReplaceCurrentColor(svgContent);

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

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}