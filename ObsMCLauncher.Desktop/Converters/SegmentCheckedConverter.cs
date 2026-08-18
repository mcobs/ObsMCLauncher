using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ObsMCLauncher.Desktop.Converters;

/// <summary>
/// 分段选择（RadioButton.segment）与配置值的双向转换：
/// Convert 比较值与参数的字符串形式；ConvertBack 在勾选时把参数还原成
/// 源属性类型（枚举名或 int），取消勾选时不回写。
/// </summary>
public sealed class SegmentCheckedConverter : IValueConverter
{
    public static readonly SegmentCheckedConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var left = value?.ToString() ?? string.Empty;
        var right = parameter?.ToString() ?? string.Empty;
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true) return Avalonia.Data.BindingOperations.DoNothing;

        var s = parameter?.ToString();
        if (string.IsNullOrEmpty(s)) return Avalonia.Data.BindingOperations.DoNothing;

        if (targetType.IsEnum)
        {
            return Enum.TryParse(targetType, s, out var result) ? result : Avalonia.Data.BindingOperations.DoNothing;
        }

        if (targetType == typeof(int) && int.TryParse(s, NumberStyles.Integer, culture, out var i))
        {
            return i;
        }

        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
