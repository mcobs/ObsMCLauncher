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
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Mirror;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.ViewModels.Notifications;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class SettingsViewModel : ViewModelBase
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
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MaxMemory)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MinMemory)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadSource)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadSourceDescription)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MirrorSourceMode)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MirrorSourceModeDescription)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MaxDownloadThreads)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadAssetsWithGame)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AutoCheckUpdate)));
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
        JavaOptions = new ObservableCollection<JavaOption>();
        HomeCards = new ObservableCollection<HomeCardInfo>();

        _isInitializing = true;
        _config = LauncherConfig.Load();

        // 应用保存的主题模式
        ApplyThemeMode(_config.ThemeMode);

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

    private int _windowStyleIndex;
    public int WindowStyleIndex
    {
        get => _windowStyleIndex;
        set
        {
            if (_windowStyleIndex != value)
            {
                _windowStyleIndex = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(WindowStyleIndex)));
                ApplyWindowStyle(value);
            }
        }
    }

    private Styles? _currentWindowStyle;

    private void ApplyWindowStyle(int styleIndex)
    {
        if (Application.Current == null) return;

        var styleUris = new[]
        {
            "avares://ObsMCLauncher.Desktop/Styles/AcrylicStyle.axaml",
            "avares://ObsMCLauncher.Desktop/Styles/GlassStyle.axaml",
            "avares://ObsMCLauncher.Desktop/Styles/FlatStyle.axaml",
            "avares://ObsMCLauncher.Desktop/Styles/CardStyle.axaml",
        };

        if (styleIndex < 0 || styleIndex >= styleUris.Length) return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var uri = new Uri(styleUris[styleIndex]);
                var style = AvaloniaXamlLoader.Load(uri) as Styles;
                if (style == null) return;

                // 移除旧的质感样式
                if (_currentWindowStyle != null)
                {
                    Application.Current.Styles.Remove(_currentWindowStyle);
                    _currentWindowStyle = null;
                }

                // 插入新质感样式（在 FluentTheme 之后）
                var fluentIndex = Application.Current.Styles.ToList().FindIndex(s => s is FluentTheme);
                if (fluentIndex >= 0)
                    Application.Current.Styles.Insert(fluentIndex + 1, style);
                else
                    Application.Current.Styles.Add(style);

                _currentWindowStyle = style;

                // 重新应用主题色，确保质感资源与当前主题一致
                UpdateThemeResources(_config.ThemeMode);
            }
            catch (Exception ex)
            {
                Status = $"切换质感失败: {ex.Message}";
            }
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
                AutoSave();
            }
        }
    }

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

            var found = await Task.Run(DetectAllJavaOptions).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                JavaOptions.Clear();

                var auto = JavaOption.Auto();
                JavaOptions.Add(auto);

                foreach (var j in found)
                    JavaOptions.Add(j);

                var custom = JavaOption.Custom();
                JavaOptions.Add(custom);

                // 根据配置选中（直接设置字段，避免触发AutoSave）
                _selectedJavaOption = PickSelectedJavaOption(found, auto, custom);
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(SelectedJavaOption)));

                Status = $"Java 扫描完成：{found.Count} 个";
            });
        }
        catch (Exception ex)
        {
            Status = $"Java 扫描失败: {ex.Message}";
        }
    }

    private JavaOption PickSelectedJavaOption(List<JavaOption> found, JavaOption auto, JavaOption custom)
    {
        return JavaSelectionMode switch
        {
            0 => auto,
            2 => custom,
            _ => found.FirstOrDefault(x => string.Equals(x.Path, JavaPath, StringComparison.OrdinalIgnoreCase))
                 ?? found.FirstOrDefault()
                 ?? auto
        };
    }

    private static List<JavaOption> DetectAllJavaOptions()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var normalized = Path.GetFullPath(path);
                    candidates.Add(normalized);
                }
            }
            catch
            {
            }
        }

        // PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var d = dir.Trim();
            if (OperatingSystem.IsWindows())
            {
                AddIfExists(Path.Combine(d, "javaw.exe"));
            }
            else
            {
                AddIfExists(Path.Combine(d, "java"));
            }
        }

        // JAVA_HOME
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            if (OperatingSystem.IsWindows())
            {
                AddIfExists(Path.Combine(javaHome, "bin", "javaw.exe"));
            }
            else
            {
                AddIfExists(Path.Combine(javaHome, "bin", "java"));
            }
        }

        // 常见目录
        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            foreach (var root in new[] { programFiles, programFilesX86 })
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

                foreach (var baseDir in new[] { "Java", "Eclipse Adoptium", "Eclipse Foundation", "Microsoft", "Zulu", "BellSoft", "Amazon Corretto", "Alibaba", "GraalVM", "SapMachine" })
                {
                    var dir = Path.Combine(root, baseDir);
                    if (!Directory.Exists(dir)) continue;

                    foreach (var sub in Directory.GetDirectories(dir))
                    {
                        AddIfExists(Path.Combine(sub, "bin", "javaw.exe"));
                    }
                }
            }

            // 用户级JDK目录
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var userJdksDir = Path.Combine(homeDir, ".jdks");
            if (Directory.Exists(userJdksDir))
            {
                foreach (var sub in Directory.GetDirectories(userJdksDir))
                {
                    AddIfExists(Path.Combine(sub, "bin", "javaw.exe"));
                }
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            var macDirs = new[]
            {
                "/Library/Java/JavaVirtualMachines",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Java", "JavaVirtualMachines"),
            };
            foreach (var baseDir in macDirs)
            {
                if (!Directory.Exists(baseDir)) continue;
                foreach (var sub in Directory.GetDirectories(baseDir))
                {
                    AddIfExists(Path.Combine(sub, "Contents", "Home", "bin", "java"));
                }
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            var linuxDirs = new[] { "/usr/lib/jvm", "/usr/java", "/opt/jdk", "/opt/jre", "/opt/java" };
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var userDirs = new[]
            {
                Path.Combine(homeDir, ".sdkman", "candidates", "java"),
                Path.Combine(homeDir, ".jdks"),
            };
            foreach (var baseDir in linuxDirs.Concat(userDirs))
            {
                if (!Directory.Exists(baseDir)) continue;
                foreach (var sub in Directory.GetDirectories(baseDir))
                {
                    AddIfExists(Path.Combine(sub, "bin", "java"));
                }
            }
            AddIfExists("/usr/bin/java");
        }

        var result = new List<JavaOption>();
        foreach (var exe in candidates)
        {
            var info = TryGetJavaVersion(exe);
            if (info != null)
                result.Add(info);
        }

        // 优先高版本
        result = result
            .OrderByDescending(x => x.MajorVersion)
            .ThenByDescending(x => x.Version)
            .ToList();

        return result;
    }

    private static JavaOption? TryGetJavaVersion(string javaExePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = javaExePath,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null) return null;

            var stderr = p.StandardError.ReadToEnd();
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);

            var text = (stderr + "\n" + stdout).Trim();

            var m = Regex.Match(text, "version\\s+\"(?<ver>[^\"]+)\"");
            if (!m.Success) return null;

            var ver = m.Groups["ver"].Value;
            var major = ParseMajor(ver);

            var arch = text.Contains("64-Bit", StringComparison.OrdinalIgnoreCase) ? "x64" : "x86";
            var vendor = DetectVendor(text);

            return new JavaOption(JavaOptionType.Detected, javaExePath)
            {
                Version = ver,
                MajorVersion = major,
                Architecture = arch,
                Source = vendor,
                Display = $"Java {major} ({arch}) - {vendor}"
            };
        }
        catch
        {
            return null;
        }
    }

    private static string DetectVendor(string output)
    {
        if (output.Contains("Dragonwell", StringComparison.OrdinalIgnoreCase))
            return "Alibaba Dragonwell";
        if (output.Contains("Zulu", StringComparison.OrdinalIgnoreCase))
            return "Azul Zulu";
        if (output.Contains("BellSoft", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Liberica", StringComparison.OrdinalIgnoreCase))
            return "Liberica";
        if (output.Contains("Temurin", StringComparison.OrdinalIgnoreCase))
            return "Eclipse Temurin";
        if (output.Contains("Adoptium", StringComparison.OrdinalIgnoreCase))
            return "Eclipse Adoptium";
        if (output.Contains("Corretto", StringComparison.OrdinalIgnoreCase))
            return "Amazon Corretto";
        if (output.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            return "Microsoft";
        if (output.Contains("GraalVM", StringComparison.OrdinalIgnoreCase))
            return "GraalVM";
        if (output.Contains("SapMachine", StringComparison.OrdinalIgnoreCase))
            return "SapMachine";
        if (output.Contains("Red Hat", StringComparison.OrdinalIgnoreCase))
            return "Red Hat";
        if (output.Contains("IBM", StringComparison.OrdinalIgnoreCase))
            return "IBM";
        if (output.Contains("Java(TM) SE", StringComparison.OrdinalIgnoreCase))
            return "Oracle";
        if (output.Contains("OpenJDK", StringComparison.OrdinalIgnoreCase))
            return "OpenJDK";
        return "Unknown";
    }

    private static int ParseMajor(string version)
    {
        // 1.8.x => 8
        // 17.0.10 => 17
        try
        {
            var parts = version.Split('.');
            if (parts.Length >= 2 && parts[0] == "1" && int.TryParse(parts[1], out var legacy))
                return legacy;

            if (int.TryParse(parts[0], out var major))
                return major;

            return 0;
        }
        catch
        {
            return 0;
        }
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
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MaxMemory)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MinMemory)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadSource)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadSourceDescription)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MaxDownloadThreads)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadAssetsWithGame)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AutoCheckUpdate)));
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
        // 按文档 4.2 节浅色模式色彩规范
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
        resources["NavItemSelectedBackgroundBrush"] = new SolidColorBrush(Color.Parse("#10B981")) { Opacity = 0.06 };

        // 浅色模式下的质感资源覆盖
        ApplyWindowStyleThemeOverride(resources, isLight: true);
    }

    private void ApplyDarkTheme(IResourceDictionary resources)
    {
        // 按文档 4.3 节深色模式色彩规范
        resources["BackgroundBrush"] = new SolidColorBrush(Color.Parse("#0B0D10"));
        resources["SurfaceBrush"] = new SolidColorBrush(Color.Parse("#16181D"));
        resources["SurfaceElevatedBrush"] = new SolidColorBrush(Color.Parse("#1E2128"));
        resources["SurfaceHoverBrush"] = new SolidColorBrush(Color.Parse("#252830"));
        resources["NavHoverBrush"] = new SolidColorBrush(Color.Parse("#252830"));
        resources["TextBrush"] = new SolidColorBrush(Color.Parse("#F1F5F9"));
        resources["TextSecondaryBrush"] = new SolidColorBrush(Color.Parse("#94A3B8"));
        resources["TextTertiaryBrush"] = new SolidColorBrush(Color.Parse("#64748B"));
        resources["BorderBrush"] = new SolidColorBrush(Color.Parse("#2A2E37"));
        resources["DividerBrush"] = new SolidColorBrush(Color.Parse("#1E2128"));
        resources["InputBackgroundBrush"] = new SolidColorBrush(Color.Parse("#16181D"));
        resources["InputForegroundBrush"] = new SolidColorBrush(Color.Parse("#F1F5F9"));
        resources["GlassmorphismBackgroundBrush"] = new SolidColorBrush(Color.Parse("#16181D")) { Opacity = 0.88 };
        resources["GlassmorphismBorderBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF")) { Opacity = 0.08 };
        resources["SystemControlBackgroundBaseHighBrush"] = new SolidColorBrush(Color.Parse("#0B0D10"));
        resources["SystemControlBackgroundAltHighBrush"] = new SolidColorBrush(Color.Parse("#16181D"));
        resources["SystemControlBackgroundBaseLowBrush"] = new SolidColorBrush(Color.Parse("#252830"));
        resources["SystemControlBackgroundBaseMediumBrush"] = new SolidColorBrush(Color.Parse("#1E2128"));
        resources["SystemControlForegroundBaseHighBrush"] = new SolidColorBrush(Color.Parse("#F1F5F9"));
        resources["SystemControlForegroundBaseLowBrush"] = new SolidColorBrush(Color.Parse("#2A2E37"));
        resources["NavItemSelectedBackgroundBrush"] = new SolidColorBrush(Color.Parse("#10B981")) { Opacity = 0.08 };

        // 深色模式下的质感资源覆盖
        ApplyWindowStyleThemeOverride(resources, isLight: false);
    }

    /// <summary>
    /// 根据当前质感风格和主题模式，覆盖质感相关的画刷资源
    /// </summary>
    private void ApplyWindowStyleThemeOverride(IResourceDictionary resources, bool isLight)
    {
        // 导航栏/标题栏直接使用与内容区一致的背景色，仅通过边框和阴影区分层级
        var bg = isLight ? "#F8FAFC" : "#0B0D10";
        var chromeBorder = isLight ? "#E2E8F0" : "#1E2128";
        // 深色模式下卡片使用更亮的表面色，增强与背景的对比
        var cardSurface = isLight ? "#FFFFFF" : "#1A1D24";
        var cardBorder = isLight ? "#E2E8F0" : "#30343D";

        switch (_windowStyleIndex)
        {
            case 0: // 亚克力：半透明+轻微背景模糊，卡片带柔和阴影
                resources["TitleBarBackgroundBrush"] = new SolidColorBrush(Color.Parse(bg)) { Opacity = 0.82 };
                resources["TitleBarBorderBrush"] = new SolidColorBrush(Color.Parse(isLight ? "#000000" : "#FFFFFF")) { Opacity = isLight ? 0.06 : 0.10 };
                resources["NavBackgroundBrush"] = new SolidColorBrush(Color.Parse(bg)) { Opacity = 0.82 };
                resources["NavBorderBrush"] = new SolidColorBrush(Color.Parse(isLight ? "#000000" : "#FFFFFF")) { Opacity = isLight ? 0.06 : 0.10 };
                resources["CardBackgroundBrush"] = new SolidColorBrush(Color.Parse(cardSurface));
                resources["CardBorderBrush"] = new SolidColorBrush(Color.Parse(cardBorder));
                resources["CardShadow"] = BoxShadows.Parse(isLight ? "0 4 12 0 #20000000" : "0 4 14 0 #50000000");
                resources["ChromeShadow"] = BoxShadows.Parse(isLight ? "0 2 6 0 #20000000" : "0 2 6 0 #40000000");
                resources["NavShadow"] = BoxShadows.Parse("0 0 0 0 transparent");
                resources["WindowShadow"] = BoxShadows.Parse(isLight ? "0 8 32 0 #20000000" : "0 8 32 0 #60000000");
                resources["WindowBackgroundBrush"] = new SolidColorBrush(Color.Parse(bg));
                break;
            case 1: // 磨砂玻璃：更强半透明模糊，边框更细，阴影更淡
                resources["TitleBarBackgroundBrush"] = new SolidColorBrush(Color.Parse(bg)) { Opacity = 0.6 };
                resources["TitleBarBorderBrush"] = new SolidColorBrush(Color.Parse(isLight ? "#000000" : "#FFFFFF")) { Opacity = isLight ? 0.03 : 0.06 };
                resources["NavBackgroundBrush"] = new SolidColorBrush(Color.Parse(bg)) { Opacity = 0.6 };
                resources["NavBorderBrush"] = new SolidColorBrush(Color.Parse(isLight ? "#000000" : "#FFFFFF")) { Opacity = isLight ? 0.03 : 0.06 };
                resources["CardBackgroundBrush"] = new SolidColorBrush(Color.Parse(cardSurface)) { Opacity = 0.85 };
                resources["CardBorderBrush"] = new SolidColorBrush(Color.Parse(isLight ? "#000000" : "#FFFFFF")) { Opacity = isLight ? 0.03 : 0.06 };
                resources["CardShadow"] = BoxShadows.Parse(isLight ? "0 2 8 0 #14000000" : "0 2 10 0 #40000000");
                resources["ChromeShadow"] = BoxShadows.Parse(isLight ? "0 1 4 0 #10000000" : "0 1 4 0 #30000000");
                resources["NavShadow"] = BoxShadows.Parse("0 0 0 0 transparent");
                resources["WindowShadow"] = BoxShadows.Parse(isLight ? "0 12 40 0 #30000000" : "0 12 40 0 #70000000");
                resources["WindowBackgroundBrush"] = new SolidColorBrush(Color.Parse(bg)) { Opacity = 0.95 };
                break;
            case 2: // 纯色扁平：无透明、无模糊，纯色表面，无阴影
                resources["TitleBarBackgroundBrush"] = new SolidColorBrush(Color.Parse(bg));
                resources["TitleBarBorderBrush"] = new SolidColorBrush(Color.Parse(chromeBorder));
                resources["NavBackgroundBrush"] = new SolidColorBrush(Color.Parse(bg));
                resources["NavBorderBrush"] = new SolidColorBrush(Color.Parse(chromeBorder));
                resources["CardBackgroundBrush"] = new SolidColorBrush(Color.Parse(cardSurface));
                resources["CardBorderBrush"] = new SolidColorBrush(Color.Parse(cardBorder));
                resources["CardShadow"] = BoxShadows.Parse("0 0 0 0 transparent");
                resources["ChromeShadow"] = BoxShadows.Parse("0 0 0 0 transparent");
                resources["NavShadow"] = BoxShadows.Parse("0 0 0 0 transparent");
                resources["WindowShadow"] = BoxShadows.Parse(isLight ? "0 0 0 1 #20000000" : "0 0 0 1 #40000000");
                resources["WindowBackgroundBrush"] = new SolidColorBrush(Color.Parse(bg));
                break;
            case 3: // 悬浮卡片：卡片带明显阴影与轻微上浮感，标题栏/侧边栏保持纯色
                resources["TitleBarBackgroundBrush"] = new SolidColorBrush(Color.Parse(bg));
                resources["TitleBarBorderBrush"] = new SolidColorBrush(Color.Parse(chromeBorder));
                resources["NavBackgroundBrush"] = new SolidColorBrush(Color.Parse(bg));
                resources["NavBorderBrush"] = new SolidColorBrush(Color.Parse(chromeBorder));
                resources["CardBackgroundBrush"] = new SolidColorBrush(Color.Parse(cardSurface));
                resources["CardBorderBrush"] = new SolidColorBrush(Color.Parse(cardBorder));
                resources["CardShadow"] = BoxShadows.Parse(isLight ? "0 8 24 0 #40000000" : "0 8 28 0 #70000000");
                resources["ChromeShadow"] = BoxShadows.Parse("0 0 0 0 transparent");
                resources["NavShadow"] = BoxShadows.Parse("0 0 0 0 transparent");
                resources["WindowShadow"] = BoxShadows.Parse(isLight ? "0 4 16 0 #30000000" : "0 4 16 0 #50000000");
                resources["WindowBackgroundBrush"] = new SolidColorBrush(Color.Parse(bg));
                break;
        }
    }

    private void OnSystemThemeChanged(object? sender, EventArgs e)
    {
        // 只有在跟随系统模式下才响应系统主题变化
        if (_config.ThemeMode == 2)
        {
            UpdateThemeResources(2);
        }
    }

    public sealed record JavaOption(JavaOptionType Type, string Path)
    {
        public string Display { get; init; } = "";
        public string Version { get; init; } = "";
        public int MajorVersion { get; init; }
        public string Architecture { get; init; } = "";
        public string Source { get; init; } = "";
        public bool IsPathVisible => Type == JavaOptionType.Detected && !string.IsNullOrWhiteSpace(Path);

        public override string ToString() => string.IsNullOrWhiteSpace(Display) ? Path : Display;

        public static JavaOption Auto() => new(JavaOptionType.Auto, "")
        {
            Display = "自动选择（根据游戏版本自动匹配）"
        };

        public static JavaOption Custom() => new(JavaOptionType.Custom, "")
        {
            Display = "自定义路径..."
        };
    }

    public enum JavaOptionType
    {
        Auto,
        Detected,
        Custom
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
