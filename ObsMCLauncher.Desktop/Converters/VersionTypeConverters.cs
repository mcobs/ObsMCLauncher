using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ObsMCLauncher.Desktop.Converters;

/// <summary>
/// 版本类型 -> 中文显示文本（正式版/快照版/远古版）
/// </summary>
public class VersionTypeTextConverter : IValueConverter
{
    public static readonly VersionTypeTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            "release" => "正式版",
            "snapshot" => "快照版",
            "old_beta" or "old_alpha" or "beta" or "alpha" => "远古版",
            _ => value as string ?? ""
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// 版本类型 -> 标签颜色
/// </summary>
public class VersionTypeColorConverter : IValueConverter
{
    public static readonly VersionTypeColorConverter Instance = new();

    private static readonly IBrush ReleaseBrush = new SolidColorBrush(Color.Parse("#10B981"));
    private static readonly IBrush SnapshotBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush LegacyBrush = new SolidColorBrush(Color.Parse("#9CA3AF"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            "release" => ReleaseBrush,
            "snapshot" => SnapshotBrush,
            "old_beta" or "old_alpha" or "beta" or "alpha" => LegacyBrush,
            _ => LegacyBrush
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// 版本类型标签可见性：正式版不显示标签，快照/远古版显示
/// </summary>
public class VersionTypeTagVisibleConverter : IValueConverter
{
    public static readonly VersionTypeTagVisibleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string t && !string.Equals(t, "release", StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// 已安装标记：values[0]=版本Id，values[1]=已安装版本Id集合
/// </summary>
public class InstalledMarkerConverter : IMultiValueConverter
{
    public static readonly InstalledMarkerConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] is string id && values[1] is HashSet<string> installedIds)
        {
            return installedIds.Contains(id);
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
