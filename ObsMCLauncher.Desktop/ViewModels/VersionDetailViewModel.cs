using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Text.Json;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Services.Installers;
using ObsMCLauncher.Core.Services.Minecraft;
using ObsMCLauncher.Core.Services.Ui;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.ViewModels.Notifications;
using ObsMCLauncher.Desktop.ViewModels.Dialogs;

namespace ObsMCLauncher.Desktop.ViewModels;

public class VersionDetailViewModel : ViewModelBase
{
    private readonly ObsMCLauncher.Core.Services.Ui.IDispatcher? _dispatcher;
    private readonly NotificationService _notificationService;
    private readonly DialogService _dialogService;
    private readonly OptiFineService _optiFineService;
    private CancellationTokenSource? _installCts;

    private string _versionTypeText = "未知类型";
    public string VersionTypeText
    {
        get => _versionTypeText;
        set => SetProperty(ref _versionTypeText, value);
    }

    private string _versionTypeColor = "#9E9E9E"; // 默认灰色
    public string VersionTypeColor
    {
        get => _versionTypeColor;
        set => SetProperty(ref _versionTypeColor, value);
    }

    private string _totalDownloadSize = "计算中...";
    public string TotalDownloadSize
    {
        get => _totalDownloadSize;
        set => SetProperty(ref _totalDownloadSize, value);
    }

    private MinecraftVersion? _versionInfo;
    public MinecraftVersion? VersionInfo
    {
        get => _versionInfo;
        set
        {
            if (SetProperty(ref _versionInfo, value))
            {
                if (value != null)
                {
                    SelectedMcVersion = value.Id;
                    ReleaseDate = value.ReleaseTime.ToString("yyyy-MM-dd");
                    UpdateVersionTypeInfo(value.Type);
                    _ = CalculateTotalSizeAsync(value.Id);
                }
            }
        }
    }

    private void UpdateVersionTypeInfo(string type)
    {
        switch (type.ToLower())
        {
            case "release":
                VersionTypeText = "正式版";
                VersionTypeColor = "#22C55E"; // Success Green
                break;
            case "snapshot":
                VersionTypeText = "快照版";
                VersionTypeColor = "#3B82F6"; // Info Blue
                break;
            case "old_alpha":
                VersionTypeText = "远古Alpha";
                VersionTypeColor = "#F59E0B"; // Warning Orange
                break;
            case "old_beta":
                VersionTypeText = "远古Beta";
                VersionTypeColor = "#F59E0B";
                break;
            default:
                VersionTypeText = "其他";
                VersionTypeColor = "#6B7280"; // Gray
                break;
        }
    }

    private async Task CalculateTotalSizeAsync(string versionId)
    {
        try
        {
            TotalDownloadSize = "计算中...";
            var versionJson = await MinecraftVersionService.GetVersionJsonAsync(versionId);
            if (string.IsNullOrEmpty(versionJson))
            {
                TotalDownloadSize = "未知";
                return;
            }

            using var doc = JsonDocument.Parse(versionJson);
            long totalSize = 0;

            // 1. 客户端 JAR 大小
            if (doc.RootElement.TryGetProperty("downloads", out var downloads) &&
                downloads.TryGetProperty("client", out var client) &&
                client.TryGetProperty("size", out var sizeProp))
            {
                totalSize += sizeProp.GetInt64();
            }

            // 2. 库文件大小 (简单估算，因为很多库可能已存在)
            if (doc.RootElement.TryGetProperty("libraries", out var libraries))
            {
                foreach (var lib in libraries.EnumerateArray())
                {
                    if (lib.TryGetProperty("downloads", out var libDownloads) &&
                        libDownloads.TryGetProperty("artifact", out var artifact) &&
                        artifact.TryGetProperty("size", out var libSize))
                    {
                        totalSize += libSize.GetInt64();
                    }
                }
            }

            TotalDownloadSize = FormatFileSize(totalSize);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("SizeCalc", $"失败: {ex.Message}");
            TotalDownloadSize = "未知";
        }
    }

