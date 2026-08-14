using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.Converters;

/// <summary>
/// 将 TokenState 转换为主题警示色（过期=ErrorBrush，即将过期/未知=WarningBrush，有效=SuccessBrush）。
/// </summary>
public sealed class TokenStateToBrushConverter : IValueConverter
{
    public static readonly TokenStateToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            TokenState.Expired => "ErrorBrush",
            TokenState.ExpiringSoon or TokenState.Unknown => "WarningBrush",
            TokenState.Valid => "SuccessBrush",
            _ => null
        };

        if (key != null &&
            Application.Current?.TryFindResource(key, out var resource) == true &&
            resource is IBrush brush)
        {
            return brush;
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
