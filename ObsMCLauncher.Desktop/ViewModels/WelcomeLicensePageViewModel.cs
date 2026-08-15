using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 欢迎流程：开源许可页。展示 LICENSE 全文，同意后按入口继续
/// （正常流程直接完成，数据迁移入口进入数据迁移页）。
/// </summary>
public partial class WelcomeLicensePageViewModel : ViewModelBase
{
    private const string LicenseResourceName = "ObsMCLauncher.Desktop.LICENSE.txt";

    private readonly WelcomeViewModel _owner;

    /// <summary>许可全文（程序集内嵌入的 LICENSE 文件）</summary>
    public string LicenseText { get; }

    /// <summary>是否勾选同意</summary>
    [ObservableProperty]
    private bool agreed;

    public WelcomeLicensePageViewModel(WelcomeViewModel owner)
    {
        _owner = owner;
        LicenseText = LoadLicenseText();
    }

    private static string LoadLicenseText()
    {
        try
        {
            var asm = typeof(WelcomeLicensePageViewModel).Assembly;
            using var stream = asm.GetManifestResourceStream(LicenseResourceName);
            if (stream == null)
                return "（未能加载许可文本）";

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            DebugLogger.Error("WelcomeLicense", $"加载许可文本失败: {ex.Message}");
            return "（未能加载许可文本）";
        }
    }

    [RelayCommand]
    private void Continue()
    {
        if (!Agreed)
            return;

        if (_owner.ContinueToMigration)
        {
            _owner.ContinueToMigration = false;
            _owner.RequestNavigate(_owner.MigrationPage);
        }
        else
        {
            _owner.Complete();
        }
    }
}
