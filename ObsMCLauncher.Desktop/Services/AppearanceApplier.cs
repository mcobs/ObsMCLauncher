using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.Services;

/// <summary>
/// 外观应用：把 <see cref="LauncherConfig"/> 里与视觉相关的字段落到
/// <c>Application.Current.Resources</c> 与 FluentAvalonia 主题上。
/// </summary>
/// <remarks>
/// 从 <c>SettingsViewModel</c> 抽出（逐行搬迁，行为等价），目的有两个：
/// 1. 设置向导的「外观」页要支持即时预览，不能为了改一个圆角去构造整个设置页 VM
///    （那会连带拖进通知服务、主页 VM、壁纸服务，并在构造期触发一次 AutoSave）；
/// 2. 主题资源只允许有一个写入点，两份实现必然漂移。
///
/// 本类只依赖传入的 <see cref="LauncherConfig"/>，不持有任何状态。
/// 壁纸与字体不在职责内：壁纸要经 <c>WallpaperService</c> 推送，字体要访问 FontManager 与缓存。
/// 但主题资源会重写导航/内容背景，壁纸必须在其后重新推送，因此各方法提供
/// <c>afterApply</c> 回调，由调用方传入自己的壁纸推送。
/// </remarks>
public static class AppearanceApplier
{
    /// <summary>默认强调色（品牌绿）</summary>
    public const string DefaultAccentHex = "#10B981";

    /// <summary>强调色十六进制字符串，空值回退默认绿</summary>
    public static string ResolveAccentHex(LauncherConfig config)
        => string.IsNullOrWhiteSpace(config.AccentColor) ? DefaultAccentHex : config.AccentColor;

    /// <summary>强调色，非法值回退默认绿</summary>
    public static Color ResolveAccentColor(LauncherConfig config)
        => Color.TryParse(ResolveAccentHex(config), out var c) ? c : Color.Parse(DefaultAccentHex);

