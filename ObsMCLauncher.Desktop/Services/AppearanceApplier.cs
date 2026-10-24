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

    /// <summary>WCAG 相对亮度（sRGB，0=纯黑 1=纯白）</summary>
    public static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>WCAG 对比度（1~21，正文需 ≥ 4.5）</summary>
    public static double ContrastRatio(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>填充型强调面（实心按钮/徽标）上的前景色：在白与近黑之间取对比度更高的那个。</summary>
    /// <remarks>
    /// 品牌绿 #10B981 上的白字只有 2.54:1，必须换色；但强调色是用户可配的（主题色选择器可以选任意色），
    /// 所以不能写死黑或白 —— 按实际亮度选。这两个候选覆盖了除「相对亮度 0.183~0.198」这条极窄带
    /// 之外的全部区间，落进去时也只是差一点点，不会再出现 2.5:1 那种级别的问题。
    /// </remarks>
    public static Color ResolveOnAccent(Color accent)
    {
        var nearBlack = Color.Parse("#0B0D10");
        return ContrastRatio(Colors.White, accent) >= ContrastRatio(nearBlack, accent)
            ? Colors.White
            : nearBlack;
    }

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
            // 设置主题变体（表面/文字/边框/阴影由 Theme.axaml 的 ThemeDictionaries 自动跟随）
            Application.Current.RequestedThemeVariant = themeMode switch
            {
                0 => ThemeVariant.Dark,
                1 => ThemeVariant.Light,
                _ => ThemeVariant.Default
            };

            // 只有强调色派生的那几个键需要重算：它们的透明度深浅色不同
            ApplyAccentResources(config, afterApply);
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

            // 主页欢迎卡片渐变、导航选中态、TabView 选中色等一整族都在 ApplyAccentPalette 里
            ApplyAccentResources(config, afterApply);
        });
    }

    /// <summary>
    /// 重写强调色派生资源（跟随系统模式下切主题时会再调一次：个别键的透明度按深浅色取值）。
    /// 静态的深浅色值已由 Styles/Theme.axaml 的 ThemeDictionaries 提供，这里不再重写。
    /// </summary>
    /// <param name="afterApply">资源写完后回调（设置页传入壁纸推送）</param>
    public static void UpdateThemeResources(LauncherConfig config, Action? afterApply = null)
        => ApplyAccentResources(config, afterApply);

    private static void ApplyAccentResources(LauncherConfig config, Action? afterApply)
    {
        if (Application.Current is not { } app || app.Resources is not { } resources) return;

        ApplyAccentPalette(resources, ResolveAccentColor(config), app.ActualThemeVariant == ThemeVariant.Light);

        // 主题资源会改写导航/内容背景，壁纸相关状态需要在其后重新应用
        afterApply?.Invoke();
    }

    /// <summary>
    /// 把强调色落到它派生出的那一族资源上。
    /// 这些都是"同一个颜色的不同用途"，必须整族一起更新 —— 漏一个就会出现
    /// "按钮变了、导航选中还是绿的"这种半截状态。
    /// </summary>
    private static void ApplyAccentPalette(IResourceDictionary resources, Color accent, bool isLight)
    {
        var accentBrush = new SolidColorBrush(accent);
        var tint = isLight ? 0.10 : 0.08;

        resources["AccentColor"] = accent;
        resources["AccentBrush"] = accentBrush;
        resources["AccentSecondaryBrush"] = new SolidColorBrush(Lighten(accent, 1.18));
        resources["AccentOverlayBrush"] = new SolidColorBrush(Color.FromArgb(0x59, accent.R, accent.G, accent.B));
        resources["PrimaryBrush"] = accentBrush;
        resources["PrimarySoftBrush"] = new SolidColorBrush(accent) { Opacity = 0.18 };
        resources["NavItemSelectedBackgroundBrush"] = new SolidColorBrush(accent) { Opacity = tint };
        resources["AccentTintBrush"] = new SolidColorBrush(accent) { Opacity = tint };

        // 填充型强调面的前景：按实际填充色的亮度在白/近黑之间选（见 ResolveOnAccent）
        resources["OnAccentBrush"] = new SolidColorBrush(ResolveOnAccent(accent));
        resources["AccentFillHoverBrush"] = new SolidColorBrush(Darken(accent, 0.86));
        resources["AccentFillPressedBrush"] = new SolidColorBrush(Darken(accent, 0.74));

        // 主页欢迎卡片渐变：起点为压暗后的同色系
        resources["HomeWelcomeGradientStart"] = Darken(accent, 0.7);
        resources["HomeWelcomeGradientEnd"] = accent;

        // 按名字读 SystemAccentColor 的着色面（悬浮下载按钮、下载管理器标题条）
        resources["SystemAccentColor"] = accent;

        // TabView 选中态里跟强调色的三条；其余 TabView 键在 ThemeDictionaries 里
        resources["TabViewItemHeaderForegroundSelected"] = accentBrush;
        resources["TabViewItemIconForegroundSelected"] = accentBrush;
        resources["TabViewItemHeaderBackgroundPointerOver"] = new SolidColorBrush(accent) { Opacity = tint };
    }

    /// <summary>按比例提亮颜色（factor 大于 1，用于强调色的浅色变体）</summary>
    public static Color Lighten(Color c, double factor)
        => Color.FromRgb(
            (byte)Math.Min(255, Math.Round(c.R * factor)),
            (byte)Math.Min(255, Math.Round(c.G * factor)),
            (byte)Math.Min(255, Math.Round(c.B * factor)));

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

    /// <summary>
    /// 密度应用到留白资源（紧凑=小、宽松=大）。
    /// 注意：光有 ContentMargin 只改了内容区外框，页面内部全是硬编码 ——
    /// 那样"密度"只挪几像素、看着像没生效，所以页面级留白单独有个 PageMargin。
    /// </summary>
    public static void ApplyDensity(LauncherConfig config)
    {
        if (Application.Current?.Resources is not { } resources) return;

        var (gutter, page) = config.Density switch
        {
            0 => (12, new Thickness(12, 16, 12, 12)),
            1 => (20, new Thickness(24, 24, 24, 16)),
            _ => (28, new Thickness(32, 32, 32, 24))
        };

        resources["ContentMargin"] = new Thickness(gutter);
        resources["PageMargin"] = page;
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
