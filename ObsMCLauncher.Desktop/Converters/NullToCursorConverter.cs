using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Input;

namespace ObsMCLauncher.Desktop.Converters;

public class NullToCursorConverter : IValueConverter
{
    public static readonly NullToCursorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value == null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
