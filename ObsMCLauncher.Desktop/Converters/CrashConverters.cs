using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
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
/// 崩溃原因置信度 → 语义画刷（取主题资源，不再是硬编码色值）。
/// 高 → <c>PrimaryBrush</c>（主题绿，与 SuccessBrush 同值）；中 → <c>WarningBrush</c>；低 → 中性灰。
/// 传 ConverterParameter="soft" 时返回同色 15% 透明版，用作徽标底色——
/// 不能直接设 Visual.Opacity（那会连子元素文字一起变淡），所以透明底要单独给画刷。
/// PrimaryBrush/WarningBrush 在亮暗主题下同值（AppearanceApplier 不覆盖它们），
/// 因此这里静态取一次资源不会在切主题时失效。
/// </summary>
public sealed class CrashConfidenceToBrushConverter : IValueConverter
{
    public static readonly CrashConfidenceToBrushConverter Instance = new();

    private const string HighFallback = "#10B981";
    private const string MediumFallback = "#F59E0B";
    private const string LowColor = "#8A8A8A";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var soft = parameter as string == "soft";
        var color = value switch
        {
            CrashConfidence.High => ResolveResourceColor("PrimaryBrush") ?? Color.Parse(HighFallback),
            CrashConfidence.Medium => ResolveResourceColor("WarningBrush") ?? Color.Parse(MediumFallback),
            _ => Color.Parse(LowColor)
        };

        return soft
            ? new SolidColorBrush(color) { Opacity = 0.15 }
            : new SolidColorBrush(color);
    }

    private static Color? ResolveResourceColor(string key)
    {
        return Application.Current?.TryGetResource(key, null, out var raw) == true
            && raw is ISolidColorBrush brush
            ? brush.Color
            : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 字符串非空白 → bool。<see cref="NullToBoolConverter"/> 只认 null 不认空串，
/// 而崩溃分析里「证据/建议」是空串而非 null，需要整行隐藏时用它。
/// </summary>
public sealed class StringNotEmptyToBoolConverter : IValueConverter
{
    public static readonly StringNotEmptyToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
