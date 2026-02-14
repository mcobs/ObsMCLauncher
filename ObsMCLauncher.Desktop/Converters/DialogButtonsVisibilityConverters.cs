using System;
using Avalonia.Data.Converters;
using ObsMCLauncher.Desktop.ViewModels.Dialogs;

namespace ObsMCLauncher.Desktop.Converters;

public sealed class DialogShowCancelConverter : IValueConverter
{
    public static readonly DialogShowCancelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value == null) return false;
        return value is DialogButtons b && (b == DialogButtons.OKCancel || b == DialogButtons.YesNoCancel);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class DialogShowOkConverter : IValueConverter
{
    public static readonly DialogShowOkConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value == null) return false;
        return value is DialogButtons b && (b == DialogButtons.OK || b == DialogButtons.OKCancel);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class DialogShowYesConverter : IValueConverter
{
    public static readonly DialogShowYesConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value == null) return false;
        return value is DialogButtons b && (b == DialogButtons.YesNo || b == DialogButtons.YesNoCancel);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class DialogShowNoConverter : IValueConverter
{
    public static readonly DialogShowNoConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value == null) return false;
        return value is DialogButtons b && (b == DialogButtons.YesNo || b == DialogButtons.YesNoCancel);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
