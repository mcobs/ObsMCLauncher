using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.Converters;

/// <summary>
/// 崩溃原因分类 → FA Symbol 图标。枚举名若不存在则回落 Find，不会抛。
/// </summary>
public sealed class CrashCategoryToSymbolConverter : IValueConverter
{
    public static readonly CrashCategoryToSymbolConverter Instance = new();

    private static readonly Dictionary<string, string> SymbolNames = new(StringComparer.Ordinal)
    {
        ["Memory"] = "Memory",
        ["Java"] = "Code",
        ["Graphics"] = "Desktop",
        ["ModLoading"] = "Library",
        ["Mixin"] = "Puzzle",
        ["ModCompat"] = "Apps",
        ["Config"] = "Settings",
        ["World"] = "Globe",
        ["Network"] = "Wifi",
        ["FileAccess"] = "Folder",
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var category = value as string ?? "";
        if (SymbolNames.TryGetValue(category, out var name) &&
            Enum.TryParse<Symbol>(name, out var symbol))
        {
            return symbol;
        }
        return Symbol.Find;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 崩溃原因置信度 → 颜色（与 BoolToColorConverter 同口径的硬编码色值）
/// 高 → 主题绿；中 → 琥珀；低 → 灰
/// </summary>
public sealed class CrashConfidenceToColorConverter : IValueConverter
{
    public static readonly CrashConfidenceToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            CrashConfidence.High => Color.Parse("#10B981"),
            CrashConfidence.Medium => Color.Parse("#F59E0B"),
            _ => Color.Parse("#8A8A8A")
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
