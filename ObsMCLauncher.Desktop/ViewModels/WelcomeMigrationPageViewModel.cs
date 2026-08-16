using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Services.Migration;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>迁移来源</summary>
public enum MigrationSource
{
    /// <summary>Plain Craft Launcher 2</summary>
    Pcl2,

    /// <summary>Hello Minecraft! Launcher 3.16 及以上（新配置格式）</summary>
    HmclNew,

    /// <summary>Hello Minecraft! Launcher 3.15.x 及以下（旧 hmcl.json 格式）</summary>
    HmclLegacy,
}

/// <summary>迁移明细行（完成页展示用）</summary>
public class MigrationDetailRow
{
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool Imported { get; init; }
    public bool Skipped { get; init; }
    public bool Warning { get; init; }
}

/// <summary>
/// 欢迎流程：数据迁移页（仅导入）。仿 ClassIsland 数据迁移向导，Carousel 分步：
/// 选择导入来源 → 配置导入（主程序路径 + 数据项） → 导入中 → 导入完成。
/// </summary>
public partial class WelcomeMigrationPageViewModel : ViewModelBase
{
    private readonly WelcomeViewModel _owner;

    /// <summary>导入中页面的最短展示时长：即便导入瞬间完成，动画也完整播放</summary>
    private static readonly TimeSpan MinimumProgressDuration = TimeSpan.FromSeconds(1.5);

    /// <summary>当前选中的迁移来源</summary>
    [ObservableProperty]
    private MigrationSource selectedSource = MigrationSource.Pcl2;

    /// <summary>来源主程序路径（exe 或 jar）</summary>
    [ObservableProperty]
    private string sourceExecutablePath = "";

    /// <summary>从主程序路径推导的安装目录</summary>
    public string SourceDirectory =>
        string.IsNullOrWhiteSpace(SourceExecutablePath) ? "" : Path.GetDirectoryName(SourceExecutablePath) ?? "";

    /// <summary>路径是否有效（目录中有对应来源的数据）</summary>
    [ObservableProperty]
    private bool isSourceValid;

    /// <summary>路径校验提示文本</summary>
    [ObservableProperty]
    private string sourceHint = "";

    /// <summary>是否导入游戏设置</summary>
    [ObservableProperty]
    private bool importGameSettings = true;

    /// <summary>是否导入应用设置</summary>
    [ObservableProperty]
    private bool importAppSettings = true;

    /// <summary>导入是否出错（完成后展示）</summary>
    [ObservableProperty]
    private string? importError;

    /// <summary>完成页结果摘要</summary>
    [ObservableProperty]
    private string resultSummary = "";

    /// <summary>迁移明细（完成页展开查看）</summary>
    [ObservableProperty]
    private IReadOnlyList<MigrationDetailRow> migrationDetails = [];

    /// <summary>是否有可展示的迁移明细</summary>
    public bool HasMigrationDetails => MigrationDetails.Count > 0;

    /// <summary>配置页的来源说明文本</summary>
    public string SourceDescription => SelectedSource switch
    {
        MigrationSource.HmclNew => "来自 Hello Minecraft! Launcher (3.16+)。",
        MigrationSource.HmclLegacy => "来自 Hello Minecraft! Launcher (≤3.15.x)。",
        _ => "来自 Plain Craft Launcher 2。"
    };

    /// <summary>主程序选择提示（按来源与操作系统）</summary>
    public string ExecutableHint => SelectedSource switch
    {
        MigrationSource.HmclNew or MigrationSource.HmclLegacy => IsWindows
            ? "HMCL.exe 或 HMCL-*.jar 的完整路径"
            : "HMCL jar 文件的完整路径",
        _ => "PCL 主程序的完整路径"
    };

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public WelcomeMigrationPageViewModel(WelcomeViewModel owner)
    {
        _owner = owner;
        AutoDetect();
    }

    /// <summary>选定导入来源，进入配置页</summary>
    [RelayCommand]
    private void Next() => PageIndex = 1;

    /// <summary>返回来源选择页</summary>
    [RelayCommand]
    private void Back() => PageIndex = 0;

    /// <summary>Carousel 当前页索引（0 选择来源 / 1 配置导入 / 2 导入中 / 3 完成）</summary>
    [ObservableProperty]
    private int pageIndex;

    /// <summary>选择某个来源（由来源列表点击触发）</summary>
    public void SelectSource(MigrationSource source)
    {
        SelectedSource = source;
        AutoDetect();
        PageIndex = 1;
    }

    /// <summary>开始导入</summary>
    [RelayCommand]
    private async Task StartImportAsync()
    {
        if (!IsSourceValid || (!ImportGameSettings && !ImportAppSettings))
            return;

        PageIndex = 2;
        ImportError = null;

        try
        {
            // 导入与最短展示时长并行：即便导入瞬间完成，动画也完整播放
            var migrationTask = Task.Run(() => Migrate());
            var animationTask = Task.Delay(MinimumProgressDuration);
            await Task.WhenAll(migrationTask, animationTask);

            var r = migrationTask.Result;
            ResultSummary = $"已导入 {r.AppSettingsImported} 项应用设置、{r.VersionsImported} 个游戏版本";
            if (r.Warnings.Count > 0)
            {
                ResultSummary += $"\n{r.Warnings.Count} 个条目被跳过";
            }

            MigrationDetails = BuildDetailRows(r);
        }
        catch (Exception ex)
        {
            await Task.Delay(MinimumProgressDuration);
            ImportError = ex.Message;
            ResultSummary = "导入失败";
            MigrationDetails = [];
        }

        PageIndex = 3;
    }

