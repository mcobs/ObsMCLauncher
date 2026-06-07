using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace ObsMCLauncher.Desktop.Converters;

internal static class SvgThemeHelper
{
    public static string ReplaceCurrentColor(string svgContent)
    {
        if (string.IsNullOrEmpty(svgContent))
            return svgContent;

        // 深色主题用白色，浅色主题用黑色
        var hexColor = "#FFFFFF";
        if (Application.Current is { } app && app.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light)
        {
            hexColor = "#000000";
        }

        return svgContent.Replace("currentColor", hexColor);
    }
}