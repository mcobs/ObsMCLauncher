#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Services.Download;
using ObsMCLauncher.Core.Services.Mirror;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.Services;
using ObsMCLauncher.Desktop.ViewModels.Notifications;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>字体下拉项：Family 供预览渲染，Display 供显示，IsDefault 标记默认字体</summary>
public sealed record FontFamilyItem(Avalonia.Media.FontFamily Family, string Display, bool IsDefault);

public partial class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly NotificationService _notificationService;
    private bool _isInitializing;
    private CancellationTokenSource? _saveNotifyCts;

    [ObservableProperty]
    private int _selectedSettingsTab;

    partial void OnSelectedSettingsTabChanged(int value)
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsGameTab)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsAppearanceTab)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsDownloadTab)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsGeneralTab)));
    }

    public bool IsGeneralTab => SelectedSettingsTab == 0;
    public bool IsAppearanceTab => SelectedSettingsTab == 1;
    public bool IsGameTab => SelectedSettingsTab == 2;
    public bool IsDownloadTab => SelectedSettingsTab == 3;

    public void Save() => AutoSave();

    public void Reload()
    {
        _isInitializing = true;
        _config = LauncherConfig.Load();

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(ThemeMode)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AccentColor)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AccentColorValue)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AccentColorPreview)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(CornerRadius)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Density)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AnimationLevel)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(WallpaperEnabled)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(WallpaperPath)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(WallpaperOpacity)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(WallpaperStretch)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(WallpaperExtendToNav)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(NavBackgroundOpacity)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(SelectedFontItem)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(SelectedFontWeight)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MaxMemory)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MinMemory)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadSource)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadSourceDescription)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MirrorSourceMode)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MirrorSourceModeDescription)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MaxDownloadThreads)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadAssetsWithGame)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AutoCheckUpdate)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(SelectedUpdateChannel)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(UpdateChannelDisplayName)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(SkipSslValidation)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(EnableFileHashVerification)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(CloseAfterLaunch)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(GameDirectoryLocation)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(CustomGameDirectory)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsCustomGameDirectory)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(GameDirectoryType)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(JavaSelectionMode)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(JavaPath)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(CustomJavaPath)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsCustomJava)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(JvmArguments)));
        JvmArgumentsEditor.SetArguments(_config.JvmArguments);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsNavCollapsed)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(NotificationPosition)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(NotificationAutoCloseSeconds)));

        UpdateGameDirectoryDisplayText();
        _ = ReloadJavaOptionsAsync();

        Status = "设置已重新加载";
        _isInitializing = false;
    }

    private LauncherConfig _config;
    private HomeViewModel? _homeViewModel;

    /// <summary>主页自定义编辑器的视图模型（复用主页数据，改动直接生效）</summary>
    public SettingsHomeViewModel SettingsHome => _settingsHome ??= new SettingsHomeViewModel(_homeViewModel!);

    private SettingsHomeViewModel? _settingsHome;

    public SettingsViewModel(NotificationService notificationService, HomeViewModel? homeViewModel = null)
    {
        _notificationService = notificationService;
        _homeViewModel = homeViewModel;

        DownloadSourceOptions = new ObservableCollection<DownloadSource>(((DownloadSource[])Enum.GetValues(typeof(DownloadSource)))
            .Where(x => x != DownloadSource.MCBBS && x != DownloadSource.Custom));
        MirrorSourceModeOptions = new ObservableCollection<MirrorSourceMode>((MirrorSourceMode[])Enum.GetValues(typeof(MirrorSourceMode)));
        GameDirectoryLocationOptions = new ObservableCollection<DirectoryLocation>((DirectoryLocation[])Enum.GetValues(typeof(DirectoryLocation)));
        GameDirectoryTypeOptions = new ObservableCollection<GameDirectoryType>((GameDirectoryType[])Enum.GetValues(typeof(GameDirectoryType)));
        MaxDownloadThreadsOptions = new ObservableCollection<int> { 4, 8, 16, 32, 64 };
        UpdateChannelOptions = new ObservableCollection<UpdateChannel>((UpdateChannel[])Enum.GetValues(typeof(UpdateChannel)));
        JavaOptions = new ObservableCollection<JavaOption>();

        _isInitializing = true;
        _config = LauncherConfig.Load();

        JvmArgumentsEditor = new JvmArgumentsEditorViewModel
        {
            ArgumentsCommit = args =>
            {
                if (_isInitializing) return;
                _config.JvmArguments = args ?? "";
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(JvmArguments)));
                AutoSave();
            }
        };

        // 应用保存的主题模式
        ApplyThemeMode(_config.ThemeMode);

        // 启动时应用已保存的自定义强调色
        ApplyAccentColor();

        // 应用已保存的圆角 / 密度 / 动画
        ApplyCornerRadius();
        ApplyDensity();
        ApplyAnimationLevel();

        // 应用已保存的壁纸配置
        ApplyWallpaper();

        // 字体列表与应用已保存的字体设置
        LoadFontFamilies();
        ApplyFont();

        // 监听系统主题变化
        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += OnSystemThemeChanged;
        }

        BrowseGameDirectoryCommand = new AsyncRelayCommand(BrowseGameDirectoryAsync);
        BrowseJavaPathCommand = new AsyncRelayCommand(BrowseJavaPathAsync);
        BrowseWallpaperCommand = new AsyncRelayCommand(BrowseWallpaperAsync);
        TestDownloadSourceCommand = new AsyncRelayCommand(TestDownloadSourceAsync);
        ResetDefaultsCommand = new RelayCommand(ResetDefaults);
        SelectCenterNotificationCommand = new RelayCommand(() => NotificationPosition = NotificationPosition.Center);
        SelectBottomRightNotificationCommand = new RelayCommand(() => NotificationPosition = NotificationPosition.BottomRight);
        SelectTabCommand = new RelayCommand<string>(tab =>
        {
            if (int.TryParse(tab, out var index))
                SelectedSettingsTab = index;
        });

        TestDialogCommand = new AsyncRelayCommand(async () =>
        {
            try
            {
                var main = NavigationStore.MainWindow;
                if (main == null)
                {
                    Status = "MainWindow 未就绪";
                    return;
                }

                var result = await main.Dialogs.ShowQuestion(
                    "测试对话框",
                    "这是一个测试对话框，用于验证模态遮罩、按钮与关闭逻辑是否正常。",
                    ViewModels.Dialogs.DialogButtons.YesNoCancel);

                Status = $"对话框返回: {result}";
            }
            catch (Exception ex)
            {
                Status = $"弹出对话框失败: {ex.Message}";
            }
        });

        UpdateGameDirectoryDisplayText();
        _ = ReloadJavaOptionsAsync();

        Status = "设置已加载";
        _isInitializing = false;
    }

    public IAsyncRelayCommand TestDialogCommand { get; }

    public IAsyncRelayCommand BrowseGameDirectoryCommand { get; }
    public IAsyncRelayCommand BrowseJavaPathCommand { get; }
    public IAsyncRelayCommand TestDownloadSourceCommand { get; }
    public IRelayCommand ResetDefaultsCommand { get; }
    public IRelayCommand SelectCenterNotificationCommand { get; }
    public IRelayCommand SelectBottomRightNotificationCommand { get; }
    public IRelayCommand<string> SelectTabCommand { get; }

    public ObservableCollection<DownloadSource> DownloadSourceOptions { get; }

    public ObservableCollection<MirrorSourceMode> MirrorSourceModeOptions { get; }

    public ObservableCollection<DirectoryLocation> GameDirectoryLocationOptions { get; }

    public ObservableCollection<GameDirectoryType> GameDirectoryTypeOptions { get; }

    public ObservableCollection<int> MaxDownloadThreadsOptions { get; }

    public ObservableCollection<UpdateChannel> UpdateChannelOptions { get; }

    public ObservableCollection<JavaOption> JavaOptions { get; }

    private JavaOption? _selectedJavaOption;
    public JavaOption? SelectedJavaOption
    {
        get => _selectedJavaOption;
        set
        {
            if (SetProperty(ref _selectedJavaOption, value))
            {
                if (value == null) return;

                switch (value.Type)
                {
                    case JavaOptionType.Auto:
                        JavaSelectionMode = 0;
                        JavaPath = "";
                        break;
                    case JavaOptionType.Custom:
                        JavaSelectionMode = 2;
                        break;
                    case JavaOptionType.Detected:
                        JavaSelectionMode = 1;
                        JavaPath = value.Path;
                        break;
                }

                if (!_isInitializing)
                {
                    AutoSave();
                }
            }
        }
    }

    public int ThemeMode
    {
        get => _config.ThemeMode;
        set
        {
            if (_config.ThemeMode != value)
            {
                _config.ThemeMode = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(ThemeMode)));

                ApplyThemeMode(value);
                AutoSave();
            }
        }
    }

    /// <summary>强调色十六进制字符串，非法值回退默认绿</summary>
    public string AccentColor
    {
        get => ResolveAccentHex();
        set
        {
            var hex = NormalizeHex(value);
            if (hex == null) return;
            SetAccentHex(hex);
        }
    }

    /// <summary>强调色（Color 类型），供取色器绑定</summary>
    public Color AccentColorValue
    {
        get => ResolveAccentColor();
        set => SetAccentHex($"#{value.R:X2}{value.G:X2}{value.B:X2}");
    }

    /// <summary>写入配置并应用到全局</summary>
    private void SetAccentHex(string hex)
    {
        if (string.Equals(_config.AccentColor, hex, StringComparison.OrdinalIgnoreCase)) return;
        _config.AccentColor = hex;
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AccentColor)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AccentColorValue)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AccentColorPreview)));
        ApplyAccentColor();
        AutoSave();
    }

    /// <summary>当前强调色的预览画刷</summary>
    public IBrush AccentColorPreview => new SolidColorBrush(ResolveAccentColor());

    /// <summary>圆角半径（0-28），应用到主要容器</summary>
    public int CornerRadius
    {
        get => _config.CornerRadius;
        set
        {
            var clamped = Math.Clamp(value, 0, 28);
            if (_config.CornerRadius != clamped)
            {
                _config.CornerRadius = clamped;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CornerRadius)));
                ApplyCornerRadius();
                AutoSave();
            }
        }
    }

    /// <summary>密度：0=紧凑 1=标准 2=宽松，影响主要界面留白</summary>
    public int Density
    {
        get => _config.Density;
        set
        {
            var clamped = Math.Clamp(value, 0, 2);
            if (_config.Density != clamped)
            {
                _config.Density = clamped;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Density)));
                ApplyDensity();
                AutoSave();
            }
        }
    }

    /// <summary>动画级别：0=禁用 1=标准 2=华丽</summary>
    public int AnimationLevel
    {
        get => _config.AnimationLevel;
        set
        {
            var clamped = Math.Clamp(value, 0, 2);
            if (_config.AnimationLevel != clamped)
            {
                _config.AnimationLevel = clamped;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(AnimationLevel)));
                ApplyAnimationLevel();
                AutoSave();
            }
        }
    }

    public bool WallpaperEnabled
    {
        get => _config.WallpaperEnabled;
        set
        {
            if (_config.WallpaperEnabled != value)
            {
                _config.WallpaperEnabled = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(WallpaperEnabled)));
                ApplyWallpaper();
                AutoSave();
            }
        }
    }

    /// <summary>壁纸路径展示（可能为 null）</summary>
    public string WallpaperPath
    {
        get => _config.WallpaperPath ?? "";
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_config.WallpaperPath != v)
            {
                _config.WallpaperPath = v;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(WallpaperPath)));
                ApplyWallpaper();
                AutoSave();
            }
        }
    }

    public double WallpaperOpacity
    {
        get => _config.WallpaperOpacity;
        set
        {
            var v = Math.Clamp(value, 0, 1);
            if (_config.WallpaperOpacity != v)
            {
                _config.WallpaperOpacity = v;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(WallpaperOpacity)));
                ApplyWallpaper();
                AutoSave();
            }
        }
    }

    public int WallpaperStretch
    {
        get => _config.WallpaperStretch;
        set
        {
            var v = Math.Clamp(value, 0, 3);
            if (_config.WallpaperStretch != v)
            {
                _config.WallpaperStretch = v;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(WallpaperStretch)));
                ApplyWallpaper();
                AutoSave();
            }
        }
    }

    public bool WallpaperExtendToNav
    {
        get => _config.WallpaperExtendToNav;
        set
        {
            if (_config.WallpaperExtendToNav != value)
            {
                _config.WallpaperExtendToNav = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(WallpaperExtendToNav)));
                ApplyWallpaper();
                AutoSave();
            }
        }
    }

    public double NavBackgroundOpacity
    {
        get => _config.NavBackgroundOpacity;
        set
        {
            var v = Math.Clamp(value, 0.1, 1);
            if (_config.NavBackgroundOpacity != v)
            {
                _config.NavBackgroundOpacity = v;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(NavBackgroundOpacity)));
                ApplyWallpaper();
                AutoSave();
            }
        }
    }

    public IAsyncRelayCommand BrowseWallpaperCommand { get; }

    // ===== 字体 =====

    /// <summary>可选字重，与 ClassIsland 一致去掉别名项（100-950）</summary>
    public static Avalonia.Media.FontWeight[] FontWeightOptions { get; } =
    [
        Avalonia.Media.FontWeight.Thin,
        Avalonia.Media.FontWeight.ExtraLight,
        Avalonia.Media.FontWeight.Light,
        Avalonia.Media.FontWeight.Normal,
        Avalonia.Media.FontWeight.Medium,
        Avalonia.Media.FontWeight.SemiBold,
        Avalonia.Media.FontWeight.Bold,
        Avalonia.Media.FontWeight.ExtraBold,
        Avalonia.Media.FontWeight.Black,
        Avalonia.Media.FontWeight.ExtraBlack
    ];

    /// <summary>系统字体列表，首项为默认字体</summary>
    public ObservableCollection<FontFamilyItem> FontFamilies { get; } = [];

    public FontFamilyItem? SelectedFontItem
    {
        get
        {
            var name = _config.CustomFontFamily;
            return FontFamilies.FirstOrDefault(f =>
                string.IsNullOrEmpty(name) ? f.IsDefault : (!f.IsDefault && f.Family.Name == name));
        }
        set
        {
            if (value is null) return;
            var name = value.IsDefault ? null : value.Family.Name;
            if (_config.CustomFontFamily != name)
            {
                _config.CustomFontFamily = name;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(SelectedFontItem)));
                ApplyFont();
                AutoSave();
            }
        }
    }

    public Avalonia.Media.FontWeight SelectedFontWeight
    {
        get => (Avalonia.Media.FontWeight)Math.Clamp(_config.CustomFontWeight, 100, 950);
        set
        {
            if (_config.CustomFontWeight != (int)value)
            {
                _config.CustomFontWeight = (int)value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(SelectedFontWeight)));
                ApplyFont();
                AutoSave();
            }
        }
    }

    private void LoadFontFamilies()
    {
        try
        {
            // 首项固定为内置 HarmonyOS 默认字体
            var defaultName = "HarmonyOS Sans SC";
            FontFamilies.Add(new FontFamilyItem(DefaultFontFamily, $"{defaultName}（默认）", true));
            foreach (var f in FontManager.Current.SystemFonts.OrderBy(x => x.Name, StringComparer.CurrentCulture))
            {
                if (string.IsNullOrEmpty(f.Name) ||
                    f.Name.Equals(defaultName, StringComparison.OrdinalIgnoreCase)) continue;
                FontFamilies.Add(new FontFamilyItem(f, f.Name, false));
            }
        }
        catch
        {
            // 拿不到系统字体列表时仅保留空列表，默认字体仍可用
        }
    }

    private CancellationTokenSource? _fontApplyCts;

    // 同名字体复用同一实例：频繁切换时反复 new FontFamily 会触发全界面
    // 字形重排，容易出现残影/合成粗体叠加等渲染异常，缓存可显著缓解
    private readonly Dictionary<string, FontFamily> _fontFamilyCache = [];

    // 主字体缺字形时的回退链，末尾的中文字体保证中文不乱码
    private static readonly string[] CjkFallbackFonts =
        ["Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Noto Sans SC"];

    // 内置默认字体：内嵌 HarmonyOS Sans SC 优先，缺字形时按序回退到其它系统中文字体
    private static readonly FontFamily DefaultFontFamily = new(
        "avares://ObsMCLauncher.Desktop/Assets/Fonts/#HarmonyOS Sans SC, HarmonyOS Sans SC, " +
        "Microsoft YaHei UI, PingFang SC, Noto Sans SC, Segoe UI, sans-serif");

    // 构造「主字体, 平台默认, 中文字体」复合字体，缺字形时按顺序回退
    private static FontFamily BuildFontFamily(string primary)
    {
        var names = new List<string> { primary };
        var defaultName = FontManager.Current.DefaultFontFamily?.Name;
        if (!string.IsNullOrEmpty(defaultName) && !names.Contains(defaultName))
        {
            names.Add(defaultName);
        }
        foreach (var f in CjkFallbackFonts)
        {
            if (!names.Contains(f)) names.Add(f);
        }
        return new FontFamily(string.Join(", ", names));
    }

    private void ApplyFont()
    {
        _fontApplyCts?.Cancel();
        _fontApplyCts = new CancellationTokenSource();
        var token = _fontApplyCts.Token;
        Dispatcher.UIThread.Post(() =>
        {
            if (token.IsCancellationRequested) return;
            if (Application.Current?.Resources is not { } resources) return;

            FontFamily family;
            if (string.IsNullOrWhiteSpace(_config.CustomFontFamily))
            {
                // 恢复默认：显式写回内置 HarmonyOS 字体，而不是 Remove。
                // 移除键时 DynamicResource 不一定重新解析，FA 控件会残留旧字体，
                // 表现为切回默认后仍是上次字体、反复切换也恢复不了
                family = DefaultFontFamily;
            }
            else
            {
                if (!_fontFamilyCache.TryGetValue(_config.CustomFontFamily, out var cached))
                {
                    cached = BuildFontFamily(_config.CustomFontFamily);
                    _fontFamilyCache[_config.CustomFontFamily] = cached;
                }
                family = cached;
            }

            resources["GlobalFontFamily"] = family;
            // FA 控件样式显式引用该令牌，不写的话导航栏等不跟随自定义字体；
            // 恢复默认时也写回同一默认字体，保证彻底还原
            resources["ContentControlThemeFontFamily"] = family;
            resources["GlobalFontWeight"] = (FontWeight)Math.Clamp(_config.CustomFontWeight, 100, 950);
        });
    }

    // 壁纸位图与画刷缓存：调透明度/拉伸时直接复用，避免反复解码大图
    private Avalonia.Media.Imaging.Bitmap? _wallpaperBitmap;
    private string? _wallpaperBitmapPath;
    private Avalonia.Media.ImageBrush? _wallpaperBrush;

    private void ApplyWallpaper()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.Resources is not { } resources) return;

            var active = false;
            if (_config.WallpaperEnabled && !string.IsNullOrWhiteSpace(_config.WallpaperPath)
                && File.Exists(_config.WallpaperPath))
            {
                try
                {
                    if (_wallpaperBitmap is null || _wallpaperBitmapPath != _config.WallpaperPath)
                    {
                        _wallpaperBitmap = new Avalonia.Media.Imaging.Bitmap(_config.WallpaperPath);
                        _wallpaperBitmapPath = _config.WallpaperPath;
                    }
                    _wallpaperBrush ??= new Avalonia.Media.ImageBrush();
                    _wallpaperBrush.Source = _wallpaperBitmap;
                    _wallpaperBrush.Stretch = StretchMap(_config.WallpaperStretch);
                    _wallpaperBrush.Opacity = _config.WallpaperOpacity;
                    resources["MainWallpaperBrush"] = _wallpaperBrush;
                    resources["IsMainWallpaperVisible"] = true;
                    active = true;
                }
                catch
                {
                    _wallpaperBitmap = null;
                    _wallpaperBitmapPath = null;
                    _wallpaperBrush = null;
                }
            }

            if (!active)
            {
                _wallpaperBitmap = null;
                _wallpaperBitmapPath = null;
                _wallpaperBrush = null;
                resources["MainWallpaperBrush"] = null;
                resources["IsMainWallpaperVisible"] = false;
            }

            // 壁纸生效时主内容区背景让位，否则恢复主题底色
            resources["NavBackgroundBrush"] = active
                ? new SolidColorBrush(Colors.Transparent)
                : new SolidColorBrush(ResolveNavBgColor());

            // 左侧导航栏。FA 展开态实际读取 ExpandedPaneBackground，
            // DefaultPaneBackground 只覆盖左迷你/顶栏场景，两个键必须同步写
            var paneOpacity = active && _config.WallpaperExtendToNav
                ? _config.NavBackgroundOpacity
                : 1.0;
            resources["NavigationViewDefaultPaneBackground"] = new SolidColorBrush(ResolveNavBgColor())
            {
                Opacity = paneOpacity
            };
            resources["NavigationViewExpandedPaneBackground"] = new SolidColorBrush(ResolveNavBgColor())
            {
                Opacity = paneOpacity
            };
        });
    }

    /// <summary>导航栏背景基准色（深/浅跟随主题）</summary>
    private Color ResolveNavBgColor()
        => Application.Current?.ActualThemeVariant == ThemeVariant.Light
            ? Color.Parse("#FFFFFF")
            : Color.Parse("#141619");

    private static Avalonia.Media.Stretch StretchMap(int mode) => mode switch
    {
        0 => Avalonia.Media.Stretch.Fill,
        2 => Avalonia.Media.Stretch.UniformToFill,
        3 => Avalonia.Media.Stretch.None,
        _ => Avalonia.Media.Stretch.Uniform
    };

    private async Task BrowseWallpaperAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null)
                return;
