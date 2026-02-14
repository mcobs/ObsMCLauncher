using System;
using Avalonia.Data.Converters;
using ObsMCLauncher.Desktop.ViewModels.Dialogs;

namespace ObsMCLauncher.Desktop.Converters;

public sealed class DialogInputVisibleConverter : IValueConverter
{
    public static readonly DialogInputVisibleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value == null) return false;
        return value is DialogType t && t == DialogType.Input;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
