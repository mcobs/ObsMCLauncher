using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 欢迎流程：开源许可页（两条流程的第 1 步）。
/// 展示 LICENSE 全文，勾选同意后才能前进——去设置流程的第一页，或去数据迁移页，
/// 由 <see cref="WelcomeViewModel"/> 当前的流程模式决定。
/// </summary>
public partial class WelcomeLicensePageViewModel : WelcomeStepViewModel
{
    private const string LicenseResourceName = "ObsMCLauncher.Desktop.LICENSE.txt";

    /// <summary>许可全文（程序集内嵌入的 LICENSE 文件）</summary>
    public string LicenseText { get; }

    /// <summary>是否勾选同意</summary>
    [ObservableProperty]
    private bool agreed;

    public WelcomeLicensePageViewModel(WelcomeViewModel owner)
        : base(owner, "同意许可条款", "要继续使用 ObsMCLauncher，您必须阅读并同意以下许可条款。")
    {
        LicenseText = LoadLicenseText();
    }

    /// <summary>未勾选同意时不能前进</summary>
    public override bool CanGoNext => Agreed;

    partial void OnAgreedChanged(bool value) => NextCommand.NotifyCanExecuteChanged();

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
}
