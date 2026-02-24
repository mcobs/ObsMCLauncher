using System;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;

namespace ObsMCLauncher.Desktop.Converters;

public sealed class NavIconAlignmentConverter : IValueConverter
{
    public static readonly NavIconAlignmentConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool isCollapsed ? (isCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left) : BindingOperations.DoNothing;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => BindingOperations.DoNothing;
}
