using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace ObsMCLauncher.Desktop.Converters;

internal static class SvgThemeHelper
{
    public static string ReplaceCurrentColor(string svgContent, ThemeVariant? theme)
    {
        if (string.IsNullOrEmpty(svgContent))
            return svgContent;

        var isLight = theme == ThemeVariant.Light ||
                      (theme == ThemeVariant.Default &&
                       Application.Current?.ActualThemeVariant == ThemeVariant.Light);

        // 浅色主题用黑色，深色主题用白色
        var hexColor = isLight ? "#000000" : "#FFFFFF";

        return svgContent.Replace("currentColor", hexColor);
    }
}
