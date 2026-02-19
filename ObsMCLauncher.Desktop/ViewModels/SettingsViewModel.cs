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
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.ViewModels.Notifications;

namespace ObsMCLauncher.Desktop.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly NotificationService _notificationService;

    public void Save() => AutoSave();

    public void Reload()
    {
        _config = LauncherConfig.Load();

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(ThemeMode)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MaxMemory)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MinMemory)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadSource)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadSourceDescription)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(MaxDownloadThreads)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(DownloadAssetsWithGame)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AutoCheckUpdate)));
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

        UpdateGameDirectoryDisplayText();
        _ = ReloadJavaOptionsAsync();
        LoadHomeCards();

        Status = "设置已重新加载";
    }

    private LauncherConfig _config;
    private HomeViewModel? _homeViewModel;

    public SettingsViewModel(NotificationService notificationService, HomeViewModel? homeViewModel = null)
    {
        _notificationService = notificationService;
        _homeViewModel = homeViewModel;

        DownloadSourceOptions = new ObservableCollection<DownloadSource>(((DownloadSource[])Enum.GetValues(typeof(DownloadSource)))
            .Where(x => x != DownloadSource.MCBBS && x != DownloadSource.Custom));
        GameDirectoryLocationOptions = new ObservableCollection<DirectoryLocation>((DirectoryLocation[])Enum.GetValues(typeof(DirectoryLocation)));
        GameDirectoryTypeOptions = new ObservableCollection<GameDirectoryType>((GameDirectoryType[])Enum.GetValues(typeof(GameDirectoryType)));
        MaxDownloadThreadsOptions = new ObservableCollection<int> { 4, 8, 16, 32, 64 };
        JavaOptions = new ObservableCollection<JavaOption>();
        HomeCards = new ObservableCollection<HomeCardInfo>();

        _config = LauncherConfig.Load();

        // 应用保存的主题模式
        ApplyThemeMode(_config.ThemeMode);

        BrowseGameDirectoryCommand = new AsyncRelayCommand(BrowseGameDirectoryAsync);
        BrowseJavaPathCommand = new AsyncRelayCommand(BrowseJavaPathAsync);
        TestDownloadSourceCommand = new AsyncRelayCommand(TestDownloadSourceAsync);
        ResetDefaultsCommand = new RelayCommand(ResetDefaults);
        MoveCardUpCommand = new RelayCommand<HomeCardInfo>(MoveCardUp);
        MoveCardDownCommand = new RelayCommand<HomeCardInfo>(MoveCardDown);

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
    }

    public IAsyncRelayCommand TestDialogCommand { get; }

    public IAsyncRelayCommand BrowseGameDirectoryCommand { get; }
    public IAsyncRelayCommand BrowseJavaPathCommand { get; }
    public IAsyncRelayCommand TestDownloadSourceCommand { get; }
    public IRelayCommand ResetDefaultsCommand { get; }
    public IRelayCommand<HomeCardInfo> MoveCardUpCommand { get; }
    public IRelayCommand<HomeCardInfo> MoveCardDownCommand { get; }

    public ObservableCollection<DownloadSource> DownloadSourceOptions { get; }

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

                AutoSave();
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

    public static string GetDirectoryLocationText(DirectoryLocation location) => location switch
    {
        DirectoryLocation.AppData => "%APPDATA%\\.minecraft（默认）",
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
            Status = "设置已自动保存";
            _notificationService.ShowCountdown("设置已自动保存", "修改已生效，3秒后确认", 3);
        }
        catch (Exception ex)
        {
            Status = $"自动保存失败: {ex.Message}";
            _notificationService.Show("保存失败", ex.Message, ViewModels.Notifications.NotificationType.Error);
        }
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

                // 根据配置选中
                SelectedJavaOption = PickSelectedJavaOption(found, auto, custom);

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
                    candidates.Add(path);
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
            AddIfExists(Path.Combine(d, "javaw.exe"));
            AddIfExists(Path.Combine(d, "java.exe"));
        }

        // 常见目录
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        foreach (var root in new[] { programFiles, programFilesX86 })
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            foreach (var baseDir in new[] { "Java", "Eclipse Adoptium" })
            {
                var dir = Path.Combine(root, baseDir);
                if (!Directory.Exists(dir)) continue;

                foreach (var sub in Directory.GetDirectories(dir))
                {
                    AddIfExists(Path.Combine(sub, "bin", "javaw.exe"));
                    AddIfExists(Path.Combine(sub, "bin", "java.exe"));
                }
            }
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

            // 示例：java version "17.0.10"  / openjdk version "21.0.2" 2024-01-16
            var m = Regex.Match(text, "version\\s+\"(?<ver>[^\"]+)\"");
            if (!m.Success) return null;

            var ver = m.Groups["ver"].Value;
            var major = ParseMajor(ver);

            var arch = text.Contains("64-Bit", StringComparison.OrdinalIgnoreCase) ? "x64" : "x86";
            var source = text.Contains("OpenJDK", StringComparison.OrdinalIgnoreCase) ? "OpenJDK" : "Oracle/Unknown";

            return new JavaOption(JavaOptionType.Detected, javaExePath)
            {
                Version = ver,
                MajorVersion = major,
                Architecture = arch,
                Source = source,
                Display = $"☕ Java {major} ({arch}) - {source}"
            };
        }
        catch
        {
            return null;
        }
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

        UpdateGameDirectoryDisplayText();
        _ = ReloadJavaOptionsAsync();

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

        // 直接更新应用程序的资源字典
        var resources = Application.Current.Resources;
        if (resources != null)
        {
            // 根据主题模式更新资源
            if (themeMode == 1) // 浅色主题
            {
                resources["BackgroundBrush"] = new SolidColorBrush(Color.Parse("#F5F5F5"));
                resources["SurfaceBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
                resources["SurfaceElevatedBrush"] = new SolidColorBrush(Color.Parse("#FAFAFA"));
                resources["SurfaceHoverBrush"] = new SolidColorBrush(Color.Parse("#F0F0F0"));
                resources["TextBrush"] = new SolidColorBrush(Color.Parse("#202020"));
                resources["TextSecondaryBrush"] = new SolidColorBrush(Color.Parse("#5A5A5A"));
                resources["TextTertiaryBrush"] = new SolidColorBrush(Color.Parse("#8A8A8A"));
                resources["BorderBrush"] = new SolidColorBrush(Color.Parse("#E0E0E0"));
                resources["DividerBrush"] = new SolidColorBrush(Color.Parse("#E8E8E8"));
                resources["InputBackgroundBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
                resources["InputForegroundBrush"] = new SolidColorBrush(Color.Parse("#202020"));
                resources["GlassmorphismBackgroundBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF")) { Opacity = 0.92 };
                resources["GlassmorphismBorderBrush"] = new SolidColorBrush(Color.Parse("#000000")) { Opacity = 0.1 };
                resources["SystemControlBackgroundBaseHighBrush"] = new SolidColorBrush(Color.Parse("#F5F5F5"));
                resources["SystemControlBackgroundAltHighBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
                resources["SystemControlBackgroundBaseLowBrush"] = new SolidColorBrush(Color.Parse("#F0F0F0"));
                resources["SystemControlBackgroundBaseMediumBrush"] = new SolidColorBrush(Color.Parse("#FAFAFA"));
                resources["SystemControlForegroundBaseHighBrush"] = new SolidColorBrush(Color.Parse("#202020"));
                resources["SystemControlForegroundBaseLowBrush"] = new SolidColorBrush(Color.Parse("#E0E0E0"));
            }
            else // 深色主题或默认
            {
                resources["BackgroundBrush"] = new SolidColorBrush(Color.Parse("#202020"));
                resources["SurfaceBrush"] = new SolidColorBrush(Color.Parse("#2C2C2C"));
                resources["SurfaceElevatedBrush"] = new SolidColorBrush(Color.Parse("#333333"));
                resources["SurfaceHoverBrush"] = new SolidColorBrush(Color.Parse("#3A3A3A"));
                resources["TextBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
                resources["TextSecondaryBrush"] = new SolidColorBrush(Color.Parse("#ADADAD"));
                resources["TextTertiaryBrush"] = new SolidColorBrush(Color.Parse("#8A8A8A"));
                resources["BorderBrush"] = new SolidColorBrush(Color.Parse("#414141"));
                resources["DividerBrush"] = new SolidColorBrush(Color.Parse("#2C2C2C"));
                resources["InputBackgroundBrush"] = new SolidColorBrush(Color.Parse("#2C2C2C"));
                resources["InputForegroundBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
                resources["GlassmorphismBackgroundBrush"] = new SolidColorBrush(Color.Parse("#2C2C2C")) { Opacity = 0.88 };
                resources["GlassmorphismBorderBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF")) { Opacity = 0.5 };
                resources["SystemControlBackgroundBaseHighBrush"] = new SolidColorBrush(Color.Parse("#202020"));
                resources["SystemControlBackgroundAltHighBrush"] = new SolidColorBrush(Color.Parse("#2C2C2C"));
                resources["SystemControlBackgroundBaseLowBrush"] = new SolidColorBrush(Color.Parse("#3A3A3A"));
                resources["SystemControlBackgroundBaseMediumBrush"] = new SolidColorBrush(Color.Parse("#333333"));
                resources["SystemControlForegroundBaseHighBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
                resources["SystemControlForegroundBaseLowBrush"] = new SolidColorBrush(Color.Parse("#414141"));
            }
        }
    }

    public sealed record JavaOption(JavaOptionType Type, string Path)
    {
        public string Display { get; init; } = "";
        public string Version { get; init; } = "";
        public int MajorVersion { get; init; }
        public string Architecture { get; init; } = "";
        public string Source { get; init; } = "";

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
