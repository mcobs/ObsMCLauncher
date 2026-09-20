using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.Converters;

/// <summary>
/// 崩溃原因分类 → FA Symbol 图标。
/// 图标名用 <see cref="Symbol"/> 枚举而不是字符串：写错名字会编译不过，
/// 不会像 <c>Enum.TryParse</c> 那样静默回落到默认图标。
/// （合法成员名单可用 .temp/probe/SymbolProbe 反射导出为 symbol-names.txt 核对。）
/// </summary>
public sealed class CrashCategoryToSymbolConverter : IValueConverter
{
    public static readonly CrashCategoryToSymbolConverter Instance = new();

    private static readonly Dictionary<string, Symbol> Map = new(StringComparer.Ordinal)
    {
        // Calculator 与「设置 → 游戏 → 最大内存分配」用同一个图标，保持一致
        ["Memory"] = Symbol.Calculator,
        ["Java"] = Symbol.Code,
        ["Graphics"] = Symbol.FullScreen,
        ["ModLoading"] = Symbol.Library,
        ["Mixin"] = Symbol.Repair,
        // 注意：SwitchApps 在 FA 2.4.1 里已过时且没有字形，改用 Link（依赖关系）
        ["ModCompat"] = Symbol.Link,
        ["Config"] = Symbol.Settings,
        ["World"] = Symbol.World,
        ["Network"] = Symbol.Wifi1,
        ["FileAccess"] = Symbol.Folder,
        ["Unknown"] = Symbol.Find,
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var category = value as string ?? "";
        return Map.TryGetValue(category, out var symbol) ? symbol : Symbol.Find;
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
