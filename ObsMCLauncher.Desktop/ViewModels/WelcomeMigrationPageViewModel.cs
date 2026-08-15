using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Services.Migration;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 欢迎流程：数据迁移页（仅导入）。仿 ClassIsland 数据迁移向导，Carousel 分步：
/// 选择导入来源 → 配置导入（PCL 目录 + 数据项） → 导入中 → 导入完成。
/// </summary>
public partial class WelcomeMigrationPageViewModel : ViewModelBase
{
    private readonly WelcomeViewModel _owner;

    /// <summary>导入来源名称</summary>
    public string SourceName { get; } = "Plain Craft Launcher 2";

    /// <summary>Carousel 当前页索引（0 选择来源 / 1 配置导入 / 2 导入中 / 3 完成）</summary>
    [ObservableProperty]
    private int pageIndex;

    /// <summary>PCL 安装目录</summary>
    [ObservableProperty]
    private string pclDirectory = "";

    /// <summary>PCL 目录是否有效（找到 PCL\Setup.ini 或 PCL.exe）</summary>
    [ObservableProperty]
    private bool isPclDirectoryValid;

    /// <summary>目录校验提示文本</summary>
    [ObservableProperty]
    private string pclDirectoryHint = "";

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

    private PclMigrationResult? _result;

    public WelcomeMigrationPageViewModel(WelcomeViewModel owner)
    {
        _owner = owner;
        PclDirectory = AutoDetectPclDirectory();
        ValidatePclDirectory(PclDirectory);
    }

    /// <summary>选定导入来源，进入配置页</summary>
    [RelayCommand]
    private void Next() => PageIndex = 1;

    /// <summary>返回来源选择页</summary>
    [RelayCommand]
    private void Back() => PageIndex = 0;

    /// <summary>开始导入</summary>
    [RelayCommand]
    private async Task StartImportAsync()
    {
        if (!IsPclDirectoryValid || (!ImportGameSettings && !ImportAppSettings))
            return;

        PageIndex = 2;
        ImportError = null;

        try
        {
            await Task.Run(() =>
            {
                _result = PclMigrationService.Migrate(PclDirectory, ImportAppSettings, ImportGameSettings);
            });

            var r = _result!;
            ResultSummary = $"已导入 {r.AppSettingsImported} 项应用设置、{r.VersionsImported} 个游戏版本";
            if (r.Warnings.Count > 0)
            {
                ResultSummary += $"\n{r.Warnings.Count} 个条目被跳过";
            }
        }
        catch (Exception ex)
        {
            ImportError = ex.Message;
            ResultSummary = "导入失败";
        }

        PageIndex = 3;
    }

    /// <summary>完成向导，结束欢迎流程</summary>
    [RelayCommand]
    private void Finish() => _owner.Complete();

    /// <summary>浏览选择 PCL 目录（由视图调用文件夹选择器后回填）</summary>
    public void SetPclDirectory(string path)
    {
        PclDirectory = path;
    }

    partial void OnPclDirectoryChanged(string value) => ValidatePclDirectory(value);

    private void ValidatePclDirectory(string path)
    {
        IsPclDirectoryValid = PclMigrationService.LooksLikePclDirectory(path);
        PclDirectoryHint = string.IsNullOrWhiteSpace(path)
            ? "请选择 PCL 所在的文件夹"
            : IsPclDirectoryValid
                ? "已找到 PCL 数据"
                : "该文件夹中未找到 PCL 的数据文件，请确认选择的是 PCL.exe 所在文件夹";
    }

    /// <summary>在常见位置自动探测 PCL 安装目录</summary>
    private static string AutoDetectPclDirectory()
    {
        var candidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        var subNames = new[] { "", "PCL", "Plain Craft Launcher", "PCL2" };

        foreach (var baseDir in candidates)
        {
            if (string.IsNullOrEmpty(baseDir)) continue;
            foreach (var sub in subNames)
            {
                var dir = string.IsNullOrEmpty(sub) ? baseDir : Path.Combine(baseDir, sub);
                if (PclMigrationService.LooksLikePclDirectory(dir))
                {
                    return dir;
                }
            }
        }

        return "";
    }
}