#pragma warning disable CS0618
            var dlg = new OpenFileDialog
            {
                Title = "选择背景壁纸",
                AllowMultiple = false,
                Filters = new() { new FileDialogFilter { Name = "图片", Extensions = { "png", "jpg", "jpeg", "webp", "bmp" } } }
            };
            var result = await dlg.ShowAsync(desktop.MainWindow);
#pragma warning restore CS0618
            var path = result?.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(path))
            {
                WallpaperEnabled = true;
                WallpaperPath = path;
            }
        }
        catch (Exception ex)
        {
            Status = $"选择壁纸失败: {ex.Message}";
        }
    }

    private Color ResolveAccentColor()
        => Color.TryParse(ResolveAccentHex(), out var c) ? c : Color.Parse("#10B981");

    private string ResolveAccentHex()
        => string.IsNullOrWhiteSpace(_config.AccentColor) ? "#10B981" : _config.AccentColor;

    /// <summary>把任意输入规整为 #RRGGBB，无法解析返回 null</summary>
    private static string? NormalizeHex(string? value)
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

    /// <summary>应用强调色到主题资源与 FluentAvalonia 控件强调色</summary>
    private void ApplyAccentColor()
    {
        if (Application.Current == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.Styles.OfType<FluentAvalonia.Styling.FluentAvaloniaTheme>().FirstOrDefault()
                    is { } faTheme)
            {
                faTheme.CustomAccentColor = ResolveAccentColor();
            }

            // 主页欢迎卡片渐变跟随强调色：起点为压暗后的同色系
            if (Application.Current?.Resources is { } resources)
            {
                var accent = ResolveAccentColor();
                resources["HomeWelcomeGradientStart"] = Darken(accent, 0.7);
                resources["HomeWelcomeGradientEnd"] = accent;
            }

            UpdateThemeResources(_config.ThemeMode);
        });
    }

    /// <summary>按比例压暗颜色（factor 越小越暗，0.7 表示保留 70% 亮度）</summary>
    private static Color Darken(Color c, double factor)
        => Color.FromRgb(
            (byte)Math.Round(c.R * factor),
            (byte)Math.Round(c.G * factor),
            (byte)Math.Round(c.B * factor));

    /// <summary>圆角半径应用到全局圆角资源（主要容器读取）</summary>
    private void ApplyCornerRadius()
    {
        if (Application.Current?.Resources is not { } resources) return;
        var r = _config.CornerRadius;
        // 柔和的层级差异：控件略小、浮层适中、卡片取设定值
        resources["ControlCornerRadius"] = new CornerRadius(Math.Max(0, r - 4));
        resources["OverlayCornerRadius"] = new CornerRadius(r);
        resources["CardCornerRadius"] = new CornerRadius(r);
    }

    /// <summary>密度应用到主要界面留白资源（紧凑=小、宽松=大）</summary>
    private void ApplyDensity()
    {
        if (Application.Current?.Resources is not { } resources) return;
        var baseMargin = _config.Density switch
        {
            0 => 12,
            1 => 20,
            _ => 28
        };
        resources["ContentMargin"] = new Thickness(baseMargin);
        resources["InnerSpacing"] = _config.Density switch { 0 => 8, 1 => 12, _ => 16 };
    }

    /// <summary>动画级别应用到全局动画开关（主要过渡读取）</summary>
    private void ApplyAnimationLevel()
    {
        if (Application.Current?.Resources is not { } resources) return;
        resources["EnabledAnimations"] = _config.AnimationLevel != 0;
        // 华丽与标准共用动画时长，禁用时由 EnabledAnimations 关闭
        resources["DefaultTransitionDuration"] = TimeSpan.FromSeconds(_config.AnimationLevel switch
        {
            2 => 0.45,
            _ => 0.25
        });
    }

    public int MaxMemory
    {
        get => _config.MaxMemory;
        set
        {
            if (_config.MaxMemory != value)
            {
                _config.MaxMemory = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(MaxMemory)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(MemoryHint)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsMemoryHintWarning)));
                AutoSave();
            }
        }
    }

    public int MinMemory
    {
        get => _config.MinMemory;
        set
        {
            if (_config.MinMemory != value)
            {
                _config.MinMemory = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(MinMemory)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(MemoryHint)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsMemoryHintWarning)));
                AutoSave();
            }
        }
    }

    /// <summary>
    /// 全局内存配置联动提示
    /// </summary>
    public string MemoryHint =>
        MaxMemory <= MinMemory
            ? $"最大内存应大于最小内存（当前最小内存 {MinMemory} MB）"
            : "建议最大内存不超过系统可用内存的 70%";

    public bool IsMemoryHintWarning => MaxMemory <= MinMemory;

    public DownloadSource DownloadSource
    {
        get => _config.DownloadSource;
        set
        {
            if (_config.DownloadSource != value)
            {
                _config.DownloadSource = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadSource)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadSourceDescription)));
                AutoSave();
            }
        }
    }

    public string DownloadSourceDescription
        => DownloadSource == DownloadSource.BMCLAPI
            ? "使用BMCLAPI镜像加速下载，适合中国大陆用户"
            : "使用官方源（可能较慢，但更稳定）";

    public MirrorSourceMode MirrorSourceMode
    {
        get => _config.MirrorSourceMode;
        set
        {
            if (_config.MirrorSourceMode != value)
            {
                _config.MirrorSourceMode = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(MirrorSourceMode)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(MirrorSourceModeDescription)));
                AutoSave();

                if (value == MirrorSourceMode.PreferMirror)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await MirrorHealthChecker.CheckAvailabilityAsync().ConfigureAwait(false);
                        }
                        catch { }
                    });
                }
            }
        }
    }

    public string MirrorSourceModeDescription
        => MirrorSourceMode == MirrorSourceMode.PreferMirror
            ? "优先从MCIM镜像源下载Mod资源，失败时自动回退至官方源"
            : "所有资源均从官方源下载，不使用镜像加速";

    public int MaxDownloadThreads
    {
        get => _config.MaxDownloadThreads;
        set
        {
            if (_config.MaxDownloadThreads != value)
            {
                _config.MaxDownloadThreads = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(MaxDownloadThreads)));
                AutoSave();
            }
        }
    }

    public bool DownloadAssetsWithGame
    {
        get => _config.DownloadAssetsWithGame;
        set
        {
            if (_config.DownloadAssetsWithGame != value)
            {
                _config.DownloadAssetsWithGame = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadAssetsWithGame)));
                AutoSave();
            }
        }
    }

    public bool AutoCheckUpdate
    {
        get => _config.AutoCheckUpdate;
        set
        {
            if (_config.AutoCheckUpdate != value)
            {
                _config.AutoCheckUpdate = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(AutoCheckUpdate)));
                AutoSave();
            }
        }
    }

    public UpdateChannel SelectedUpdateChannel
    {
        get => _config.UpdateChannel;
        set
        {
            if (_config.UpdateChannel != value)
            {
                _config.UpdateChannel = value;
                UpdateService.SetChannel(value);
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(SelectedUpdateChannel)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(UpdateChannelDisplayName)));
                AutoSave();
            }
        }
    }

    public string UpdateChannelDisplayName => UpdateService.GetChannelDisplayName(SelectedUpdateChannel);

    public bool SkipSslValidation
    {
        get => _config.SkipSslValidation;
        set
        {
            if (_config.SkipSslValidation != value)
            {
                _config.SkipSslValidation = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(SkipSslValidation)));
                AutoSave();

                if (value)
                {
                    _notificationService.Show("安全警告",
                        "已禁用SSL证书验证，这会使你的网络请求面临中间人攻击风险。仅在信任的网络环境下使用此选项。",
                        ViewModels.Notifications.NotificationType.Warning, 8);
                }
            }
        }
    }

    public bool EnableFileHashVerification
    {
        get => _config.EnableFileHashVerification;
        set
        {
            if (_config.EnableFileHashVerification != value)
            {
                _config.EnableFileHashVerification = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(EnableFileHashVerification)));
                AutoSave();
            }
        }
    }

    public bool CloseAfterLaunch
    {
        get => _config.CloseAfterLaunch;
        set
        {
            if (_config.CloseAfterLaunch != value)
            {
                _config.CloseAfterLaunch = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CloseAfterLaunch)));
                AutoSave();
            }
        }
    }

    public DirectoryLocation GameDirectoryLocation
    {
        get => _config.GameDirectoryLocation;
        set
        {
            if (_config.GameDirectoryLocation != value)
            {
                _config.GameDirectoryLocation = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(GameDirectoryLocation)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsCustomGameDirectory)));
                UpdateGameDirectoryDisplayText();
                AutoSave();
            }
        }
    }

    public bool IsCustomGameDirectory => GameDirectoryLocation == DirectoryLocation.Custom;

    public string CustomGameDirectory
    {
        get => _config.CustomGameDirectory;
        set
        {
            if (_config.CustomGameDirectory != value)
            {
                _config.CustomGameDirectory = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CustomGameDirectory)));
                UpdateGameDirectoryDisplayText();
                AutoSave();
            }
        }
    }

    private string _gameDirectoryDisplayText = "";
    public string GameDirectoryDisplayText
    {
        get => _gameDirectoryDisplayText;
        private set
        {
            if (_gameDirectoryDisplayText != value)
            {
                _gameDirectoryDisplayText = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(GameDirectoryDisplayText)));
            }
        }
    }

    public GameDirectoryType GameDirectoryType
    {
        get => _config.GameDirectoryType;
        set
        {
            if (_config.GameDirectoryType != value)
            {
                _config.GameDirectoryType = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(GameDirectoryType)));
                AutoSave();
            }
        }
    }

    public int JavaSelectionMode
    {
        get => _config.JavaSelectionMode;
        set
        {
            if (_config.JavaSelectionMode != value)
            {
                _config.JavaSelectionMode = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(JavaSelectionMode)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsCustomJava)));
                AutoSave();
            }
        }
    }

    public bool IsCustomJava => JavaSelectionMode == 2;

    public string JavaPath
    {
        get => _config.JavaPath;
        set
        {
            if (_config.JavaPath != value)
            {
                _config.JavaPath = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(JavaPath)));
                AutoSave();
            }
        }
    }

    public string CustomJavaPath
    {
        get => _config.CustomJavaPath;
        set
        {
            if (_config.CustomJavaPath != value)
            {
                _config.CustomJavaPath = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CustomJavaPath)));
                AutoSave();
            }
        }
    }

    public string JvmArguments
    {
        get => _config.JvmArguments;
        set
        {
            if (_config.JvmArguments != value)
            {
                _config.JvmArguments = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(JvmArguments)));
                AutoSave();
            }
        }
    }

    /// <summary>全局 JVM 参数编辑器（快捷参数 + 预设 + 自由编辑），供「游戏设置」页使用。</summary>
    public JvmArgumentsEditorViewModel JvmArgumentsEditor { get; }

    public bool IsNavCollapsed
    {
        get => _config.IsNavCollapsed;
        set
        {
            if (_config.IsNavCollapsed != value)
            {
                _config.IsNavCollapsed = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsNavCollapsed)));
                AutoSave();
            }
        }
    }

    public NotificationPosition NotificationPosition
    {
        get => _config.NotificationPosition;
        set
        {
            if (_config.NotificationPosition != value)
            {
                _config.NotificationPosition = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(NotificationPosition)));

                if (NavigationStore.MainWindow != null)
                {
                    NavigationStore.MainWindow.NotificationPosition = value;
                }

                AutoSave();
            }
        }
    }

    public int NotificationAutoCloseSeconds
    {
        get => _config.NotificationAutoCloseSeconds;
        set
        {
            if (_config.NotificationAutoCloseSeconds != value)
            {
                var clamped = Math.Clamp(value, 3, 30);
                _config.NotificationAutoCloseSeconds = clamped;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(NotificationAutoCloseSeconds)));

                if (NavigationStore.MainWindow != null)
                {
                    NavigationStore.MainWindow.Notifications.AutoCloseSeconds = clamped;
                }

                AutoSave();
            }
        }
    }

    public static string GetDirectoryLocationText(DirectoryLocation location) => location switch
    {
        DirectoryLocation.AppData => OperatingSystem.IsWindows() ? "%APPDATA%\\.minecraft（默认）"
            : OperatingSystem.IsMacOS() ? "~/Library/Application Support/minecraft（默认）"
            : "~/.minecraft（默认）",
        DirectoryLocation.RunningDirectory => "运行目录\\.minecraft",
        DirectoryLocation.Custom => "自定义路径",
        _ => location.ToString()
    };

    public static string GetGameDirectoryTypeText(GameDirectoryType type) => type switch
    {
        GameDirectoryType.RootFolder => "关闭 - 所有版本共享mods文件夹",
        GameDirectoryType.VersionFolder => "开启 - 每个版本使用独立mods文件夹",
        _ => type.ToString()
    };

    private string _status = "";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    private bool _isSaveNotificationVisible;
    public bool IsSaveNotificationVisible
    {
        get => _isSaveNotificationVisible;
        set => SetProperty(ref _isSaveNotificationVisible, value);
    }

    private int _saveProgress;
    public int SaveProgress
    {
        get => _saveProgress;
        set => SetProperty(ref _saveProgress, value);
    }

    private void AutoSave()
    {
        try
        {
            _config.Save();

            if (!_isInitializing)
            {
                _saveNotifyCts?.Cancel();
                _saveNotifyCts?.Dispose();
                _saveNotifyCts = new CancellationTokenSource();
                _ = DebouncedSaveNotificationAsync(_saveNotifyCts.Token);
            }
        }
        catch (Exception ex)
        {
            Status = $"自动保存失败: {ex.Message}";
            _notificationService.Show("保存失败", ex.Message, ViewModels.Notifications.NotificationType.Error);
        }
    }

    private async Task DebouncedSaveNotificationAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Status = "设置已自动保存";
            _notificationService.ShowCountdown("设置已自动保存", "修改已生效，3秒后确认", 3);
        });
    }

    private void UpdateGameDirectoryDisplayText()
    {
        GameDirectoryDisplayText = $"当前目录：{_config.GameDirectory}";
    }

    private async Task ReloadJavaOptionsAsync()
    {
        try
        {
            Status = "正在扫描 Java...";

            var found = await JavaOptionsProvider.ScanAsync().ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                JavaOptions.Clear();

                foreach (var j in JavaOptionsProvider.BuildOptionList(found))
                    JavaOptions.Add(j);

                // 根据配置选中（直接设置字段，避免触发AutoSave）
                _selectedJavaOption = PickSelectedJavaOption(found);
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(SelectedJavaOption)));

                Status = $"Java 扫描完成：{found.Count} 个";
            });
        }
        catch (Exception ex)
        {
            Status = $"Java 扫描失败: {ex.Message}";
        }
    }

    private JavaOption PickSelectedJavaOption(List<JavaOption> found)
    {
        var auto = JavaOption.Auto();
        var custom = JavaOption.Custom();

        return JavaSelectionMode switch
        {
            0 => auto,
            2 => custom,
            _ => found.FirstOrDefault(x => string.Equals(x.Path, JavaPath, StringComparison.OrdinalIgnoreCase))
                 ?? found.FirstOrDefault()
                 ?? auto
        };
    }


    private async Task BrowseGameDirectoryAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null)
                return;