    /// <summary>把任意输入规整为 #RRGGBB，无法解析返回 null</summary>
    public static string? NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = value.Trim();
        if (s.StartsWith("#")) s = s[1..];
        if (s.Length == 3 && int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out _))
            s = string.Concat(s.Select(ch => new string(ch, 2)));
        if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out _))
            return null;
        return $"#{s.ToUpperInvariant()}";
    }

    /// <summary>按比例压暗颜色（factor 越小越暗，0.7 表示保留 70% 亮度）</summary>
    public static Color Darken(Color c, double factor)
        => Color.FromRgb(
            (byte)Math.Round(c.R * factor),
            (byte)Math.Round(c.G * factor),
            (byte)Math.Round(c.B * factor));

    /// <summary>
    /// 应用主题模式（0=深色 1=浅色 2=跟随系统）。
    /// 与旧的设置页实现一致走 UI 线程 Post，调用方不必关心线程。
    /// 主题模式在调用瞬间捕获，避免连续切换时被后一次覆盖。
    /// </summary>
    /// <param name="afterApply">主题资源写完后回调（设置页传入壁纸推送）</param>
    public static void ApplyThemeMode(LauncherConfig config, Action? afterApply = null)
    {
        if (Application.Current == null) return;

        var themeMode = config.ThemeMode;

        Dispatcher.UIThread.Post(() =>
        {
            // 设置主题变体
            Application.Current.RequestedThemeVariant = themeMode switch
            {
                0 => ThemeVariant.Dark,
                1 => ThemeVariant.Light,
                _ => ThemeVariant.Default
            };

            // 手动更新主题资源
            UpdateThemeResourcesCore(themeMode, config, afterApply);
        });
    }

    /// <summary>
    /// 应用强调色到主题资源与 FluentAvalonia 控件强调色。
    /// 强调色本身是主题资源的一部分（导航选中态、TabView 前景），所以末尾必然重写一次主题资源。
    /// </summary>
    /// <param name="afterApply">主题资源写完后回调（设置页传入壁纸推送）</param>
    public static void ApplyAccentColor(LauncherConfig config, Action? afterApply = null)
    {
        if (Application.Current == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            var accent = ResolveAccentColor(config);

            if (Application.Current?.Styles
                    .OfType<FluentAvalonia.Styling.FluentAvaloniaTheme>()
                    .FirstOrDefault() is { } faTheme)
            {
                faTheme.CustomAccentColor = accent;
            }

            // 主页欢迎卡片渐变跟随强调色：起点为压暗后的同色系
            if (Application.Current?.Resources is { } resources)
            {
                resources["HomeWelcomeGradientStart"] = Darken(accent, 0.7);
                resources["HomeWelcomeGradientEnd"] = accent;
            }

            UpdateThemeResourcesCore(config.ThemeMode, config, afterApply);
        });
    }

    /// <summary>按配置里的主题模式重写全局主题资源字典（跟随系统时按实际变体决定）</summary>
    /// <param name="afterApply">主题资源写完后回调（设置页传入壁纸推送）</param>
    public static void UpdateThemeResources(LauncherConfig config, Action? afterApply = null)
        => UpdateThemeResourcesCore(config.ThemeMode, config, afterApply);

    private static void UpdateThemeResourcesCore(int themeMode, LauncherConfig config, Action? afterApply)
    {
        if (Application.Current == null) return;

        var resources = Application.Current.Resources;
        if (resources == null) return;

        // 对于跟随系统模式，需要检测实际的主题
        bool isLightTheme;
        if (themeMode == 2)
        {
            // 跟随系统：根据实际主题变体决定
            var actualTheme = Application.Current.ActualThemeVariant;
            isLightTheme = actualTheme == ThemeVariant.Light;
        }
        else
        {
            // 0=深色, 1=浅色
            isLightTheme = themeMode == 1;
        }

        var accent = ResolveAccentColor(config);

        if (isLightTheme)
        {
            ApplyLightTheme(resources, accent);
        }
        else
        {
            ApplyDarkTheme(resources, accent);
        }

        // 主题资源会重写导航/内容背景，壁纸相关状态需要在其后重新应用
        afterApply?.Invoke();
    }

    private static void ApplyLightTheme(IResourceDictionary resources, Color accentColor)
    {
        // 三级表面色阶：浅色模式明度递增（灰 -> 浅灰 -> 纯白），层级清晰
        resources["LayerFillColorDefaultBrush"] = new SolidColorBrush(Color.Parse("#F1F5F9"));
        resources["LayerFillColorAltBrush"] = new SolidColorBrush(Color.Parse("#F8FAFC"));
        resources["LayerFillColorPrimaryBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
        resources["LayerFillColorSecondaryBrush"] = new SolidColorBrush(Color.Parse("#E8ECF1"));

        // 兼容旧 key，全部对齐到三级表面色阶
        resources["BackgroundBrush"] = new SolidColorBrush(Color.Parse("#F8FAFC"));
        resources["SurfaceBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
        resources["SurfaceElevatedBrush"] = new SolidColorBrush(Color.Parse("#F1F5F9"));
        resources["SurfaceHoverBrush"] = new SolidColorBrush(Color.Parse("#E8ECF1"));
        resources["NavHoverBrush"] = new SolidColorBrush(Color.Parse("#E8ECF1"));
        resources["TextBrush"] = new SolidColorBrush(Color.Parse("#0F172A"));
        resources["TextSecondaryBrush"] = new SolidColorBrush(Color.Parse("#475569"));
        resources["TextTertiaryBrush"] = new SolidColorBrush(Color.Parse("#94A3B8"));
        resources["BorderBrush"] = new SolidColorBrush(Color.Parse("#E2E8F0"));
        resources["DividerBrush"] = new SolidColorBrush(Color.Parse("#F1F5F9"));
        resources["InputBackgroundBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
        resources["InputForegroundBrush"] = new SolidColorBrush(Color.Parse("#0F172A"));
        resources["GlassmorphismBackgroundBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF")) { Opacity = 0.92 };
        resources["GlassmorphismBorderBrush"] = new SolidColorBrush(Color.Parse("#000000")) { Opacity = 0.06 };
        resources["SystemControlBackgroundBaseHighBrush"] = new SolidColorBrush(Color.Parse("#F8FAFC"));
        resources["SystemControlBackgroundAltHighBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
        resources["SystemControlBackgroundBaseLowBrush"] = new SolidColorBrush(Color.Parse("#E8ECF1"));
        resources["SystemControlBackgroundBaseMediumBrush"] = new SolidColorBrush(Color.Parse("#F1F5F9"));
        resources["SystemControlForegroundBaseHighBrush"] = new SolidColorBrush(Color.Parse("#0F172A"));
        resources["SystemControlForegroundBaseLowBrush"] = new SolidColorBrush(Color.Parse("#E2E8F0"));
        resources["NavItemSelectedBackgroundBrush"] = new SolidColorBrush(accentColor) { Opacity = 0.10 };

        // 导航栏 / 标题栏 / 窗口 / 卡片背景，浅色模式下必须同步更新，否则会残留深色底
        resources["NavBackgroundBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
        resources["NavBorderBrush"] = new SolidColorBrush(Color.Parse("#E2E8F0"));
        resources["TitleBarBackgroundBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
        resources["TitleBarBorderBrush"] = new SolidColorBrush(Color.Parse("#E2E8F0"));
        resources["TitleBarButtonHoverBrush"] = new SolidColorBrush(Colors.Black) { Opacity = 0.06 };
        resources["TitleBarButtonPressedBrush"] = new SolidColorBrush(Colors.Black) { Opacity = 0.10 };
        resources["WindowBackgroundBrush"] = new SolidColorBrush(Color.Parse("#F8FAFC"));
        resources["CardBackgroundBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
        resources["CardBorderBrush"] = new SolidColorBrush(Color.Parse("#E2E8F0"));

        // FluentAvalonia 控件资源（NavigationView / SettingsExpander 等）
        resources["NavigationViewDefaultPaneBackground"] = new SolidColorBrush(Color.Parse("#F8FAFC"));
        resources["NavigationViewContentBackground"] = new SolidColorBrush(Colors.Transparent);
        resources["CardStrokeColorDefaultBrush"] = new SolidColorBrush(Color.Parse("#E2E8F0"));
        resources["DividerStrokeColorDefaultBrush"] = new SolidColorBrush(Color.Parse("#F1F5F9"));
        resources["TextFillColorPrimaryBrush"] = new SolidColorBrush(Color.Parse("#0F172A"));
        resources["TextFillColorSecondaryBrush"] = new SolidColorBrush(Color.Parse("#475569"));
        resources["TextFillColorTertiaryBrush"] = new SolidColorBrush(Color.Parse("#94A3B8"));
        resources["SubtleFillColorSecondaryBrush"] = new SolidColorBrush(Color.Parse("#E8ECF1"));
        resources["SubtleFillColorTertiaryBrush"] = new SolidColorBrush(Color.Parse("#F1F5F9"));
        resources["ControlFillColorDefaultBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
        resources["InfoBarInformationalSeverityBackgroundBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));

        ApplyTabViewTheme(resources, isLight: true, accentColor);
    }

    private static void ApplyDarkTheme(IResourceDictionary resources, Color accentColor)
    {
        // 三级表面色阶：深色模式每级约 +5% 亮度
        resources["LayerFillColorDefaultBrush"] = new SolidColorBrush(Color.Parse("#0B0D10"));
        resources["LayerFillColorAltBrush"] = new SolidColorBrush(Color.Parse("#141619"));
        resources["LayerFillColorPrimaryBrush"] = new SolidColorBrush(Color.Parse("#1C1F26"));
        resources["LayerFillColorSecondaryBrush"] = new SolidColorBrush(Color.Parse("#252830"));

        // 兼容旧 key，全部对齐到三级表面色阶
        resources["BackgroundBrush"] = new SolidColorBrush(Color.Parse("#0B0D10"));
        resources["SurfaceBrush"] = new SolidColorBrush(Color.Parse("#141619"));
        resources["SurfaceElevatedBrush"] = new SolidColorBrush(Color.Parse("#1C1F26"));
        resources["SurfaceHoverBrush"] = new SolidColorBrush(Color.Parse("#252830"));
        resources["NavHoverBrush"] = new SolidColorBrush(Color.Parse("#252830"));
        resources["TextBrush"] = new SolidColorBrush(Color.Parse("#F1F5F9"));
        resources["TextSecondaryBrush"] = new SolidColorBrush(Color.Parse("#94A3B8"));
        resources["TextTertiaryBrush"] = new SolidColorBrush(Color.Parse("#64748B"));
        resources["BorderBrush"] = new SolidColorBrush(Color.Parse("#2A2E37"));
        resources["DividerBrush"] = new SolidColorBrush(Color.Parse("#1E2128"));
        resources["InputBackgroundBrush"] = new SolidColorBrush(Color.Parse("#141619"));
        resources["InputForegroundBrush"] = new SolidColorBrush(Color.Parse("#F1F5F9"));
        resources["GlassmorphismBackgroundBrush"] = new SolidColorBrush(Color.Parse("#141619")) { Opacity = 0.88 };
        resources["GlassmorphismBorderBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF")) { Opacity = 0.08 };
        resources["SystemControlBackgroundBaseHighBrush"] = new SolidColorBrush(Color.Parse("#0B0D10"));
        resources["SystemControlBackgroundAltHighBrush"] = new SolidColorBrush(Color.Parse("#141619"));
        resources["SystemControlBackgroundBaseLowBrush"] = new SolidColorBrush(Color.Parse("#252830"));
        resources["SystemControlBackgroundBaseMediumBrush"] = new SolidColorBrush(Color.Parse("#1C1F26"));
        resources["SystemControlForegroundBaseHighBrush"] = new SolidColorBrush(Color.Parse("#F1F5F9"));
        resources["SystemControlForegroundBaseLowBrush"] = new SolidColorBrush(Color.Parse("#2A2E37"));
        resources["NavItemSelectedBackgroundBrush"] = new SolidColorBrush(accentColor) { Opacity = 0.08 };

        // 导航栏 / 标题栏 / 窗口 / 卡片背景，深色模式下同步恢复
        resources["NavBackgroundBrush"] = new SolidColorBrush(Color.Parse("#141619"));
        resources["NavBorderBrush"] = new SolidColorBrush(Color.Parse("#1E2128"));
        resources["TitleBarBackgroundBrush"] = new SolidColorBrush(Color.Parse("#141619"));
        resources["TitleBarBorderBrush"] = new SolidColorBrush(Color.Parse("#1E2128"));
        resources["TitleBarButtonHoverBrush"] = new SolidColorBrush(Colors.White) { Opacity = 0.09 };
        resources["TitleBarButtonPressedBrush"] = new SolidColorBrush(Colors.White) { Opacity = 0.16 };
        resources["WindowBackgroundBrush"] = new SolidColorBrush(Color.Parse("#0B0D10"));
        resources["CardBackgroundBrush"] = new SolidColorBrush(Color.Parse("#1C1F26"));
        resources["CardBorderBrush"] = new SolidColorBrush(Color.Parse("#2A2E37"));

        // FluentAvalonia 控件资源（NavigationView / SettingsExpander 等）
        resources["NavigationViewDefaultPaneBackground"] = new SolidColorBrush(Color.Parse("#141619"));
        resources["NavigationViewContentBackground"] = new SolidColorBrush(Colors.Transparent);
        resources["CardStrokeColorDefaultBrush"] = new SolidColorBrush(Color.Parse("#2A2E37"));
        resources["DividerStrokeColorDefaultBrush"] = new SolidColorBrush(Color.Parse("#1E2128"));
        resources["TextFillColorPrimaryBrush"] = new SolidColorBrush(Color.Parse("#F1F5F9"));
        resources["TextFillColorSecondaryBrush"] = new SolidColorBrush(Color.Parse("#94A3B8"));
        resources["TextFillColorTertiaryBrush"] = new SolidColorBrush(Color.Parse("#64748B"));
        resources["SubtleFillColorSecondaryBrush"] = new SolidColorBrush(Color.Parse("#252830"));
        resources["SubtleFillColorTertiaryBrush"] = new SolidColorBrush(Color.Parse("#1C1F26"));
        resources["ControlFillColorDefaultBrush"] = new SolidColorBrush(Color.Parse("#141619"));
        resources["InfoBarInformationalSeverityBackgroundBrush"] = new SolidColorBrush(Color.Parse("#1C1F26"));

        ApplyTabViewTheme(resources, isLight: false, accentColor);
    }

    /// <summary>
    /// FluentAvalonia TabView 主题资源：让 tab 选择栏跟随应用三级表面色阶，
    /// 避免 Fluent 默认暖灰（#282828 等）与冷色系主题产生隔阂。
    /// 选中 tab 与内容区共用「页面底色」，tab 条与顶部标题栏同色。
    /// </summary>
    private static void ApplyTabViewTheme(IResourceDictionary resources, bool isLight, Color accentColor)
    {
        var accent = new SolidColorBrush(accentColor);

        resources["TabViewBackground"] = new SolidColorBrush(Color.Parse(isLight ? "#FFFFFF" : "#141619"));
        resources["TabViewBorderBrush"] = new SolidColorBrush(Color.Parse(isLight ? "#F1F5F9" : "#1E2128"));
        resources["TabViewItemHeaderBackground"] = Brushes.Transparent;
        resources["TabViewItemHeaderBackgroundSelected"] = new SolidColorBrush(Color.Parse(isLight ? "#F8FAFC" : "#0B0D10"));
        resources["TabViewItemHeaderBackgroundPointerOver"] = new SolidColorBrush(accentColor) { Opacity = isLight ? 0.10 : 0.08 };
        resources["TabViewItemHeaderBackgroundPressed"] = new SolidColorBrush(Color.Parse(isLight ? "#F1F5F9" : "#1C1F26"));

        resources["TabViewItemHeaderForeground"] = new SolidColorBrush(Color.Parse(isLight ? "#475569" : "#94A3B8"));
        resources["TabViewItemHeaderForegroundSelected"] = accent;
        resources["TabViewItemHeaderForegroundPointerOver"] = new SolidColorBrush(Color.Parse(isLight ? "#0F172A" : "#F1F5F9"));
        resources["TabViewItemHeaderForegroundPressed"] = new SolidColorBrush(Color.Parse(isLight ? "#94A3B8" : "#64748B"));

        resources["TabViewItemIconForeground"] = new SolidColorBrush(Color.Parse(isLight ? "#475569" : "#94A3B8"));
        resources["TabViewItemIconForegroundSelected"] = accent;
        resources["TabViewItemIconForegroundPointerOver"] = new SolidColorBrush(Color.Parse(isLight ? "#0F172A" : "#F1F5F9"));
        resources["TabViewItemIconForegroundPressed"] = new SolidColorBrush(Color.Parse(isLight ? "#94A3B8" : "#64748B"));
    }

    /// <summary>圆角半径应用到全局圆角资源（主要容器读取）</summary>
    public static void ApplyCornerRadius(LauncherConfig config)
    {
        if (Application.Current?.Resources is not { } resources) return;
        var r = config.CornerRadius;
        // 柔和的层级差异：控件略小、浮层适中、卡片取设定值
        resources["ControlCornerRadius"] = new CornerRadius(Math.Max(0, r - 4));
        resources["OverlayCornerRadius"] = new CornerRadius(r);
        resources["CardCornerRadius"] = new CornerRadius(r);
    }

    /// <summary>密度应用到主要界面留白资源（紧凑=小、宽松=大）</summary>
    public static void ApplyDensity(LauncherConfig config)
    {
        if (Application.Current?.Resources is not { } resources) return;
        var baseMargin = config.Density switch
        {
            0 => 12,
            1 => 20,
            _ => 28
        };
        resources["ContentMargin"] = new Thickness(baseMargin);
        resources["InnerSpacing"] = config.Density switch { 0 => 8, 1 => 12, _ => 16 };
    }

    /// <summary>动画级别应用到全局动画开关（主要过渡读取）</summary>
    public static void ApplyAnimationLevel(LauncherConfig config)
    {
        if (Application.Current?.Resources is not { } resources) return;
        resources["EnabledAnimations"] = config.AnimationLevel != 0;
        // 华丽与标准共用动画时长，禁用时由 EnabledAnimations 关闭
        resources["DefaultTransitionDuration"] = TimeSpan.FromSeconds(config.AnimationLevel switch
        {
            2 => 0.45,
            _ => 0.25
        });
    }
}
