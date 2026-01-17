using System;
using System.Globalization;
using System.Windows.Data;
using ObsMCLauncher.ViewModels;

namespace ObsMCLauncher.Utils.Converters
{
    public class PluginTabToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not PluginSubTab tab)
                return false;

            var p = parameter?.ToString();
            return string.Equals(p, tab.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not bool b || !b)
                return Binding.DoNothing;

            var p = parameter?.ToString();
            if (string.Equals(p, "Market", StringComparison.OrdinalIgnoreCase))
                return PluginSubTab.Market;
            if (string.Equals(p, "Installed", StringComparison.OrdinalIgnoreCase))
                return PluginSubTab.Installed;

            return Binding.DoNothing;
        }
    }
}