#pragma warning disable CS0618
            var dlg = new OpenFolderDialog { Title = "选择游戏目录" };
            var path = await dlg.ShowAsync(desktop.MainWindow);
#pragma warning restore CS0618

            if (!string.IsNullOrWhiteSpace(path))
            {
                CustomGameDirectory = path;
            }
        }
        catch (Exception ex)
        {
            Status = $"浏览失败: {ex.Message}";
        }
    }

    private async Task BrowseJavaPathAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null)
                return;

#pragma warning disable CS0618
            var dlg = new OpenFileDialog
            {
                Title = "选择 Java 可执行文件",
                AllowMultiple = false,
                Filters = new() { new FileDialogFilter { Name = "Java", Extensions = { "exe" } } }
            };

            var result = await dlg.ShowAsync(desktop.MainWindow);
#pragma warning restore CS0618

            var path = result?.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(path))
            {
                JavaSelectionMode = 2;
                CustomJavaPath = path;
                SelectedJavaOption = JavaOptions.FirstOrDefault(x => x.Type == JavaOptionType.Custom) ?? JavaOption.Custom();
            }
        }
        catch (Exception ex)
        {
            Status = $"浏览失败: {ex.Message}";
        }
    }

    private async Task TestDownloadSourceAsync()
    {
        var main = NavigationStore.MainWindow;
        if (main == null)
        {
            Status = "MainWindow 未就绪";
            return;
        }

        // 根据当前选择的下载源取对应的服务，并以其版本清单接口作为连通性测试目标
        var sourceName = DownloadSource == DownloadSource.Official ? "官方源" : "BMCLAPI 镜像";
        var service = DownloadSource == DownloadSource.Official
            ? (IDownloadSourceService)new MojangDownloadSourceService()
            : new BmclapiDownloadSourceService();
        var url = service.GetVersionManifestUrl();

        string message;
        NotificationType type;
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = HttpClientFactory.CreateClient(timeout: TimeSpan.FromSeconds(10));
            using var response = await client.GetAsync(url).ConfigureAwait(false);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                message = $"{sourceName}连接正常，延迟约 {sw.ElapsedMilliseconds} ms";
                type = NotificationType.Success;
            }
            else
            {
                message = $"{sourceName}响应异常（HTTP {(int)response.StatusCode}）";
                type = NotificationType.Warning;
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            message = $"{sourceName}连接失败：{ex.Message}";
            type = NotificationType.Error;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Status = $"下载源测试：{message}";
            main.Notifications.Show("下载源测试", message, type, 5);
        });
    }

    private void ResetDefaults()
    {
        _config = new LauncherConfig();

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(ThemeMode)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AccentColor)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AccentColorValue)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AccentColorPreview)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(CornerRadius)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Density)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AnimationLevel)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(WallpaperEnabled)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(WallpaperPath)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(WallpaperOpacity)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(WallpaperStretch)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(WallpaperExtendToNav)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(NavBackgroundOpacity)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(SelectedFontItem)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(SelectedFontWeight)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MaxMemory)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MinMemory)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadSource)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadSourceDescription)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MaxDownloadThreads)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadAssetsWithGame)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AutoCheckUpdate)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(SelectedUpdateChannel)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(UpdateChannelDisplayName)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(SkipSslValidation)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(EnableFileHashVerification)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(CloseAfterLaunch)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(GameDirectoryLocation)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(CustomGameDirectory)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsCustomGameDirectory)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(GameDirectoryType)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(JavaSelectionMode)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(JavaPath)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(CustomJavaPath)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsCustomJava)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(JvmArguments)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsNavCollapsed)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(NotificationPosition)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(NotificationAutoCloseSeconds)));

        UpdateGameDirectoryDisplayText();
        _ = ReloadJavaOptionsAsync();

        if (NavigationStore.MainWindow != null)
        {
            NavigationStore.MainWindow.NotificationPosition = NotificationPosition.Center;
            NavigationStore.MainWindow.Notifications.AutoCloseSeconds = 5;
        }

        ApplyCornerRadius();
        ApplyDensity();
        ApplyAnimationLevel();
        ApplyWallpaper();
        ApplyFont();

        AutoSave();
    }

    private void ApplyThemeMode(int themeMode)
    {
        if (Application.Current == null) return;

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
            UpdateThemeResources(themeMode);
        });
    }

    private void UpdateThemeResources(int themeMode)
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

        if (isLightTheme)
        {
            ApplyLightTheme(resources);
        }
        else
        {
            ApplyDarkTheme(resources);
        }

        // 主题资源会重写导航/内容背景，壁纸相关状态需要在其后重新应用
        ApplyWallpaper();
    }

    private void ApplyLightTheme(IResourceDictionary resources)
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
        resources["NavItemSelectedBackgroundBrush"] = new SolidColorBrush(ResolveAccentColor()) { Opacity = 0.10 };

        // 导航栏 / 标题栏 / 窗口 / 卡片背景，浅色模式下必须同步更新，否则会残留深色底
        resources["NavBackgroundBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
        resources["NavBorderBrush"] = new SolidColorBrush(Color.Parse("#E2E8F0"));
        resources["TitleBarBackgroundBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
        resources["TitleBarBorderBrush"] = new SolidColorBrush(Color.Parse("#E2E8F0"));
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

        ApplyTabViewTheme(resources, isLight: true, ResolveAccentColor());
    }

    private void ApplyDarkTheme(IResourceDictionary resources)
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
        resources["NavItemSelectedBackgroundBrush"] = new SolidColorBrush(ResolveAccentColor()) { Opacity = 0.08 };

        // 导航栏 / 标题栏 / 窗口 / 卡片背景，深色模式下同步恢复
        resources["NavBackgroundBrush"] = new SolidColorBrush(Color.Parse("#141619"));
        resources["NavBorderBrush"] = new SolidColorBrush(Color.Parse("#1E2128"));
        resources["TitleBarBackgroundBrush"] = new SolidColorBrush(Color.Parse("#141619"));
        resources["TitleBarBorderBrush"] = new SolidColorBrush(Color.Parse("#1E2128"));
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

        ApplyTabViewTheme(resources, isLight: false, ResolveAccentColor());
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

    private void OnSystemThemeChanged(object? sender, EventArgs e)
    {
        // 只有在跟随系统模式下才响应系统主题变化
        if (_config.ThemeMode == 2)
        {
            UpdateThemeResources(2);
        }
    }

    public void Dispose()
    {
        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged -= OnSystemThemeChanged;
        }
        _saveNotifyCts?.Cancel();
        _saveNotifyCts?.Dispose();
        _fontApplyCts?.Cancel();
        _fontApplyCts?.Dispose();
        GC.SuppressFinalize(this);
    }

}
