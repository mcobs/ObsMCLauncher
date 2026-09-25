using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Modpack;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.ViewModels.Notifications;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 实例页「导出」标签页。
///
/// 布局是三段式（选项区 / 文件树 / 固定操作条），对应 <c>ModpackExportPage.axaml</c> 的
/// <c>RowDefinitions="Auto,*,Auto"</c> —— 树必须拿到确定高度、自己滚动，不能套在页面级 ScrollViewer 里
/// （否则两级滚动抢滚轮，且树会失去虚拟化）。
/// </summary>
public partial class ModpackExportPageViewModel : ObservableObject
{
    private readonly NotificationService _notificationService;

    private string _versionId = "";
    private string _versionDirectory = "";
    private string _runDirectory = "";
    private bool _isIsolated = true;

    private ModpackScanResult? _scan;
    private CancellationTokenSource? _exportCts;
    private bool _hasContext;

    public ModpackExportPageViewModel(NotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    // ===== 选项 =====

    /// <summary>0 = Modrinth (.mrpack)，1 = CurseForge (.zip)。</summary>
    [ObservableProperty]
    private int _formatIndex;

    [ObservableProperty]
    private string _packName = "";

    [ObservableProperty]
    private string _packVersion = "1.0.0";

    [ObservableProperty]
    private string _author = "";

    [ObservableProperty]
    private string _packDescription = "";

    [ObservableProperty]
    private string _homepageUrl = "";

    [ObservableProperty]
    private bool _resolveRemoteFiles = true;

    public bool IsCurseForgeFormat => FormatIndex == 1;

    /// <summary>给 RadioButton 双向绑定用（RadioButton 的 IsChecked 是 bool）。</summary>
    public bool IsFormatModrinth
    {
        get => FormatIndex == 0;
        set
        {
            if (value)
                FormatIndex = 0;
        }
    }

    public bool IsFormatCurseForge
    {
        get => FormatIndex == 1;
        set
        {
            if (value)
                FormatIndex = 1;
        }
    }

    public string FormatHint => IsCurseForgeFormat
        ? "CurseForge 包（.zip）：兼容 CurseForge 启动器与 PCL/HMCL；作者为必填项。"
        : "Modrinth 包（.mrpack）：跨启动器通用，去重粒度最好。";

    public bool IsAuthorRequired => IsCurseForgeFormat;

    // ===== 扫描 =====

    [ObservableProperty]
    private ObservableCollection<ModpackExportFileNode> _rootNodes = new();

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _hasScanResult;

    [ObservableProperty]
    private string _scanSummary = "";

    [ObservableProperty]
    private string _scanWarning = "";

    [ObservableProperty]
    private bool _hasScanWarning;

    [ObservableProperty]
    private string _isolationWarning = "";

    [ObservableProperty]
    private bool _hasIsolationWarning;

    // ===== 选择统计 =====

    [ObservableProperty]
    private int _selectedFileCount;

    [ObservableProperty]
    private string _selectedSizeText = "0 B";

    // ===== 导出 =====

    [ObservableProperty]
    private string _outputPath = "";

    [ObservableProperty]
    private string _outputPathDisplay = "未选择（导出时会询问）";

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private double _exportProgress;

    [ObservableProperty]
    private string _exportStatus = "";

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private string _resultSummary = "";

    [ObservableProperty]
    private string _resultWarnings = "";

    [ObservableProperty]
    private bool _hasResultWarnings;

    /// <summary>是否可以开始导出（选项合法 + 有选中内容 + 当前没有在导出）。</summary>
    public bool CanExport => !IsExporting
                             && !IsScanning
                             && SelectedFileCount > 0
                             && !string.IsNullOrWhiteSpace(PackName)
                             && !string.IsNullOrWhiteSpace(PackVersion)
                             && (!IsCurseForgeFormat || !string.IsNullOrWhiteSpace(Author));

    // ===== 上下文 =====

    /// <summary>
    /// 由 <c>InstanceViewModel</c> 在切换版本时调用（<c>SetVersion</c> 是唯一版本切换点）。
    /// </summary>
    public void SetContext(string versionId, string versionDirectory, string runDirectory, bool isIsolated)
    {
        var changed = !string.Equals(_versionId, versionId, StringComparison.Ordinal)
                      || !string.Equals(runDirectory, _runDirectory, StringComparison.OrdinalIgnoreCase);

        _hasContext = true;
        _versionId = versionId;
        _versionDirectory = versionDirectory;
        _runDirectory = runDirectory;
        _isIsolated = isIsolated;

        if (!changed)
            return;

        ResetForNewVersion();
        _ = RescanAsync();
    }

    private void ResetForNewVersion()
    {
        CancelExport();

        _scan = null;
        RootNodes = new ObservableCollection<ModpackExportFileNode>();
        HasScanResult = false;
        ScanSummary = "";
        ScanWarning = "";
        HasScanWarning = false;
        SelectedFileCount = 0;
        SelectedSizeText = "0 B";
        OutputPath = "";
        OutputPathDisplay = "未选择（导出时会询问）";
        HasResult = false;
        ResultSummary = "";
        ResultWarnings = "";
        HasResultWarnings = false;
        ExportProgress = 0;
        ExportStatus = "";

        PackName = _versionId;
        PackVersion = "1.0.0";
        Author = "";
        PackDescription = "";
        HomepageUrl = "";
        FormatIndex = 0;
        ResolveRemoteFiles = true;

        IsolationWarning = _isIsolated
            ? ""
            : "当前版本未启用版本隔离：导出范围是整个游戏目录，请留意勾选内容（存档、其它版本的共享文件都在里面）。";
        HasIsolationWarning = !_isIsolated;

        OnPropertyChanged(nameof(IsCurseForgeFormat));
        OnPropertyChanged(nameof(IsFormatModrinth));
        OnPropertyChanged(nameof(IsFormatCurseForge));
        OnPropertyChanged(nameof(FormatHint));
        OnPropertyChanged(nameof(IsAuthorRequired));
        RaiseCanExportChanged();
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        if (!_hasContext || IsScanning || IsExporting)
            return;

        IsScanning = true;
        HasScanWarning = false;
        ScanWarning = "";
        RaiseCanExportChanged();

        try
        {
            var options = new ModpackExportOptions
            {
                RunDirectory = _runDirectory,
                VersionDirectory = _versionDirectory,
                VersionName = _versionId,
                OutputPath = OutputPath
            };

            var scan = await Task.Run(() => ModpackFileScanner.Scan(options));

            _scan = scan;

            var nodes = new ObservableCollection<ModpackExportFileNode>();
            foreach (var root in scan.Roots)
                nodes.Add(ModpackExportFileNode.Build(root, UpdateSelectionStats));

            RootNodes = nodes;

            // 必须先把树建好再回填 OwnedFilePaths（它要按树结构找"最深节点"）
            AttachOwnedFiles(scan);

            HasScanResult = true;

            ScanSummary = scan.AllFiles.Count == 0
                ? "运行目录里没有可导出的内容。"
                : $"共 {scan.AllFiles.Count} 个可导出文件 · {FormatSize(scan.TotalBytes)}"
                  + (scan.HiddenCount > 0 ? $"（已自动排除 {scan.HiddenCount} 项日志/缓存等）" : "");

            if (scan.Warnings.Count > 0)
            {
                ScanWarning = string.Join("\n", scan.Warnings.Take(5));
                HasScanWarning = true;
            }

            UpdateSelectionStats();
        }
        catch (Exception ex)
        {
            HasScanResult = false;
            ScanWarning = ex.Message;
            HasScanWarning = true;
            DebugLogger.Error("ModpackExport", $"扫描导出内容失败: {ex.Message}");
        }
        finally
        {
            IsScanning = false;
            RaiseCanExportChanged();
        }
    }

    /// <summary>
    /// 把每个文件挂到"最深的那个树节点"上（每个文件只属于一个节点）。
    /// 这样"勾选某个目录"就是取它的 <c>OwnedFilePaths</c>，既不会重复也不会漏掉深度受限目录里的文件。
    /// </summary>
    private void AttachOwnedFiles(ModpackScanResult scan)
    {
        var nodeMap = new Dictionary<string, ModpackExportFileNode>(StringComparer.OrdinalIgnoreCase);
        IndexNodes(RootNodes, nodeMap);

        foreach (var file in scan.AllFiles)
        {
            var owner = FindDeepestNode(nodeMap, file.RelativePath);
            owner?.OwnedFilePaths.Add(file.RelativePath);
        }
    }

    private static void IndexNodes(
        IEnumerable<ModpackExportFileNode> nodes,
        Dictionary<string, ModpackExportFileNode> map)
    {
        foreach (var node in nodes)
        {
            map[node.RelativePath] = node;
            IndexNodes(node.Children, map);
        }
    }

    private static ModpackExportFileNode? FindDeepestNode(
        IReadOnlyDictionary<string, ModpackExportFileNode> map,
        string relativePath)
    {
        var candidate = relativePath;
        while (!string.IsNullOrEmpty(candidate))
        {
            if (map.TryGetValue(candidate, out var node))
                return node;

            var slash = candidate.LastIndexOf('/');
            if (slash <= 0)
                return null;
            candidate = candidate.Substring(0, slash);
        }

        return null;
    }

    // ===== 选择 =====

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var node in RootNodes)
            node.SetCheckedRecursive(true);
        UpdateSelectionStats();
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var node in RootNodes)
            node.SetCheckedRecursive(false);
        UpdateSelectionStats();
    }

    [RelayCommand]
    private void ApplyDefaults()
    {
        foreach (var node in RootNodes)
            node.ApplyDefaults();
        UpdateSelectionStats();
    }

    /// <summary>
    /// 请求把展开/收起应用到视图。
    ///
    /// TreeViewItem.IsExpanded 在 Avalonia 11.3.11 上**不能用绑定驱动**
    /// （ItemContainerTheme 的 ControlTheme Setter、普通 Style 的 Setter 两种写法都会崩），
    /// 所以 VM 只负责改节点状态，实际落到容器上由视图完成。
    /// </summary>
    public event Action<bool>? TreeExpansionRequested;

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var node in RootNodes)
            node.SetExpandedRecursive(true);
        TreeExpansionRequested?.Invoke(true);
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var node in RootNodes)
            node.SetExpandedRecursive(false);
        TreeExpansionRequested?.Invoke(false);
    }

    /// <summary>树里任何一次勾选变更后由视图调用（也用于初始化）。</summary>
    public void UpdateSelectionStats()
    {
        var files = new List<string>();
        foreach (var node in RootNodes)
            node.CollectCheckedFiles(files);

        SelectedFileCount = files.Count;
        SelectedSizeText = FormatSize(EstimateSelectedBytes(files));
        RaiseCanExportChanged();
    }

    private long EstimateSelectedBytes(List<string> files)
    {
        if (_scan == null || files.Count == 0)
            return 0;

        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _scan.AllFiles)
            sizes[entry.RelativePath] = entry.Length;

        long total = 0;
        foreach (var file in files)
        {
            if (sizes.TryGetValue(file, out var length))
                total += length;
        }
        return total;
    }

    // ===== 导出 =====

    [RelayCommand]
    private async Task ChooseOutputPathAsync()
    {
        var file = await PickOutputPathAsync();
        if (file == null)
            return;

        OutputPath = file;
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (!CanExport)
            return;

        // 输出位置留空时在这里补问（对齐 PCL：配置里给了路径就直接用）
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            var picked = await PickOutputPathAsync();
            if (string.IsNullOrWhiteSpace(picked))
                return;
            OutputPath = picked;
        }

        var files = new List<string>();
        foreach (var node in RootNodes)
            node.CollectCheckedFiles(files);

        if (files.Count == 0)
        {
            _notificationService.Show("无法导出", "没有选中任何文件", NotificationType.Warning);
            return;
        }

        IsExporting = true;
        HasResult = false;
        HasResultWarnings = false;
        ExportProgress = 0;
        ExportStatus = "正在准备导出...";
        RaiseCanExportChanged();

        _exportCts = new CancellationTokenSource();
        var token = _exportCts.Token;

        try
        {
            var options = new ModpackExportOptions
            {
                Format = IsCurseForgeFormat ? ModpackExportFormat.CurseForge : ModpackExportFormat.Modrinth,
                Name = PackName.Trim(),
                Version = PackVersion.Trim(),
                Author = Author.Trim(),
                Description = PackDescription.Trim(),
                Url = HomepageUrl.Trim(),
                ResolveRemoteFiles = ResolveRemoteFiles,
                RunDirectory = _runDirectory,
                VersionDirectory = _versionDirectory,
                VersionName = _versionId,
                OutputPath = OutputPath,
                IncludePaths = files
            };

            var progress = new Progress<ModpackExportProgress>(p =>
            {
                ExportProgress = Math.Clamp(p.Percentage, 0, 100);
                ExportStatus = p.Message;
            });

            var result = await ModpackExportService.ExportAsync(options, progress, token);

            ExportProgress = 100;
            ExportStatus = "导出完成";
            HasResult = true;
            ResultSummary = BuildResultSummary(result);

            if (result.Warnings.Count > 0)
            {
                ResultWarnings = string.Join("\n", result.Warnings.Take(8));
                HasResultWarnings = true;
            }

            _notificationService.Show("导出完成", $"整合包已导出到 {Path.GetFileName(result.OutputPath)}",
                NotificationType.Success, 3);
        }
        catch (OperationCanceledException)
        {
            ExportStatus = "已取消";
            ExportProgress = 0;
            _notificationService.Show("已取消", "导出已取消，未完成的文件已清理", NotificationType.Info);
        }
        catch (Exception ex)
        {
            ExportStatus = "导出失败";
            ExportProgress = 0;
            HasResult = false;
            ScanWarning = ex.Message;
            HasScanWarning = true;
            _notificationService.Show("导出失败", ex.Message, NotificationType.Error);
            DebugLogger.Error("ModpackExport", $"导出失败: {ex}");
        }
        finally
        {
            _exportCts?.Dispose();
            _exportCts = null;
            IsExporting = false;
            RaiseCanExportChanged();
        }
    }

    [RelayCommand]
    private void CancelExport()
    {
        try
        {
            _exportCts?.Cancel();
        }
        catch
        {
            // 取消本身失败无所谓，导出流程会自己收尾
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        try
        {
            var directory = Path.GetDirectoryName(OutputPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return;

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("ModpackExport", $"打开导出目录失败: {ex.Message}");
        }
    }

    private async Task<string?> PickOutputPathAsync()
    {
        var storageProvider = (Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.StorageProvider;
        if (storageProvider == null)
            return null;

        var isMrpack = !IsCurseForgeFormat;
        var extension = isMrpack ? ".mrpack" : ".zip";
        var suggested = $"{SafeFileName(PackName)}{(string.IsNullOrWhiteSpace(PackVersion) ? "" : " " + PackVersion.Trim())}{extension}";

        var fileTypes = isMrpack
            ? new[]
            {
                new FilePickerFileType("Modrinth 整合包") { Patterns = new[] { "*.mrpack" } },
                new FilePickerFileType("压缩文件") { Patterns = new[] { "*.zip" } }
            }
            : new[]
            {
                new FilePickerFileType("CurseForge 整合包") { Patterns = new[] { "*.zip" } },
                new FilePickerFileType("Modrinth 整合包") { Patterns = new[] { "*.mrpack" } }
            };

        try
        {
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "选择导出位置",
                SuggestedFileName = suggested,
                DefaultExtension = extension.TrimStart('.'),
                FileTypeChoices = fileTypes
            });

            if (file == null)
                return null;

            var path = file.Path.LocalPath;
            OutputPath = path;
            OutputPathDisplay = path;

            // 用户手动把扩展名删掉时补回来
            if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
            {
                path += extension;
                OutputPath = path;
                OutputPathDisplay = path;
            }

            return OutputPath;
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("ModpackExport", $"选择导出位置失败: {ex.Message}");
            return null;
        }
    }

    private string BuildResultSummary(ModpackExportResult result)
    {
        var parts = new List<string>
        {
            $"{result.TotalFiles} 个文件",
            $"引用已托管资源 {result.RemoteReferencedFiles} 个",
            $"直接打包 {result.PackedFiles} 个",
            $"包体 {FormatSize(result.OutputBytes)}"
        };

        if (result.SkippedFiles > 0)
            parts.Add($"跳过 {result.SkippedFiles} 个");

        return string.Join(" · ", parts);
    }

    private static string SafeFileName(string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "modpack" : name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            trimmed = trimmed.Replace(c, '_');
        return trimmed;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private void RaiseCanExportChanged() => OnPropertyChanged(nameof(CanExport));

    partial void OnFormatIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsCurseForgeFormat));
        OnPropertyChanged(nameof(IsFormatModrinth));
        OnPropertyChanged(nameof(IsFormatCurseForge));
        OnPropertyChanged(nameof(FormatHint));
        OnPropertyChanged(nameof(IsAuthorRequired));
        RaiseCanExportChanged();
    }

    partial void OnPackNameChanged(string value) => RaiseCanExportChanged();

    partial void OnPackVersionChanged(string value) => RaiseCanExportChanged();

    partial void OnAuthorChanged(string value) => RaiseCanExportChanged();

    partial void OnSelectedFileCountChanged(int value) => RaiseCanExportChanged();

    partial void OnIsExportingChanged(bool value) => RaiseCanExportChanged();

    partial void OnIsScanningChanged(bool value) => RaiseCanExportChanged();

    partial void OnOutputPathChanged(string value)
    {
        OutputPathDisplay = string.IsNullOrWhiteSpace(value) ? "未选择（导出时会询问）" : value;
    }
}
