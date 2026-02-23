using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Services.Minecraft;
using ObsMCLauncher.Desktop.ViewModels.Notifications;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class InstanceViewModel : ViewModelBase
{
    private readonly NotificationService _notificationService;
    private ObsMCLauncher.Core.Services.Minecraft.InstalledVersion? _version;
    private string _versionPath = string.Empty;
    private bool _isLoadingConfig;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _versionId = "-";

    [ObservableProperty]
    private string _actualVersion = "-";

    [ObservableProperty]
    private string _versionType = "-";

    [ObservableProperty]
    private string _lastPlayed = "-";

    [ObservableProperty]
    private string _storagePath = "-";

    [ObservableProperty]
    private int _isolationMode;

    [ObservableProperty]
    private ObservableCollection<WorldInfo> _worlds = new();

    [ObservableProperty]
    private ObservableCollection<ModInfo> _mods = new();

    [ObservableProperty]
    private bool _hasWorlds;

    [ObservableProperty]
    private bool _hasMods;

    [ObservableProperty]
    private bool _isVisible;

    public Action? OnCloseRequested { get; set; }

    public InstanceViewModel(NotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void SetVersion(ObsMCLauncher.Core.Services.Minecraft.InstalledVersion version)
    {
        _version = version;
        _versionPath = version.Path;
        IsVisible = true;
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (_version == null) return;

        IsLoading = true;

        try
        {
            await Task.Run(() =>
            {
                LoadVersionInfo();
                LoadWorlds();
                LoadMods();
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void LoadVersionInfo()
    {
        if (_version == null) return;

        VersionId = _version.Id;
        ActualVersion = _version.ActualVersionId ?? _version.Id;
        VersionType = _version.Type ?? "未知";

        var lastPlayedFile = Path.Combine(_versionPath, ".lastplayed");
        if (File.Exists(lastPlayedFile))
        {
            try
            {
                var lastPlayed = File.GetLastWriteTime(lastPlayedFile);
                LastPlayed = lastPlayed.ToString("yyyy-MM-dd HH:mm");
            }
            catch { LastPlayed = "从未"; }
        }
        else
        {
            LastPlayed = "从未";
        }

        var config = LauncherConfig.Load();
        var versionConfig = config.VersionIsolationSettings?.FirstOrDefault(v => v.VersionId == _version.Id);
        if (versionConfig != null)
        {
            _isLoadingConfig = true;
            IsolationMode = versionConfig.IsolationMode switch
            {
                "enabled" => 1,
                "disabled" => 2,
                _ => 0
            };
            _isLoadingConfig = false;
        }

        var gameDir = GetGameDirectory();
        StoragePath = gameDir;
    }

    private string GetGameDirectory()
    {
        if (_version == null) return "-";

        var config = LauncherConfig.Load();
        var useIsolation = config.GameDirectoryType == GameDirectoryType.VersionFolder;

        var versionConfig = config.VersionIsolationSettings?.FirstOrDefault(v => v.VersionId == _version.Id);
        if (versionConfig != null)
        {
            useIsolation = versionConfig.IsolationMode switch
            {
                "enabled" => true,
                "disabled" => false,
                _ => config.GameDirectoryType == GameDirectoryType.VersionFolder
            };
        }

        if (useIsolation)
        {
            return Path.Combine(Path.GetDirectoryName(_versionPath) ?? "", _version.Id);
        }

        return Path.Combine(Path.GetDirectoryName(_versionPath) ?? "", "..", "common");
    }

    private void LoadWorlds()
    {
        Worlds.Clear();
        var gameDir = GetGameDirectory();
        var savesDir = Path.Combine(gameDir, "saves");

        if (Directory.Exists(savesDir))
        {
            foreach (var dir in Directory.GetDirectories(savesDir))
            {
                try
                {
                    var levelDat = Path.Combine(dir, "level.dat");
                    if (File.Exists(levelDat))
                    {
                        var info = new WorldInfo
                        {
                            Name = Path.GetFileName(dir),
                            Path = dir,
                            LastModified = File.GetLastWriteTime(dir)
                        };
                        Worlds.Add(info);
                    }
                }
                catch { }
            }
        }

        HasWorlds = Worlds.Count > 0;
    }

    private void LoadMods()
    {
        Mods.Clear();
        var gameDir = GetGameDirectory();
        var modsDir = Path.Combine(gameDir, "mods");

        if (Directory.Exists(modsDir))
        {
            foreach (var file in Directory.GetFiles(modsDir, "*.jar"))
            {
                try
                {
                    var info = new ModInfo
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        FileName = Path.GetFileName(file),
                        Path = file,
                        Size = new FileInfo(file).Length
                    };
                    Mods.Add(info);
                }
                catch { }
            }
        }

        HasMods = Mods.Count > 0;
    }

    [RelayCommand]
    private void Close()
    {
        IsVisible = false;
        OnCloseRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (string.IsNullOrEmpty(StoragePath) || !Directory.Exists(StoragePath)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = StoragePath,
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private void OpenSavesFolder()
    {
        var gameDir = GetGameDirectory();
        var savesDir = Path.Combine(gameDir, "saves");
        if (!Directory.Exists(savesDir)) Directory.CreateDirectory(savesDir);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = savesDir,
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        var gameDir = GetGameDirectory();
        var modsDir = Path.Combine(gameDir, "mods");
        if (!Directory.Exists(modsDir)) Directory.CreateDirectory(modsDir);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = modsDir,
                UseShellExecute = true
            });
        }
        catch { }
    }

    partial void OnIsolationModeChanged(int value)
    {
        if (_version == null) return;

        var config = LauncherConfig.Load();
        config.VersionIsolationSettings ??= new();

        var existing = config.VersionIsolationSettings.FirstOrDefault(v => v.VersionId == _version.Id);
        if (existing == null)
        {
            existing = new VersionIsolationSetting { VersionId = _version.Id };
            config.VersionIsolationSettings.Add(existing);
        }

        existing.IsolationMode = value switch
        {
            1 => "enabled",
            2 => "disabled",
            _ => "global"
        };

        config.Save();

        if (!_isLoadingConfig)
        {
            _notificationService.Show("已保存", "版本隔离设置已更新", NotificationType.Success, 2);
        }
    }
}

public class WorldInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}

public class ModInfo
{
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
}
