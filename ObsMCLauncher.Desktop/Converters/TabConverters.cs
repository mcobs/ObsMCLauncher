using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Converters;

public class TabIndexToBoolConverter : IValueConverter
{
    public static readonly TabIndexToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int tabIndex && parameter is string param)
        {
            if (int.TryParse(param, out int targetIndex))
            {
                return tabIndex == targetIndex;
            }
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is string param)
        {
            if (int.TryParse(param, out int targetIndex))
            {
                return targetIndex;
            }
        }
        return -1;
    }
}

public class PluginTabToBoolConverter : IValueConverter
{
    public static readonly PluginTabToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ViewModels.PluginSubTab tab && parameter is string param)
        {
            if (Enum.TryParse<ViewModels.PluginSubTab>(param, out var targetTab))
            {
                return tab == targetTab;
            }
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is string param)
        {
            if (Enum.TryParse<ViewModels.PluginSubTab>(param, out var targetTab))
            {
                return targetTab;
            }
        }
        return ViewModels.PluginSubTab.Installed;
    }
}

public class BoolToColorConverter : IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isOnline)
        {
            return isOnline ? Color.Parse("#10B981") : Color.Parse("#DC3545");
        }
        return Color.Parse("#8A8A8A");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class FilePathToBitmapConverter : IValueConverter
{
    public static readonly FilePathToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                return new Bitmap(path);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class PingLevelToColorConverter : IValueConverter
{
    public static readonly PingLevelToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            "优秀" => Color.Parse("#10B981"),
            "良好" => Color.Parse("#22C55E"),
            "一般" => Color.Parse("#F59E0B"),
            "较差" => Color.Parse("#EF4444"),
            _ => Color.Parse("#8A8A8A")
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class GreaterThanOrEqualConverter : IValueConverter
{
    public static readonly GreaterThanOrEqualConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int tabIndex && parameter is string param)
        {
            if (int.TryParse(param, out int targetIndex))
            {
                return tabIndex >= targetIndex;
            }
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class SelectedTabContentConverter : IMultiValueConverter
{
    public static readonly SelectedTabContentConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] is ObservableCollection<TabItemViewModel> tabs && values[1] is int selectedIndex)
        {
            if (selectedIndex >= 0 && selectedIndex < tabs.Count)
            {
                return tabs[selectedIndex].Content;
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class TabContentTypeConverter : IMultiValueConverter
{
    public static readonly TabContentTypeConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] is ObservableCollection<TabItemViewModel> tabs && values[1] is int selectedIndex)
        {
            if (selectedIndex >= 0 && selectedIndex < tabs.Count && parameter is string expectedType)
            {
                var content = tabs[selectedIndex].Content;
                if (content != null)
                {
                    return content.GetType().Name == expectedType;
                }
            }
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
