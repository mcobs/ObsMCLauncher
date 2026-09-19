using Avalonia.Media;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Desktop.Services;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 向导「外观」页：主题 / 强调色 / 圆角 / 密度 / 动画。
/// <para>
/// 这类改动一律走 <see cref="AppearanceApplier"/>——和设置页共用同一套实现，
/// 所以向导里选什么即时就是什么效果，不需要等到进主界面。
/// </para>
/// </summary>
public partial class WelcomeAppearancePageViewModel : WelcomeStepViewModel
{
    public WelcomeAppearancePageViewModel(WelcomeViewModel owner)
        : base(owner, "外观", "调整启动器的配色与观感，改动会立刻生效。")
    {
    }

    private LauncherConfig Config => Owner.Config;

    /// <summary>主题模式：0=深色 1=浅色 2=跟随系统</summary>
    public int ThemeMode
    {
        get => Config.ThemeMode;
        set
        {
            if (Config.ThemeMode == value) return;
            Config.ThemeMode = value;
            OnPropertyChanged();
            AppearanceApplier.ApplyThemeMode(Config);
        }
    }

    /// <summary>强调色（Color 类型，供取色器双向绑定）</summary>
    public Color AccentColorValue
    {
        get => AppearanceApplier.ResolveAccentColor(Config);
        set
        {
            var hex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
            if (string.Equals(Config.AccentColor, hex, System.StringComparison.OrdinalIgnoreCase)) return;
            Config.AccentColor = hex;
            OnPropertyChanged();
            AppearanceApplier.ApplyAccentColor(Config);
        }
    }

    /// <summary>圆角半径（0-28）：拉动滑块即时改全局圆角</summary>
    public int CornerRadius
    {
        get => Config.CornerRadius;
        set
        {
            var clamped = System.Math.Clamp(value, 0, 28);
            if (Config.CornerRadius == clamped) return;
            Config.CornerRadius = clamped;
            OnPropertyChanged();
            AppearanceApplier.ApplyCornerRadius(Config);
        }
    }

    /// <summary>密度：0=紧凑 1=标准 2=宽松</summary>
    public int Density
    {
        get => Config.Density;
        set
        {
            var clamped = System.Math.Clamp(value, 0, 2);
            if (Config.Density == clamped) return;
            Config.Density = clamped;
            OnPropertyChanged();
            AppearanceApplier.ApplyDensity(Config);
        }
    }

    /// <summary>动画级别：0=禁用 1=标准 2=华丽</summary>
    public int AnimationLevel
    {
        get => Config.AnimationLevel;
        set
        {
            var clamped = System.Math.Clamp(value, 0, 2);
            if (Config.AnimationLevel == clamped) return;
            Config.AnimationLevel = clamped;
            OnPropertyChanged();
            AppearanceApplier.ApplyAnimationLevel(Config);
        }
    }
}
