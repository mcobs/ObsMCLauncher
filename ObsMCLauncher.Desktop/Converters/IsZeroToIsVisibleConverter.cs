using System;
using Avalonia.Data.Converters;

namespace ObsMCLauncher.Desktop.Converters;

/// <summary>
/// 数值为 0 时返回 true（用于空状态显示，如 Accounts.Count == 0）。
/// </summary>
public sealed class IsZeroToIsVisibleConverter : IValueConverter
{
    public static readonly IsZeroToIsVisibleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is int i) return i == 0;
        if (value is long l) return l == 0;
        if (value is double d) return Math.Abs(d) < 0.00001;
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
