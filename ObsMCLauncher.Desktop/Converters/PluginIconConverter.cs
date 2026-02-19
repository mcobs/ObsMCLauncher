using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.Converters;

public class PluginIconConverter : IValueConverter
{
    public static readonly PluginIconConverter Instance = new();
    private static readonly HttpClient _httpClient = new();
    private static readonly string _defaultIcon = "avares://ObsMCLauncher.Desktop/Assets/default_plugin.png";

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
                return new Bitmap(stream);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginIconConverter", $"加载图标失败: {iconPath}. Error: {ex.Message}");
        }

        return LoadDefaultIcon();
    }

    private static Bitmap? LoadDefaultIcon()
    {
        try
        {
            var asset = Avalonia.Platform.AssetLoader.Open(new Uri(_defaultIcon));
            return new Bitmap(asset);
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
