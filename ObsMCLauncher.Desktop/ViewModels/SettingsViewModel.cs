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
using ObsMCLauncher.Core.Services.Mirror;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.Services;
using ObsMCLauncher.Desktop.ViewModels.Notifications;

namespace ObsMCLauncher.Desktop.ViewModels;

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

    public bool IsGameTab => SelectedSettingsTab == 0;
    public bool IsAppearanceTab => SelectedSettingsTab == 1;
    public bool IsDownloadTab => SelectedSettingsTab == 2;
    public bool IsGeneralTab => SelectedSettingsTab == 3;

    public void Save() => AutoSave();

    public void Reload()
    {
        _isInitializing = true;
        _config = LauncherConfig.Load();

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(ThemeMode)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AccentColor)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AccentColorPreview)));
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
        LoadHomeCards();

        Status = "设置已重新加载";
        _isInitializing = false;
    }

    private LauncherConfig _config;
    private HomeViewModel? _homeViewModel;

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
        HomeCards = new ObservableCollection<HomeCardInfo>();

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

        // 监听系统主题变化
        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += OnSystemThemeChanged;
        }

        BrowseGameDirectoryCommand = new AsyncRelayCommand(BrowseGameDirectoryAsync);
        BrowseJavaPathCommand = new AsyncRelayCommand(BrowseJavaPathAsync);
        TestDownloadSourceCommand = new AsyncRelayCommand(TestDownloadSourceAsync);
        ResetDefaultsCommand = new RelayCommand(ResetDefaults);
        MoveCardUpCommand = new RelayCommand<HomeCardInfo>(MoveCardUp);
        MoveCardDownCommand = new RelayCommand<HomeCardInfo>(MoveCardDown);
        SelectCenterNotificationCommand = new RelayCommand(() => NotificationPosition = NotificationPosition.Center);
        SelectBottomRightNotificationCommand = new RelayCommand(() => NotificationPosition = NotificationPosition.BottomRight);
        SelectTabCommand = new RelayCommand<string>(tab =>
        {
            if (int.TryParse(tab, out var index))
                SelectedSettingsTab = index;
        });

        SetAccentColorCommand = new RelayCommand<string>(hex => AccentColor = hex ?? "");

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
        LoadHomeCards();

        Status = "设置已加载";
        _isInitializing = false;
    }

    public IAsyncRelayCommand TestDialogCommand { get; }

    public IAsyncRelayCommand BrowseGameDirectoryCommand { get; }
    public IAsyncRelayCommand BrowseJavaPathCommand { get; }
    public IAsyncRelayCommand TestDownloadSourceCommand { get; }
    public IRelayCommand ResetDefaultsCommand { get; }
    public IRelayCommand<HomeCardInfo> MoveCardUpCommand { get; }
    public IRelayCommand<HomeCardInfo> MoveCardDownCommand { get; }
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

    public ObservableCollection<HomeCardInfo> HomeCards { get; }

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
            if (!string.Equals(_config.AccentColor, hex, StringComparison.OrdinalIgnoreCase))
            {
                _config.AccentColor = hex;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(AccentColor)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(AccentColorPreview)));
                ApplyAccentColor();
                AutoSave();
            }
        }
    }

    /// <summary>预设色板（hex 列表），供设置页快速选择</summary>
    public ObservableCollection<string> PresetAccentColors { get; } = new()
    {
        "#10B981", "#3B82F6", "#8B5CF6", "#EC4899",
        "#EF4444", "#F59E0B", "#06B6D4", "#F97316", "#6366F1", "#14B8A6"
    };

    /// <summary>当前强调色的预览画刷</summary>
    public IBrush AccentColorPreview => new SolidColorBrush(ResolveAccentColor());

    public IRelayCommand<string> SetAccentColorCommand { get; }

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
            UpdateThemeResources(_config.ThemeMode);
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

        main.Notifications.Show("下载源测试", $"当前下载源: {DownloadSource}", ViewModels.Notifications.NotificationType.Info, 3);
        await Task.CompletedTask;
    }

    private void ResetDefaults()
    {
        _config = new LauncherConfig();

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(ThemeMode)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AccentColor)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AccentColorPreview)));
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
        GC.SuppressFinalize(this);
    }

    #region 主页卡片管理

    public void RefreshPluginCards()
    {
        if (_homeViewModel == null) return;

        // 获取当前所有插件卡片
        var pluginCards = _homeViewModel.HomeCards.Where(c => c.IsPluginCard).ToList();
        var cardConfigs = _config.HomeCards ?? new List<HomeCardConfig>();

        // 移除现有的插件卡片
        var existingPluginCards = HomeCards.Where(c => c.IsPluginCard).ToList();
        foreach (var card in existingPluginCards)
        {
            HomeCards.Remove(card);
        }

        // 添加新的插件卡片
        foreach (var pluginCard in pluginCards)
        {
            var cardConfig = cardConfigs.FirstOrDefault(c => c.CardId == pluginCard.CardId);
            var isEnabled = cardConfig?.IsEnabled ?? true;

            var cardCopy = new HomeCardInfo
            {
                CardId = pluginCard.CardId,
                Title = pluginCard.Title,
                Description = pluginCard.Description,
                Icon = pluginCard.Icon,
                CommandId = pluginCard.CommandId,
                Payload = pluginCard.Payload,
                IsPluginCard = true,
                PluginId = pluginCard.PluginId,
                IsEnabled = isEnabled,
                Order = cardConfig?.Order ?? (HomeCards.Count + 1000) // 放在最后
            };
            HomeCards.Add(cardCopy);
        }

        // 重新排序
        var sortedCards = HomeCards.OrderBy(c => c.Order).ToList();
        HomeCards.Clear();
        foreach (var card in sortedCards)
        {
            HomeCards.Add(card);
        }

        for (int i = 0; i < HomeCards.Count; i++)
        {
            HomeCards[i].Order = i;
        }

        DebugLogger.Info("Settings", $"已刷新插件卡片，共 {pluginCards.Count} 个");
    }

    private void LoadHomeCards()
    {
        HomeCards.Clear();

        var defaultCards = new List<HomeCardInfo>
        {
            new HomeCardInfo { CardId = "welcome", Title = "欢迎使用黑曜石启动器！", Description = "开始你的Minecraft之旅", Icon = "🎉", Order = 0 },
            new HomeCardInfo { CardId = "news", Title = "查看最新的 Minecraft 新闻", Description = "了解游戏动态", Icon = "📰", Order = 1 },
            new HomeCardInfo { CardId = "multiplayer", Title = "多人联机", Description = "加入服务器与好友一起游戏", Icon = "🌐", CommandId = "navigate:multiplayer", Order = 2 },
            new HomeCardInfo { CardId = "mods", Title = "资源下载", Description = "下载Mod、材质包等资源", Icon = "📦", CommandId = "navigate:resources", Order = 3 }
        };

        var cardConfigs = _config.HomeCards ?? new List<HomeCardConfig>();

        foreach (var card in defaultCards)
        {
            var cardConfig = cardConfigs.FirstOrDefault(c => c.CardId == card.CardId);
            card.IsEnabled = cardConfig?.IsEnabled ?? true;
            card.Order = cardConfig?.Order ?? defaultCards.IndexOf(card);
            HomeCards.Add(card);
        }

        // 添加插件卡片（从HomeViewModel获取）
        if (_homeViewModel != null)
        {
            var pluginCards = _homeViewModel.HomeCards.Where(c => c.IsPluginCard).ToList();
            foreach (var pluginCard in pluginCards)
            {
                // 从配置中获取插件卡片的状态
                var cardConfig = cardConfigs.FirstOrDefault(c => c.CardId == pluginCard.CardId);
                var isEnabled = cardConfig?.IsEnabled ?? true;

                var cardCopy = new HomeCardInfo
                {
                    CardId = pluginCard.CardId,
                    Title = pluginCard.Title,
                    Description = pluginCard.Description,
                    Icon = pluginCard.Icon,
                    CommandId = pluginCard.CommandId,
                    Payload = pluginCard.Payload,
                    IsPluginCard = true,
                    PluginId = pluginCard.PluginId,
                    IsEnabled = isEnabled,
                    Order = cardConfig?.Order ?? (HomeCards.Count + 1000) // 放在最后
                };
                HomeCards.Add(cardCopy);
            }
        }

        var sortedCards = HomeCards.OrderBy(c => c.Order).ToList();
        HomeCards.Clear();
        foreach (var card in sortedCards)
        {
            HomeCards.Add(card);
        }

        for (int i = 0; i < HomeCards.Count; i++)
        {
            HomeCards[i].Order = i;
        }
    }

    public void OnCardEnabledChanged(HomeCardInfo card)
    {
        if (card == null) return;

        // 插件卡片也需要保存到配置中
        var cardConfig = _config.HomeCards.FirstOrDefault(c => c.CardId == card.CardId);
        if (cardConfig == null)
        {
            cardConfig = new HomeCardConfig
            {
                CardId = card.CardId,
                IsEnabled = card.IsEnabled,
                Order = card.Order,
                IsPluginCard = card.IsPluginCard,
                PluginId = card.PluginId
            };
            _config.HomeCards.Add(cardConfig);
        }
        else
        {
            cardConfig.IsEnabled = card.IsEnabled;
            cardConfig.IsPluginCard = card.IsPluginCard;
            cardConfig.PluginId = card.PluginId;
        }

        _config.Save();
        RefreshHomeCards();

        DebugLogger.Info("Settings", $"卡片状态改变: {card.Title} (插件卡片: {card.IsPluginCard}) -> {card.IsEnabled}");
    }

    private void MoveCardUp(HomeCardInfo? card)
    {
        if (card == null) return;

        var index = HomeCards.IndexOf(card);
        if (index <= 0) return;

        HomeCards.RemoveAt(index);
        HomeCards.Insert(index - 1, card);

        ApplyCardOrder();
    }

    private void MoveCardDown(HomeCardInfo? card)
    {
        if (card == null) return;

        var index = HomeCards.IndexOf(card);
        if (index < 0 || index >= HomeCards.Count - 1) return;

        HomeCards.RemoveAt(index);
        HomeCards.Insert(index + 1, card);

        ApplyCardOrder();
    }

    private void ApplyCardOrder()
    {
        for (int i = 0; i < HomeCards.Count; i++)
        {
            HomeCards[i].Order = i;

            var cardConfig = _config.HomeCards.FirstOrDefault(c => c.CardId == HomeCards[i].CardId);
            if (cardConfig == null)
            {
                cardConfig = new HomeCardConfig
                {
                    CardId = HomeCards[i].CardId,
                    IsEnabled = HomeCards[i].IsEnabled,
                    Order = i,
                    IsPluginCard = HomeCards[i].IsPluginCard,
                    PluginId = HomeCards[i].PluginId
                };
                _config.HomeCards.Add(cardConfig);
            }
            else
            {
                cardConfig.Order = i;
                cardConfig.IsEnabled = HomeCards[i].IsEnabled;
                cardConfig.IsPluginCard = HomeCards[i].IsPluginCard;
                cardConfig.PluginId = HomeCards[i].PluginId;
            }
        }

        _config.Save();
        RefreshHomeCards();
    }

    private void RefreshHomeCards()
    {
        if (NavigationStore.MainWindow?.NavItems.FirstOrDefault(x => x.Title == "主页")?.Page is HomeViewModel homeVm)
        {
            homeVm.RefreshHomeCards();
        }
    }

    #endregion
}
