using System;
using Avalonia.Data.Converters;

namespace ObsMCLauncher.Desktop.Converters;

public sealed class FirstCharConverter : IValueConverter
{
    public static readonly FirstCharConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s))
            return string.Empty;

        return s.Trim()[0].ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
