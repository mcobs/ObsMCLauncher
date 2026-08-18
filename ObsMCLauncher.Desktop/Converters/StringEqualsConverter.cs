using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ObsMCLauncher.Desktop.Converters;

public sealed class StringEqualsConverter : IValueConverter
{
    public static readonly StringEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var left = value?.ToString() ?? string.Empty;
        var right = parameter?.ToString() ?? string.Empty;
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 仅用于 OneWay 绑定
        return Avalonia.Data.BindingOperations.DoNothing;
    }
}

/// <summary>
/// 多值：比较两个字符串是否相等，相等返回加粗边框，用于色板选中态
/// </summary>
public sealed class StringEqualsToThicknessConverter : Avalonia.Data.Converters.IMultiValueConverter
{
    public static readonly StringEqualsToThicknessConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not { Count: >= 2 }) return new Avalonia.Thickness(0);
        var left = values[0]?.ToString() ?? string.Empty;
        var right = values[1]?.ToString() ?? string.Empty;
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            ? new Avalonia.Thickness(3)
            : new Avalonia.Thickness(1);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}