    private string _selectedMcVersion = "";
    public string SelectedMcVersion
    {
        get => _selectedMcVersion;
        set
        {
            if (_selectedMcVersion != value)
            {
                _selectedMcVersion = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(SelectedMcVersion)));
                InstallCommand.NotifyCanExecuteChanged();

                if (!string.IsNullOrWhiteSpace(_selectedMcVersion))
                {
                    _ = LoadLoaderVersionsAsync();
                }
            }
        }
    }

    private string _customVersionName = "";
    public string CustomVersionName
    {
        get => _customVersionName;
        set
        {
            if (_customVersionName != value)
            {
                _customVersionName = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CustomVersionName)));
                InstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private string _forgeVersion = "";
    public string ForgeVersion
    {
        get => _forgeVersion;
        set
        {
            if (_forgeVersion != value)
            {
                _forgeVersion = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(ForgeVersion)));
                InstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private string _fabricLoaderVersion = "";
    public string FabricLoaderVersion
    {
        get => _fabricLoaderVersion;
        set
        {
            if (_fabricLoaderVersion != value)
            {
                _fabricLoaderVersion = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(FabricLoaderVersion)));
                InstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private string _quiltLoaderVersion = "";
    public string QuiltLoaderVersion
    {
        get => _quiltLoaderVersion;
        set
        {
            if (_quiltLoaderVersion != value)
            {
                _quiltLoaderVersion = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(QuiltLoaderVersion)));
                InstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private string _neoForgeVersion = "";
    public string NeoForgeVersion
    {
        get => _neoForgeVersion;
        set
        {
            if (_neoForgeVersion != value)
            {
                _neoForgeVersion = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(NeoForgeVersion)));
                InstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _isForgeVersionsLoaded;
    public bool IsForgeVersionsLoaded
    {
        get => _isForgeVersionsLoaded;
        set
        {
            if (SetProperty(ref _isForgeVersionsLoaded, value))
            {
                OnPropertyChanged(nameof(CanSelectForge));
            }
        }
    }

    private bool _isNeoForgeVersionsLoaded;
    public bool IsNeoForgeVersionsLoaded
    {
        get => _isNeoForgeVersionsLoaded;
        set
        {
            if (SetProperty(ref _isNeoForgeVersionsLoaded, value))
            {
                OnPropertyChanged(nameof(CanSelectNeoForge));
            }
        }
    }

    private bool _isFabricVersionsLoaded;
    public bool IsFabricVersionsLoaded
    {
        get => _isFabricVersionsLoaded;
        set
        {
            if (SetProperty(ref _isFabricVersionsLoaded, value))
            {
                OnPropertyChanged(nameof(CanSelectFabric));
            }
        }
    }

    private bool _isQuiltVersionsLoaded;
    public bool IsQuiltVersionsLoaded
    {
        get => _isQuiltVersionsLoaded;
        set
        {
            if (SetProperty(ref _isQuiltVersionsLoaded, value))
            {
                OnPropertyChanged(nameof(CanSelectQuilt));
            }
        }
    }

    private bool _isOptiFineVersionsLoaded;
    public bool IsOptiFineVersionsLoaded
    {
        get => _isOptiFineVersionsLoaded;
        set
        {
            if (SetProperty(ref _isOptiFineVersionsLoaded, value))
            {
                OnPropertyChanged(nameof(CanSelectOptiFine));
            }
        }
    }

    public bool CanSelectForge => IsForgeVersionsLoaded;
    public bool CanSelectNeoForge => IsNeoForgeVersionsLoaded;
    public bool CanSelectFabric => IsFabricVersionsLoaded;
    public bool CanSelectQuilt => IsQuiltVersionsLoaded;
    public bool CanSelectOptiFine => IsOptiFineVersionsLoaded;

    private bool _isOptiFineEnabled;
    public bool IsOptiFineEnabled
    {
        get => _isOptiFineEnabled;
        set
        {
            if (SetProperty(ref _isOptiFineEnabled, value))
            {
                InstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private OptifineVersionModel? _selectedOptiFineVersion;
    public OptifineVersionModel? SelectedOptiFineVersion
    {
        get => _selectedOptiFineVersion;
        set
        {
            if (SetProperty(ref _selectedOptiFineVersion, value))
            {
                InstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private string _releaseDate = "";
    public string ReleaseDate
    {
        get => _releaseDate;
        set
        {
            if (_releaseDate != value)
            {
                _releaseDate = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(ReleaseDate)));
            }
        }
    }

    private string _status = "";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    private double _currentFileProgress;
    public double CurrentFileProgress
    {
        get => _currentFileProgress;
        set => SetProperty(ref _currentFileProgress, value);
    }

    private string _currentFileName = "";
    public string CurrentFileName
    {
        get => _currentFileName;
        set => SetProperty(ref _currentFileName, value);
    }

    private string _downloadSpeed = "0 KB/s";
    public string DownloadSpeed
    {
        get => _downloadSpeed;
        set => SetProperty(ref _downloadSpeed, value);
    }

    private string _downloadSizeStatus = "0 MB / 0 MB";
    public string DownloadSizeStatus
    {
        get => _downloadSizeStatus;
        set => SetProperty(ref _downloadSizeStatus, value);
    }

    private string _fileCountStatus = "0 / 0 个文件";
    public string FileCountStatus
    {
        get => _fileCountStatus;
        set => SetProperty(ref _fileCountStatus, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                InstallCommand.NotifyCanExecuteChanged();
                CancelInstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    // Loader selection flags
    private bool _isVanillaSelected = true;
    public bool IsVanillaSelected
    {
        get => _isVanillaSelected;
        set
        {
            if (_isVanillaSelected != value)
            {
                _isVanillaSelected = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsVanillaSelected)));
                InstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _isForgeSelected;
    public bool IsForgeSelected
    {
        get => _isForgeSelected;
        set
        {
            if (_isForgeSelected != value)
            {
                _isForgeSelected = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsForgeSelected)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CanSelectForge)));
                InstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _isNeoForgeSelected;
    public bool IsNeoForgeSelected
    {
        get => _isNeoForgeSelected;
        set
        {
            if (_isNeoForgeSelected != value)
            {
                _isNeoForgeSelected = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsNeoForgeSelected)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CanSelectNeoForge)));
                InstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _isFabricSelected;
    public bool IsFabricSelected
    {
        get => _isFabricSelected;
        set
        {
            if (_isFabricSelected != value)
            {
                _isFabricSelected = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsFabricSelected)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CanSelectFabric)));
                InstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _isQuiltSelected;
    public bool IsQuiltSelected
    {
        get => _isQuiltSelected;
        set
        {
            if (_isQuiltSelected != value)
            {
                _isQuiltSelected = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsQuiltSelected)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CanSelectQuilt)));
                InstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ObservableCollection<string> ForgeVersionOptions { get; } = new();
    public ObservableCollection<string> NeoForgeVersionOptions { get; } = new();
    public ObservableCollection<string> FabricLoaderVersionOptions { get; } = new();
    public ObservableCollection<string> QuiltLoaderVersionOptions { get; } = new();
    public ObservableCollection<OptifineVersionModel> OptiFineVersionOptions { get; } = new();

    public IRelayCommand BackCommand { get; }
    public IAsyncRelayCommand InstallCommand { get; }
    public IRelayCommand CancelInstallCommand { get; }
    public IAsyncRelayCommand LoadLoaderVersionsCommand { get; }

    public VersionDetailViewModel(ObsMCLauncher.Core.Services.Ui.IDispatcher? dispatcher, NotificationService notificationService)
    {
        _dispatcher = dispatcher;
        _notificationService = notificationService;
        _dialogService = NavigationStore.MainWindow?.Dialogs ?? new DialogService();
        _optiFineService = new OptiFineService(ObsMCLauncher.Core.Services.Minecraft.DownloadSourceManager.Instance);

        // 必须先初始化命令，因为后续的属性赋值会触发 NotifyCanExecuteChanged()
        BackCommand = new RelayCommand(BackToVersionList);
        InstallCommand = new AsyncRelayCommand(InstallAsync, CanInstall);
        CancelInstallCommand = new RelayCommand(CancelInstall, () => IsBusy);
        LoadLoaderVersionsCommand = new AsyncRelayCommand(LoadLoaderVersionsAsync, () => !IsBusy);

        var cfg = LauncherConfig.Load();
        var selectedId = cfg.SelectedVersion ?? "";
        
        // 直接赋值给字段以避免触发未完全初始化的逻辑，或者在命令初始化后赋值
        _selectedMcVersion = selectedId;
        _customVersionName = string.IsNullOrEmpty(selectedId) ? "" : $"{selectedId}-custom";

        Status = string.IsNullOrEmpty(selectedId)
            ? "未选择版本，请先在主页选择一个本地版本"
            : $"当前版本: {selectedId}";

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            _ = LoadLoaderVersionsAsync();
        }
    }

    public VersionDetailViewModel(MinecraftVersion version, ObsMCLauncher.Core.Services.Ui.IDispatcher? dispatcher, NotificationService notificationService)
        : this(dispatcher, notificationService)
    {
        VersionInfo = version;
        CustomVersionName = $"{version.Id}-custom";
    }

    private void BackToVersionList()
    {
        var main = NavigationStore.MainWindow;
        if (main == null) return;

        var target = main.NavItems?.FirstOrDefault(x => x.Title == "版本管理" || x.Title == "版本下载");
        if (target != null)
        {
            main.SelectedNavItem = target;
        }
    }

    private bool CanInstall()
    {
        if (IsBusy) return false;
        if (string.IsNullOrWhiteSpace(SelectedMcVersion)) return false;
        if (string.IsNullOrWhiteSpace(CustomVersionName)) return false;

        if (IsVanillaSelected) 
        {
            if (IsOptiFineEnabled) return SelectedOptiFineVersion != null;
            return true;
        }
        
        if (IsForgeSelected) return IsForgeVersionsLoaded && !string.IsNullOrWhiteSpace(ForgeVersion);
        if (IsNeoForgeSelected) return IsNeoForgeVersionsLoaded && !string.IsNullOrWhiteSpace(NeoForgeVersion);
        if (IsFabricSelected) return !string.IsNullOrWhiteSpace(FabricLoaderVersion);
        if (IsQuiltSelected) return !string.IsNullOrWhiteSpace(QuiltLoaderVersion);

        return true;
    }

    private void CancelInstall() => _installCts?.Cancel();

    private async Task InstallAsync()
    {
        if (!CanInstall()) return;

        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();
        var token = _installCts.Token;

        try
        {
            IsBusy = true;
            Progress = 0;
            var cfg = LauncherConfig.Load();
            var gameDir = cfg.GameDirectory;

            // 定义进度处理本地函数
            void HandleDetailedProgress(ObsMCLauncher.Core.Services.Minecraft.DownloadProgress p, double offset, double scale)
            {
                Status = p.Status;
                CurrentFileName = p.CurrentFile;
                CurrentFileProgress = p.CurrentFilePercentage;
                DownloadSpeed = FormatSpeed(p.DownloadSpeed);
                DownloadSizeStatus = $"{FormatFileSize(p.TotalDownloadedBytes)} / {FormatFileSize(p.TotalBytes)}";
                FileCountStatus = $"{p.CompletedFiles} / {p.TotalFiles} 个文件";
                Progress = offset + (p.OverallPercentage * scale / 100.0);
            }

            // 1. 处理加载器安装
            if (IsForgeSelected)
            {
                await ForgeService.InstallForgeAsync(SelectedMcVersion, ForgeVersion, gameDir, CustomVersionName, 
                    null, // 忽略简易回调
                    p => HandleDetailedProgress(p, 0, 80)); // 0-80% 为加载器安装
            }
            else if (IsFabricSelected)
            {
                await FabricService.InstallFabricAsync(SelectedMcVersion, FabricLoaderVersion, gameDir, CustomVersionName, (msg, done, speed, total) =>
                {
                    Status = msg;
                    CurrentFileName = msg;
                    DownloadSpeed = FormatSpeed(speed);
                    DownloadSizeStatus = $"{FormatFileSize(done)} / {FormatFileSize(total)}";
                    var pct = total > 0 ? (double)done / total * 100.0 : 0;
                    Progress = pct * 0.8;
                    CurrentFileProgress = pct;
                }, token);
            }
            else if (IsQuiltSelected)
            {
                await QuiltService.InstallQuiltAsync(SelectedMcVersion, QuiltLoaderVersion, gameDir, CustomVersionName, (msg, done, speed, total) =>
                {
                    Status = msg;
                    CurrentFileName = msg;
                    DownloadSpeed = FormatSpeed(speed);
                    DownloadSizeStatus = $"{FormatFileSize(done)} / {FormatFileSize(total)}";
                    var pct = total > 0 ? (double)done / total * 100.0 : 0;
                    Progress = pct * 0.8;
                    CurrentFileProgress = pct;
                }, token);
            }
            else if (IsNeoForgeSelected)
            {
                await NeoForgeService.InstallNeoForgeAsync(SelectedMcVersion, NeoForgeVersion, gameDir, CustomVersionName, (msg, pct) =>
                {
                    Status = msg;
                    CurrentFileName = msg;
                    Progress = pct * 0.8;
                    CurrentFileProgress = pct;
                });
            }

            // 2. 处理 OptiFine 安装
            if (IsOptiFineEnabled && SelectedOptiFineVersion != null)
            {
                // 兼容性校验
                if (IsForgeSelected && !string.IsNullOrEmpty(SelectedOptiFineVersion.Forge))
                {
                    if (!SelectedOptiFineVersion.Forge.Contains(ForgeVersion))
                    {
                        Status = $"警告: OptiFine 建议配合 {SelectedOptiFineVersion.Forge} 使用";
                    }
                }

                Status = "正在准备 OptiFine 安装...";
                var tempDir = Path.Combine(gameDir, "temp");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                var optiPath = Path.Combine(tempDir, SelectedOptiFineVersion.Filename);
                
                await _optiFineService.DownloadOptifineInstallerAsync(SelectedOptiFineVersion, optiPath, (msg, p, tp, b, tb) =>
                {
                    Status = msg;
                    CurrentFileName = SelectedOptiFineVersion.Filename;
                    CurrentFileProgress = p;
                    DownloadSizeStatus = $"{FormatFileSize(b)} / {FormatFileSize(tb)}";
                    Progress = 80 + (p * 0.1);
                }, token);

                if (IsForgeSelected || IsNeoForgeSelected)
                {
                    var modsDir = cfg.GetModsDirectory(CustomVersionName);
                    await _optiFineService.DownloadOptiFineAsModAsync(SelectedOptiFineVersion, modsDir, (msg, p, tp, b, tb) =>
                    {
                        Status = "正在将 OptiFine 安装为 Mod...";
                        CurrentFileName = SelectedOptiFineVersion.Filename;
                        CurrentFileProgress = p;
                        Progress = 90 + (p * 0.1);
                    }, token);
                }
                else
                {
                    var javaPath = cfg.GetActualJavaPath(SelectedMcVersion);
                    await _optiFineService.InstallOptifineAsync(SelectedOptiFineVersion, optiPath, gameDir, SelectedMcVersion, javaPath, CustomVersionName, null, (msg, p, tp, b, tb) =>
                    {
                        Status = msg;
                        CurrentFileProgress = p;
                        Progress = 80 + (p * 0.2);
                    }, token);
                }
            }

            Progress = 100;
            Status = "安装成功！";
            _notificationService.Show("安装成功", $"{SelectedMcVersion} 已成功安装", NotificationType.Success);

            // 如果开启了自动下载资源，则在后台启动下载任务
            if (cfg.DownloadAssetsWithGame)
            {
                _ = Task.Run(async () =>
                {
                    var cts = new CancellationTokenSource();
                    var task = Core.Services.Download.DownloadTaskManager.Instance.AddTask(
                        $"补全资源: {CustomVersionName}",
                        Core.Services.Download.DownloadTaskType.Resource,
                        cts);

                    try
                    {
                        await AssetsDownloadService.DownloadAndCheckAssetsAsync(
                            gameDir,
                            CustomVersionName,
                            (p, total, msg, speed) =>
                            {
                                Core.Services.Download.DownloadTaskManager.Instance.UpdateTaskProgress(task.Id, p, msg);
                            },
                            cts.Token);

                        Core.Services.Download.DownloadTaskManager.Instance.CompleteTask(task.Id);
                    }
                    catch (Exception ex)
                    {
                        Core.Services.Download.DownloadTaskManager.Instance.FailTask(task.Id, ex.Message);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            Status = "安装已取消";
        }
        catch (Exception ex)
        {
            Status = $"安装失败: {ex.Message}";
            _notificationService.Show("安装失败", ex.Message, NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024) return $"{bytesPerSecond:F1} B/s";
        if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024:F1} KB/s";
        return $"{bytesPerSecond / (1024 * 1024):F1} MB/s";
    }

    private string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }

    private async Task LoadLoaderVersionsAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedMcVersion)) return;

        try
        {
            Status = "正在加载加载器版本列表...";
            IsForgeVersionsLoaded = false;
            IsNeoForgeVersionsLoaded = false;
            IsFabricVersionsLoaded = false;
            IsQuiltVersionsLoaded = false;
            IsOptiFineVersionsLoaded = false;

            var forgeTask = ForgeService.GetForgeVersionsAsync(SelectedMcVersion);
            var fabricTask = FabricService.GetFabricVersionsAsync(SelectedMcVersion);
            var quiltTask = QuiltService.GetQuiltVersionsAsync(SelectedMcVersion);
            var neoForgeTask = NeoForgeService.GetNeoForgeVersionsAsync(SelectedMcVersion);
            var optiTask = _optiFineService.GetOptifineVersionsAsync(SelectedMcVersion);

            await Task.WhenAll(forgeTask, fabricTask, quiltTask, neoForgeTask, optiTask);

            if (_dispatcher != null)
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    UpdateOptions(
                        forgeTask.Result.Select(x => x.Version), 
                        fabricTask.Result.Select(x => x.Version), 
                        quiltTask.Result.Select(x => x.Version),
                        neoForgeTask.Result.Select(x => x.Version),
                        optiTask.Result);
                });
            }
        }
        catch (Exception ex)
        {
            Status = $"加载失败: {ex.Message}";
        }
    }

    private void UpdateOptions(IEnumerable<string> forge, IEnumerable<string> fabric, IEnumerable<string> quilt, IEnumerable<string> neoForge, IEnumerable<OptifineVersionModel> opti)
    {
        ForgeVersionOptions.Clear();
        foreach (var v in forge.Distinct()) ForgeVersionOptions.Add(v);
        ForgeVersion = ForgeVersionOptions.FirstOrDefault() ?? "";
        IsForgeVersionsLoaded = ForgeVersionOptions.Any();

        NeoForgeVersionOptions.Clear();
        foreach (var v in neoForge.Distinct()) NeoForgeVersionOptions.Add(v);
        NeoForgeVersion = NeoForgeVersionOptions.FirstOrDefault() ?? "";
        IsNeoForgeVersionsLoaded = NeoForgeVersionOptions.Any();

        FabricLoaderVersionOptions.Clear();
        foreach (var v in fabric.Distinct()) FabricLoaderVersionOptions.Add(v);
        FabricLoaderVersion = FabricLoaderVersionOptions.FirstOrDefault() ?? "";
        IsFabricVersionsLoaded = FabricLoaderVersionOptions.Any();

        QuiltLoaderVersionOptions.Clear();
        foreach (var v in quilt.Distinct()) QuiltLoaderVersionOptions.Add(v);
        QuiltLoaderVersion = QuiltLoaderVersionOptions.FirstOrDefault() ?? "";
        IsQuiltVersionsLoaded = QuiltLoaderVersionOptions.Any();

        OptiFineVersionOptions.Clear();
        // 排序：按 FullVersion 降序排列（最新优先）
        var sortedOpti = opti.OrderByDescending(x => x.FullVersion).ToList();
        foreach (var v in sortedOpti) OptiFineVersionOptions.Add(v);
        SelectedOptiFineVersion = OptiFineVersionOptions.FirstOrDefault();
        IsOptiFineVersionsLoaded = OptiFineVersionOptions.Any();

        if (!IsForgeVersionsLoaded) Status = "未能获取 Forge 版本列表，请检查网络或下载源";
        else if (!IsNeoForgeVersionsLoaded) Status = "未能获取 NeoForge 版本列表，请检查网络或下载源";
        else if (!IsFabricVersionsLoaded) Status = "未能获取 Fabric 版本列表，请检查网络或下载源";
        else if (!IsQuiltVersionsLoaded) Status = "未能获取 Quilt 版本列表，请检查网络或下载源";
        else Status = "加载器列表已更新";
    }
}