    partial void OnMigrationDetailsChanged(IReadOnlyList<MigrationDetailRow> value)
        => OnPropertyChanged(nameof(HasMigrationDetails));

    /// <summary>把迁移结果条目转成完成页展示行</summary>
    private static IReadOnlyList<MigrationDetailRow> BuildDetailRows(PclMigrationResult result)
    {
        var rows = new List<MigrationDetailRow>(result.Items.Count);
        foreach (var item in result.Items)
        {
            rows.Add(new MigrationDetailRow
            {
                Category = item.Category,
                Name = item.Name,
                Detail = item.Detail,
                Imported = item.State == MigrationItemState.Imported,
                Skipped = item.State == MigrationItemState.Skipped,
                Warning = item.State == MigrationItemState.Warning,
            });
        }
        return rows;
    }

    private PclMigrationResult Migrate() => SelectedSource switch
    {
        MigrationSource.HmclNew => HmclMigrationService.Migrate(SourceDirectory, true, ImportAppSettings, ImportGameSettings),
        MigrationSource.HmclLegacy => HmclMigrationService.Migrate(SourceDirectory, false, ImportAppSettings, ImportGameSettings),
        _ => PclMigrationService.Migrate(SourceDirectory, ImportAppSettings, ImportGameSettings)
    };

    /// <summary>完成向导，结束欢迎流程</summary>
    [RelayCommand]
    private void Finish() => _owner.Complete();

    /// <summary>浏览选择主程序（由视图调用文件选择器后回填）</summary>
    public void SetSourceExecutable(string path)
    {
        SourceExecutablePath = path;
    }

    partial void OnSourceExecutablePathChanged(string value) => ValidateSource(value);

    partial void OnSelectedSourceChanged(MigrationSource value)
    {
        OnPropertyChanged(nameof(SourceDescription));
        OnPropertyChanged(nameof(ExecutableHint));
        ValidateSource(SourceExecutablePath);
    }

    private void ValidateSource(string path)
    {
        var dir = SourceDirectory;
        if (string.IsNullOrWhiteSpace(path))
        {
            IsSourceValid = false;
            SourceHint = "请选择主程序";
            return;
        }

        IsSourceValid = SelectedSource switch
        {
            MigrationSource.HmclNew => HmclMigrationService.DetectHmclData(dir, true, out _),
            MigrationSource.HmclLegacy => HmclMigrationService.DetectHmclData(dir, false, out _),
            _ => PclMigrationService.LooksLikePclDirectory(dir)
        };

        HmclMigrationService.DetectHmclData(dir, true, out var newDetail);
        HmclMigrationService.DetectHmclData(dir, false, out var legacyDetail);
        SourceHint = SelectedSource switch
        {
            MigrationSource.HmclNew => IsSourceValid ? "已找到 HMCL 数据" : newDetail,
            MigrationSource.HmclLegacy => IsSourceValid ? "已找到 HMCL 数据" : legacyDetail,
            _ => IsSourceValid ? "已找到 PCL 数据" : "该目录中未找到 PCL 数据（PCL\\Setup.ini）"
        };
    }

    /// <summary>在常见位置自动探测当前来源的主程序</summary>
    private void AutoDetect()
    {
        var detected = SelectedSource switch
        {
            MigrationSource.HmclNew or MigrationSource.HmclLegacy => AutoDetectHmcl(),
            _ => AutoDetectPcl()
        };

        if (detected != null)
        {
            SourceExecutablePath = detected;
        }
    }

    /// <summary>在常见位置探测 PCL 主程序，返回 exe 完整路径；未找到返回 null</summary>
    private static string? AutoDetectPcl()
    {
        var exeNames = new[] { "PCL.exe", "Plain Craft Launcher.exe" };
        return AutoDetectExecutable(dir => File.Exists(Path.Combine(dir, "PCL", "Setup.ini")), exeNames);
    }

    /// <summary>在常见位置探测 HMCL 主程序（数据目录存在 launcher-settings.json 或 hmcl.json）</summary>
    private static string? AutoDetectHmcl()
    {
        string[] exeNames = IsWindows
            ? ["HMCL.exe", "hmcl.exe"]
            : ["HMCL.jar", "hmcl.jar"];

        bool HasData(string dir)
        {
            if (HmclMigrationService.LooksLikeHmclNewDirectory(dir)) return true;
            foreach (var candidate in HmclMigrationService.GetLegacyConfigCandidates(dir))
            {
                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate)) return true;
            }
            return false;
        }

        return AutoDetectExecutable(HasData, exeNames);
    }

    /// <summary>通用探测：在桌面/文档/下载/用户目录及其常见子目录里找带数据的主程序</summary>
    private static string? AutoDetectExecutable(Func<string, bool> hasData, string[] exeNames)
    {
        var candidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        var subNames = new[] { "", "PCL", "Plain Craft Launcher", "PCL2", "HMCL", "Minecraft", "启动器" };

        foreach (var baseDir in candidates)
        {
            if (string.IsNullOrEmpty(baseDir)) continue;
            foreach (var sub in subNames)
            {
                var dir = string.IsNullOrEmpty(sub) ? baseDir : Path.Combine(baseDir, sub);
                if (!hasData(dir)) continue;

                foreach (var exe in exeNames)
                {
                    var exePath = Path.Combine(dir, exe);
                    if (File.Exists(exePath))
                    {
                        return exePath;
                    }
                }
            }
        }

        return null;
    }
}
