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

        // 与 TextSecondaryBrush 对齐，避免纯黑/纯白对比度过强
        var hexColor = isLight ? "#475569" : "#94A3B8";

        return svgContent.Replace("currentColor", hexColor);
    }
}
